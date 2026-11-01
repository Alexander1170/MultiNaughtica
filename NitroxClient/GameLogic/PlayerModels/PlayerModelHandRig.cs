using System;
using System.Collections.Generic;
using System.Text;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using UnityEngine;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// One hand of a worn model, placed where the vanilla hand is (docs/coop/PLAYER_MODELS.md, "The hands"). Every frame, after the pose copy of the body: the hand takes the turn of the vanilla hand (by its palm), the
/// two bones of the arm are solved so that the knuckles of the model's hand are where the knuckles of the vanilla hand are (<see cref="PlayerModelArmIk" />: the model's shoulder, upper arm and forearm do not
/// have to be the vanilla ones), and the fingers take the pose of the vanilla fingers, so that the hand closes round what the vanilla hand holds. It only changes bones of the model and reads bones of the vanilla
/// body; it is made from a <see cref="PlayerModelHandCalibration" />, which knows no model, so it is the same for every model. Used for the copy of the local player's own body; the remote copies can use it
/// the same way.
/// </summary>
internal sealed class PlayerModelHandRig
{
    private struct Finger
    {
        public Transform Vanilla;
        public Transform Model;
        public Quaternion K;
        public Quaternion RestLocal;
        public bool Thumb;
    }

    private readonly string side;
    private readonly bool left;
    private readonly Transform arm;
    private readonly Transform foreArm;
    private readonly Transform hand;
    private readonly Transform vanillaShoulder;
    private readonly Transform vanillaElbow;
    private readonly Transform vanillaHand;
    private readonly Transform[] knuckles;
    private readonly Transform[] vanillaKnuckles;
    private readonly Quaternion k;
    private readonly Vector3 modelPalmForward;
    private readonly System.Numerics.Vector3 foreArmAxis;
    private readonly Vector3 palmForward;
    private readonly Vector3 palmNormal;
    private readonly Vector3 palmThumb;
    private readonly Finger[] fingers;
    private readonly Vector3 foreArmRestPosition;
    private readonly Vector3 handRestPosition;
    private Quaternion fingerTurn = Quaternion.identity;
    private bool fingersApplied;
    private bool thumbsApplied;

    private PlayerModelHandRig(PlayerModelHandCalibration calibration, Transform arm, Transform foreArm, Transform hand, Transform vanillaShoulder, Transform vanillaElbow, Transform vanillaHand, Transform[] knuckles,
                               Transform[] vanillaKnuckles, Finger[] fingers)
    {
        side = calibration.Side;
        left = calibration.Side == "Left";
        this.arm = arm;
        this.foreArm = foreArm;
        this.hand = hand;
        this.vanillaShoulder = vanillaShoulder;
        this.vanillaElbow = vanillaElbow;
        this.vanillaHand = vanillaHand;
        this.knuckles = knuckles;
        this.vanillaKnuckles = vanillaKnuckles;
        this.fingers = fingers;
        foreArmRestPosition = foreArm.localPosition;
        handRestPosition = hand.localPosition;
        k = ToUnity(calibration.K);
        modelPalmForward = ToUnity(calibration.ModelPalmForward);
        foreArmAxis = calibration.ForeArmAxis;
        palmForward = ToUnity(calibration.PalmForward);
        palmNormal = ToUnity(calibration.PalmNormal);
        palmThumb = ToUnity(calibration.PalmThumb);
    }

    /// <summary>"Left" or "Right".</summary>
    public string Side => side;

    /// <summary>The number of finger bones that follow a vanilla finger.</summary>
    public int FingerCount => fingers.Length;

    /// <summary>How far the knuckles were from where they were asked to be after the last frame's solve (metres): 0 unless the arm is too short.</summary>
    public float LastShortfall { get; private set; }

    /// <summary>False when the last frame's solve could not be made (a bone with no length): the hand then only has the pose copy of the body.</summary>
    public bool LastSolved { get; private set; }

    /// <summary>The factor the arm was made longer by in the last frame (1: not at all).</summary>
    public float LastStretch { get; private set; } = 1f;

    /// <summary>Whether the last frame followed the hand target of a tool and not the vanilla hand.</summary>
    public bool UsedGripTarget { get; private set; }

    /// <summary>When it did: how far the vanilla hand was from that target (metres) and how far it was turned from it (degrees): what the hand of the vanilla body gets wrong while the player looks about.</summary>
    public float LastTargetApart { get; private set; }

    public float LastTargetAngle { get; private set; }

    /// <summary>Whether this is the left hand (the one that holds the PDA).</summary>
    public bool IsLeft => left;

    /// <summary>
    /// Makes the rig of one hand: the bones of the model (from <paramref name="nodes" />, the transforms of the file's nodes) and of the vanilla body (<paramref name="vanillaBones" />, by name) that the calibration names.
    /// Null with the reason in <paramref name="problem" /> when one of them is not there.
    /// </summary>
    public static PlayerModelHandRig Create(PlayerModelHandCalibration calibration, Transform[] nodes, Dictionary<string, Transform> vanillaBones, out string problem)
    {
        problem = null;
        Transform arm = Node(nodes, calibration.ArmNode);
        Transform foreArm = Node(nodes, calibration.ForeArmNode);
        Transform hand = Node(nodes, calibration.HandNode);
        if (!arm || !foreArm || !hand)
        {
            problem = $"{calibration.Side} arm, forearm or hand of the model is not in the scene";
            return null;
        }
        if (!vanillaBones.TryGetValue(calibration.VanillaArm, out Transform vanillaShoulder) || !vanillaBones.TryGetValue(calibration.VanillaForeArm, out Transform vanillaElbow) ||
            !vanillaBones.TryGetValue(calibration.VanillaHand, out Transform vanillaHand))
        {
            problem = $"the vanilla body has no {calibration.VanillaArm}, {calibration.VanillaForeArm} and {calibration.VanillaHand}";
            return null;
        }
        Transform[] knuckles = new Transform[calibration.KnuckleNodes.Count];
        Transform[] vanillaKnuckles = new Transform[calibration.VanillaKnuckles.Count];
        for (int i = 0; i < knuckles.Length; i++)
        {
            knuckles[i] = Node(nodes, calibration.KnuckleNodes[i]);
            if (!knuckles[i])
            {
                problem = $"a knuckle bone of the {calibration.Side} hand of the model is not in the scene";
                return null;
            }
        }
        for (int i = 0; i < vanillaKnuckles.Length; i++)
        {
            if (!vanillaBones.TryGetValue(calibration.VanillaKnuckles[i], out vanillaKnuckles[i]) || !vanillaKnuckles[i])
            {
                problem = $"the vanilla body has no {calibration.VanillaKnuckles[i]}";
                return null;
            }
        }
        List<Finger> fingerList = [];
        foreach (PlayerModelBoneConstant constant in calibration.Fingers)
        {
            Transform model = Node(nodes, constant.MixamoNode);
            if (!model || !vanillaBones.TryGetValue(constant.Vanilla, out Transform vanilla) || !vanilla)
            {
                // a finger that is not all there keeps the pose copy of the body; the others are not held back by it
                continue;
            }
            fingerList.Add(new Finger { Vanilla = vanilla, Model = model, K = ToUnity(constant.K), RestLocal = model.localRotation, Thumb = PlayerModelHands.IsThumb(constant) });
        }
        return new PlayerModelHandRig(calibration, arm, foreArm, hand, vanillaShoulder, vanillaElbow, vanillaHand, knuckles, vanillaKnuckles, fingerList.ToArray());
    }

    private static Transform Node(Transform[] nodes, int index) => index >= 0 && index < nodes.Length ? nodes[index] : null;

    /// <summary>
    /// Puts the hand where the vanilla hand is, now (after the pose copy of the body): the turn of the hand, the stretch of an arm that is too short, the solve of the arm, the fingers. <paramref name="gripTarget" /> is
    /// where the game's IK wants the vanilla hand (the hand target of the tool that this hand holds, fixed to the tool: null when the hand holds none); the hand follows it and not the vanilla hand, which is aimed about as
    /// the player looks up or down. <paramref name="pdaOpen" /> and <paramref name="camera" /> are for the pull back of the left hand while the PDA is open (<see cref="PlayerModelHandSettings.PdaPullBack" />).
    /// </summary>
    public void Apply(PlayerModelHandSettings settings, bool pdaOpen, Transform camera, Transform gripTarget)
    {
        // the bones of the arm start from their own lengths: what an arm was stretched to in the last frame is not the arm
        foreArm.localPosition = foreArmRestPosition;
        hand.localPosition = handRestPosition;
        if (!settings.Enabled)
        {
            RestoreFingers();
            LastSolved = false;
            LastShortfall = 0f;
            LastStretch = 1f;
            UsedGripTarget = false;
            return;
        }

        // where the hand of the vanilla body is, and where its knuckles are in the frame of its hand; the hand follows the hand target of a tool when it holds one
        Vector3 vanillaPosition = vanillaHand.position;
        Quaternion vanillaRotation = vanillaHand.rotation;
        Vector3 knuckleOffset = Quaternion.Inverse(vanillaRotation) * (Middle(vanillaKnuckles) - vanillaPosition);
        Quaternion followedTurn = Quaternion.identity;
        UsedGripTarget = false;
        LastTargetApart = 0f;
        LastTargetAngle = 0f;
        if (gripTarget && settings.ToolTargets)
        {
            Quaternion targetRotation = gripTarget.rotation;
            LastTargetApart = Vector3.Distance(vanillaPosition, gripTarget.position);
            LastTargetAngle = Quaternion.Angle(vanillaRotation, targetRotation);
            followedTurn = targetRotation * Quaternion.Inverse(vanillaRotation);
            vanillaPosition = gripTarget.position;
            vanillaRotation = targetRotation;
            UsedGripTarget = true;
        }
        fingerTurn = followedTurn;

        // the hand is turned like the vanilla hand, by its palm; the knuckles are where that turn puts them
        Quaternion handRotation = vanillaRotation * k;
        if (settings.RollTrimDegrees != 0f)
        {
            handRotation *= Quaternion.AngleAxis(settings.RollTrimDegrees, modelPalmForward);
        }
        hand.rotation = handRotation;

        Vector3 vanillaAnchor = vanillaPosition + (vanillaRotation * knuckleOffset);
        if (settings.AnchorForward != 0f || settings.AnchorPalm != 0f || settings.AnchorThumb != 0f)
        {
            vanillaAnchor += vanillaRotation * ((palmForward * settings.AnchorForward) + (palmNormal * settings.AnchorPalm) + (palmThumb * settings.AnchorThumb));
        }
        if (left && pdaOpen && settings.PdaPullBack != 0f && camera)
        {
            // the left hand holds the PDA: a glove too big for the vanilla grip comes through it, so the hand is brought toward the camera, away from what the PDA draws
            Vector3 toCamera = camera.position - vanillaAnchor;
            if (toCamera.sqrMagnitude > 1e-6f)
            {
                vanillaAnchor += toCamera.normalized * settings.PdaPullBack;
            }
        }

        // an arm that is too short for the grip is made longer (its two joints are moved out along their bones), by as much as the grip needs and the settings allow
        Vector3 shoulder = arm.position;
        LastStretch = 1f;
        if (settings.MaxStretch > 1f)
        {
            Vector3 elbowAtRest = foreArm.position;
            Vector3 wristAtRest = hand.position;
            Vector3 wantedWrist = vanillaAnchor - (Middle(knuckles) - wristAtRest);
            LastStretch = PlayerModelArmIk.StretchFor(Vector3.Distance(shoulder, elbowAtRest), Vector3.Distance(elbowAtRest, wristAtRest), Vector3.Distance(shoulder, wantedWrist), settings.MaxStretch);
            if (LastStretch > 1.0005f)
            {
                foreArm.localPosition = foreArmRestPosition * LastStretch;
                hand.localPosition = handRestPosition * LastStretch;
            }
            else
            {
                LastStretch = 1f;
            }
        }

        Vector3 elbow = foreArm.position;
        Vector3 wrist = hand.position;
        ArmState state = new()
        {
            ShoulderPosition = ToNumerics(shoulder),
            ElbowPosition = ToNumerics(elbow),
            WristPosition = ToNumerics(wrist),
            ArmRotation = ToNumerics(arm.rotation),
            ForeArmRotation = ToNumerics(foreArm.rotation),
            KnuckleOffsetFromWrist = ToNumerics(Middle(knuckles) - wrist)
        };
        ArmTarget target = new()
        {
            Anchor = ToNumerics(vanillaAnchor),
            HandRotation = ToNumerics(handRotation),
            Pole = ToNumerics(elbow - shoulder),
            FallbackPole = ToNumerics(vanillaElbow.position - vanillaShoulder.position),
            ForeArmAxis = foreArmAxis,
            TwistShare = settings.TwistShare
        };
        ArmResult result = PlayerModelArmIk.Place(state, target);
        LastSolved = result.Solved;
        LastShortfall = result.Shortfall;
        if (result.Solved)
        {
            // one after the other: a bone's rotation is set in the world and what hangs on it goes along, so the next one is set over it
            arm.rotation = ToUnity(result.ArmRotation);
            foreArm.rotation = ToUnity(result.ForeArmRotation);
        }
        hand.rotation = handRotation;

        ApplyFingers(settings);
    }

    /// <summary>An emote plays and the clip turns the whole model: the arm is not stretched (it is the clip's), and the fingers are the clip's too.</summary>
    public void Release()
    {
        foreArm.localPosition = foreArmRestPosition;
        hand.localPosition = handRestPosition;
        LastStretch = 1f;
        UsedGripTarget = false;
    }

    private void ApplyFingers(PlayerModelHandSettings settings)
    {
        bool fingersOn = settings.Fingers;
        bool thumbsOn = settings.Fingers && settings.Thumbs;
        if (!fingersOn)
        {
            RestoreFingers();
            return;
        }
        if (!thumbsOn && thumbsApplied)
        {
            for (int i = 0; i < fingers.Length; i++)
            {
                if (fingers[i].Thumb && fingers[i].Model)
                {
                    fingers[i].Model.localRotation = fingers[i].RestLocal;
                }
            }
        }
        for (int i = 0; i < fingers.Length; i++)
        {
            if (!fingers[i].Model || !fingers[i].Vanilla || (fingers[i].Thumb && !thumbsOn))
            {
                continue;
            }
            fingers[i].Model.rotation = fingerTurn * fingers[i].Vanilla.rotation * fingers[i].K;
        }
        fingersApplied = true;
        thumbsApplied = thumbsOn;
    }

    /// <summary>The fingers go back to the rest pose of the file (the switch is off, or the hands are): what the pose copy of the body never turns stays where the file has it.</summary>
    private void RestoreFingers()
    {
        if (!fingersApplied)
        {
            return;
        }
        for (int i = 0; i < fingers.Length; i++)
        {
            if (fingers[i].Model)
            {
                fingers[i].Model.localRotation = fingers[i].RestLocal;
            }
        }
        fingersApplied = false;
        thumbsApplied = false;
    }

    /// <summary>
    /// Where the model's knuckles are against the vanilla knuckles right now (the Debug command <c>vufp measure</c>, run at the end of a frame after the solve): the distance, and the offset in the frame of the
    /// vanilla palm (along the fingers, out of the palm, toward the thumb), which is what the anchor settings correct; how far the palm faces from the vanilla palm; the last solve.
    /// </summary>
    public string Describe()
    {
        Vector3 vanillaAnchor = Middle(vanillaKnuckles);
        Vector3 modelAnchor = Middle(knuckles);
        Vector3 apart = modelAnchor - vanillaAnchor;
        Quaternion frame = vanillaHand.rotation;
        Vector3 forward = frame * palmForward;
        Vector3 normal = frame * palmNormal;
        Vector3 thumb = frame * palmThumb;
        // the row of the knuckles across the fingers: the two palms face the same way when the two rows lie the same way
        Vector3 modelRow = knuckles.Length > 3 ? knuckles[0].position - knuckles[3].position : Vector3.zero;
        Vector3 vanillaRow = vanillaKnuckles.Length > 3 ? vanillaKnuckles[0].position - vanillaKnuckles[3].position : Vector3.zero;
        float palmApart = modelRow.sqrMagnitude > 1e-8f && vanillaRow.sqrMagnitude > 1e-8f
            ? Vector3.Angle(Vector3.ProjectOnPlane(modelRow, forward), Vector3.ProjectOnPlane(vanillaRow, forward))
            : -1f;
        StringBuilder text = new();
        text.Append($"{side}: knucklesApart={apart.magnitude:F3}m (forward {Vector3.Dot(apart, forward):F3}, palm {Vector3.Dot(apart, normal):F3}, thumb {Vector3.Dot(apart, thumb):F3}) palmApart={palmApart:F1}deg ");
        text.Append($"solved={LastSolved} shortfall={LastShortfall:F3}m stretch={LastStretch:F3} fingers={fingers.Length} handApart={Vector3.Distance(hand.position, vanillaHand.position):F3}m ");
        text.Append($"toolTarget={UsedGripTarget} (the vanilla hand is {LastTargetApart:F3}m and {LastTargetAngle:F1}deg from it)");
        return text.ToString();
    }

    private static Vector3 Middle(Transform[] bones)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < bones.Length; i++)
        {
            sum += bones[i].position;
        }
        return bones.Length > 0 ? sum / bones.Length : sum;
    }

    private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.x, v.y, v.z);

    private static System.Numerics.Quaternion ToNumerics(Quaternion q) => new(q.x, q.y, q.z, q.w);

    private static Vector3 ToUnity(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Quaternion ToUnity(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
}
