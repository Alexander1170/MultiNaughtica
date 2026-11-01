using System.Numerics;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The emotes of one model played on models that bring none (docs/coop/PLAYER_MODELS.md, "Shared emotes"): models with the same Mixamo skeleton can have another rest pose and other proportions (one may stand
/// in a T), so the clip is not copied bone by bone: every bone that both skeletons have takes the world rotation that the source's bone has, times a constant that lines the two rest poses up.
/// </summary>
[TestClass]
public class PlayerModelEmoteRetargetTest
{

    /// <summary>The models that play the source's emotes in these tests: two other rigs, and models made from the reference skeleton with other proportions and another rest pose (taller, shorter, longer and shorter arms, wider shoulders, a T, arms down).</summary>
    private static IReadOnlyList<(string Name, PlayerModelRest Skeleton)> Targets()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        return
        [
            ("a rig with helper bones", RigFixture.ExtrasRest()),
            ("a second rig in a T", RigFixture.AlternateRest()),
            ("taller", RestVariants.Scaled(reference, 1.15f)),
            ("shorter", RestVariants.Scaled(reference, 0.85f)),
            ("longer arms", RestVariants.WithLimbLengths(reference, 1.25f, 1.25f, 1f)),
            ("shorter arms", RestVariants.WithLimbLengths(reference, 0.8f, 0.8f, 1f)),
            ("wider shoulders", RestVariants.WithShoulderWidth(reference, 1.5f)),
            ("arms raised into a T", RestVariants.WithArmsRaised(reference, 35f)),
            ("arms down along the body", RestVariants.WithArmsRaised(reference, -20f))
        ];
    }

    private static float Between(Random random, float low, float high) => low + ((float)random.NextDouble() * (high - low));

    private static float Angle(Quaternion a, Quaternion b)
    {
        float dot = Math.Min(1f, Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))));
        return (float)(2.0 * Math.Acos(dot) * 180.0 / Math.PI);
    }

    private static float Angle(Vector3 a, Vector3 b)
    {
        float cos = Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b))));
        return (float)(Math.Acos(cos) * 180.0 / Math.PI);
    }

    [TestMethod]
    public void EveryModelWithTheMixamoSkeletonSharesTheBodyAndTheFingersWithTheReferenceSkeleton()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        foreach ((string file, PlayerModelRest target) in new[]
                 {
                     ("the reference skeleton", RigFixture.ReferenceRest()), ("a rig with helper bones", RigFixture.ExtrasRest()),
                     ("a second rig", RigFixture.AlternateRest())
                 })
        {
            PlayerModelEmoteRetarget? retarget = PlayerModelEmoteRetarget.Create(reference, PlayerModelBoneNames.Mixamo, target, PlayerModelBoneNames.Mixamo, out string reason);

            retarget.Should().NotBeNull($"{file}: {reason}");
            int expected = PlayerModelRig.Pairs.Count + (2 * PlayerModelHands.FINGER_BONES_PER_HAND);
            retarget!.MappedBones.Should().Be(expected, file);
            retarget.DrivenNodes.Should().HaveCount(expected).And.OnlyHaveUniqueItems();
            PlayerModelEmoteRetarget.SharedBoneCount(reference, PlayerModelBoneNames.Mixamo, target, PlayerModelBoneNames.Mixamo).Should().Be(expected);
        }
    }

    [TestMethod]
    public void AtTheRestPoseOfTheSourceTheBonesOfTheOtherModelPointWhereTheBonesOfTheSourcePoint()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        foreach ((string file, PlayerModelRest target) in Targets())
        {
            PlayerModelEmoteRetarget retarget = PlayerModelEmoteRetarget.Create(reference, PlayerModelBoneNames.Mixamo, target, PlayerModelBoneNames.Mixamo, out _)!;
            GlbClipData holdsRest = new("rest", 1f, [], reference.Count);
            Quaternion[] locals = new Quaternion[target.Count];
            bool[] has = new bool[target.Count];

            retarget.Sample(holdsRest, 0f, locals, has, out _, out bool hasHips);

            hasHips.Should().BeFalse("a clip that has no track for the hips gives no height");
            has.Count(flag => flag).Should().Be(retarget.MappedBones);
            RigSim sim = new(target, 1f);
            for (int node = 0; node < target.Count; node++)
            {
                if (has[node])
                {
                    sim.SetLocalRotation(node, locals[node]);
                }
            }
            foreach (PlayerModelBonePair pair in PlayerModelRig.Pairs.Where(candidate => candidate.MixamoAim != null))
            {
                Vector3 referenceDirection = reference.Position(reference.Find($"mixamorig:{pair.MixamoAim}")) - reference.Position(reference.Find($"mixamorig:{pair.Mixamo}"));
                int bone = target.Find($"mixamorig:{pair.Mixamo}");
                int aim = target.Find($"mixamorig:{pair.MixamoAim}");
                Angle(sim.Position(aim) - sim.Position(bone), referenceDirection).Should().BeLessThan(0.5f, $"{file}: {pair.Mixamo} points to {pair.MixamoAim} as the source's does");
            }
        }
    }

    [TestMethod]
    public void ATurnOfABoneOfTheSourceIsTheSameTurnInTheWorldOnTheOtherModel()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        Random random = new(31);
        foreach ((string file, PlayerModelRest target) in Targets())
        {
            PlayerModelEmoteRetarget retarget = PlayerModelEmoteRetarget.Create(reference, PlayerModelBoneNames.Mixamo, target, PlayerModelBoneNames.Mixamo, out _)!;
            Quaternion[] atRest = new Quaternion[target.Count];
            bool[] has = new bool[target.Count];
            retarget.Sample(new GlbClipData("rest", 1f, [], reference.Count), 0f, atRest, has, out _, out _);
            RigSim restSim = new(target, 1f);
            RigSim referenceRest = new(reference, 1f);
            for (int node = 0; node < target.Count; node++)
            {
                if (has[node])
                {
                    restSim.SetLocalRotation(node, atRest[node]);
                }
            }

            for (int pose = 0; pose < 20; pose++)
            {
                // a clip that turns some bones of the source by random angles (in glTF space, as the file has them)
                RigSim referencePosed = new(reference, 1f);
                List<GlbTrack> tracks = [];
                foreach (int node in Enumerable.Range(0, reference.Count).Where(candidate => random.NextDouble() < 0.4))
                {
                    Quaternion turn = Quaternion.CreateFromYawPitchRoll(Between(random, -1f, 1f), Between(random, -1f, 1f), Between(random, -1f, 1f));
                    // the rest rotation of the node in the file, then the turn on top of it
                    Quaternion restLocal = RestLocal(reference, node);
                    Quaternion local = Quaternion.Normalize(restLocal * turn);
                    // the file stores glTF space: the mirror of the game's (x, y, z, w) -> (x, -y, -z, w) is its own inverse
                    tracks.Add(new GlbTrack(node, true, [0f, 1f], [local.X, -local.Y, -local.Z, local.W, local.X, -local.Y, -local.Z, local.W], false));
                    referencePosed.SetLocalRotation(node, local);
                }
                GlbClipData clip = new("random", 1f, tracks, reference.Count);
                Quaternion[] locals = new Quaternion[target.Count];
                bool[] hasNow = new bool[target.Count];
                retarget.Sample(clip, 0.5f, locals, hasNow, out _, out _);
                RigSim targetPosed = new(target, 1f);
                for (int node = 0; node < target.Count; node++)
                {
                    if (hasNow[node])
                    {
                        targetPosed.SetLocalRotation(node, locals[node]);
                    }
                }

                // every shared bone has been turned in the world by what the source's bone was turned by, from where it was at rest
                foreach (string canonical in PlayerModelRig.Pairs.Select(pair => pair.Mixamo))
                {
                    int s = reference.Find($"mixamorig:{canonical}");
                    int t = target.Find($"mixamorig:{canonical}");
                    Quaternion referenceDelta = referencePosed.Rotation(s) * Quaternion.Inverse(referenceRest.Rotation(s));
                    Quaternion targetDelta = targetPosed.Rotation(t) * Quaternion.Inverse(restSim.Rotation(t));
                    Angle(referenceDelta, targetDelta).Should().BeLessThan(0.1f, $"{file}: {canonical} turns as the source's does");
                }
            }
        }
    }

    private static Quaternion RestLocal(PlayerModelRest rest, int node)
    {
        int parent = rest.Parent(node);
        return parent < 0 ? rest.Rotation(node) : Quaternion.Normalize(Quaternion.Inverse(rest.Rotation(parent)) * rest.Rotation(node));
    }

    [TestMethod]
    public void TheHeightOfTheHipsIsTheClipsScaledToTheHeightOfTheOtherModelsHips()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        PlayerModelRest max = RigFixture.AlternateRest();
        PlayerModelEmoteRetarget retarget = PlayerModelEmoteRetarget.Create(reference, PlayerModelBoneNames.Mixamo, max, PlayerModelBoneNames.Mixamo, out _)!;
        int referenceHips = reference.Find("mixamorig:Hips");
        // the clip says (in glTF space, where the height is y) that the hips are 0.9 m up at the start and 1.3 m at the end
        GlbClipData clip = new("jump", 1f, [new GlbTrack(referenceHips, false, [0f, 1f], [0f, 0.9f, 0f, 0f, 1.3f, 0f], false)], reference.Count);
        float ratio = max.Position(max.Find("mixamorig:Hips")).Y / reference.Position(referenceHips).Y;

        retarget.Sample(clip, 0f, new Quaternion[max.Count], new bool[max.Count], out float atStart, out bool hasHips);
        retarget.Sample(clip, 1f, new Quaternion[max.Count], new bool[max.Count], out float atEnd, out _);

        hasHips.Should().BeTrue();
        atStart.Should().BeApproximately(0.9f * ratio, 1e-4f);
        atEnd.Should().BeApproximately(1.3f * ratio, 1e-4f);
    }

    [TestMethod]
    public void TheBonesThatOnlyTheOtherModelHasStayAtRest()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        PlayerModelRest extras = RigFixture.ExtrasRest();
        PlayerModelEmoteRetarget retarget = PlayerModelEmoteRetarget.Create(reference, PlayerModelBoneNames.Mixamo, extras, PlayerModelBoneNames.Mixamo, out _)!;
        Quaternion[] locals = new Quaternion[extras.Count];
        bool[] has = new bool[extras.Count];

        retarget.Sample(new GlbClipData("rest", 1f, [], reference.Count), 0f, locals, has, out _, out _);

        int hair = extras.Find("Bone_029");
        int headFront = extras.Find("headfront");
        hair.Should().BeGreaterThanOrEqualTo(0);
        has[hair].Should().BeFalse("the hair is not a bone of the source: it keeps its rest pose in the frame of its parent");
        has[headFront].Should().BeFalse();
        retarget.DrivenNodes.Should().NotContain(hair);
        retarget.DrivenNodes.Should().OnlyContain(node => node >= 0 && node < extras.Count);
    }

    [TestMethod]
    public void TwoSkeletonsThatShareTooFewBonesAreNotRetargeted()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        PlayerModelRest strange = new(Enumerable.Range(0, reference.Count).Select(node => node < 5 ? reference.Name(node) : $"other_{node}").ToArray(), Enumerable.Range(0, reference.Count).Select(reference.Parent).ToArray(),
                                      Enumerable.Range(0, reference.Count).Select(reference.Position).ToArray(), Enumerable.Range(0, reference.Count).Select(reference.Rotation).ToArray());

        PlayerModelEmoteRetarget? retarget = PlayerModelEmoteRetarget.Create(reference, PlayerModelBoneNames.Mixamo, strange, PlayerModelBoneNames.Mixamo, out string reason);

        retarget.Should().BeNull();
        reason.Should().Contain("share only").And.Contain($"{PlayerModelEmoteRetarget.MIN_MAPPED_BONES}");
    }
}
