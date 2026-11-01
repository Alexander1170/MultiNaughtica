using System.Numerics;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The calibration of the hands (docs/coop/PLAYER_MODELS.md, "The hands"): the hand is turned by its palm, grips at the middle of its knuckles and has fifteen finger bones that follow the vanilla fingers, worked out
/// from the rest pose of the skeleton alone. First on the reference skeleton, then on models made from it with other proportions (taller, shorter, longer arms, bigger hands, wider shoulders, another rest pose), so that
/// nothing in the arithmetic belongs to one skeleton.
/// </summary>
[TestClass]
public class PlayerModelHandsTest
{
    private static PlayerModelRest RealRest() => RigFixture.ReferenceRest();

    private static IReadOnlyList<(string Name, PlayerModelRest Skeleton)> Models()
    {
        PlayerModelRest reference = RealRest();
        return
        [
            ("the reference skeleton", reference),
            ("taller", RestVariants.Scaled(reference, 1.15f)),
            ("shorter", RestVariants.Scaled(reference, 0.85f)),
            ("longer arms", RestVariants.WithLimbLengths(reference, 1.2f, 1.2f, 1f)),
            ("shorter arms", RestVariants.WithLimbLengths(reference, 0.85f, 0.85f, 1f)),
            ("a short upper arm and a long forearm", RestVariants.WithLimbLengths(reference, 0.8f, 1.25f, 1f)),
            ("bigger hands", RestVariants.WithLimbLengths(reference, 1f, 1f, 1.4f)),
            ("smaller hands", RestVariants.WithLimbLengths(reference, 1f, 1f, 0.7f)),
            ("wider shoulders", RestVariants.WithShoulderWidth(reference, 1.5f)),
            ("arms raised into a T", RestVariants.WithArmsRaised(reference, 35f)),
            ("arms down along the body", RestVariants.WithArmsRaised(reference, -20f)),
            ("a second rig with other proportions", RigFixture.AlternateRest()),
            ("a rig with helper bones", RigFixture.ExtrasRest())
        ];
    }

    private static PlayerModelRest Renamed(PlayerModelRest rest, Func<string, string> rename)
    {
        return new PlayerModelRest(Enumerable.Range(0, rest.Count).Select(node => rename(rest.Name(node))).ToArray(), Enumerable.Range(0, rest.Count).Select(rest.Parent).ToArray(),
                                   Enumerable.Range(0, rest.Count).Select(rest.Position).ToArray(), Enumerable.Range(0, rest.Count).Select(rest.Rotation).ToArray());
    }

    private static float Angle(Vector3 a, Vector3 b)
    {
        float cos = Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b))));
        return (float)(Math.Acos(cos) * 180.0 / Math.PI);
    }

    /// <summary>The part of a direction that lies across an axis.</summary>
    private static Vector3 Across(Vector3 axis, Vector3 v)
    {
        axis = Vector3.Normalize(axis);
        return v - (axis * Vector3.Dot(v, axis));
    }

    private static float Between(Random random, float low, float high) => low + ((float)random.NextDouble() * (high - low));

    private static Quaternion RandomTurn(Random random, float maxDegrees)
    {
        Vector3 axis;
        do
        {
            axis = new Vector3(Between(random, -1f, 1f), Between(random, -1f, 1f), Between(random, -1f, 1f));
        } while (axis.LengthSquared() < 0.05f);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), Between(random, -maxDegrees, maxDegrees) * (float)Math.PI / 180f);
    }

    private static IEnumerable<PlayerModelHandCalibration> Hands(PlayerModelHandsCalibration calibration)
    {
        calibration.Left.Should().NotBeNull();
        calibration.Right.Should().NotBeNull();
        return [calibration.Left!, calibration.Right!];
    }

    [TestMethod]
    public void BothHandsOfTheReferenceSkeletonAreCalibratedWithAllTheirFingers()
    {
        PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(RealRest(), VanillaBindReader.Bind());

        calibration.Missing.Should().BeEmpty();
        calibration.HandCount.Should().Be(2);
        foreach (PlayerModelHandCalibration hand in Hands(calibration))
        {
            hand.Fingers.Should().HaveCount(PlayerModelHands.FINGER_BONES_PER_HAND);
            hand.Fingers.Select(finger => finger.Mixamo).Should().OnlyHaveUniqueItems();
            hand.Fingers.Select(finger => finger.Vanilla).Should().OnlyHaveUniqueItems();
            hand.Fingers.Count(PlayerModelHands.IsThumb).Should().Be(PlayerModelHands.JOINTS_PER_FINGER);
            hand.KnuckleNodes.Should().HaveCount(4).And.OnlyContain(node => node >= 0);
            hand.VanillaKnuckles.Should().HaveCount(4);
        }
        calibration.Left!.VanillaHand.Should().Be("hand_L");
        calibration.Right!.VanillaHand.Should().Be("hand_R");
        calibration.Right.Fingers.Count(finger => finger.Vanilla.EndsWith("_tip_rig", StringComparison.Ordinal)).Should().Be(5, "the right hand of the vanilla body calls the last joint of a finger tip_rig");
        calibration.Left.Fingers.Count(finger => finger.Vanilla.EndsWith("_tip", StringComparison.Ordinal)).Should().Be(5, "and the left hand tip");
    }

    [TestMethod]
    public void ThePalmOfTheReferenceSkeletonIsRolledAboutSixteenDegreesFromTheVanillaPalmAndTheTwoHandsRollOppositely()
    {
        PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(RealRest(), VanillaBindReader.Bind());

        Console.WriteLine($"palm roll: left {calibration.Left!.RollDegrees:F2} deg, right {calibration.Right!.RollDegrees:F2} deg; whole turn {calibration.Left.FixDegrees:F1} / {calibration.Right.FixDegrees:F1} deg");
        Math.Abs(calibration.Left.RollDegrees).Should().BeInRange(12f, 20f);
        Math.Abs(calibration.Right.RollDegrees).Should().BeInRange(12f, 20f);
        Math.Sign(calibration.Left.RollDegrees).Should().NotBe(Math.Sign(calibration.Right.RollDegrees), "the two hands are mirror images");
        calibration.Left.FixDegrees.Should().BeInRange(25f, 50f);
        calibration.Right.FixDegrees.Should().BeInRange(25f, 50f);
    }

    [TestMethod]
    public void AfterTheConstantThePalmOfTheModelFacesWhereTheVanillaPalmFacesForAnyModel()
    {
        Dictionary<string, PlayerModelBind> bind = VanillaBindReader.Bind();
        foreach ((string name, PlayerModelRest rest) in Models())
        {
            PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(rest, bind);
            calibration.Missing.Should().BeEmpty(name);
            foreach (PlayerModelHandCalibration hand in Hands(calibration))
            {
                // the turn that lines the model's rest hand up with the vanilla hand at bind
                Quaternion fix = bind[hand.VanillaHand].Rotation * hand.K * Quaternion.Inverse(rest.Rotation(hand.HandNode));
                Vector3 modelWrist = rest.Position(hand.HandNode);
                Vector3 vanillaWrist = bind[hand.VanillaHand].Position;
                Vector3 modelForward = Vector3.Transform(rest.Position(hand.KnuckleNodes[1]) - modelWrist, fix);
                Vector3 vanillaForward = bind[hand.VanillaKnuckles[1]].Position - vanillaWrist;
                Angle(modelForward, vanillaForward).Should().BeLessThan(0.5f, $"{name}, {hand.Side}: the fingers point the same way");

                // the row of knuckles from the little finger to the index finger lies the same way across the fingers
                Vector3 modelRow = Vector3.Transform(rest.Position(hand.KnuckleNodes[0]) - rest.Position(hand.KnuckleNodes[3]), fix);
                Vector3 vanillaRow = bind[hand.VanillaKnuckles[0]].Position - bind[hand.VanillaKnuckles[3]].Position;
                Angle(Across(vanillaForward, modelRow), Across(vanillaForward, vanillaRow)).Should().BeLessThan(0.5f, $"{name}, {hand.Side}: the palm faces the same way");

                // the constants of the palm in the vanilla hand's frame are three unit vectors at right angles
                hand.PalmForward.Length().Should().BeApproximately(1f, 1e-3f);
                hand.PalmNormal.Length().Should().BeApproximately(1f, 1e-3f);
                hand.PalmThumb.Length().Should().BeApproximately(1f, 1e-3f);
                Vector3.Dot(hand.PalmForward, hand.PalmNormal).Should().BeApproximately(0f, 2e-3f);
                Vector3.Dot(hand.PalmForward, hand.PalmThumb).Should().BeApproximately(0f, 2e-3f);
                Vector3.Dot(hand.PalmNormal, hand.PalmThumb).Should().BeApproximately(0f, 2e-3f);
                // and the palm of the vanilla hand at bind is what they say, in the world
                Angle(Vector3.Transform(hand.PalmForward, bind[hand.VanillaHand].Rotation), vanillaForward).Should().BeLessThan(0.5f);
            }
        }
    }

    [TestMethod]
    public void EveryFingerBoneOfAnyModelPointsWhereItsVanillaFingerPointsAtBind()
    {
        Dictionary<string, PlayerModelBind> bind = VanillaBindReader.Bind();
        foreach ((string name, PlayerModelRest rest) in Models())
        {
            PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(rest, bind);
            foreach (PlayerModelHandCalibration hand in Hands(calibration))
            {
                hand.Fingers.Should().HaveCount(PlayerModelHands.FINGER_BONES_PER_HAND, $"{name}, {hand.Side}");
                foreach (PlayerModelBoneConstant finger in hand.Fingers)
                {
                    // the stretch of the model's finger from this joint to the next, in the frame of the bone
                    Vector3 aimInBone = Vector3.Transform(rest.Position(finger.MixamoAimNode) - rest.Position(finger.MixamoNode), Quaternion.Inverse(rest.Rotation(finger.MixamoNode)));
                    Quaternion modelAtBind = bind[finger.Vanilla].Rotation * finger.K;
                    Vector3 vanillaAim = VanillaStretch(bind, hand, finger);

                    Angle(Vector3.Transform(aimInBone, modelAtBind), vanillaAim).Should().BeLessThan(0.5f, $"{name}: {finger.Mixamo} follows {finger.Vanilla}");
                }
            }
        }
    }

    /// <summary>Where the vanilla finger bone points at bind: to its next joint; the last bone of a finger goes on the way the one before it points (the joints of a finger are listed in order).</summary>
    private static Vector3 VanillaStretch(Dictionary<string, PlayerModelBind> bind, PlayerModelHandCalibration hand, PlayerModelBoneConstant finger)
    {
        if (finger.VanillaAim != null)
        {
            return bind[finger.VanillaAim].Position - bind[finger.Vanilla].Position;
        }
        int index = hand.Fingers.ToList().IndexOf(finger);
        return bind[finger.Vanilla].Position - bind[hand.Fingers[index - 1].Vanilla].Position;
    }

    [TestMethod]
    public void EveryFingerBoneKeepsItsRollAgainstThePalmAtBind()
    {
        Dictionary<string, PlayerModelBind> bind = VanillaBindReader.Bind();
        foreach ((string name, PlayerModelRest rest) in Models())
        {
            PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(rest, bind);
            foreach (PlayerModelHandCalibration hand in Hands(calibration))
            {
                // the palm of the vanilla hand, in the world at bind; and the palm of the model's hand taken along by a finger bone: the same direction seen across the finger, whichever finger
                Vector3 vanillaNormal = Vector3.Transform(hand.PalmNormal, bind[hand.VanillaHand].Rotation);
                Quaternion handFix = bind[hand.VanillaHand].Rotation * hand.K * Quaternion.Inverse(rest.Rotation(hand.HandNode));
                Vector3 modelNormalAtRest = Vector3.Transform(vanillaNormal, Quaternion.Inverse(handFix));
                foreach (PlayerModelBoneConstant finger in hand.Fingers.Where(finger => !PlayerModelHands.IsThumb(finger)))
                {
                    Quaternion fingerFix = bind[finger.Vanilla].Rotation * finger.K * Quaternion.Inverse(rest.Rotation(finger.MixamoNode));
                    Vector3 vanillaAim = VanillaStretch(bind, hand, finger);
                    Vector3 carried = Vector3.Transform(modelNormalAtRest, fingerFix);
                    Angle(Across(vanillaAim, carried), Across(vanillaAim, vanillaNormal)).Should().BeLessThan(1f, $"{name}: {finger.Mixamo} lies against the palm the way {finger.Vanilla} does");
                }
            }
        }
    }

    [TestMethod]
    public void AModelWithoutKnuckleBonesKeepsItsHandsAtThePoseCopyAndSaysWhy()
    {
        PlayerModelRest withoutIndex = Renamed(RealRest(), node => node.Contains("HandIndex") ? $"gone_{node}" : node);

        PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(withoutIndex, VanillaBindReader.Bind());

        calibration.Left.Should().BeNull();
        calibration.Right.Should().BeNull();
        calibration.HandCount.Should().Be(0);
        calibration.Missing.Should().HaveCount(2);
        calibration.Missing.Should().OnlyContain(text => text.Contains("knuckle bones") && text.Contains("pose copy"));
    }

    [TestMethod]
    public void AModelWithoutThumbsIsCalibratedWithoutThemAndAFingerThatIsMissingIsNamed()
    {
        PlayerModelRest withoutThumbs = Renamed(RealRest(), node => node.Contains("HandThumb") ? $"gone_{node}" : node);

        PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(withoutThumbs, VanillaBindReader.Bind());

        calibration.HandCount.Should().Be(2);
        foreach (PlayerModelHandCalibration hand in Hands(calibration))
        {
            hand.Fingers.Should().HaveCount(PlayerModelHands.FINGER_BONES_PER_HAND - PlayerModelHands.JOINTS_PER_FINGER);
            hand.Fingers.Should().NotContain(finger => PlayerModelHands.IsThumb(finger));
        }
        calibration.Missing.Should().HaveCount(2);
        calibration.Missing.Should().OnlyContain(text => text.Contains("Thumb"));
    }

    [TestMethod]
    public void AVanillaBodyWithoutAKnuckleLeavesThatHandAtThePoseCopy()
    {
        Dictionary<string, PlayerModelBind> bind = VanillaBindReader.Bind();
        bind.Remove("hand_R_midl_base");

        PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(RealRest(), bind);

        calibration.Right.Should().BeNull();
        calibration.Left.Should().NotBeNull();
        calibration.Missing.Should().ContainSingle().Which.Should().Contain("hand_R_midl_base");
    }

    [TestMethod]
    public void ARigThatNamesItsBonesInItsOwnWayIsCalibratedThroughTheBoneNames()
    {
        Dictionary<string, PlayerModelBind> bind = VanillaBindReader.Bind();
        PlayerModelHandsCalibration reference = PlayerModelHands.Calibrate(RealRest(), bind);
        // another exporter's prefix, and one finger that the rig spells in a way of its own
        PlayerModelRest other = Renamed(RealRest(), node => node == "mixamorig:RightHandIndex1" ? "R_Pointer_01" : node.StartsWith("mixamorig:", StringComparison.Ordinal) ? $"rig_{node.Substring("mixamorig:".Length)}" : node);
        PlayerModelBoneNames names = new("rig_", new Dictionary<string, string> { ["RightHandIndex1"] = "R_Pointer_01" });

        PlayerModelHandsCalibration calibration = PlayerModelHands.Calibrate(other, bind, names);

        calibration.Missing.Should().BeEmpty();
        calibration.Right!.K.Should().Be(reference.Right!.K);
        calibration.Left!.K.Should().Be(reference.Left!.K);
        calibration.Right.Fingers.Select(finger => finger.K).Should().Equal(reference.Right.Fingers.Select(finger => finger.K));
        PlayerModelHands.Calibrate(other, bind).HandCount.Should().Be(0, "with the Mixamo names the rig is not found");
    }

    /// <summary>
    /// What the hands do each frame, on a skeleton that is posed in the test: random poses of the vanilla arm (each bone turned from its bind pose), the pose copy of the arm, then the turn of the hand, the
    /// stretch of an arm that is too short (when it is switched on), the solve and the application of the three rotations. How many poses were tried, how many of them had a grip that the arm reaches, and the
    /// biggest distance between the knuckles of the model and the vanilla knuckles among those.
    /// </summary>
    private static (int Tried, int Reached, float Worst) SimulateGrips(string name, PlayerModelRest rest, bool stretchAllowed, int poses, int seed)
    {
        Dictionary<string, PlayerModelBind> bind = VanillaBindReader.Bind();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaBindReader.Attach();
        Random random = new(seed);
        PlayerModelCalibration body = PlayerModelRig.Calibrate(rest, bind, attachPosition, attachRotation);
        PlayerModelHandsCalibration hands = PlayerModelHands.Calibrate(rest, bind);
        RigSim sim = new(rest, body.Scale);
        int reached = 0;
        int tried = 0;
        float worst = 0f;
        foreach (PlayerModelHandCalibration hand in Hands(hands))
        {
            PlayerModelBoneConstant armK = body.Bones.Single(bone => bone.Mixamo == $"{hand.Side}Arm");
            PlayerModelBoneConstant foreK = body.Bones.Single(bone => bone.Mixamo == $"{hand.Side}ForeArm");
            PlayerModelBind vanillaShoulder = bind[hand.VanillaArm];
            PlayerModelBind vanillaElbow = bind[hand.VanillaForeArm];
            PlayerModelBind vanillaHand = bind[hand.VanillaHand];
            for (int pose = 0; pose < poses; pose++)
            {
                // the vanilla arm in a pose: each bone turned by its own turn from the bind pose, the joints where those turns put them
                Quaternion turnShoulder = RandomTurn(random, 60f);
                Quaternion turnElbow = RandomTurn(random, 60f);
                Quaternion turnHand = RandomTurn(random, 60f);
                Vector3 vElbow = vanillaShoulder.Position + Vector3.Transform(vanillaElbow.Position - vanillaShoulder.Position, turnShoulder);
                Vector3 vWrist = vElbow + Vector3.Transform(vanillaHand.Position - vanillaElbow.Position, turnElbow);
                Vector3 vAnchor = Vector3.Zero;
                foreach (string knuckle in hand.VanillaKnuckles)
                {
                    vAnchor += vWrist + Vector3.Transform(bind[knuckle].Position - vanillaHand.Position, turnHand);
                }
                vAnchor /= hand.VanillaKnuckles.Count;
                Quaternion vanillaHandNow = Quaternion.Normalize(turnHand * vanillaHand.Rotation);

                // the pose copy of the arm, then what the hands do after it
                sim.Stretch(hand.ForeArmNode, 1f);
                sim.Stretch(hand.HandNode, 1f);
                sim.SetRotation(hand.ArmNode, Quaternion.Normalize(turnShoulder * vanillaShoulder.Rotation * armK.K));
                sim.SetRotation(hand.ForeArmNode, Quaternion.Normalize(turnElbow * vanillaElbow.Rotation * foreK.K));
                Quaternion handRotation = Quaternion.Normalize(vanillaHandNow * hand.K);
                sim.SetRotation(hand.HandNode, handRotation);
                Vector3 shoulder = sim.Position(hand.ArmNode);
                Vector3 wristAtRest = sim.Position(hand.HandNode);
                Vector3 wanted = vAnchor - (sim.Middle(hand.KnuckleNodes) - wristAtRest);
                float stretch = stretchAllowed
                    ? PlayerModelArmIk.StretchFor(Vector3.Distance(shoulder, sim.Position(hand.ForeArmNode)), Vector3.Distance(sim.Position(hand.ForeArmNode), wristAtRest), Vector3.Distance(shoulder, wanted), PlayerModelHandSettings.DEFAULT_MAX_STRETCH)
                    : 1f;
                sim.Stretch(hand.ForeArmNode, stretch);
                sim.Stretch(hand.HandNode, stretch);
                Vector3 modelAnchor = sim.Middle(hand.KnuckleNodes);
                ArmState state = new()
                {
                    ShoulderPosition = shoulder,
                    ElbowPosition = sim.Position(hand.ForeArmNode),
                    WristPosition = sim.Position(hand.HandNode),
                    ArmRotation = sim.Rotation(hand.ArmNode),
                    ForeArmRotation = sim.Rotation(hand.ForeArmNode),
                    KnuckleOffsetFromWrist = modelAnchor - sim.Position(hand.HandNode)
                };
                ArmTarget target = new()
                {
                    Anchor = vAnchor,
                    HandRotation = handRotation,
                    Pole = state.ElbowPosition - state.ShoulderPosition,
                    FallbackPole = vElbow - vanillaShoulder.Position,
                    ForeArmAxis = hand.ForeArmAxis,
                    TwistShare = PlayerModelArmIk.DEFAULT_TWIST_SHARE
                };

                ArmResult result = PlayerModelArmIk.Place(state, target);

                sim.SetRotation(hand.ArmNode, result.ArmRotation);
                sim.SetRotation(hand.ForeArmNode, result.ForeArmRotation);
                sim.SetRotation(hand.HandNode, result.HandRotation);
                Vector3 after = sim.Middle(hand.KnuckleNodes);
                float upper = Vector3.Distance(state.ShoulderPosition, state.ElbowPosition);
                float fore = Vector3.Distance(state.ElbowPosition, state.WristPosition);
                float distance = (vAnchor - state.KnuckleOffsetFromWrist - state.ShoulderPosition).Length();
                tried++;
                (after - result.ReachedAnchor).Length().Should().BeLessThan(2e-3f, $"{name}: the arm says where its knuckles are");
                // the hand is turned the way the vanilla hand is turned
                Math.Abs(Quaternion.Dot(sim.Rotation(hand.HandNode), handRotation)).Should().BeApproximately(1f, 1e-3f);
                if (distance >= Math.Abs(upper - fore) + 0.003f && distance <= upper + fore - 0.002f)
                {
                    reached++;
                    float apart = (after - vAnchor).Length();
                    worst = Math.Max(worst, apart);
                    apart.Should().BeLessThan(2e-3f, $"{name}, {hand.Side} hand, pose {pose}: the knuckles of the model are where the vanilla knuckles are");
                }
            }
        }
        return (tried, reached, worst);
    }

    [TestMethod]
    public void TheHandsLandOnTheVanillaGripsForModelsOfAnyProportions()
    {
        foreach ((string name, PlayerModelRest rest) in Models())
        {
            (int tried, int reached, float worstApart) = SimulateGrips(name, rest, true, 150, 17);

            Console.WriteLine($"{name}: {reached} of {tried} random poses of the vanilla arm have a grip inside the reach of the arm, stretched (worst {worstApart * 1000f:F2} mm)");
            worstApart.Should().BeLessThan(2e-3f);
            reached.Should().BeGreaterThan(tried / 8, $"{name}: a good part of the vanilla poses are inside the reach of this model's arms");
        }
    }

    [TestMethod]
    public void AnArmThatIsTooShortIsStretchedToTheGripSoThatMoreGripsAreReached()
    {
        PlayerModelRest reference = RealRest();
        foreach ((string name, PlayerModelRest rest) in new[] { ("arms 20% shorter", RestVariants.WithLimbLengths(reference, 0.8f, 0.8f, 1f)), ("arms 12% shorter, shoulders low", RestVariants.WithLimbLengths(reference, 0.88f, 0.88f, 1f)) })
        {
            (int tried, int without, float withoutWorst) = SimulateGrips(name, rest, false, 150, 29);
            (int triedWith, int with, float worst) = SimulateGrips(name, rest, true, 150, 29);

            triedWith.Should().Be(tried);
            withoutWorst.Should().BeLessThan(2e-3f);
            Console.WriteLine($"{name}: {without} of {tried} grips reached as it is, {with} with the arm stretched (worst {worst * 1000f:F2} mm)");
            with.Should().BeGreaterThan(without, $"{name}: a stretch brings grips within reach");
            with.Should().BeGreaterThan(tried * 3 / 5, $"{name}: and most of them");
        }
    }

    [TestMethod]
    public void TheHandSettingsStartWithEverythingOnAndNothingMoved()
    {
        PlayerModelHandSettings settings = new();

        settings.Enabled.Should().BeTrue();
        settings.Fingers.Should().BeTrue();
        settings.Thumbs.Should().BeTrue();
        settings.ToolTargets.Should().BeTrue("a hand follows the hand target of the tool it holds");
        settings.MaxStretch.Should().Be(PlayerModelHandSettings.DEFAULT_MAX_STRETCH);
        settings.PdaPullBack.Should().Be(0f);
        settings.AnchorForward.Should().Be(0f);
        settings.AnchorPalm.Should().Be(0f);
        settings.AnchorThumb.Should().Be(0f);
        settings.RollTrimDegrees.Should().Be(0f);
        settings.TwistShare.Should().Be(PlayerModelArmIk.DEFAULT_TWIST_SHARE);
    }

    [TestMethod]
    public void TwistBetweenIsTheAngleThatTurnsOneDirectionOntoAnotherAboutAnAxis()
    {
        PlayerModelHands.TwistBetween(Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ).Should().BeApproximately(-90f, 1e-3f, "a turn about +Y takes Z to X, so X to Z is the other way");
        PlayerModelHands.TwistBetween(Vector3.UnitY, Vector3.UnitX, Vector3.UnitX).Should().BeApproximately(0f, 1e-3f);
        PlayerModelHands.TwistBetween(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX).Should().Be(0f, "a direction along the axis has no twist");
        Random random = new(5);
        for (int i = 0; i < 200; i++)
        {
            Vector3 axis = Vector3.Normalize(new Vector3(Between(random, -1f, 1f), Between(random, -1f, 1f), Between(random, -1f, 1f)));
            Vector3 from = Across(axis, new Vector3(Between(random, -1f, 1f), Between(random, -1f, 1f), Between(random, -1f, 1f)));
            if (from.Length() < 0.1f)
            {
                continue;
            }
            float angle = Between(random, -170f, 170f);
            Vector3 to = Vector3.Transform(from, PlayerModelArmIk.AxisAngle(axis, angle));

            PlayerModelHands.TwistBetween(axis, from, to).Should().BeApproximately(angle, 0.05f);
        }
    }
}
