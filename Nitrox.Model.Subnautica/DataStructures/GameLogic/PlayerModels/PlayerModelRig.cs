using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// A bone of the model's skeleton that takes the pose of a bone of the vanilla body (docs/coop/PLAYER_MODELS.md, "The pose copy"). <see cref="MixamoAim" /> and <see cref="VanillaAim" /> name the bone each skeleton
/// has next along the chain (an arm points to its forearm): the direction to it is how the two rest poses are lined up. Null for a bone without one (the head): the two are taken as lined up already.
/// </summary>
public sealed class PlayerModelBonePair
{
    public PlayerModelBonePair(string mixamo, string vanilla, string? mixamoAim, string? vanillaAim, bool followsSegment = false)
    {
        Mixamo = mixamo;
        Vanilla = vanilla;
        MixamoAim = mixamoAim;
        VanillaAim = vanillaAim;
        FollowsSegment = followsSegment;
    }

    /// <summary>The name of the node in the file, without the "mixamorig:" that the exporter puts in front.</summary>
    public string Mixamo { get; }

    /// <summary>The name of the bone under <c>player_view/export_skeleton</c> of the vanilla body.</summary>
    public string Vanilla { get; }

    public string? MixamoAim { get; }

    public string? VanillaAim { get; }

    /// <summary>
    /// True for the bones of the spine and the neck. The vanilla spine is a chain that starts at the head, so the stretch between two of its joints is turned by the upper bone, while the model's stretch between the
    /// same two joints is turned by the lower one: a pivot to pivot copy of the rotations lets the two skeletons differ more and more up the chain when the spine is bent (sitting). These bones are turned
    /// on from the copied rotation so that their stretch points where the vanilla stretch between the same two joints points.
    /// </summary>
    public bool FollowsSegment { get; }
}

/// <summary>A pose in the frame of the vanilla body (<c>player_view</c>: feet on the floor at y = 0, the body facing +Z): metres and a rotation, in the game's space.</summary>
public readonly struct PlayerModelBind
{
    public PlayerModelBind(Vector3 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    public Vector3 Position { get; }

    public Quaternion Rotation { get; }
}

/// <summary>
/// What the pose copy needs for one bone: every frame the world rotation of the model's bone is <c>vanilla.rotation * K</c>. K = inverse(Wv) * Qfix * Wm, with Wv the world rotation of the vanilla bone at bind, Wm the
/// one of the model's bone at rest and Qfix the shortest rotation that turns the model's bone (at rest) to point where the vanilla bone points (at bind).
/// </summary>
public sealed class PlayerModelBoneConstant
{
    public PlayerModelBoneConstant(int mixamoNode, string mixamo, string vanilla, Quaternion k, float fixDegrees, int mixamoAimNode, string? vanillaAim, Vector3 localAim, bool followsSegment)
    {
        MixamoNode = mixamoNode;
        Mixamo = mixamo;
        Vanilla = vanilla;
        K = k;
        FixDegrees = fixDegrees;
        MixamoAimNode = mixamoAimNode;
        VanillaAim = vanillaAim;
        LocalAim = localAim;
        FollowsSegment = followsSegment;
    }

    /// <summary>The index of the node in <see cref="GlbModel.Nodes" />.</summary>
    public int MixamoNode { get; }

    public string Mixamo { get; }

    public string Vanilla { get; }

    public Quaternion K { get; }

    /// <summary>The node (index in <see cref="GlbModel.Nodes" />) and the vanilla bone that this bone points to, -1 and null for none (for the checks of the live probe: how far the two point apart).</summary>
    public int MixamoAimNode { get; }

    public string? VanillaAim { get; }

    /// <summary>The direction from this bone to its aim bone in the frame of the bone itself (at rest); the frame it is turned in when <see cref="FollowsSegment" />.</summary>
    public Vector3 LocalAim { get; }

    /// <summary>Whether the bone is turned on from the copied rotation until its stretch to the aim bone points where the vanilla stretch points (see <see cref="PlayerModelBonePair.FollowsSegment" />).</summary>
    public bool FollowsSegment { get; }

    /// <summary>The angle of Qfix: how far the model's rest pose is from the vanilla one at this bone (for the log, and for the test that notices a bone mapped to the wrong side).</summary>
    public float FixDegrees { get; }
}

/// <summary>Which of the two scale factors sizes the model: the one of the hips (the feet stay on the floor) or the one of the head (the head lands where the vanilla head is).</summary>
public enum PlayerModelScaleSource
{
    Hips,
    Head
}

/// <summary>The result of <see cref="PlayerModelRig.Calibrate" />: everything about a model and the vanilla body that does not change from frame to frame.</summary>
public sealed class PlayerModelCalibration
{
    public PlayerModelCalibration(IReadOnlyList<PlayerModelBoneConstant> bones, IReadOnlyList<string> missing, float hipsFactor, float headFactor, float scale, int hipsNode, Vector3 hipsRest,
                                  Vector3 vanillaHipsBind, int attachHandNode, Vector3 attachPosition, Quaternion attachRotation)
    {
        Bones = bones;
        Missing = missing;
        HipsFactor = hipsFactor;
        HeadFactor = headFactor;
        Scale = scale;
        HipsNode = hipsNode;
        HipsRest = hipsRest;
        VanillaHipsBind = vanillaHipsBind;
        AttachHandNode = attachHandNode;
        AttachPosition = attachPosition;
        AttachRotation = attachRotation;
    }

    /// <summary>The bones that follow a vanilla bone, parents before children.</summary>
    public IReadOnlyList<PlayerModelBoneConstant> Bones { get; }

    /// <summary>What was asked for and not found (a node missing in the file or a bone missing in the vanilla rig): those bones stay at rest.</summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>The height of the vanilla hips at bind over the height of the model's hips at rest.</summary>
    public float HipsFactor { get; }

    /// <summary>The height of the vanilla head bone at bind over the height of the model's head bone at rest.</summary>
    public float HeadFactor { get; }

    /// <summary>The factor the model is sized with (<see cref="HipsFactor" /> unless the head was asked for).</summary>
    public float Scale { get; }

    /// <summary>True when the head factor is more than <see cref="PlayerModelRig.FACTOR_TOLERANCE" /> away from the hips factor: the two sizings give a visibly different model.</summary>
    public bool FactorsDiffer => PlayerModelRig.FactorsDiffer(HipsFactor, HeadFactor);

    public int HipsNode { get; }

    /// <summary>The rest position of the model's hips relative to the model root, in the model's own units (the root is not scaled yet).</summary>
    public Vector3 HipsRest { get; }

    /// <summary>The bind position of the vanilla hips in the frame of the body.</summary>
    public Vector3 VanillaHipsBind { get; }

    /// <summary>The node of the model's right hand, which carries the attach point of held items; -1 when there is none.</summary>
    public int AttachHandNode { get; }

    /// <summary>Where the held items attach, in the frame of the model's right hand and in the model's own units.</summary>
    public Vector3 AttachPosition { get; }

    public Quaternion AttachRotation { get; }
}

/// <summary>
/// The game-free part of the pose copy (docs/coop/PLAYER_MODELS.md): which bone of the model follows which bone of the vanilla body, and the arithmetic that lines the two rest poses up. The vanilla body is a rig
/// of its own (the head is the root of its chain), driven by an Animator and Final IK; the model is a Mixamo rig in an A-pose. Every frame, for a mapped bone, the world rotation of the model's bone is the
/// world rotation of the vanilla bone times a constant (<see cref="PlayerModelBoneConstant.K" />), which is worked out here from the two rest poses; the code of the game only multiplies.
/// </summary>
public static class PlayerModelRig
{
    public const string MIXAMO_PREFIX = "mixamorig:";

    /// <summary>The two scale factors may differ by this much (a fraction) before the model is shown with both sizings for a person to choose.</summary>
    public const float FACTOR_TOLERANCE = 0.08f;

    public const string HIPS = "Hips";

    public const string HEAD = "Head";

    public const string RIGHT_HAND = "RightHand";

    public const string VANILLA_HIPS = "hips";

    public const string VANILLA_HEAD = "head_rig";

    /// <summary>The camera bone of the vanilla body: where the diver's eyes are.</summary>
    public const string VANILLA_CAMERA = "Cam";

    public const string VANILLA_RIGHT_HAND = "hand_R";

    /// <summary>The empty transform of the vanilla right hand that held items hang on (<c>RemotePlayer.ItemAttachPoint</c>).</summary>
    public const string VANILLA_ATTACH = "attach1";

    private static PlayerModelBonePair P(string mixamo, string vanilla, string? mixamoAim, string? vanillaAim, bool followsSegment = false) => new(mixamo, vanilla, mixamoAim, vanillaAim, followsSegment);

    /// <summary>
    /// The bones of the model that follow the vanilla body, parents before children. The fingers, the end bones and the two helper bones of the head are not here: they stay at their rest pose in the frame
    /// of their parent. The vanilla spine is chest, spine_3, spine_2, spine_1, hips (a chain that starts at the head, so the hips are its deepest bone); the clavicles hang on the chest.
    /// </summary>
    public static IReadOnlyList<PlayerModelBonePair> Pairs { get; } =
    [
        P("Hips", "hips", "Spine", "spine_1", true),
        P("Spine", "spine_1", "Spine1", "spine_2", true),
        P("Spine1", "spine_2", "Spine2", "spine_3", true),
        P("Spine2", "spine_3", "Neck", "chest", true),
        P("Neck", "neck", "Head", "head_rig", true),
        P("Head", "head_rig", null, null),
        P("LeftShoulder", "clav_L", "LeftArm", "shoulder_L"),
        P("LeftArm", "shoulder_L", "LeftForeArm", "elbow_L"),
        P("LeftForeArm", "elbow_L", "LeftHand", "hand_L"),
        P("LeftHand", "hand_L", "LeftHandMiddle1", "hand_L_midl_base"),
        P("RightShoulder", "clav_R", "RightArm", "shoulder_R"),
        P("RightArm", "shoulder_R", "RightForeArm", "elbow_R"),
        P("RightForeArm", "elbow_R", "RightHand", "hand_R"),
        P("RightHand", "hand_R", "RightHandMiddle1", "hand_R_midl_base"),
        P("LeftUpLeg", "thigh_L", "LeftLeg", "calf_L"),
        P("LeftLeg", "calf_L", "LeftFoot", "ankle_L"),
        P("LeftFoot", "ankle_L", "LeftToeBase", "toe_L_rig"),
        P("LeftToeBase", "toe_L_rig", "LeftToe_End", "Flipper1_L"),
        P("RightUpLeg", "thigh_R", "RightLeg", "calf_R"),
        P("RightLeg", "calf_R", "RightFoot", "ankle_R"),
        P("RightFoot", "ankle_R", "RightToeBase", "toe_R"),
        P("RightToeBase", "toe_R", "RightToe_End", "Flipper1_R")
    ];

    /// <summary>The names of the vanilla bones the calibration needs the bind pose of (the bones of <see cref="Pairs" />, their aim bones, the hips and the head).</summary>
    public static IReadOnlyCollection<string> VanillaBoneNames
    {
        get
        {
            HashSet<string> names = [VANILLA_HIPS, VANILLA_HEAD, VANILLA_RIGHT_HAND];
            foreach (PlayerModelBonePair pair in Pairs)
            {
                names.Add(pair.Vanilla);
                if (pair.VanillaAim != null)
                {
                    names.Add(pair.VanillaAim);
                }
            }
            return names;
        }
    }

    /// <summary>The rotation that turns the direction <paramref name="from" /> onto <paramref name="to" /> by the shortest way (nothing for the same direction, a half turn for opposite ones).</summary>
    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() < 1e-12f || to.LengthSquared() < 1e-12f)
        {
            return Quaternion.Identity;
        }
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float dot = Vector3.Dot(from, to);
        if (dot > 0.999999f)
        {
            return Quaternion.Identity;
        }
        if (dot < -0.999999f)
        {
            Vector3 axis = Vector3.Cross(Vector3.UnitX, from);
            if (axis.LengthSquared() < 1e-6f)
            {
                axis = Vector3.Cross(Vector3.UnitY, from);
            }
            axis = Vector3.Normalize(axis);
            return new Quaternion(axis.X, axis.Y, axis.Z, 0f);
        }
        Vector3 cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross.X, cross.Y, cross.Z, 1f + dot));
    }

    /// <summary>The index of the node of the model with this name without the exporter's "mixamorig:" in front of it, -1 when there is none.</summary>
    public static int FindNode(GlbModel glb, string name) => glb.FindNode($"{MIXAMO_PREFIX}{name}");

    /// <summary>The angle of a rotation in degrees (0 to 180).</summary>
    public static float AngleDegrees(Quaternion rotation)
    {
        float w = Math.Min(1f, Math.Abs(rotation.W));
        return 2f * (float)Math.Acos(w) * 180f / (float)Math.PI;
    }

    /// <summary>The constant K of one bone: <c>inverse(Wv) * Qfix * Wm</c> (see <see cref="PlayerModelBoneConstant" />).</summary>
    public static Quaternion Constant(Quaternion vanillaBindWorld, Quaternion mixamoRestWorld, Quaternion fix)
    {
        return Quaternion.Normalize(Quaternion.Inverse(vanillaBindWorld) * fix * mixamoRestWorld);
    }

    /// <summary>Whether the two scale factors differ by more than <see cref="FACTOR_TOLERANCE" />, as a fraction of the first.</summary>
    public static bool FactorsDiffer(float hipsFactor, float headFactor)
    {
        return hipsFactor > 0f && Math.Abs(headFactor / hipsFactor - 1f) > FACTOR_TOLERANCE;
    }

    /// <summary>
    /// The rest pose of every node of the model in the game's space relative to the model root: the glTF rest pose (translation and rotation of each node, composed down the tree; a scale other than 1 is
    /// not applied) mirrored with <see cref="GlbSpace" />.
    /// </summary>
    public static (Vector3[] Positions, Quaternion[] Rotations) MixamoRest(GlbModel glb)
    {
        int count = glb.Nodes.Count;
        Vector3[] positions = new Vector3[count];
        Quaternion[] rotations = new Quaternion[count];
        bool[] done = new bool[count];

        void Resolve(int index)
        {
            if (done[index])
            {
                return;
            }
            GlbNode node = glb.Nodes[index];
            Vector3 translation = new(node.Translation[0], node.Translation[1], node.Translation[2]);
            Quaternion rotation = new(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3]);
            if (node.Parent >= 0)
            {
                Resolve(node.Parent);
                positions[index] = positions[node.Parent] + Vector3.Transform(translation, rotations[node.Parent]);
                rotations[index] = rotations[node.Parent] * rotation;
            }
            else
            {
                positions[index] = translation;
                rotations[index] = rotation;
            }
            done[index] = true;
        }

        for (int i = 0; i < count; i++)
        {
            Resolve(i);
        }
        for (int i = 0; i < count; i++)
        {
            GlbSpace.Vector(positions[i].X, positions[i].Y, positions[i].Z, out float x, out float y, out float z);
            positions[i] = new Vector3(x, y, z);
            GlbSpace.Rotation(rotations[i].X, rotations[i].Y, rotations[i].Z, rotations[i].W, out float rx, out float ry, out float rz, out float rw);
            rotations[i] = Quaternion.Normalize(new Quaternion(rx, ry, rz, rw));
        }
        return (positions, rotations);
    }

    /// <summary>
    /// Lines the model's rest pose up with the bind pose of the vanilla body. <paramref name="vanillaBind" /> holds the bind pose of every vanilla bone by name (<see cref="VanillaBoneNames" />), in the frame of
    /// <c>player_view</c>; <paramref name="vanillaAttachPosition" /> and <paramref name="vanillaAttachRotation" /> are the pose of the item attach point (<see cref="VANILLA_ATTACH" />) in the frame of the vanilla
    /// right hand, which is a fixed child of it. A pair whose bones are not found is left out and named in <see cref="PlayerModelCalibration.Missing" />.
    /// </summary>
    public static PlayerModelCalibration Calibrate(GlbModel glb, IReadOnlyDictionary<string, PlayerModelBind> vanillaBind, Vector3 vanillaAttachPosition, Quaternion vanillaAttachRotation,
                                                   PlayerModelScaleSource scaleSource = PlayerModelScaleSource.Hips)
    {
        return Calibrate(PlayerModelRest.FromGlb(glb), vanillaBind, vanillaAttachPosition, vanillaAttachRotation, scaleSource);
    }

    /// <summary>
    /// The same for a rest pose that is not (or not yet) a file's: <paramref name="boneNames" /> says how the rig spells its bones (the Mixamo names with the exporter's prefix when none is given), and
    /// <paramref name="scaleOverride" /> is the factor that sizes the model when it is not to be worked out from the hips or the head, and <paramref name="eyeHeight" /> the height of the model's eyes in its own units
    /// (the head factor then puts them where the diver's camera is).
    /// </summary>
    public static PlayerModelCalibration Calibrate(PlayerModelRest rest, IReadOnlyDictionary<string, PlayerModelBind> vanillaBind, Vector3 vanillaAttachPosition, Quaternion vanillaAttachRotation,
                                                   PlayerModelScaleSource scaleSource = PlayerModelScaleSource.Hips, PlayerModelBoneNames? boneNames = null, float? scaleOverride = null, float? eyeHeight = null)
    {
        boneNames ??= PlayerModelBoneNames.Mixamo;
        List<PlayerModelBoneConstant> bones = [];
        List<string> missing = [];
        Dictionary<string, Quaternion> fixes = new();

        foreach (PlayerModelBonePair pair in Pairs)
        {
            int node = rest.Find(boneNames, pair.Mixamo);
            if (node < 0)
            {
                missing.Add($"model node {pair.Mixamo}");
                continue;
            }
            if (!vanillaBind.TryGetValue(pair.Vanilla, out PlayerModelBind bind))
            {
                missing.Add($"vanilla bone {pair.Vanilla}");
                continue;
            }

            Quaternion fix = Quaternion.Identity;
            if (pair.MixamoAim != null && pair.VanillaAim != null)
            {
                int aimNode = rest.Find(boneNames, pair.MixamoAim);
                if (aimNode >= 0 && vanillaBind.TryGetValue(pair.VanillaAim, out PlayerModelBind aimBind))
                {
                    fix = FromTo(rest.Position(aimNode) - rest.Position(node), aimBind.Position - bind.Position);
                }
                else
                {
                    missing.Add($"aim of {pair.Mixamo} ({pair.MixamoAim} / {pair.VanillaAim}): the bone is left unaligned");
                }
            }
            fixes[pair.Mixamo] = fix;
            int aimIndex = pair.MixamoAim != null ? rest.Find(boneNames, pair.MixamoAim) : -1;
            Vector3 localAim = Vector3.Zero;
            if (aimIndex >= 0)
            {
                Vector3 toAim = rest.Position(aimIndex) - rest.Position(node);
                if (toAim.LengthSquared() > 1e-12f)
                {
                    localAim = Vector3.Transform(Vector3.Normalize(toAim), Quaternion.Inverse(rest.Rotation(node)));
                }
            }
            bool followsSegment = pair.FollowsSegment && aimIndex >= 0 && localAim != Vector3.Zero && vanillaBind.ContainsKey(pair.VanillaAim!);
            bones.Add(new PlayerModelBoneConstant(node, pair.Mixamo, pair.Vanilla, Constant(bind.Rotation, rest.Rotation(node), fix), AngleDegrees(fix), aimIndex, aimIndex >= 0 ? pair.VanillaAim : null, localAim, followsSegment));
        }

        int hipsNode = rest.Find(boneNames, HIPS);
        int headNode = rest.Find(boneNames, HEAD);
        float hipsFactor = 1f;
        float headFactor = 1f;
        Vector3 hipsRest = Vector3.Zero;
        Vector3 vanillaHips = Vector3.Zero;
        if (hipsNode >= 0 && rest.Position(hipsNode).Y > 0.01f && vanillaBind.TryGetValue(VANILLA_HIPS, out PlayerModelBind hipsBind))
        {
            hipsFactor = hipsBind.Position.Y / rest.Position(hipsNode).Y;
            hipsRest = rest.Position(hipsNode);
            vanillaHips = hipsBind.Position;
        }
        else
        {
            missing.Add("hips height: the model is not sized");
        }
        if (eyeHeight.HasValue && eyeHeight.Value > 0.01f && vanillaBind.TryGetValue(VANILLA_CAMERA, out PlayerModelBind cameraBind))
        {
            // the profile knows where the eyes of the model are: they are put where the eyes of the diver are
            headFactor = cameraBind.Position.Y / eyeHeight.Value;
        }
        else if (headNode >= 0 && rest.Position(headNode).Y > 0.01f && vanillaBind.TryGetValue(VANILLA_HEAD, out PlayerModelBind headBind))
        {
            headFactor = headBind.Position.Y / rest.Position(headNode).Y;
        }
        float scale = scaleOverride ?? (scaleSource == PlayerModelScaleSource.Head ? headFactor : hipsFactor);

        // the attach point of held items: in the frame of the model's right hand at the pose the hands are lined up to, with the same offset and the same orientation in the world as the vanilla one
        int handNode = rest.Find(boneNames, RIGHT_HAND);
        Vector3 attachPosition = Vector3.Zero;
        Quaternion attachRotation = Quaternion.Identity;
        if (handNode >= 0 && vanillaBind.TryGetValue(VANILLA_RIGHT_HAND, out PlayerModelBind handBind) && fixes.TryGetValue(RIGHT_HAND, out Quaternion handFix) && scale > 0f)
        {
            Quaternion handAligned = handFix * rest.Rotation(handNode);
            Quaternion toHand = Quaternion.Inverse(handAligned);
            Vector3 worldOffset = Vector3.Transform(vanillaAttachPosition, handBind.Rotation);
            attachPosition = Vector3.Transform(worldOffset, toHand) / scale;
            attachRotation = Quaternion.Normalize(toHand * handBind.Rotation * vanillaAttachRotation);
        }
        else
        {
            handNode = -1;
            missing.Add("right hand: held items stay on the vanilla hand");
        }

        return new PlayerModelCalibration(bones, missing, hipsFactor, headFactor, scale, hipsNode, hipsRest, vanillaHips, handNode, attachPosition, attachRotation);
    }
}
