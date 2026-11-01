using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using UnityEngine;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// A custom player model that a remote player wears (docs/coop/PLAYER_MODELS.md, "The pose copy"): a skinned copy of the model that stands in the place of his vanilla body. The vanilla body stays, hidden: its
/// Animator and Final IK keep posing its skeleton as Nitrox already makes them (swimming, walking, sitting, piloting, holding a tool), and every frame, after all of that has run, the bones of the model take the
/// world rotation of the vanilla bone they follow times a constant (<see cref="PlayerModelRig" />), the hips take its place, the root takes its size and place. Held items hang on a new attach point at the hand
/// of the model. It lives as a child of the body, so that it goes where the body goes and is gone with it. <see cref="CustomPlayerModels" /> makes, keeps and poses them.
/// </summary>
public sealed class CustomPlayerModel : MonoBehaviour
{
    /// <summary>Fewer bones than this and the model would stand in a heap: the copy is not made and the vanilla body stays.</summary>
    private const int MIN_FOLLOWED_BONES = 15;

    private const string VANILLA_PDA_ATTACH = "attachL";

    private struct FollowedBone
    {
        public string Name;
        public Transform Vanilla;
        public Transform Model;
        public Quaternion K;

        /// <summary>The bone each one points to (for <see cref="CustomPlayerModel.Measure" /> only): null for none.</summary>
        public Transform VanillaAim;

        public Transform ModelAim;

        /// <summary>For the bones of the spine and the neck: the direction to the aim bone in the bone's own frame, and whether the bone is turned on until its stretch points where the vanilla stretch points.</summary>
        public Vector3 LocalAim;

        public bool CorrectsAim;
    }

    private RemotePlayer owner;
    private string modelId;
    private Transform view;
    private FollowedBone[] followed;
    private Transform modelHips;
    private Transform vanillaHips;
    private Vector3 hipsRest;
    private Vector3 vanillaHipsBind;
    private float inverseScale;
    private Vector3 rootScale;
    private Animator animator;
    private AnimatorCullingMode originalCulling;
    private readonly List<Renderer> hiddenRenderers = [];
    private Transform vanillaAttach;
    private Transform modelAttach;
    private SkinnedMeshRenderer modelRenderer;
    private bool restored;
    private LoadedPlayerModel loaded;
    private Transform[] nodes;
    private HashSet<Transform> followedModelBones;
    private EmotePlayback emote;

    // the copy of the local player's own body (docs/coop/PLAYER_MODELS.md, "Your own model"): the vanilla meshes are not drawn (Renderer.forceRenderingOff: the game goes on switching their enabled flags itself, which
    // would bring the body back), the copy is drawn where the vanilla body would be, and its head is hidden while the player looks through its eyes
    private bool isLocal;
    private Transform keptAttach;
    private Transform keptPdaAttach;
    private readonly List<Renderer> forcedOff = [];
    private SkinnedMeshRenderer[] modelRenderers;
    private bool modelShown = true;
    private Transform[] hiddenBones = Array.Empty<Transform>();
    private Vector3[] hiddenBoneScales = Array.Empty<Vector3>();
    private Transform[] trimBones = Array.Empty<Transform>();
    private Vector3[] trimBoneScales = Array.Empty<Vector3>();
    private float[] trimFactors = Array.Empty<float>();
    private Vector3 firstPersonOffset;
    private bool headHidden;
    private bool emoteView;
    private float nextScanAt;

    // the hands (docs/coop/PLAYER_MODELS.md, "The hands"): placed where the vanilla hands are, with fingers that follow the vanilla fingers. Only the copy of the local player's own body has them for now
    private PlayerModelHandRig[] handRigs;
    private string handsNote = "no hands";

    private PlayerModelHandSettings handSettings = new();

    /// <summary>What the hands of this copy do: switched on or off, the fingers, the thumbs, the small corrections. They start as the profile of the model has them; the Debug command <c>vufp</c> changes them while a model is tuned.</summary>
    public PlayerModelHandSettings HandSettings => handSettings;

    /// <summary>What the Debug command <c>vufp</c> last set: a copy that is made later starts from it and not from the profile (so that a tuned value survives the copy being made again).</summary>
    public static PlayerModelHandSettings DebugHandSettings { get; set; }

    /// <summary>Whether the hands of this copy are placed where the vanilla hands are (a copy of the local player's own body, and not switched off).</summary>
    public bool HandsActive => handRigs != null && handSettings.Enabled;

    /// <summary>An emote that is playing: the clip, when it started and the arrays that every frame's sample is made in (so that a frame allocates nothing).</summary>
    private sealed class EmotePlayback
    {
        public string Id;
        public GlbClipData Clip;
        public float StartTime;
        public int HipsNode;
        public System.Numerics.Quaternion[] Rotations;
        public System.Numerics.Vector3[] Translations;
        public bool[] HasRotation;
        public bool[] HasTranslation;

        /// <summary>Not null for a shared emote: the clip is the source model's and this puts it on the bones of this model (<see cref="SharedEmotes" />).</summary>
        public PlayerModelEmoteRetarget Retarget;
    }

    public RemotePlayer Owner => owner;

    public string ModelId => modelId;

    public PlayerModelCalibration Calibration { get; private set; }

    /// <summary>The factor the model is sized with (the hips factor, unless the head was asked for).</summary>
    public float Scale { get; private set; }

    /// <summary>The number of bones that follow a bone of the vanilla body.</summary>
    public int FollowedCount => followed?.Length ?? 0;

    /// <summary>The number of vanilla meshes that are hidden.</summary>
    public int HiddenCount => isLocal ? forcedOff.Count : hiddenRenderers.Count;

    /// <summary>Whether this is the copy of the local player's own body (the emote camera and his own first-person view).</summary>
    public bool IsLocal => isLocal;

    /// <summary>Whether the emote camera uses the copy now: it is drawn whatever the vanilla body does and its head is its own.</summary>
    public bool EmoteView => emoteView;

    /// <summary>Whether the head of the copy is hidden (the player looks through its eyes).</summary>
    public bool HeadHidden => headHidden;

    /// <summary>Whether the copy is drawn this frame (the local copy follows the vanilla body: <see cref="FirstPersonModelRules.Shown" />).</summary>
    public bool ModelShown => modelShown;

    /// <summary>The one line the log gets when the model is put on.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>Whether the vanilla body is drawn too (the Debug command <c>vumodel vanilla on</c>, to see the two over one another).</summary>
    public bool VanillaVisible { get; private set; }

    /// <summary>
    /// Puts <paramref name="model" /> on <paramref name="player" />: everything that can fail is done first (the model, the calibration, the bones), the vanilla body is hidden last. Null with the reason in
    /// <paramref name="failure" /> when it could not be done; nothing has been changed then.
    /// </summary>
    internal static CustomPlayerModel Create(RemotePlayer player, LoadedPlayerModel model, Material suit, PlayerModelScaleSource scaleSource, out string failure)
    {
        return Create(new BodyHandle(player, player.PlayerModel, player.ItemAttachPoint, player.PlayerName), model, suit, scaleSource, out failure);
    }

    /// <summary>
    /// Puts <paramref name="model" /> on the body of the local player (<paramref name="playerModel" /> is its <c>player_view</c>), for the emote camera (<see cref="CustomPlayerModels.WearLocal" />): the same pose copy and the same
    /// emote clips that the others see on this player, with no owner and no held items to take over (the tool is put away while the camera is out). Null with the reason in <paramref name="failure" /> when it cannot be done.
    /// </summary>
    internal static CustomPlayerModel CreateLocal(GameObject playerModel, LoadedPlayerModel model, Material suit, PlayerModelScaleSource scaleSource, out string failure)
    {
        return Create(new BodyHandle(null, playerModel, null, "this player"), model, suit, scaleSource, out failure);
    }

    /// <summary>The body that a model is put on: a remote player's, or the local player's own (no owner, no item attach point).</summary>
    private readonly struct BodyHandle
    {
        public BodyHandle(RemotePlayer owner, GameObject playerModel, Transform itemAttach, string name)
        {
            Owner = owner;
            PlayerModel = playerModel;
            ItemAttach = itemAttach;
            Name = name;
        }

        public RemotePlayer Owner { get; }

        public GameObject PlayerModel { get; }

        public Transform ItemAttach { get; }

        public string Name { get; }
    }

    private static CustomPlayerModel Create(BodyHandle body, LoadedPlayerModel model, Material suit, PlayerModelScaleSource scaleSource, out string failure)
    {
        GameObject createdRoot = null;
        try
        {
            return Build(body, model, suit, scaleSource, out failure, ref createdRoot);
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[PlayerModels] '{model.Id}' could not be put on {body.Name}");
            if (createdRoot)
            {
                // its OnDestroy gives the vanilla body back whatever was changed
                Destroy(createdRoot);
            }
            failure = $"{exception.GetType().Name}: {exception.Message}";
            return null;
        }
    }

    private static CustomPlayerModel Build(BodyHandle body, LoadedPlayerModel model, Material suit, PlayerModelScaleSource scaleSource, out string failure, ref GameObject createdRoot)
    {
        failure = "";
        Transform view = body.PlayerModel ? body.PlayerModel.transform : null;
        Transform skeleton = view ? view.Find("export_skeleton") : null;
        if (!skeleton || !view.parent)
        {
            failure = "the body has no player_view/export_skeleton";
            return null;
        }

        Dictionary<string, Transform> vanillaBones = new();
        foreach (Transform bone in skeleton.GetComponentsInChildren<Transform>(true))
        {
            vanillaBones[bone.name] = bone;
        }
        Dictionary<string, PlayerModelBind> bind = CaptureVanillaBind(view);
        Transform attach = body.ItemAttach;
        Vector3 attachPosition = attach ? attach.localPosition : Vector3.zero;
        Quaternion attachRotation = attach ? attach.localRotation : Quaternion.identity;
        PlayerModelProfile profile = model.Profile;
        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model.Rest, bind, ToNumerics(attachPosition), ToNumerics(attachRotation), profile.ScaleSource ?? scaleSource, profile.BoneNames, profile.ScaleFactor, profile.EyeHeight);

        GameObject root = PlayerModelBuilder.Instantiate(model, view.parent, suit, out Transform[] nodes);
        createdRoot = root;
        CustomPlayerModel worn = root.AddComponent<CustomPlayerModel>();
        List<FollowedBone> followedBones = [];
        List<string> lacking = [];
        foreach (PlayerModelBoneConstant constant in calibration.Bones)
        {
            if (vanillaBones.TryGetValue(constant.Vanilla, out Transform vanilla))
            {
                Transform vanillaAim = null;
                if (constant.VanillaAim != null)
                {
                    vanillaBones.TryGetValue(constant.VanillaAim, out vanillaAim);
                }
                followedBones.Add(new FollowedBone
                {
                    Name = constant.Mixamo,
                    Vanilla = vanilla,
                    Model = nodes[constant.MixamoNode],
                    K = ToUnity(constant.K),
                    VanillaAim = vanillaAim,
                    ModelAim = constant.MixamoAimNode >= 0 ? nodes[constant.MixamoAimNode] : null,
                    LocalAim = ToUnity(constant.LocalAim),
                    CorrectsAim = constant.FollowsSegment && vanillaAim
                });
            }
            else
            {
                lacking.Add(constant.Vanilla);
            }
        }
        if (followedBones.Count < MIN_FOLLOWED_BONES)
        {
            failure = $"only {followedBones.Count} bones of the model could follow a bone of the vanilla body ({string.Join(", ", calibration.Missing.Concat(lacking.Select(name => $"vanilla {name}")))})";
            worn.restored = true;
            Destroy(root);
            return null;
        }

        worn.owner = body.Owner;
        worn.modelId = model.Id;
        worn.loaded = model;
        worn.nodes = nodes;
        worn.view = view;
        worn.followed = followedBones.ToArray();
        worn.Calibration = calibration;
        worn.Scale = calibration.Scale;
        worn.inverseScale = calibration.Scale > 0f ? 1f / calibration.Scale : 1f;
        worn.rootScale = Vector3.one * calibration.Scale;
        worn.modelRenderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (calibration.HipsNode >= 0 && vanillaBones.TryGetValue(PlayerModelRig.VANILLA_HIPS, out Transform vanillaHips))
        {
            worn.modelHips = nodes[calibration.HipsNode];
            worn.vanillaHips = vanillaHips;
            worn.hipsRest = ToUnity(calibration.HipsRest);
            worn.vanillaHipsBind = ToUnity(calibration.VanillaHipsBind);
        }
        if (body.Owner == null)
        {
            worn.handSettings = (DebugHandSettings ?? profile.Hands).Copy();
            worn.MakeHands(model, bind, vanillaBones, nodes);
        }
        worn.Pose();

        SkyApplier applier = root.AddComponent<SkyApplier>();
        applier.anchorSky = Skies.Auto;
        applier.emissiveFromPower = false;
        applier.dynamic = true;
        applier.renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // from here the vanilla body is changed
        worn.animator = view.GetComponent<Animator>();
        string culling = "no Animator";
        if (worn.animator)
        {
            worn.originalCulling = worn.animator.cullingMode;
            culling = $"{worn.originalCulling} -> {AnimatorCullingMode.AlwaysAnimate}";
            // with every vanilla mesh hidden a culling animator sees nothing to draw and stops posing the bones that the copy follows
            worn.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        vanillaBones.TryGetValue(VANILLA_PDA_ATTACH, out Transform pdaAttach);
        worn.modelRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        worn.ResolveFirstPersonBones(model, nodes);
        if (body.Owner == null)
        {
            // the local player's own body: what his hands hold stays at the vanilla hand, where the game puts it and the arms grip it
            worn.isLocal = true;
            worn.headHidden = true;
            worn.HideVanillaMeshesLocal(view, view.Find(NitroxClient.GameLogic.PlayerLogic.PlayerModel.PlayerEquipmentConstants.ITEM_ATTACH_POINT_GAME_OBJECT_NAME), pdaAttach);
        }
        else
        {
            worn.HideVanillaMeshes(view, attach, pdaAttach);
        }
        int moved = worn.TakeOverItemAttach(attach, calibration, nodes);

        StringBuilder summary = new();
        summary.Append($"'{model.Id}' is worn by {body.Name}: scale {calibration.Scale:F4} (hips {calibration.HipsFactor:F4} = vanilla {calibration.VanillaHipsBind.Y:F3} m / model {calibration.HipsRest.Y:F3} m, ");
        summary.Append($"head {calibration.HeadFactor:F4}, they differ by {Math.Abs(calibration.HeadFactor / calibration.HipsFactor - 1f) * 100f:F1}%{(calibration.FactorsDiffer ? " (more than 8%)" : "")}, sized by {scaleSource}), ");
        summary.Append($"{worn.followed.Length} bones follow the vanilla body ({worn.followed.Count(bone => bone.CorrectsAim)} of them along their stretch), {worn.HiddenCount} vanilla meshes hidden{(worn.isLocal ? " (not drawn)" : "")}, animator culling {culling}, item attach {(worn.modelAttach ? $"moved {moved} items to the model's hand" : "left on the vanilla hand")}");
        if (calibration.Missing.Count > 0 || lacking.Count > 0)
        {
            summary.Append($", missing: {string.Join("; ", calibration.Missing.Concat(lacking.Select(name => $"vanilla {name}")))}");
        }
        summary.Append($", {worn.handsNote}, profile {(profile == PlayerModelProfile.Default ? "none" : $"'{profile.DisplayNameOr(model.Id)}'")}");
        worn.Summary = summary.ToString();
        return worn;
    }

    /// <summary>
    /// The bones that the profile of the model hides while the player looks through its eyes (the head, unless it says otherwise: the helmet or the hair that hang on it go with it), the bones it makes smaller (a
    /// shoulder pad that comes within the near clip of the camera) and where it puts the whole model against the vanilla body. A bone that the file lacks is named in the log and left out.
    /// </summary>
    private void ResolveFirstPersonBones(LoadedPlayerModel model, Transform[] modelNodes)
    {
        PlayerModelProfile profile = model.Profile;
        List<Transform> hidden = [];
        List<Vector3> hiddenScales = [];
        foreach (string bone in profile.HiddenBones)
        {
            int node = model.Rest.Find(profile.BoneNames, bone);
            if (node < 0 || node >= modelNodes.Length || !modelNodes[node])
            {
                Log.Warn($"[PlayerModels] '{model.Id}' has no bone '{bone}' to hide in first person");
                continue;
            }
            hidden.Add(modelNodes[node]);
            hiddenScales.Add(modelNodes[node].localScale);
        }
        hiddenBones = hidden.ToArray();
        hiddenBoneScales = hiddenScales.ToArray();
        List<Transform> trimmed = [];
        List<Vector3> trimmedScales = [];
        List<float> factors = [];
        foreach (KeyValuePair<string, float> trim in profile.Trims)
        {
            int node = model.Rest.Find(profile.BoneNames, trim.Key);
            if (node < 0 || node >= modelNodes.Length || !modelNodes[node])
            {
                Log.Warn($"[PlayerModels] '{model.Id}' has no bone '{trim.Key}' to trim in first person");
                continue;
            }
            trimmed.Add(modelNodes[node]);
            trimmedScales.Add(modelNodes[node].localScale);
            factors.Add(trim.Value);
        }
        trimBones = trimmed.ToArray();
        trimBoneScales = trimmedScales.ToArray();
        trimFactors = factors.ToArray();
        firstPersonOffset = ToUnity(profile.FirstPersonOffset);
    }

    /// <summary>
    /// Makes the rigs of the two hands (<see cref="PlayerModelHandRig" />) from the calibration of the model's rest pose against the vanilla bind pose. A hand that cannot be made (a bone is not there) is named in
    /// the note and keeps the pose copy of the body.
    /// </summary>
    private void MakeHands(LoadedPlayerModel model, Dictionary<string, PlayerModelBind> bind, Dictionary<string, Transform> vanillaBones, Transform[] modelNodes)
    {
        PlayerModelHandsCalibration hands = PlayerModelHands.Calibrate(model.Rest, bind, model.Profile.BoneNames);
        List<PlayerModelHandRig> rigs = [];
        List<string> notes = [];
        foreach (PlayerModelHandCalibration calibration in new[] { hands.Left, hands.Right })
        {
            if (calibration == null)
            {
                continue;
            }
            PlayerModelHandRig rig = PlayerModelHandRig.Create(calibration, modelNodes, vanillaBones, out string problem);
            if (rig == null)
            {
                notes.Add($"{calibration.Side} hand: {problem}");
                continue;
            }
            rigs.Add(rig);
            notes.Add($"{calibration.Side} hand rolled {calibration.RollDegrees:F1} deg, {rig.FingerCount} finger bones");
        }
        notes.AddRange(hands.Missing);
        handRigs = rigs.Count > 0 ? rigs.ToArray() : null;
        handsNote = handRigs != null ? $"hands: {string.Join("; ", notes)}" : $"no hands ({string.Join("; ", notes)})";
    }

    /// <summary>The hands of the copy are put where the vanilla hands are (after the pose copy of the body, which they correct): the turn of each hand, the arm that reaches the vanilla grip, the fingers.</summary>
    private void ApplyHands()
    {
        if (handRigs == null)
        {
            return;
        }
        PlayerModelHandSettings settings = handSettings;
        Player player = Player.main;
        PDA pda = player ? player.GetPDA() : null;
        bool pdaOpen = pda && pda.isInUse;
        Transform camera = null;
        if (settings.PdaPullBack != 0f)
        {
            Camera main = MainCamera.camera;
            camera = main ? main.transform : null;
        }
        // where the game's IK wants the hands while a tool is held: the hand targets of the tool (fixed to it)
        Transform leftTarget = null;
        Transform rightTarget = null;
        if (settings.ToolTargets && player && !pdaOpen)
        {
            PlayerTool tool = Inventory.main ? Inventory.main.GetHeldTool() : null;
            if (tool && tool.isDrawn)
            {
                rightTarget = tool.rightHandIKTarget;
                ArmsController arms = player.armsController;
                leftTarget = tool.useLeftAimTargetOnPlayer ? (arms ? arms.attachedLeftHandTarget : null) : tool.leftHandIKTarget;
            }
        }
        for (int i = 0; i < handRigs.Length; i++)
        {
            handRigs[i].Apply(settings, pdaOpen, camera, handRigs[i].IsLeft ? leftTarget : rightTarget);
        }
    }

    /// <summary>The bind pose of the vanilla body of the local player, for the report of <see cref="PlayerModelCalibrator" />.</summary>
    internal static Dictionary<string, PlayerModelBind> VanillaBindOf(Transform view) => CaptureVanillaBind(view);

    /// <summary>The bind pose of every bone of the vanilla body by name, in the frame of <c>player_view</c> (what the Debug command <c>vumodel dump</c> prints): from the biggest skinned mesh of the body.</summary>
    private static Dictionary<string, PlayerModelBind> CaptureVanillaBind(Transform view)
    {
        Dictionary<string, PlayerModelBind> result = new();
        SkinnedMeshRenderer biggest = null;
        foreach (SkinnedMeshRenderer candidate in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh && (!biggest || candidate.bones.Length > biggest.bones.Length))
            {
                biggest = candidate;
            }
        }
        if (!biggest)
        {
            return result;
        }
        Matrix4x4 toView = view.worldToLocalMatrix;
        Matrix4x4 meshToWorld = biggest.transform.localToWorldMatrix;
        Matrix4x4[] bindPoses = biggest.sharedMesh.bindposes;
        Transform[] bones = biggest.bones;
        for (int i = 0; i < bones.Length && i < bindPoses.Length; i++)
        {
            if (!bones[i])
            {
                continue;
            }
            Matrix4x4 relative = toView * meshToWorld * bindPoses[i].inverse;
            Vector3 position = relative.GetColumn(3);
            result[bones[i].name] = new PlayerModelBind(ToNumerics(position), ToNumerics(relative.rotation));
        }
        return result;
    }

    private void HideVanillaMeshes(Transform bodyView, Transform heldItemAttach, Transform pdaAttach)
    {
        foreach (Renderer candidate in bodyView.GetComponentsInChildren<Renderer>(true))
        {
            if (!(candidate is SkinnedMeshRenderer || candidate is MeshRenderer) || !candidate.enabled)
            {
                continue;
            }
            Transform at = candidate.transform;
            // what the hands hold (a tool, a PDA) is not part of the body
            if ((heldItemAttach && at.IsChildOf(heldItemAttach)) || (pdaAttach && at.IsChildOf(pdaAttach)))
            {
                continue;
            }
            candidate.enabled = false;
            hiddenRenderers.Add(candidate);
        }
        VanillaVisible = false;
    }

    /// <summary>
    /// The local body: every mesh of <c>player_view</c> that is not what the hands hold (a tool at the attach point, the PDA) is switched off for the camera, which the game does not know about and cannot undo (it
    /// goes on switching the <c>enabled</c> flags of the equipment and the arms itself: <see cref="ApplyLocalState" /> reads them to know when the vanilla body would be drawn). The shadow goes with it.
    /// </summary>
    private void HideVanillaMeshesLocal(Transform bodyView, Transform heldItemAttach, Transform pdaAttach)
    {
        keptAttach = heldItemAttach;
        keptPdaAttach = pdaAttach;
        ScanVanilla(bodyView);
        VanillaVisible = false;
    }

    /// <summary>Switches off every mesh of the local body that is not off yet: the ones the game put on it since the last look (equipment that was put on) too.</summary>
    private void ScanVanilla(Transform bodyView)
    {
        foreach (Renderer candidate in bodyView.GetComponentsInChildren<Renderer>(true))
        {
            if (!(candidate is SkinnedMeshRenderer || candidate is MeshRenderer) || candidate.forceRenderingOff)
            {
                continue;
            }
            Transform at = candidate.transform;
            if ((keptAttach && at.IsChildOf(keptAttach)) || (keptPdaAttach && at.IsChildOf(keptPdaAttach)))
            {
                continue;
            }
            candidate.forceRenderingOff = true;
            forcedOff.Add(candidate);
        }
    }

    /// <summary>Shows or hides the vanilla body again (a Debug aid: both drawn over one another show how well the model follows).</summary>
    public void SetVanillaVisible(bool visible)
    {
        foreach (Renderer hidden in hiddenRenderers)
        {
            if (hidden)
            {
                hidden.enabled = visible;
            }
        }
        foreach (Renderer hidden in forcedOff)
        {
            if (hidden)
            {
                hidden.forceRenderingOff = !visible;
            }
        }
        VanillaVisible = visible;
    }

    /// <summary>The emote camera uses the copy (true: the copy is drawn whatever the vanilla body does and its head is its own) or gives it back to the first-person view (false: the head is hidden again).</summary>
    internal void SetEmoteView(bool on)
    {
        emoteView = on;
        headHidden = isLocal && !on;
    }

    /// <summary>
    /// Makes a new attach point at the right hand of the model, with the same offset and the same orientation (in the world) as the vanilla one has at its hand, hands the items that are held now over to it and
    /// makes it the one that <see cref="RemotePlayer.ItemAttachPoint" /> names, so that the tools drawn from now on go there. Returns the number of items that were moved.
    /// </summary>
    private int TakeOverItemAttach(Transform vanilla, PlayerModelCalibration calibration, Transform[] nodes)
    {
        if (!vanilla || calibration.AttachHandNode < 0)
        {
            return 0;
        }
        vanillaAttach = vanilla;
        GameObject attachObject = new("CustomPlayerModelItemAttach");
        modelAttach = attachObject.transform;
        modelAttach.SetParent(nodes[calibration.AttachHandNode], false);
        modelAttach.localPosition = ToUnity(calibration.AttachPosition);
        modelAttach.localRotation = ToUnity(calibration.AttachRotation);

        int moved = 0;
        List<Transform> children = vanilla.Cast<Transform>().ToList();
        foreach (Transform child in children)
        {
            if (child.name.StartsWith("attach1_", StringComparison.Ordinal))
            {
                // the two helpers of the vanilla attach point (a forward and an up marker): the same ones for the new one
                Transform helper = new GameObject(child.name).transform;
                helper.SetParent(modelAttach, false);
                helper.localPosition = child.localPosition;
                helper.localRotation = child.localRotation;
                continue;
            }
            child.SetParent(modelAttach, false);
            moved++;
        }
        owner.SetItemAttachPoint(modelAttach);
        return moved;
    }

    /// <summary>Gives the vanilla body back what the model changed (the meshes, the animator, the items) without destroying the model.</summary>
    private void RestoreVanillaBody()
    {
        if (restored)
        {
            return;
        }
        restored = true;
        foreach (Renderer hidden in hiddenRenderers)
        {
            if (hidden)
            {
                hidden.enabled = true;
            }
        }
        hiddenRenderers.Clear();
        foreach (Renderer hidden in forcedOff)
        {
            if (hidden)
            {
                hidden.forceRenderingOff = false;
            }
        }
        forcedOff.Clear();
        if (animator)
        {
            animator.cullingMode = originalCulling;
        }
        if (modelAttach && vanillaAttach)
        {
            foreach (Transform child in modelAttach.Cast<Transform>().Where(child => !child.name.StartsWith("attach1_", StringComparison.Ordinal)).ToList())
            {
                child.SetParent(vanillaAttach, false);
            }
            owner?.SetItemAttachPoint(vanillaAttach);
        }
    }

    private void OnDestroy()
    {
        RestoreVanillaBody();
        if (modelRenderer && modelRenderer.sharedMaterial)
        {
            Destroy(modelRenderer.sharedMaterial);
        }
        CustomPlayerModels.Forget(this);
    }

    /// <summary>
    /// The pose of this frame (called by <see cref="CustomPlayerModels" /> in the first <c>Camera.onPreCull</c> of the frame, after the Animator and Final IK have run): the root is where the body is and as big as
    /// the scale says, every followed bone has the world rotation of its vanilla bone times its constant, and the hips are where the vanilla hips moved to, from their rest.
    /// </summary>
    internal void Pose()
    {
        PoseBones();
        ApplyLocalState();
    }

    /// <summary>
    /// The local copy follows the vanilla body: it is drawn when the vanilla body would be (the game switches the meshes on and off for the seats, the PDA, the cinematics), the meshes that the game put on the body
    /// since the last look are switched off too, and the head is hidden while the player looks through its eyes.
    /// </summary>
    private void ApplyLocalState()
    {
        if (!isLocal || !view)
        {
            return;
        }
        if (Time.unscaledTime >= nextScanAt)
        {
            nextScanAt = Time.unscaledTime + FirstPersonModelRules.SCAN_INTERVAL_SECONDS;
            ScanVanilla(view);
        }
        bool anyVanillaOn = false;
        for (int i = 0; i < forcedOff.Count; i++)
        {
            Renderer vanilla = forcedOff[i];
            if (vanilla && vanilla.enabled && vanilla.gameObject.activeInHierarchy)
            {
                anyVanillaOn = true;
                break;
            }
        }
        bool show = FirstPersonModelRules.Shown(emoteView, view.gameObject.activeInHierarchy, view.lossyScale.y, anyVanillaOn);
        if (show != modelShown && modelRenderers != null)
        {
            modelShown = show;
            foreach (SkinnedMeshRenderer modelMesh in modelRenderers)
            {
                if (modelMesh)
                {
                    modelMesh.enabled = show;
                }
            }
        }
        float hiddenScale = FirstPersonModelRules.HeadScale(headHidden);
        for (int i = 0; i < hiddenBones.Length; i++)
        {
            if (hiddenBones[i])
            {
                hiddenBones[i].localScale = hiddenBoneScales[i] * hiddenScale;
            }
        }
        for (int i = 0; i < trimBones.Length; i++)
        {
            if (trimBones[i])
            {
                trimBones[i].localScale = trimBoneScales[i] * (headHidden ? trimFactors[i] : 1f);
            }
        }
    }

    private void PoseBones()
    {
        if (!view || followed == null)
        {
            return;
        }
        Transform rootTransform = transform;
        rootTransform.localScale = Vector3.Scale(view.localScale, rootScale);

        // an emote: after its first fraction of a second the clip poses the whole model; until then its pose is mixed over the one that follows the body
        float emoteElapsed = 0f;
        float emoteBlend = 0f;
        Vector3 rootPosition = view.position;
        if (isLocal && headHidden && firstPersonOffset != Vector3.zero)
        {
            // the profile of the model puts it a little off the vanilla body in first person (a chest that comes within the near clip of the camera)
            rootPosition += view.rotation * firstPersonOffset;
        }
        Quaternion rootRotation = view.rotation;
        if (emote != null)
        {
            emoteElapsed = Time.time - emote.StartTime;
            float sinceStart = emoteElapsed;
            bool keepGoing = false;
#if DEBUG
            // Debug (the live check, whose commands take seconds): the clip of the local player's own emote goes round until he stops it
            if (owner == null && NitroxClient.MonoBehaviours.PlayerEmoteDriver.DebugForever && emote.Clip.Duration > 0.05f)
            {
                keepGoing = true;
                emoteElapsed = sinceStart % emote.Clip.Duration;
            }
#endif
            if (!keepGoing && PlayerEmoteRules.IsOver(emoteElapsed, emote.Clip.Duration))
            {
                Log.Info($"[PlayerModels] the emote '{emote.Id}' of {NameOfOwner()} is over");
                EndEmote();
            }
            else
            {
                emoteBlend = PlayerEmoteRules.BlendFactor(sinceStart);
                EmoteRoot(out Vector3 emotePosition, out Quaternion emoteRotation);
                rootPosition = Vector3.Lerp(rootPosition, emotePosition, emoteBlend);
                rootRotation = Quaternion.Slerp(rootRotation, emoteRotation, emoteBlend);
            }
        }
        rootTransform.SetPositionAndRotation(rootPosition, rootRotation);
        if (emote != null && emoteBlend >= 1f)
        {
            if (handRigs != null)
            {
                for (int i = 0; i < handRigs.Length; i++)
                {
                    handRigs[i].Release();
                }
            }
            ApplyEmotePose(emoteElapsed, 1f);
            return;
        }

        FollowedBone[] bones = followed;
        for (int i = 0; i < bones.Length; i++)
        {
            Quaternion rotation = bones[i].Vanilla.rotation * bones[i].K;
            if (bones[i].CorrectsAim)
            {
                // the stretch between two joints of the spine is turned by the upper bone in the vanilla body and by the lower one in the model: turn this bone on until its stretch points where the vanilla one does
                Vector3 wanted = bones[i].VanillaAim.position - bones[i].Vanilla.position;
                if (wanted.sqrMagnitude > 1e-8f)
                {
                    rotation = Quaternion.FromToRotation(rotation * bones[i].LocalAim, wanted) * rotation;
                }
            }
            bones[i].Model.rotation = rotation;
        }
        if (modelHips && vanillaHips)
        {
            Vector3 moved = view.InverseTransformPoint(vanillaHips.position) - vanillaHipsBind;
            modelHips.localPosition = hipsRest + moved * inverseScale;
        }
        // the hands come last: the arms are solved from where the shoulders are now
        ApplyHands();
        if (emote != null)
        {
            ApplyEmotePose(emoteElapsed, emoteBlend);
        }
    }

    /// <summary>The clips of this model that are emotes, for the wheel (the ones that the Debug command <c>vumodel emotes</c> lists).</summary>
    internal IReadOnlyList<EmoteEntry> Emotes
    {
        get
        {
            if (loaded == null)
            {
                return Array.Empty<EmoteEntry>();
            }
            // its own clips and the shared ones that its skeleton can play (null while the shared ones are being read: the own clips are what there is)
            return PlayerModelLibrary.EmotesOf(modelId) ?? PlayerEmoteRules.EmotesOf(loaded.Glb.Clips);
        }
    }

    /// <summary>True while an emote plays on this model (the pose copy of the body is paused then).</summary>
    public bool IsEmoting => emote != null;

    /// <summary>The name of the clip that plays, empty when none does.</summary>
    public string EmoteId => emote != null ? emote.Id : "";

    /// <summary>
    /// Plays the clip <paramref name="clipName" /> of the model on this body: from now on the clip poses the model instead of the vanilla body, until it is over (<see cref="PlayerEmoteRules.PlaySeconds" />) or
    /// <see cref="StopEmote" /> is called. False with the reason in <paramref name="message" /> when the model has no such clip or it cannot be read; the body is left as it is then. Another emote that plays is replaced.
    /// </summary>
    internal bool PlayEmote(string clipName, out string message)
    {
        if (loaded == null || nodes == null || !PlayerModelRules.IsValidId(clipName))
        {
            message = "this model has no clips here";
            return false;
        }
        GlbClipData clip = loaded.Clip(clipName);
        PlayerModelEmoteRetarget retarget = null;
        if (clip == null)
        {
            // a model with no emote of that name plays the shared one, if there is one, on its own bones
            clip = SharedEmotes.ClipFor(loaded, clipName, out retarget);
        }
        if (clip == null)
        {
            message = $"the model '{modelId}' has no clip '{clipName}' that can be read";
            return false;
        }
        if (emote != null)
        {
            EndEmote();
        }
        int count = nodes.Length;
        emote = new EmotePlayback
        {
            Id = clipName,
            Clip = clip,
            Retarget = retarget,
            StartTime = Time.time,
            HipsNode = Calibration != null ? Calibration.HipsNode : -1,
            Rotations = new System.Numerics.Quaternion[count],
            Translations = new System.Numerics.Vector3[count],
            HasRotation = new bool[count],
            HasTranslation = new bool[count]
        };
        message = $"{NameOfOwner()} plays '{clipName}' ({clip.Duration:F2} s{(PlayerEmoteRules.IsLooping(clip.Duration) ? $", repeated for {PlayerEmoteRules.LOOP_PLAY_SECONDS:F0} s" : "")}{(retarget != null ? ", shared, retargeted onto his bones" : "")})";
        return true;
    }

    /// <summary>Stops the emote that plays (the model follows the body again from the next frame). False when none played.</summary>
    internal bool StopEmote()
    {
        if (emote == null)
        {
            return false;
        }
        Log.Info($"[PlayerModels] the emote '{emote.Id}' of {NameOfOwner()} was stopped");
        EndEmote();
        return true;
    }

    private string NameOfOwner() => owner != null ? owner.PlayerName : "a player";

    /// <summary>The emote is over or was stopped: the bones that the pose copy does not drive (the fingers, say) were turned by the clip and are put back to the rest pose of the file.</summary>
    private void EndEmote()
    {
        EmotePlayback finished = emote;
        emote = null;
        if (finished == null || nodes == null || loaded == null)
        {
            return;
        }
        followedModelBones ??= new HashSet<Transform>(followed.Select(bone => bone.Model));
        if (finished.Retarget != null)
        {
            // the nodes that were turned are this model's own, not the tracks of the clip (which are the source model's)
            foreach (int node in finished.Retarget.DrivenNodes)
            {
                BackToRest(node);
            }
            return;
        }
        foreach (GlbTrack track in finished.Clip.Tracks)
        {
            if (track.IsRotation)
            {
                BackToRest(track.Node);
            }
        }
    }

    /// <summary>Puts a bone that the pose copy does not drive (a finger) back to the rest pose of the file.</summary>
    private void BackToRest(int node)
    {
        if (node < 0 || node >= nodes.Length || !nodes[node] || followedModelBones.Contains(nodes[node]))
        {
            return;
        }
        float[] rest = loaded.Glb.Nodes[node].Rotation;
        GlbSpace.Rotation(rest[0], rest[1], rest[2], rest[3], out float x, out float y, out float z, out float w);
        nodes[node].localRotation = new Quaternion(x, y, z, w);
    }

    /// <summary>
    /// Where the model stands while it emotes: upright whatever the vanilla body does (a swimmer's body is turned over, the emote is not), facing where the body faces, with its hips where the vanilla hips are (so
    /// it stands on the floor of a room and floats upright in the water).
    /// </summary>
    private void EmoteRoot(out Vector3 position, out Quaternion rotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.ProjectOnPlane(view.up, Vector3.up);
        }
        rotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.Euler(0f, view.eulerAngles.y, 0f);
        Vector3 hips = vanillaHips ? vanillaHips.position : view.position + view.up * hipsRest.y * transform.localScale.y;
        position = hips - Vector3.up * (hipsRest.y * transform.localScale.y);
    }

    /// <summary>
    /// The pose of the clip at <paramref name="elapsed" /> seconds after the start, mixed over what the bones have now by <paramref name="blend" /> (1: replaces it). Every bone that the clip turns gets the
    /// clip's local rotation; the hips keep their rest place sideways and take the clip's height (a jump, a crouch, a backflip), so that nobody drifts away from his own body.
    /// </summary>
    private void ApplyEmotePose(float elapsed, float blend)
    {
        EmotePlayback playing = emote;
        if (playing.Retarget != null)
        {
            // a shared emote: the source model's clip, put on this model's bones (the rotations that come out are in the game's space already)
            playing.Retarget.Sample(playing.Clip, PlayerEmoteRules.ClipTime(elapsed, playing.Clip.Duration), playing.Rotations, playing.HasRotation, out float sharedHeight, out bool hasSharedHeight);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!playing.HasRotation[i] || !nodes[i])
                {
                    continue;
                }
                System.Numerics.Quaternion shared = playing.Rotations[i];
                Quaternion sharedWanted = new(shared.X, shared.Y, shared.Z, shared.W);
                nodes[i].localRotation = blend >= 1f ? sharedWanted : Quaternion.Slerp(nodes[i].localRotation, sharedWanted, blend);
            }
            if (modelHips && hasSharedHeight)
            {
                Vector3 sharedHips = new(hipsRest.x, sharedHeight, hipsRest.z);
                modelHips.localPosition = blend >= 1f ? sharedHips : Vector3.Lerp(modelHips.localPosition, sharedHips, blend);
            }
            return;
        }
        playing.Clip.Sample(PlayerEmoteRules.ClipTime(elapsed, playing.Clip.Duration), playing.Rotations, playing.HasRotation, playing.Translations, playing.HasTranslation);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!playing.HasRotation[i] || !nodes[i])
            {
                continue;
            }
            System.Numerics.Quaternion q = playing.Rotations[i];
            GlbSpace.Rotation(q.X, q.Y, q.Z, q.W, out float x, out float y, out float z, out float w);
            Quaternion wanted = new(x, y, z, w);
            nodes[i].localRotation = blend >= 1f ? wanted : Quaternion.Slerp(nodes[i].localRotation, wanted, blend);
        }
        if (modelHips && playing.HipsNode >= 0 && playing.HipsNode < playing.HasTranslation.Length && playing.HasTranslation[playing.HipsNode])
        {
            System.Numerics.Vector3 t = playing.Translations[playing.HipsNode];
            GlbSpace.Vector(t.X, t.Y, t.Z, out _, out float height, out _);
            Vector3 wantedHips = new(hipsRest.x, height, hipsRest.z);
            modelHips.localPosition = blend >= 1f ? wantedHips : Vector3.Lerp(modelHips.localPosition, wantedHips, blend);
        }
    }

    /// <summary>Where the body is, for a camera that looks at it: the vanilla hips (the middle of the body in any pose), or a metre above the feet.</summary>
    public Vector3 AimPoint => vanillaHips ? vanillaHips.position : (view ? view.position + view.up : transform.position);

    /// <summary>The way the body faces, flattened: where it walks, and the way a swimmer's back is turned.</summary>
    public Vector3 Facing => view ? view.forward : transform.forward;

    /// <summary>Where everything is, for the Debug command <c>vumodel where</c> (a model that cannot be seen: is the body active, where is the model, does the camera see its renderer).</summary>
    internal string Where()
    {
        Transform body = owner?.Body ? owner.Body.transform : null;
        string vehicle = owner?.Vehicle ? $"{owner.Vehicle.name} seat={(owner.Vehicle.playerPosition ? owner.Vehicle.playerPosition.transform.position.ToString("F2") : "none")}" : "none";
        string renderer = modelRenderer ? $"enabled={modelRenderer.enabled} visible={modelRenderer.isVisible} layer={modelRenderer.gameObject.layer} boundsCenter={modelRenderer.bounds.center.ToString("F2")} boundsSize={modelRenderer.bounds.size.ToString("F2")}" : "none";
        StringBuilder where = new();
        where.Append($"bodyActive={(body ? body.gameObject.activeInHierarchy : false)} bodyAt={(body ? body.position.ToString("F2") : "none")} bodyParent={(body && body.parent ? body.parent.name : "none")} viewAt={(view ? view.position.ToString("F2") : "none")}");
        where.Append($" vanillaHipsAt={(vanillaHips ? vanillaHips.position.ToString("F2") : "none")} modelRootActive={gameObject.activeInHierarchy} modelRootAt={transform.position.ToString("F2")} modelHipsAt={(modelHips ? modelHips.position.ToString("F2") : "none")}");
        where.Append($" scale={transform.lossyScale.ToString("F3")} renderer: {renderer} vehicle: {vehicle}");
        return where.ToString();
    }

    /// <summary>
    /// How well the model follows the vanilla body right now (the Debug command <c>vumodel error</c>, run at the end of the frame): the angle between the direction a bone points to in the model and in the vanilla
    /// body (mean, largest and the bone it is), and the distance between the model's and the vanilla body's hands, feet and head in metres. After a pose copy of this very frame all angles are zero; they are not
    /// when the vanilla skeleton still moved after the copy (a pose copy that came before Final IK).
    /// </summary>
    internal string Measure()
    {
        float sum = 0f;
        float max = 0f;
        int count = 0;
        string worst = "none";
        StringBuilder distances = new();
        foreach (FollowedBone bone in followed)
        {
            if (bone.ModelAim && bone.VanillaAim)
            {
                float angle = Vector3.Angle(bone.ModelAim.position - bone.Model.position, bone.VanillaAim.position - bone.Vanilla.position);
                sum += angle;
                count++;
                if (angle > max)
                {
                    max = angle;
                    worst = bone.Name;
                }
            }
            if (bone.Name is "Head" or "LeftHand" or "RightHand" or "LeftFoot" or "RightFoot")
            {
                distances.Append($" {bone.Name}={Vector3.Distance(bone.Model.position, bone.Vanilla.position):F3}m");
            }
        }
        StringBuilder held = new();
        if (modelAttach)
        {
            foreach (Transform child in modelAttach)
            {
                if (!child.name.StartsWith("attach1_", StringComparison.Ordinal))
                {
                    held.Append($" {child.name}(active={child.gameObject.activeSelf})");
                }
            }
        }
        string attach = modelAttach && owner != null ? (owner.ItemAttachPoint == modelAttach ? "model's hand" : "NOT the model's hand") : "vanilla";
        StringBuilder handText = new();
        if (handRigs != null)
        {
            foreach (PlayerModelHandRig rig in handRigs)
            {
                handText.Append($" | {rig.Describe()}");
            }
        }
        return $"meanAngle={(count > 0 ? sum / count : 0f):F2}deg maxAngle={max:F2}deg worst={worst} bones={count} apart:{distances} itemAttach={attach} held:{(held.Length > 0 ? held.ToString() : " none")} handsActive={HandsActive}{handText}";
    }

    private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.x, v.y, v.z);

    private static System.Numerics.Quaternion ToNumerics(Quaternion q) => new(q.x, q.y, q.z, q.w);

    private static Vector3 ToUnity(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Quaternion ToUnity(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
}
