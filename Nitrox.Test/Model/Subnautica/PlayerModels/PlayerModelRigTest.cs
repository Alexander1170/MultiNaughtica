using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The arithmetic of the pose copy (docs/coop/PLAYER_MODELS.md, "The pose copy"): how the rest pose of the Mixamo skeleton is lined up with the bind pose of the vanilla body, so that the model's bone follows the
/// 2026-10-29 dumped (<see cref="VanillaBindFixture" />).
/// </summary>
[TestClass]
public class PlayerModelRigTest
{
    private static readonly Regex bindLine = new(@"^(\S+) pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\) rot=\(([-\d.]+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\)$");

    private static float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static Dictionary<string, PlayerModelBind> VanillaBind()
    {
        Dictionary<string, PlayerModelBind> bind = new();
        foreach (string raw in VanillaBindFixture.BIND.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            Match m = bindLine.Match(line);
            m.Success.Should().BeTrue($"the fixture line '{line}' has to be readable");
            bind[m.Groups[1].Value] = new PlayerModelBind(new Vector3(F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value)),
                                                          Quaternion.Normalize(new Quaternion(F(m.Groups[5].Value), F(m.Groups[6].Value), F(m.Groups[7].Value), F(m.Groups[8].Value))));
        }
        return bind;
    }

    private static (Vector3 Position, Quaternion Rotation) VanillaAttach()
    {
        Match m = Regex.Match(VanillaBindFixture.ATTACH, @"pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\) rot=\(([-\d.]+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\)");
        m.Success.Should().BeTrue();
        return (new Vector3(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value)),
                Quaternion.Normalize(new Quaternion(F(m.Groups[4].Value), F(m.Groups[5].Value), F(m.Groups[6].Value), F(m.Groups[7].Value))));
    }

    private static GlbModel RealModel()
    {
        return RigFixture.Reference();
    }

    private static Quaternion RandomRotation(Random random) => Quaternion.CreateFromYawPitchRoll((float)(random.NextDouble() * 6.2), (float)(random.NextDouble() * 3.1 - 1.5), (float)(random.NextDouble() * 6.2));

    private static void AssertSameDirection(Vector3 actual, Vector3 expected, float toleranceDegrees = 0.1f)
    {
        float cos = Vector3.Dot(Vector3.Normalize(actual), Vector3.Normalize(expected));
        float degrees = (float)(Math.Acos(Math.Min(1f, cos)) * 180.0 / Math.PI);
        degrees.Should().BeLessThan(toleranceDegrees, $"{actual} should point where {expected} points");
    }

    [TestMethod]
    public void FromToTurnsOneDirectionOntoAnotherTheShortWay()
    {
        Random random = new(11);
        for (int i = 0; i < 200; i++)
        {
            Vector3 from = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
            Vector3 to = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
            Quaternion q = PlayerModelRig.FromTo(from, to);

            AssertSameDirection(Vector3.Transform(from, q), to);
            (q.Length()).Should().BeApproximately(1f, 1e-4f);
        }
    }

    [TestMethod]
    public void FromToHandlesTheSameAndTheOppositeDirection()
    {
        PlayerModelRig.FromTo(Vector3.UnitY, Vector3.UnitY).Should().Be(Quaternion.Identity);
        PlayerModelRig.FromTo(Vector3.Zero, Vector3.UnitY).Should().Be(Quaternion.Identity);

        Quaternion half = PlayerModelRig.FromTo(Vector3.UnitY, -Vector3.UnitY);
        AssertSameDirection(Vector3.Transform(Vector3.UnitY, half), -Vector3.UnitY);
        PlayerModelRig.AngleDegrees(half).Should().BeApproximately(180f, 0.1f);
        PlayerModelRig.AngleDegrees(Quaternion.Identity).Should().Be(0f);
    }

    [TestMethod]
    public void TheModelBoneFollowsTheVanillaBoneWhateverThePose()
    {
        Random random = new(5);
        for (int i = 0; i < 100; i++)
        {
            Quaternion vanillaBind = RandomRotation(random);
            Quaternion mixamoRest = RandomRotation(random);
            Vector3 vanillaAim = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
            Vector3 mixamoAim = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
            Quaternion fix = PlayerModelRig.FromTo(mixamoAim, vanillaAim);
            Quaternion k = PlayerModelRig.Constant(vanillaBind, mixamoRest, fix);
            // the direction that the model's bone has in its own frame (it points along mixamoAim at rest)
            Vector3 aimInBone = Vector3.Transform(mixamoAim, Quaternion.Inverse(mixamoRest));

            for (int pose = 0; pose < 10; pose++)
            {
                Quaternion delta = RandomRotation(random);
                Quaternion vanillaNow = delta * vanillaBind;
                Quaternion modelNow = Quaternion.Normalize(vanillaNow * k);

                // the vanilla bone now points along delta * vanillaAim: so does the model's
                AssertSameDirection(Vector3.Transform(aimInBone, modelNow), Vector3.Transform(vanillaAim, delta));
            }
        }
    }

    [TestMethod]
    public void WithoutAnyMovementTheModelBoneIsTheRestPoseTurnedByTheFix()
    {
        Quaternion vanillaBind = RandomRotation(new Random(3));
        Quaternion mixamoRest = RandomRotation(new Random(4));
        Quaternion fix = PlayerModelRig.FromTo(Vector3.UnitX, new Vector3(1f, -1f, 0f));

        Quaternion k = PlayerModelRig.Constant(vanillaBind, mixamoRest, fix);

        Quaternion atBind = Quaternion.Normalize(vanillaBind * k);
        Math.Abs(Quaternion.Dot(atBind, Quaternion.Normalize(fix * mixamoRest))).Should().BeApproximately(1f, 1e-4f);
    }

    [TestMethod]
    public void TheFactorsDifferBeyondEightPercent()
    {
        PlayerModelRig.FactorsDiffer(1.0155f, 1.0745f).Should().BeFalse("5.8% is inside the tolerance");
        PlayerModelRig.FactorsDiffer(1f, 1.081f).Should().BeTrue();
        PlayerModelRig.FactorsDiffer(1f, 0.919f).Should().BeTrue();
        PlayerModelRig.FactorsDiffer(1f, 1.079f).Should().BeFalse();
        PlayerModelRig.FactorsDiffer(0f, 1f).Should().BeFalse("no hips factor, nothing to compare");
    }

    [TestMethod]
    public void TheBoneMapHasNoDuplicatesAndAnAimForEveryBoneButTheHead()
    {
        PlayerModelRig.Pairs.Select(pair => pair.Mixamo).Should().OnlyHaveUniqueItems();
        PlayerModelRig.Pairs.Select(pair => pair.Vanilla).Should().OnlyHaveUniqueItems();
        PlayerModelRig.Pairs.Where(pair => pair.MixamoAim == null || pair.VanillaAim == null).Select(pair => pair.Mixamo).Should().Equal("Head");
        PlayerModelRig.Pairs.Should().HaveCount(22);
        PlayerModelRig.VanillaBoneNames.Should().Contain(new[] { "hips", "head_rig", "hand_R", "spine_3", "chest", "Flipper1_L", "toe_R" });
    }

    [TestMethod]
    public void TheRestPoseIsMirroredIntoTheGamesSpace()
    {
        GlbModel model = RealModel();

        (Vector3[] positions, Quaternion[] rotations) = PlayerModelRig.MixamoRest(model);

        int hips = model.FindNode("mixamorig:Hips");
        int leftHand = model.FindNode("mixamorig:LeftHand");
        int rightHand = model.FindNode("mixamorig:RightHand");
        positions[hips].Y.Should().BeApproximately(0.969f, 0.002f);
        positions[hips].X.Should().BeApproximately(0.019f, 0.002f, "glTF has the hips at x = -0.019, the mirror gives +0.019");
        positions[leftHand].X.Should().BeLessThan(-0.5f, "the left of a character that faces +Z is at -X in the game");
        positions[rightHand].X.Should().BeGreaterThan(0.5f);
        positions[model.FindNode("mixamorig:HeadTop_End")].Y.Should().BeApproximately(1.693f, 0.002f);
        rotations.Should().OnlyContain(q => Math.Abs(q.Length() - 1f) < 1e-3f);
    }

    [TestMethod]
    public void TheBoneMapNamesNodesThatTheReferenceSkeletonHasInTheOrderOfTheTree()
    {
        GlbModel model = RealModel();

        List<int> seen = [];
        foreach (PlayerModelBonePair pair in PlayerModelRig.Pairs)
        {
            int node = PlayerModelRig.FindNode(model, pair.Mixamo);
            node.Should().BeGreaterThanOrEqualTo(0, $"{pair.Mixamo} has to be in the file");
            if (pair.MixamoAim != null)
            {
                PlayerModelRig.FindNode(model, pair.MixamoAim).Should().BeGreaterThanOrEqualTo(0, $"{pair.MixamoAim} has to be in the file");
            }
            // a parent that is mapped too has been listed before its child
            for (int parent = model.Nodes[node].Parent; parent >= 0; parent = model.Nodes[parent].Parent)
            {
                bool mapped = PlayerModelRig.Pairs.Any(other => PlayerModelRig.FindNode(model, other.Mixamo) == parent);
                if (mapped)
                {
                    seen.Should().Contain(parent, $"{pair.Mixamo} comes after its parent {model.Nodes[parent].Name}");
                    break;
                }
            }
            seen.Add(node);
        }
    }

    [TestMethod]
    public void TheReferenceSkeletonIsCalibratedAgainstTheRealVanillaBody()
    {
        GlbModel model = RealModel();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaAttach();

        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model, VanillaBind(), attachPosition, attachRotation);

        Console.WriteLine($"calibration: hips {calibration.HipsFactor:F4}, head {calibration.HeadFactor:F4}, scale {calibration.Scale:F4}, attach {calibration.AttachPosition} {calibration.AttachRotation}");
        foreach (PlayerModelBoneConstant bone in calibration.Bones)
        {
            Console.WriteLine($"  {bone.Mixamo,-14} <- {bone.Vanilla,-11} fix {bone.FixDegrees,6:F1} deg");
        }

        calibration.Missing.Should().BeEmpty();
        calibration.Bones.Should().HaveCount(22);
        calibration.HipsFactor.Should().BeApproximately(1.0155f, 0.003f, "0.984 m of the vanilla hips over 0.969 m of the model's");
        calibration.HeadFactor.Should().BeApproximately(1.0745f, 0.004f, "1.615 m of the vanilla head over 1.503 m of the model's");
        calibration.FactorsDiffer.Should().BeFalse("the two sizings differ by 5.8%, inside the 8% that ask for a picture of each");
        calibration.Scale.Should().Be(calibration.HipsFactor);
        calibration.AttachHandNode.Should().Be(model.FindNode("mixamorig:RightHand"));
        // a bone that was mapped to the wrong side would need a turn of more than 90 degrees to be lined up; the arms really are 25 to 30 degrees off, the rest less
        calibration.Bones.Should().OnlyContain(bone => bone.FixDegrees < 60f, "every rest bone is close to the vanilla one");
        calibration.Bones.Single(bone => bone.Mixamo == "Head").FixDegrees.Should().Be(0f);

        PlayerModelCalibration byHead = PlayerModelRig.Calibrate(model, VanillaBind(), attachPosition, attachRotation, PlayerModelScaleSource.Head);
        byHead.Scale.Should().Be(byHead.HeadFactor);
        byHead.HipsFactor.Should().Be(calibration.HipsFactor);
    }

    [TestMethod]
    public void TheLimbsOfTheModelPointWhereTheVanillaLimbsPointAtBind()
    {
        GlbModel model = RealModel();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaAttach();
        Dictionary<string, PlayerModelBind> bind = VanillaBind();
        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model, bind, attachPosition, attachRotation);
        (Vector3[] restPosition, Quaternion[] restRotation) = PlayerModelRig.MixamoRest(model);

        foreach (PlayerModelBonePair pair in PlayerModelRig.Pairs.Where(pair => pair.MixamoAim != null))
        {
            PlayerModelBoneConstant bone = calibration.Bones.Single(candidate => candidate.Mixamo == pair.Mixamo);
            int aimNode = PlayerModelRig.FindNode(model, pair.MixamoAim);
            Vector3 aimInBone = Vector3.Transform(restPosition[aimNode] - restPosition[bone.MixamoNode], Quaternion.Inverse(restRotation[bone.MixamoNode]));

            // the vanilla bone at bind is its own bind rotation: the model's bone is that times K
            Quaternion modelAtBind = bind[pair.Vanilla].Rotation * bone.K;
            Vector3 vanillaAim = bind[pair.VanillaAim!].Position - bind[pair.Vanilla].Position;

            AssertSameDirection(Vector3.Transform(aimInBone, modelAtBind), vanillaAim, 0.5f);
        }
    }

    [TestMethod]
    public void TheLeftAndTheRightOfTheModelAreLinedUpAlike()
    {
        GlbModel model = RealModel();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaAttach();

        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model, VanillaBind(), attachPosition, attachRotation);

        foreach (string part in new[] { "Shoulder", "Arm", "ForeArm", "Hand", "UpLeg", "Leg", "Foot", "ToeBase" })
        {
            float left = calibration.Bones.Single(bone => bone.Mixamo == "Left" + part).FixDegrees;
            float right = calibration.Bones.Single(bone => bone.Mixamo == "Right" + part).FixDegrees;
            right.Should().BeApproximately(left, 8f, $"the two {part} start from mirror images of one another");
        }
    }

    [TestMethod]
    public void TheItemAttachPointKeepsItsPlaceAndItsAimInTheWorldAtBind()
    {
        GlbModel model = RealModel();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaAttach();
        Dictionary<string, PlayerModelBind> bind = VanillaBind();
        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model, bind, attachPosition, attachRotation);

        PlayerModelBoneConstant hand = calibration.Bones.Single(bone => bone.Mixamo == "RightHand");
        Quaternion handNow = bind["hand_R"].Rotation * hand.K;

        // the offset of the attach point from the hand, in the world, is the vanilla one (in metres: the model's units times the scale)
        Vector3 offset = Vector3.Transform(calibration.AttachPosition * calibration.Scale, handNow);
        Vector3 vanillaOffset = Vector3.Transform(attachPosition, bind["hand_R"].Rotation);
        (offset - vanillaOffset).Length().Should().BeLessThan(0.002f);
        // and so is its orientation
        Quaternion attachNow = Quaternion.Normalize(handNow * calibration.AttachRotation);
        Quaternion vanillaNow = Quaternion.Normalize(bind["hand_R"].Rotation * attachRotation);
        Math.Abs(Quaternion.Dot(attachNow, vanillaNow)).Should().BeApproximately(1f, 1e-3f);
    }

    [TestMethod]
    public void OnlyTheSpineAndTheNeckAreTurnedAlongTheirStretch()
    {
        PlayerModelRig.Pairs.Where(pair => pair.FollowsSegment).Select(pair => pair.Mixamo).Should().Equal("Hips", "Spine", "Spine1", "Spine2", "Neck");
    }

    [TestMethod]
    public void TheLocalAimOfABoneIsTheDirectionToItsAimBoneInTheFrameOfTheBone()
    {
        GlbModel model = RealModel();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaAttach();
        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model, VanillaBind(), attachPosition, attachRotation);
        (Vector3[] restPosition, Quaternion[] restRotation) = PlayerModelRig.MixamoRest(model);

        foreach (PlayerModelBoneConstant bone in calibration.Bones.Where(candidate => candidate.MixamoAimNode >= 0))
        {
            Vector3 toAim = Vector3.Normalize(restPosition[bone.MixamoAimNode] - restPosition[bone.MixamoNode]);
            AssertSameDirection(Vector3.Transform(bone.LocalAim, restRotation[bone.MixamoNode]), toAim, 0.05f);
            (bone.LocalAim.Length()).Should().BeApproximately(1f, 1e-4f);
        }
        calibration.Bones.Where(bone => bone.FollowsSegment).Select(bone => bone.Mixamo).Should().Equal("Hips", "Spine", "Spine1", "Spine2", "Neck");
        calibration.Bones.Where(bone => bone.Mixamo == "Head").Should().OnlyContain(bone => bone.LocalAim == Vector3.Zero && bone.MixamoAimNode == -1);
    }

    [TestMethod]
    public void ABentSpineKeepsItsStretchesWhereTheVanillaOnesPointWithTheAimCorrection()
    {
        // the model's bone turned on from the copied rotation until its stretch points where the wanted direction points (what CustomPlayerModel.Pose does for the spine)
        Random random = new(9);
        for (int i = 0; i < 200; i++)
        {
            Quaternion copied = RandomRotation(random);
            Vector3 localAim = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() + 0.5f, (float)random.NextDouble() - 0.5f));
            Vector3 wanted = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));

            Quaternion corrected = Quaternion.Normalize(PlayerModelRig.FromTo(Vector3.Transform(localAim, copied), wanted) * copied);

            AssertSameDirection(Vector3.Transform(localAim, corrected), wanted);
        }
    }

    [TestMethod]
    public void ABoneThatTheVanillaBodyLacksIsNamedAndLeftAtRest()
    {
        GlbModel model = RealModel();
        (Vector3 attachPosition, Quaternion attachRotation) = VanillaAttach();
        Dictionary<string, PlayerModelBind> bind = VanillaBind();
        bind.Remove("calf_L");

        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model, bind, attachPosition, attachRotation);

        calibration.Missing.Should().Contain(text => text.Contains("calf_L"));
        calibration.Bones.Should().NotContain(bone => bone.Mixamo == "LeftLeg");
        calibration.Bones.Should().HaveCount(21);
    }
}
