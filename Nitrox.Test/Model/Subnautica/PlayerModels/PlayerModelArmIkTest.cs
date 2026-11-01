using System.Numerics;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The arm solve of the hands (docs/coop/PLAYER_MODELS.md, "The hands"): a model whose shoulder is lower, whose upper arm is shorter or whose forearm is longer than the vanilla body's still puts its hand where the
/// vanilla hand is. Arms of every proportion, on made-up numbers: the solve measures the bones it is given and knows no model.
/// </summary>
[TestClass]
public class PlayerModelArmIkTest
{
    private static float Between(Random random, float low, float high) => low + ((float)random.NextDouble() * (high - low));

    private static Vector3 RandomUnit(Random random)
    {
        while (true)
        {
            Vector3 v = new(Between(random, -1f, 1f), Between(random, -1f, 1f), Between(random, -1f, 1f));
            if (v.LengthSquared() is > 0.01f and < 1f)
            {
                return Vector3.Normalize(v);
            }
        }
    }

    private static Quaternion RandomRotation(Random random) => Quaternion.CreateFromYawPitchRoll(Between(random, 0f, 6.2f), Between(random, -1.5f, 1.5f), Between(random, 0f, 6.2f));

    /// <summary>Where the elbow and the wrist are once the upper arm and then the forearm have been turned by the swings (rotations in the world, in front of what the bones have).</summary>
    private static (Vector3 Elbow, Vector3 Wrist) Apply(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion upper, Quaternion fore)
    {
        Vector3 newElbow = shoulder + Vector3.Transform(elbow - shoulder, upper);
        Vector3 newWrist = newElbow + Vector3.Transform(Vector3.Transform(wrist - elbow, upper), fore);
        return (newElbow, newWrist);
    }

    [TestMethod]
    public void TheWristReachesEveryPlaceThatTheArmCanReachWhateverItsProportions()
    {
        Random random = new(21);
        for (int i = 0; i < 600; i++)
        {
            float upper = Between(random, 0.1f, 0.55f);
            float fore = Between(random, 0.1f, 0.55f);
            Vector3 shoulder = new(Between(random, -1f, 1f), Between(random, -1f, 1f), Between(random, -1f, 1f));
            Vector3 elbow = shoulder + (RandomUnit(random) * upper);
            Vector3 wrist = elbow + (RandomUnit(random) * fore);
            float reach = Between(random, Math.Abs(upper - fore) + 0.02f, upper + fore - 0.02f);
            Vector3 wanted = shoulder + (RandomUnit(random) * reach);
            Vector3 pole = RandomUnit(random);

            bool solved = PlayerModelArmIk.SolveSwings(shoulder, elbow, wrist, wanted, pole, Vector3.UnitY, out Quaternion upperSwing, out Quaternion foreSwing, out Vector3 reached);

            solved.Should().BeTrue();
            (Vector3 newElbow, Vector3 newWrist) = Apply(shoulder, elbow, wrist, upperSwing, foreSwing);
            (newWrist - wanted).Length().Should().BeLessThan(2e-3f, $"arm {upper:F2} + {fore:F2} to a place {reach:F2} away");
            (reached - wanted).Length().Should().BeLessThan(1e-4f);
            Vector3.Distance(shoulder, newElbow).Should().BeApproximately(upper, 1e-3f, "the bones keep their length");
            Vector3.Distance(newElbow, newWrist).Should().BeApproximately(fore, 1e-3f);

            // the elbow bends to the side that the pole points to
            Vector3 line = Vector3.Normalize(wanted - shoulder);
            Vector3 bend = newElbow - shoulder - (line * Vector3.Dot(newElbow - shoulder, line));
            Vector3 poleSide = pole - (line * Vector3.Dot(pole, line));
            if (poleSide.Length() > 0.1f && bend.Length() > 5e-3f)
            {
                Vector3.Dot(Vector3.Normalize(bend), Vector3.Normalize(poleSide)).Should().BeGreaterThan(0.99f);
            }
        }
    }

    [TestMethod]
    public void AnArmThatIsTooShortIsStretchedTowardThePlaceAndSaysSo()
    {
        Vector3 shoulder = Vector3.Zero;
        Vector3 elbow = new(0.25f, 0f, 0f);
        Vector3 wrist = new(0.45f, -0.1f, 0f);
        Vector3 wanted = new(0f, 0.2f, 2f);

        PlayerModelArmIk.SolveSwings(shoulder, elbow, wrist, wanted, Vector3.UnitY, Vector3.UnitY, out Quaternion upperSwing, out Quaternion foreSwing, out Vector3 reached).Should().BeTrue();

        (Vector3 newElbow, Vector3 newWrist) = Apply(shoulder, elbow, wrist, upperSwing, foreSwing);
        float length = Vector3.Distance(elbow, shoulder) + Vector3.Distance(wrist, elbow);
        Vector3 toward = Vector3.Normalize(wanted);
        Vector3.Dot(Vector3.Normalize(newWrist), toward).Should().BeGreaterThan(0.9999f, "the arm points at the place");
        newWrist.Length().Should().BeApproximately(length, 1e-3f, "the arm is stretched to the full");
        Vector3.Dot(Vector3.Normalize(newElbow), toward).Should().BeGreaterThan(0.999f);
        (reached - newWrist).Length().Should().BeLessThan(1e-3f);
    }

    [TestMethod]
    public void AStraightArmBendsTheWayTheFallbackSaysAndWithoutHintsItDropsTheElbow()
    {
        Vector3 shoulder = Vector3.Zero;
        Vector3 elbow = new(0.25f, 0f, 0f);
        Vector3 wrist = new(0.5f, 0f, 0f);
        Vector3 wanted = new(0.35f, 0f, 0f);

        // the pose copy left the arm straight: no way to bend, so the vanilla elbow decides
        PlayerModelArmIk.SolveSwings(shoulder, elbow, wrist, wanted, new Vector3(1f, 0f, 0f), new Vector3(0.1f, 0f, 0.3f), out Quaternion upperSwing, out Quaternion foreSwing, out _).Should().BeTrue();
        (Vector3 bentElbow, _) = Apply(shoulder, elbow, wrist, upperSwing, foreSwing);
        bentElbow.Z.Should().BeGreaterThan(0.05f);

        // no hint at all: the elbow goes down
        PlayerModelArmIk.SolveSwings(shoulder, elbow, wrist, wanted, Vector3.Zero, Vector3.Zero, out upperSwing, out foreSwing, out _).Should().BeTrue();
        (Vector3 droppedElbow, _) = Apply(shoulder, elbow, wrist, upperSwing, foreSwing);
        droppedElbow.Y.Should().BeLessThan(-0.05f);

        // and for an arm that points straight down there is no down to bend to: it goes back
        PlayerModelArmIk.SolveSwings(shoulder, elbow, wrist, new Vector3(0f, -0.35f, 0f), Vector3.Zero, Vector3.Zero, out upperSwing, out foreSwing, out _).Should().BeTrue();
        (Vector3 backElbow, _) = Apply(shoulder, elbow, wrist, upperSwing, foreSwing);
        backElbow.Z.Should().BeLessThan(-0.05f);
    }

    [TestMethod]
    public void AnArmWithABoneOfNoLengthOrAPlaceOnTheShoulderIsLeftAsItWas()
    {
        PlayerModelArmIk.SolveSwings(Vector3.Zero, Vector3.Zero, new Vector3(0.2f, 0f, 0f), new Vector3(0.1f, 0.1f, 0f), Vector3.UnitY, Vector3.UnitY, out Quaternion upperSwing, out Quaternion foreSwing, out Vector3 reached).Should().BeFalse();
        upperSwing.Should().Be(Quaternion.Identity);
        foreSwing.Should().Be(Quaternion.Identity);
        reached.Should().Be(new Vector3(0.2f, 0f, 0f));

        PlayerModelArmIk.SolveSwings(Vector3.Zero, new Vector3(0.25f, 0f, 0f), new Vector3(0.5f, 0f, 0f), Vector3.Zero, Vector3.UnitY, Vector3.UnitY, out _, out _, out _).Should().BeFalse();
        PlayerModelArmIk.SolveSwings(Vector3.Zero, new Vector3(0.25f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(float.NaN, 0f, 0f), Vector3.UnitY, Vector3.UnitY, out _, out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TheTwistOfARotationAboutAnAxisIsTheAngleThatWasPutInWhateverTheSwing()
    {
        Random random = new(8);
        for (int i = 0; i < 500; i++)
        {
            Vector3 axis = RandomUnit(random);
            Vector3 swingAxis = Vector3.Normalize(Vector3.Cross(axis, RandomUnit(random)));
            Quaternion swing = Quaternion.CreateFromAxisAngle(swingAxis, Between(random, -1.4f, 1.4f));
            float twist = Between(random, -170f, 170f);

            Quaternion rotation = swing * PlayerModelArmIk.AxisAngle(axis, twist);

            PlayerModelArmIk.TwistDegrees(rotation, axis).Should().BeApproximately(twist, 0.01f);
            PlayerModelArmIk.TwistDegrees(new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W), axis).Should().BeApproximately(twist, 0.01f, "a quaternion and its negative are the same turn");
        }
        PlayerModelArmIk.TwistDegrees(Quaternion.Identity, Vector3.UnitY).Should().Be(0f);
        PlayerModelArmIk.TwistDegrees(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f), Vector3.Zero).Should().Be(0f);
        PlayerModelArmIk.AxisAngle(Vector3.Zero, 30f).Should().Be(Quaternion.Identity);
    }

    [TestMethod]
    public void AnArmIsStretchedOnlyAsFarAsTheGripNeedsAndTheSettingsAllow()
    {
        // 0.5 m of arm reaches 0.49 m (REACH_MARGIN of it) as it is
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, 0.3f, 1.2f).Should().Be(1f);
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, 0.49f, 1.2f).Should().BeApproximately(1f, 1e-4f);
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, 0.539f, 1.2f).Should().BeApproximately(1.1f, 1e-3f);
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, 0.9f, 1.2f).Should().Be(1.2f, "never more than the settings allow");
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, 0.9f, 1f).Should().Be(1f, "1 never stretches");
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, 0.9f, 0.5f).Should().Be(1f, "an arm is never made shorter");
        PlayerModelArmIk.StretchFor(0f, 0.22f, 0.9f, 1.2f).Should().Be(1f);
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, float.NaN, 1.2f).Should().Be(1f);
        PlayerModelArmIk.StretchFor(0.28f, 0.22f, -1f, 1.2f).Should().Be(1f);
    }

    /// <summary>An arm of the given bones as the game has it: the bones hang on one another, each one along its own local axis, and the hand has knuckles at an offset in its own frame.</summary>
    private sealed class ArmRig
    {
        public Vector3 Shoulder;
        public Vector3 UpperLocal;
        public Vector3 ForeLocal;
        public Vector3 KnuckleLocal;

        public Vector3 Elbow(Quaternion arm) => Shoulder + Vector3.Transform(UpperLocal, arm);

        public Vector3 Wrist(Quaternion arm, Quaternion fore) => Elbow(arm) + Vector3.Transform(ForeLocal, fore);

        public Vector3 Anchor(Quaternion arm, Quaternion fore, Quaternion hand) => Wrist(arm, fore) + Vector3.Transform(KnuckleLocal, hand);
    }

    [TestMethod]
    public void PlaceLandsTheKnucklesOnTheTargetWithTheHandTurnedAsAskedForArmsOfEveryProportion()
    {
        (string Name, float Upper, float Fore, Vector3 Shoulder)[] proportions =
        [
            ("average", 0.28f, 0.24f, new Vector3(0.19f, 1.34f, 0f)),
            ("tall", 0.34f, 0.30f, new Vector3(0.22f, 1.60f, 0f)),
            ("short", 0.20f, 0.17f, new Vector3(0.15f, 0.95f, 0f)),
            ("long arms", 0.40f, 0.36f, new Vector3(0.19f, 1.34f, 0f)),
            ("short arms", 0.15f, 0.13f, new Vector3(0.19f, 1.34f, 0f)),
            ("wide shoulders", 0.28f, 0.24f, new Vector3(0.38f, 1.34f, 0f)),
            ("a short upper arm and a long forearm", 0.22f, 0.32f, new Vector3(0.19f, 1.34f, 0f)),
            ("a long upper arm and a short forearm", 0.36f, 0.16f, new Vector3(0.19f, 1.34f, 0f))
        ];
        Random random = new(44);
        foreach ((string name, float upper, float fore, Vector3 shoulder) in proportions)
        {
            ArmRig rig = new()
            {
                Shoulder = shoulder,
                UpperLocal = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f)) * upper,
                ForeLocal = Vector3.Normalize(new Vector3(-0.1f, 1f, 0.3f)) * fore,
                KnuckleLocal = new Vector3(0.01f, 0.11f, 0.02f)
            };
            int reached = 0;
            for (int i = 0; i < 200; i++)
            {
                // the arm as the pose copy left it, a hand that the vanilla body turns some way, and a place for the knuckles that this arm can reach (or, now and then, one that it cannot)
                Quaternion arm = RandomRotation(random);
                Quaternion foreArm = RandomRotation(random);
                Quaternion hand = RandomRotation(random);
                Vector3 wantedAnchor = shoulder + (RandomUnit(random) * Between(random, 0.05f, (upper + fore) * 1.15f));
                Vector3 axis = Vector3.Normalize(rig.ForeLocal);
                Vector3 elbow = rig.Elbow(arm);
                Vector3 wrist = rig.Wrist(arm, foreArm);
                ArmState state = new()
                {
                    ShoulderPosition = shoulder,
                    ElbowPosition = elbow,
                    WristPosition = wrist,
                    ArmRotation = arm,
                    ForeArmRotation = foreArm,
                    KnuckleOffsetFromWrist = Vector3.Transform(rig.KnuckleLocal, hand)
                };
                ArmTarget target = new()
                {
                    Anchor = wantedAnchor,
                    HandRotation = hand,
                    Pole = elbow - shoulder,
                    FallbackPole = new Vector3(0.1f, -1f, -0.4f),
                    ForeArmAxis = axis,
                    TwistShare = 0.5f
                };

                ArmResult result = PlayerModelArmIk.Place(state, target);

                result.Solved.Should().BeTrue();
                result.HandRotation.Should().Be(hand);
                // the bones as the game sets them: in the world, one after the other
                Vector3 anchor = rig.Anchor(result.ArmRotation, result.ForeArmRotation, result.HandRotation);
                float distance = (wantedAnchor - state.KnuckleOffsetFromWrist - shoulder).Length();
                float nearest = Math.Abs(upper - fore);
                float farthest = upper + fore;
                (anchor - result.ReachedAnchor).Length().Should().BeLessThan(2e-3f, $"{name}: the arm says where its knuckles are");
                if (distance >= nearest + 0.003f && distance <= farthest - 0.002f)
                {
                    reached++;
                    (anchor - wantedAnchor).Length().Should().BeLessThan(2e-3f, $"{name}: the knuckles are where they were asked to be");
                    result.Shortfall.Should().BeLessThan(2e-3f);
                }
                else if (distance > farthest + 0.005f)
                {
                    result.Shortfall.Should().BeApproximately(distance - farthest, 4e-3f, $"{name}: an arm that is too short says by how much");
                }
                else if (distance < nearest - 0.005f)
                {
                    result.Shortfall.Should().BeApproximately(nearest - distance, 4e-3f, $"{name}: an arm cannot fold up to nothing and says by how much");
                }
            }
            reached.Should().BeGreaterThan(60, $"{name}: a good part of the places are inside the reach of the arm");
        }
    }

    [TestMethod]
    public void TheForearmTakesItsShareOfTheTwistOfTheWristAndTheHandKeepsItsTurn()
    {
        Random random = new(3);
        ArmRig rig = new() { Shoulder = Vector3.Zero, UpperLocal = new Vector3(0f, 0.28f, 0f), ForeLocal = new Vector3(0.02f, 0.24f, 0.01f), KnuckleLocal = new Vector3(0f, 0.1f, 0f) };
        Vector3 axis = Vector3.Normalize(rig.ForeLocal);
        for (int i = 0; i < 100; i++)
        {
            Quaternion arm = RandomRotation(random);
            Quaternion foreArm = RandomRotation(random);
            Quaternion hand = RandomRotation(random);
            ArmState state = new()
            {
                ShoulderPosition = Vector3.Zero,
                ElbowPosition = rig.Elbow(arm),
                WristPosition = rig.Wrist(arm, foreArm),
                ArmRotation = arm,
                ForeArmRotation = foreArm,
                KnuckleOffsetFromWrist = Vector3.Transform(rig.KnuckleLocal, hand)
            };
            Vector3 wanted = RandomUnit(random) * Between(random, 0.2f, 0.45f);
            ArmTarget whole = new() { Anchor = wanted, HandRotation = hand, Pole = state.ElbowPosition, FallbackPole = Vector3.UnitY, ForeArmAxis = axis, TwistShare = 0f };
            ArmTarget half = whole;
            half.TwistShare = 0.5f;
            ArmTarget all = whole;
            all.TwistShare = 1f;

            ArmResult atWrist = PlayerModelArmIk.Place(state, whole);
            ArmResult shared = PlayerModelArmIk.Place(state, half);
            ArmResult atForearm = PlayerModelArmIk.Place(state, all);

            float twistLeft = PlayerModelArmIk.TwistDegrees(Quaternion.Inverse(atWrist.ForeArmRotation) * hand, axis);
            float twistHalf = PlayerModelArmIk.TwistDegrees(Quaternion.Inverse(shared.ForeArmRotation) * hand, axis);
            float twistNone = PlayerModelArmIk.TwistDegrees(Quaternion.Inverse(atForearm.ForeArmRotation) * hand, axis);
            twistHalf.Should().BeApproximately(twistLeft * 0.5f, 0.05f, "half of the twist goes to the forearm");
            twistNone.Should().BeApproximately(0f, 0.05f, "all of it");
            // the forearm turns about its own axis, so the wrist does not move
            (rig.Wrist(shared.ArmRotation, shared.ForeArmRotation) - rig.Wrist(atWrist.ArmRotation, atWrist.ForeArmRotation)).Length().Should().BeLessThan(1e-4f);
            shared.HandRotation.Should().Be(hand);
        }
    }
}
