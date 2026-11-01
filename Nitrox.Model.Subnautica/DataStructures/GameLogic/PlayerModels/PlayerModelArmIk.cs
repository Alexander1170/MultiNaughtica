using System;
using System.Numerics;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>What the arm of the model is like this frame, after the pose copy: where its joints are and how its two bones are turned (all in the world), and where its knuckles are in relation to its wrist.</summary>
public struct ArmState
{
    /// <summary>The upper arm bone (the shoulder joint), the forearm bone (the elbow) and the hand bone (the wrist).</summary>
    public Vector3 ShoulderPosition;

    public Vector3 ElbowPosition;

    public Vector3 WristPosition;

    /// <summary>How the upper arm bone and the forearm bone are turned now.</summary>
    public Quaternion ArmRotation;

    public Quaternion ForeArmRotation;

    /// <summary>From the wrist to the knuckle anchor of the model's hand, with the hand turned the way it will be (<see cref="ArmTarget.HandRotation" />).</summary>
    public Vector3 KnuckleOffsetFromWrist;
}

/// <summary>Where the hand has to be: the knuckle anchor of the hand of the vanilla body, and the way the hand has to be turned.</summary>
public struct ArmTarget
{
    public Vector3 Anchor;

    public Quaternion HandRotation;

    /// <summary>The way the elbow was bent by the pose copy (from the shoulder), and the way the vanilla elbow is bent: the second one decides when the first one is a straight arm.</summary>
    public Vector3 Pole;

    public Vector3 FallbackPole;

    /// <summary>The direction from the forearm bone to the hand bone, in the frame of the forearm bone: the axis the forearm turns about when it takes its share of the twist of the wrist.</summary>
    public Vector3 ForeArmAxis;

    /// <summary>The share of the twist between the forearm and the hand that the forearm takes (0: all of it stays at the wrist).</summary>
    public float TwistShare;
}

/// <summary>What <see cref="PlayerModelArmIk.Place" /> made of the arm: how the three bones are turned (in the world) and where the anchor of the hand ends up.</summary>
public struct ArmResult
{
    public Quaternion ArmRotation;

    public Quaternion ForeArmRotation;

    public Quaternion HandRotation;

    /// <summary>Where the knuckle anchor of the model's hand is now.</summary>
    public Vector3 ReachedAnchor;

    /// <summary>How far the anchor is from where it was asked to be: 0 unless the arm is too short to reach it.</summary>
    public float Shortfall;

    /// <summary>False when the arm was left as it was (a bone with no length, a target on the shoulder): only the hand was turned.</summary>
    public bool Solved;
}

/// <summary>
/// The arm of a model that has to hold what the vanilla hand holds (docs/coop/PLAYER_MODELS.md, "The hands"). The pose copy turns every bone like the vanilla one, which puts the hand where the vanilla hand is only
/// when the arm has the vanilla proportions and the shoulder is where the vanilla shoulder is; a model with another shoulder height, another upper arm or another forearm holds a tool a few centimetres off. This
/// solves the two bones of the arm (the upper arm and the forearm) so that the knuckles of the model's hand are where the knuckles of the vanilla hand are, and turns the hand like the vanilla hand. The model
/// can have any size and any arm: the bones are measured from the pose they are in, nothing here knows a model. Plain numbers (System.Numerics), so that the tests can check it without the game.
/// </summary>
public static class PlayerModelArmIk
{
    /// <summary>A bone shorter than this (metres) is not a bone: the arm is left as the pose copy made it.</summary>
    public const float MIN_BONE_LENGTH = 1e-4f;

    /// <summary>The elbow bends the way the pose copy bent it; an elbow nearer to the line from the shoulder to the wrist than this fraction of the upper arm is a straight arm that has no way, and the vanilla elbow decides.</summary>
    public const float STRAIGHT_FRACTION = 0.02f;

    /// <summary>The model has no twist bones (the vanilla body has two in the forearm), so the forearm takes half of the twist of the wrist and the wrist keeps the other half.</summary>
    public const float DEFAULT_TWIST_SHARE = 0.5f;

    /// <summary>An arm that is stretched is stretched until it reaches with this much of its length: not to the last millimetre, a straight arm has no way to bend.</summary>
    public const float REACH_MARGIN = 0.98f;

    private const float EPSILON = 1e-5f;

    /// <summary>
    /// Solves the two bones: how much the upper arm bone and then the forearm bone have to be turned (a rotation in the world to put in front of what the bone has now) so that the wrist is at
    /// <paramref name="wantedWrist" />, or as near to it as the arm reaches (it is stretched toward it). The elbow goes to the side that <paramref name="pole" /> points to (<paramref name="fallbackPole" /> when
    /// the arm is straight). False, with nothing changed, when a bone has no length or the wanted place is the shoulder itself.
    /// </summary>
    public static bool SolveSwings(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 wantedWrist, Vector3 pole, Vector3 fallbackPole, out Quaternion upperSwing, out Quaternion foreSwing, out Vector3 reachedWrist)
    {
        upperSwing = Quaternion.Identity;
        foreSwing = Quaternion.Identity;
        reachedWrist = wrist;

        float upper = Vector3.Distance(shoulder, elbow);
        float fore = Vector3.Distance(elbow, wrist);
        Vector3 toWanted = wantedWrist - shoulder;
        float distance = toWanted.Length();
        if (upper < MIN_BONE_LENGTH || fore < MIN_BONE_LENGTH || distance < MIN_BONE_LENGTH || IsBad(distance) || IsBad(upper) || IsBad(fore))
        {
            return false;
        }

        // the wrist cannot be nearer than the difference of the two bones, nor farther than their sum
        float reach = Math.Min(Math.Max(distance, Math.Abs(upper - fore) + EPSILON), upper + fore - EPSILON);
        Vector3 direction = toWanted / distance;
        float along = ((upper * upper) - (fore * fore) + (reach * reach)) / (2f * reach);
        float height = (float)Math.Sqrt(Math.Max(0f, (upper * upper) - (along * along)));
        Vector3 newElbow = shoulder + (direction * along) + (BendDirection(direction, pole, fallbackPole, upper) * height);
        Vector3 newWrist = shoulder + (direction * reach);

        // the upper arm turns until it points at the new elbow; the forearm, which went along with it, then turns until it points at the wrist
        upperSwing = PlayerModelRig.FromTo(elbow - shoulder, newElbow - shoulder);
        Vector3 wristAfterUpper = newElbow + Vector3.Transform(wrist - elbow, upperSwing);
        foreSwing = PlayerModelRig.FromTo(wristAfterUpper - newElbow, newWrist - newElbow);
        reachedWrist = newWrist;
        return true;
    }

    /// <summary>
    /// How much an arm has to be made longer to reach a place <paramref name="distance" /> from its shoulder: 1 when it reaches as it is (with <see cref="REACH_MARGIN" /> of its length), else the factor that does, at
    /// most <paramref name="maxStretch" />. A model with shorter arms than the diver, or with lower shoulders, would otherwise hold a tool short of the grip.
    /// </summary>
    public static float StretchFor(float upperLength, float foreLength, float distance, float maxStretch)
    {
        if (maxStretch <= 1f || upperLength < MIN_BONE_LENGTH || foreLength < MIN_BONE_LENGTH || IsBad(distance) || distance <= 0f)
        {
            return 1f;
        }
        float needed = distance / ((upperLength + foreLength) * REACH_MARGIN);
        return Math.Min(Math.Max(needed, 1f), maxStretch);
    }

    /// <summary>
    /// The angle in degrees (-180 to 180) of the twist about <paramref name="axis" /> in <paramref name="relative" />, a rotation that is the swing (the turn of the axis) and then the twist. The twist of the hand
    /// against the forearm: how far the palm is rolled on from where the forearm has it.
    /// </summary>
    public static float TwistDegrees(Quaternion relative, Vector3 axis)
    {
        float length = axis.Length();
        if (length < 1e-6f)
        {
            return 0f;
        }
        axis /= length;
        Quaternion q = Quaternion.Normalize(relative);
        if (q.W < 0f)
        {
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        }
        float projected = Vector3.Dot(axis, new Vector3(q.X, q.Y, q.Z));
        return (float)(2.0 * Math.Atan2(projected, q.W) * 180.0 / Math.PI);
    }

    /// <summary>A turn of <paramref name="degrees" /> about <paramref name="axis" /> (which need not be a unit vector; nothing for none).</summary>
    public static Quaternion AxisAngle(Vector3 axis, float degrees)
    {
        float length = axis.Length();
        if (length < 1e-6f)
        {
            return Quaternion.Identity;
        }
        return Quaternion.CreateFromAxisAngle(axis / length, degrees * (float)Math.PI / 180f);
    }

    /// <summary>
    /// Puts the hand of the arm where the vanilla hand is: the two bones are solved so that the knuckle anchor lands on <see cref="ArmTarget.Anchor" />, the forearm takes its share of the twist between it and
    /// the hand, and the hand has <see cref="ArmTarget.HandRotation" />. The rotations are in the world, for the three bones, in the order the bones are set (the upper arm, the forearm, the hand).
    /// </summary>
    public static ArmResult Place(in ArmState state, in ArmTarget target)
    {
        ArmResult result = new()
        {
            ArmRotation = state.ArmRotation,
            ForeArmRotation = state.ForeArmRotation,
            HandRotation = target.HandRotation,
            ReachedAnchor = state.WristPosition + state.KnuckleOffsetFromWrist,
            Shortfall = 0f,
            Solved = false
        };
        Vector3 wantedWrist = target.Anchor - state.KnuckleOffsetFromWrist;
        if (!SolveSwings(state.ShoulderPosition, state.ElbowPosition, state.WristPosition, wantedWrist, target.Pole, target.FallbackPole, out Quaternion upperSwing, out Quaternion foreSwing, out Vector3 reachedWrist))
        {
            result.Shortfall = (target.Anchor - result.ReachedAnchor).Length();
            return result;
        }

        Quaternion arm = Quaternion.Normalize(upperSwing * state.ArmRotation);
        Quaternion foreArm = Quaternion.Normalize(foreSwing * upperSwing * state.ForeArmRotation);
        if (target.TwistShare != 0f)
        {
            // how far the hand is rolled from the forearm (about the forearm's own axis), and the share of it that the forearm takes over
            float twist = TwistDegrees(Quaternion.Inverse(foreArm) * target.HandRotation, target.ForeArmAxis);
            foreArm = Quaternion.Normalize(foreArm * AxisAngle(target.ForeArmAxis, twist * target.TwistShare));
        }
        result.ArmRotation = arm;
        result.ForeArmRotation = foreArm;
        result.ReachedAnchor = reachedWrist + state.KnuckleOffsetFromWrist;
        result.Shortfall = (target.Anchor - result.ReachedAnchor).Length();
        result.Solved = true;
        return result;
    }

    private static Vector3 BendDirection(Vector3 direction, Vector3 pole, Vector3 fallbackPole, float upperLength)
    {
        Vector3 bend = Perpendicular(pole, direction);
        if (bend.Length() >= upperLength * STRAIGHT_FRACTION && !IsBad(bend.Length()))
        {
            return Vector3.Normalize(bend);
        }
        bend = Perpendicular(fallbackPole, direction);
        if (bend.LengthSquared() > 1e-10f && !IsBad(bend.LengthSquared()))
        {
            return Vector3.Normalize(bend);
        }
        // no hint at all: the elbow goes down, or back when the arm points straight up or down
        bend = Perpendicular(-Vector3.UnitY, direction);
        if (bend.LengthSquared() < 1e-6f)
        {
            bend = Perpendicular(-Vector3.UnitZ, direction);
        }
        return Vector3.Normalize(bend);
    }

    private static Vector3 Perpendicular(Vector3 vector, Vector3 axis) => vector - (axis * Vector3.Dot(vector, axis));

    private static bool IsBad(float value) => float.IsNaN(value) || float.IsInfinity(value);
}
