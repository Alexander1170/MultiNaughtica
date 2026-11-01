using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>What the hands of a model do beyond the pose copy of the body: whether they are placed at all, whether the fingers copy the vanilla fingers, and the small corrections for a model whose hand does not sit right.</summary>
public sealed class PlayerModelHandSettings
{
    /// <summary>The hands of the model are put where the vanilla hands are (the arm solve and the roll of the hand): off, they only have the pose copy of the body.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The fingers take the pose of the vanilla fingers (a closed grip round a tool).</summary>
    public bool Fingers { get; set; } = true;

    /// <summary>
    /// The hand of a held tool goes where the game's IK wants the vanilla hand (the hand target of the tool, which is fixed to the tool) and not where the vanilla hand happens to be: the vanilla hand is aimed about as the
    /// player looks up or down and a glove that follows it slides round the tool. Off: the hand follows the vanilla hand, as it did.
    /// </summary>
    public bool ToolTargets { get; set; } = true;

    /// <summary>The most that an arm is made longer, as a factor of its length, to reach a grip that the arm is too short for (a model with shorter arms than the diver, or lower shoulders): 1 never stretches.</summary>
    public float MaxStretch { get; set; } = DEFAULT_MAX_STRETCH;

    /// <summary>The longest an arm is made (1.2 times its length) unless the profile or a Debug command says another number.</summary>
    public const float DEFAULT_MAX_STRETCH = 1.2f;

    /// <summary>The thumbs take the pose of the vanilla thumbs too (the thumb of a model rests at a very different angle from the vanilla one: the rest of the fingers can be right and the thumb not).</summary>
    public bool Thumbs { get; set; } = true;

    /// <summary>How far the left hand is pulled toward the camera while the PDA is open (metres): a glove that is too big for the vanilla grip pokes through the PDA.</summary>
    public float PdaPullBack { get; set; }

    /// <summary>Where the knuckle anchor sits relative to the vanilla knuckles, in the frame of the vanilla palm (metres): along the fingers, out of the palm, toward the thumb. The same for both hands.</summary>
    public float AnchorForward { get; set; }

    public float AnchorPalm { get; set; }

    public float AnchorThumb { get; set; }

    /// <summary>The model's hand is rolled on by this many degrees about its fingers, on top of what the calibration found.</summary>
    public float RollTrimDegrees { get; set; }

    /// <summary>The share of the twist of the wrist that the forearm takes (<see cref="PlayerModelArmIk.DEFAULT_TWIST_SHARE" />).</summary>
    public float TwistShare { get; set; } = PlayerModelArmIk.DEFAULT_TWIST_SHARE;

    /// <summary>A copy that can be changed without changing this one (every worn model has settings of its own, which start as the profile's).</summary>
    public PlayerModelHandSettings Copy()
    {
        return (PlayerModelHandSettings)MemberwiseClone();
    }
}

/// <summary>One hand of a model lined up with one hand of the vanilla body: which bones, how the hand is turned (the palm, not only the direction of the fingers), where its grip is, and the fingers.</summary>
public sealed class PlayerModelHandCalibration
{
    public PlayerModelHandCalibration(string side, int armNode, int foreArmNode, int handNode, string vanillaArm, string vanillaForeArm, string vanillaHand, Quaternion k, float rollDegrees, float fixDegrees,
                                      Vector3 foreArmAxis, Vector3 modelPalmForward, IReadOnlyList<int> knuckleNodes, IReadOnlyList<string> vanillaKnuckles, Vector3 palmForward, Vector3 palmNormal, Vector3 palmThumb,
                                      IReadOnlyList<PlayerModelBoneConstant> fingers, IReadOnlyList<string> missing)
    {
        Side = side;
        ArmNode = armNode;
        ForeArmNode = foreArmNode;
        HandNode = handNode;
        VanillaArm = vanillaArm;
        VanillaForeArm = vanillaForeArm;
        VanillaHand = vanillaHand;
        K = k;
        RollDegrees = rollDegrees;
        FixDegrees = fixDegrees;
        ForeArmAxis = foreArmAxis;
        ModelPalmForward = modelPalmForward;
        KnuckleNodes = knuckleNodes;
        VanillaKnuckles = vanillaKnuckles;
        PalmForward = palmForward;
        PalmNormal = palmNormal;
        PalmThumb = palmThumb;
        Fingers = fingers;
        Missing = missing;
    }

    /// <summary>"Left" or "Right".</summary>
    public string Side { get; }

    /// <summary>The nodes (indices into the file's nodes) of the upper arm, the forearm and the hand of the model.</summary>
    public int ArmNode { get; }

    public int ForeArmNode { get; }

    public int HandNode { get; }

    /// <summary>The bones of the vanilla body they follow: <c>shoulder_R</c> (the upper arm), <c>elbow_R</c> (the forearm), <c>hand_R</c>.</summary>
    public string VanillaArm { get; }

    public string VanillaForeArm { get; }

    public string VanillaHand { get; }

    /// <summary>
    /// The turn of the model's hand: its world rotation is the world rotation of the vanilla hand times this. Like <see cref="PlayerModelBoneConstant.K" />, but the two rest poses are lined up by the palm (the
    /// direction of the fingers and the way the palm faces), not by the direction of the fingers alone.
    /// </summary>
    public Quaternion K { get; }

    /// <summary>How far the palm of the model is rolled about its fingers from the vanilla palm once the fingers point the same way (what the direction alone would have left): for the log and the tests.</summary>
    public float RollDegrees { get; }

    /// <summary>The whole turn from the rest pose of the model's hand to the vanilla hand at bind, in degrees.</summary>
    public float FixDegrees { get; }

    /// <summary>The direction from the forearm bone to the hand bone in the frame of the forearm bone at rest: the axis the forearm rolls about.</summary>
    public Vector3 ForeArmAxis { get; }

    /// <summary>The direction of the fingers in the frame of the model's hand bone at rest: the axis a roll trim turns the hand about.</summary>
    public Vector3 ModelPalmForward { get; }

    /// <summary>The nodes of the four knuckle bones of the model (index, middle, ring, little finger): their middle is where the model's hand grips.</summary>
    public IReadOnlyList<int> KnuckleNodes { get; }

    /// <summary>The knuckle bones of the vanilla hand (<c>hand_R_point_base</c>, <c>midl</c>, <c>ring</c>, <c>pinky</c>) whose middle is the vanilla hand's grip.</summary>
    public IReadOnlyList<string> VanillaKnuckles { get; }

    /// <summary>The palm of the vanilla hand in the frame of the vanilla hand bone: along the fingers, out of the palm, toward the thumb (unit vectors, at right angles to one another).</summary>
    public Vector3 PalmForward { get; }

    public Vector3 PalmNormal { get; }

    public Vector3 PalmThumb { get; }

    /// <summary>The finger bones of the model, joint by joint (three per finger, the thumb last), with the vanilla bone each one follows; a finger whose bones are not all there is left out and named in <see cref="Missing" />.</summary>
    public IReadOnlyList<PlayerModelBoneConstant> Fingers { get; }

    public IReadOnlyList<string> Missing { get; }
}

/// <summary>The two hands of a model, lined up with the two hands of the vanilla body (<see cref="PlayerModelHands.Calibrate" />); a hand that could not be is null and named in <see cref="Missing" />.</summary>
public sealed class PlayerModelHandsCalibration
{
    public PlayerModelHandsCalibration(PlayerModelHandCalibration? left, PlayerModelHandCalibration? right, IReadOnlyList<string> missing)
    {
        Left = left;
        Right = right;
        Missing = missing;
    }

    public PlayerModelHandCalibration? Left { get; }

    public PlayerModelHandCalibration? Right { get; }

    /// <summary>What was asked for and not found, for the log: a hand or a finger whose bones are not in the file or not in the vanilla body is left at the pose copy of the body.</summary>
    public IReadOnlyList<string> Missing { get; }

    public int HandCount => (Left != null ? 1 : 0) + (Right != null ? 1 : 0);
}

/// <summary>
/// The calibration of the hands (docs/coop/PLAYER_MODELS.md, "The hands"), from the rest pose of the model's skeleton and the bind pose of the vanilla body, with no knowledge of any model: the hand is turned by its
/// palm (the fingers' direction and the way the knuckle row lies, which tell the roll that the direction alone leaves open), it grips at the middle of its four knuckle bones, and every finger bone follows the vanilla
/// finger bone that is next to the same joint, turned so that it points where the vanilla bone points at bind and rolls the same way against the palm. Plain numbers (System.Numerics), tested without the game.
/// </summary>
public static class PlayerModelHands
{
    /// <summary>The fingers, with the name the vanilla body gives them in its bones (<c>hand_R_point_base</c>: the index finger is "point") and the name the Mixamo rig gives them; the thumb is last.</summary>
    private static readonly (string Vanilla, string Mixamo)[] fingerNames =
    [
        ("point", "Index"),
        ("midl", "Middle"),
        ("ring", "Ring"),
        ("pinky", "Pinky"),
        ("thumb", "Thumb")
    ];

    /// <summary>The joints of a finger the model follows the vanilla body at: three (the knuckle, the middle joint and the last one). The fourth bone of a Mixamo finger is its tip, which no mesh follows.</summary>
    public const int JOINTS_PER_FINGER = 3;

    /// <summary>The number of finger bones of one hand that are followed when the model has them all.</summary>
    public const int FINGER_BONES_PER_HAND = 15;

    private const float MIN_LENGTH = 1e-4f;

    /// <summary>
    /// Lines the hands of the model up with the hands of the vanilla body. <paramref name="vanillaBind" /> is the bind pose of the vanilla bones by name, in the frame of <c>player_view</c> (the same dictionary that
    /// <see cref="PlayerModelRig.Calibrate" /> takes); the model is its rest pose and the names of its bones.
    /// </summary>
    public static PlayerModelHandsCalibration Calibrate(PlayerModelRest rest, IReadOnlyDictionary<string, PlayerModelBind> vanillaBind, PlayerModelBoneNames? boneNames = null)
    {
        boneNames ??= PlayerModelBoneNames.Mixamo;
        List<string> missing = [];
        PlayerModelHandCalibration? left = CalibrateHand(rest, vanillaBind, boneNames, false, missing);
        PlayerModelHandCalibration? right = CalibrateHand(rest, vanillaBind, boneNames, true, missing);
        return new PlayerModelHandsCalibration(left, right, missing);
    }

    private static PlayerModelHandCalibration? CalibrateHand(PlayerModelRest rest, IReadOnlyDictionary<string, PlayerModelBind> bind, PlayerModelBoneNames boneNames, bool right, List<string> missing)
    {
        string side = right ? "Right" : "Left";
        string v = right ? "R" : "L";
        string vanillaArm = $"shoulder_{v}";
        string vanillaForeArm = $"elbow_{v}";
        string vanillaHand = $"hand_{v}";
        int arm = rest.Find(boneNames, $"{side}Arm");
        int foreArm = rest.Find(boneNames, $"{side}ForeArm");
        int hand = rest.Find(boneNames, $"{side}Hand");
        int index = rest.Find(boneNames, $"{side}HandIndex1");
        int middle = rest.Find(boneNames, $"{side}HandMiddle1");
        int ring = rest.Find(boneNames, $"{side}HandRing1");
        int pinky = rest.Find(boneNames, $"{side}HandPinky1");
        string[] vanillaKnuckles = [$"hand_{v}_point_base", $"hand_{v}_midl_base", $"hand_{v}_ring_base", $"hand_{v}_pinky_base"];

        List<string> lacking = [];
        if (arm < 0 || foreArm < 0 || hand < 0)
        {
            lacking.Add($"the model has no {side}Arm, {side}ForeArm and {side}Hand");
        }
        if (index < 0 || middle < 0 || ring < 0 || pinky < 0)
        {
            lacking.Add($"the model has no knuckle bones ({side}HandIndex1, Middle1, Ring1, Pinky1)");
        }
        foreach (string name in new[] { vanillaArm, vanillaForeArm, vanillaHand }.Concat(vanillaKnuckles))
        {
            if (!bind.ContainsKey(name))
            {
                lacking.Add($"the vanilla body has no {name}");
            }
        }
        if (lacking.Count > 0)
        {
            missing.Add($"{side} hand stays at the pose copy of the body: {string.Join("; ", lacking)}");
            return null;
        }

        Vector3 modelWrist = rest.Position(hand);
        Vector3 vanillaWrist = bind[vanillaHand].Position;
        if (!TryPalm(modelWrist, rest.Position(middle), rest.Position(index), rest.Position(pinky), right, out PalmAxes modelPalm)
            || !TryPalm(vanillaWrist, bind[vanillaKnuckles[1]].Position, bind[vanillaKnuckles[0]].Position, bind[vanillaKnuckles[3]].Position, right, out PalmAxes vanillaPalm))
        {
            missing.Add($"{side} hand stays at the pose copy of the body: its knuckles do not make a palm (the bones lie on one another)");
            return null;
        }

        // the hand: the direction of the fingers first, then the roll about it that puts the palm where the vanilla palm faces
        Quaternion aim = PlayerModelRig.FromTo(modelPalm.Forward, vanillaPalm.Forward);
        float roll = TwistBetween(vanillaPalm.Forward, Vector3.Transform(modelPalm.Normal, aim), vanillaPalm.Normal);
        Quaternion fix = Quaternion.Normalize(PlayerModelArmIk.AxisAngle(vanillaPalm.Forward, roll) * aim);
        Quaternion handRest = rest.Rotation(hand);
        Quaternion handBind = bind[vanillaHand].Rotation;
        Quaternion k = PlayerModelRig.Constant(handBind, handRest, fix);

        Vector3 toHand = rest.Position(hand) - rest.Position(foreArm);
        Vector3 foreArmAxis = toHand.LengthSquared() > MIN_LENGTH * MIN_LENGTH ? Vector3.Transform(Vector3.Normalize(toHand), Quaternion.Inverse(rest.Rotation(foreArm))) : Vector3.UnitY;

        List<PlayerModelBoneConstant> fingers = [];
        foreach ((string vanillaFinger, string mixamoFinger) in fingerNames)
        {
            PlayerModelBoneConstant[]? joints = CalibrateFinger(rest, bind, boneNames, side, v, vanillaFinger, mixamoFinger, modelPalm, vanillaPalm, out string? reason);
            if (joints == null)
            {
                missing.Add($"{side}Hand{mixamoFinger} stays at the pose copy of the body: {reason}");
                continue;
            }
            fingers.AddRange(joints);
        }

        Quaternion toVanillaHand = Quaternion.Inverse(handBind);
        return new PlayerModelHandCalibration(side, arm, foreArm, hand, vanillaArm, vanillaForeArm, vanillaHand, k, roll, PlayerModelRig.AngleDegrees(fix), foreArmAxis,
                                              Vector3.Transform(modelPalm.Forward, Quaternion.Inverse(handRest)), new[] { index, middle, ring, pinky }, vanillaKnuckles,
                                              Vector3.Transform(vanillaPalm.Forward, toVanillaHand), Vector3.Transform(vanillaPalm.Normal, toVanillaHand), Vector3.Transform(vanillaPalm.Thumb, toVanillaHand), fingers, Array.Empty<string>());
    }

    /// <summary>The three joints of one finger: each model bone turned to point where its vanilla bone points at bind and rolled so that it lies against the palm the way the vanilla one does. Null with the reason when a bone is lacking.</summary>
    private static PlayerModelBoneConstant[]? CalibrateFinger(PlayerModelRest rest, IReadOnlyDictionary<string, PlayerModelBind> bind, PlayerModelBoneNames boneNames, string side, string v, string vanillaFinger, string mixamoFinger,
                                                              PalmAxes modelPalm, PalmAxes vanillaPalm, out string? reason)
    {
        reason = null;
        string[] vanillaBones = [$"hand_{v}_{vanillaFinger}_base", $"hand_{v}_{vanillaFinger}_mid", FirstThatExists(bind, $"hand_{v}_{vanillaFinger}_tip_rig", $"hand_{v}_{vanillaFinger}_tip")];
        int[] nodes = new int[JOINTS_PER_FINGER + 1];
        for (int joint = 0; joint <= JOINTS_PER_FINGER; joint++)
        {
            nodes[joint] = rest.Find(boneNames, $"{side}Hand{mixamoFinger}{joint + 1}");
        }
        foreach (int node in nodes)
        {
            if (node < 0)
            {
                reason = $"the model has no {side}Hand{mixamoFinger}1 to 4";
                return null;
            }
        }
        foreach (string name in vanillaBones)
        {
            if (name.Length == 0 || !bind.ContainsKey(name))
            {
                reason = $"the vanilla body has no {(name.Length == 0 ? $"hand_{v}_{vanillaFinger}_tip" : name)}";
                return null;
            }
        }

        PlayerModelBoneConstant[] joints = new PlayerModelBoneConstant[JOINTS_PER_FINGER];
        for (int joint = 0; joint < JOINTS_PER_FINGER; joint++)
        {
            Vector3 modelDirection = rest.Position(nodes[joint + 1]) - rest.Position(nodes[joint]);
            // the bone of the vanilla finger points to the next joint; the last one has none, and goes on the way the one before it points
            Vector3 vanillaDirection = joint < JOINTS_PER_FINGER - 1
                ? bind[vanillaBones[joint + 1]].Position - bind[vanillaBones[joint]].Position
                : bind[vanillaBones[joint]].Position - bind[vanillaBones[joint - 1]].Position;
            if (modelDirection.LengthSquared() < MIN_LENGTH * MIN_LENGTH || vanillaDirection.LengthSquared() < MIN_LENGTH * MIN_LENGTH)
            {
                reason = $"a segment of the finger has no length ({side}Hand{mixamoFinger}{joint + 1})";
                return null;
            }
            Vector3 vanillaAxis = Vector3.Normalize(vanillaDirection);
            Quaternion aim = PlayerModelRig.FromTo(modelDirection, vanillaDirection);
            float roll = TwistBetween(vanillaAxis, Vector3.Transform(modelPalm.Normal, aim), vanillaPalm.Normal);
            Quaternion fix = Quaternion.Normalize(PlayerModelArmIk.AxisAngle(vanillaAxis, roll) * aim);
            Quaternion k = PlayerModelRig.Constant(bind[vanillaBones[joint]].Rotation, rest.Rotation(nodes[joint]), fix);
            joints[joint] = new PlayerModelBoneConstant(nodes[joint], $"{side}Hand{mixamoFinger}{joint + 1}", vanillaBones[joint], k, PlayerModelRig.AngleDegrees(fix), nodes[joint + 1],
                                                        joint < JOINTS_PER_FINGER - 1 ? vanillaBones[joint + 1] : null, Vector3.Zero, false);
        }
        return joints;
    }

    /// <summary>Whether the finger bone at this joint is a thumb joint: the thumbs can be switched off on their own.</summary>
    public static bool IsThumb(PlayerModelBoneConstant finger) => finger.Mixamo.Contains("HandThumb");

    private static string FirstThatExists(IReadOnlyDictionary<string, PlayerModelBind> bind, string first, string second)
    {
        if (bind.ContainsKey(first))
        {
            return first;
        }
        return bind.ContainsKey(second) ? second : "";
    }

    /// <summary>The way a palm faces: the fingers' direction, the direction out of the palm and the direction to the thumb, three unit vectors at right angles to one another.</summary>
    private readonly struct PalmAxes
    {
        public PalmAxes(Vector3 forward, Vector3 normal, Vector3 thumb)
        {
            Forward = forward;
            Normal = normal;
            Thumb = thumb;
        }

        public Vector3 Forward { get; }

        public Vector3 Normal { get; }

        public Vector3 Thumb { get; }
    }

    /// <summary>
    /// The palm from the wrist and the knuckles: the fingers point from the wrist to the middle knuckle, the knuckles lie in a row from the little finger to the index finger (which is on the thumb's side), and the
    /// palm faces at right angles to both, out of the palm (the other way round for a right hand than for a left hand, which the cross product of the two gives the same way for both). False for knuckles in a heap.
    /// </summary>
    private static bool TryPalm(Vector3 wrist, Vector3 middle, Vector3 index, Vector3 pinky, bool right, out PalmAxes axes)
    {
        axes = default;
        Vector3 forward = middle - wrist;
        Vector3 row = index - pinky;
        if (forward.LengthSquared() < MIN_LENGTH * MIN_LENGTH || row.LengthSquared() < MIN_LENGTH * MIN_LENGTH)
        {
            return false;
        }
        forward = Vector3.Normalize(forward);
        Vector3 normal = Vector3.Cross(row, forward);
        if (right)
        {
            normal = -normal;
        }
        normal -= forward * Vector3.Dot(normal, forward);
        if (normal.LengthSquared() < 1e-10f)
        {
            return false;
        }
        normal = Vector3.Normalize(normal);
        Vector3 thumb = row - (forward * Vector3.Dot(row, forward));
        thumb -= normal * Vector3.Dot(thumb, normal);
        if (thumb.LengthSquared() < 1e-10f)
        {
            return false;
        }
        axes = new PalmAxes(forward, normal, Vector3.Normalize(thumb));
        return true;
    }

    /// <summary>The angle in degrees (-180 to 180) that turns <paramref name="from" /> onto <paramref name="to" /> about <paramref name="axis" />, each seen along the axis (the part of them across it); 0 when either lies along it.</summary>
    public static float TwistBetween(Vector3 axis, Vector3 from, Vector3 to)
    {
        axis = Vector3.Normalize(axis);
        Vector3 a = from - (axis * Vector3.Dot(from, axis));
        Vector3 b = to - (axis * Vector3.Dot(to, axis));
        if (a.LengthSquared() < 1e-8f || b.LengthSquared() < 1e-8f)
        {
            return 0f;
        }
        return (float)(Math.Atan2(Vector3.Dot(axis, Vector3.Cross(a, b)), Vector3.Dot(a, b)) * 180.0 / Math.PI);
    }
}
