using System.Numerics;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The keys of a clip (docs/coop/PLAYER_MODELS.md, emotes): the interpolation between two keys (rotations on the shortest arc), what happens before the first and after the last key, what a sample fills in,
/// and the clips of a small synthetic file.
/// </summary>
[TestClass]
public class GlbClipDataTest
{
    private static GlbTrack RotationTrack(int node, bool step, params (float Time, Quaternion Value)[] keys)
    {
        return new GlbTrack(node, true, keys.Select(key => key.Time).ToArray(), keys.SelectMany(key => new[] { key.Value.X, key.Value.Y, key.Value.Z, key.Value.W }).ToArray(), step);
    }

    private static GlbTrack TranslationTrack(int node, bool step, params (float Time, Vector3 Value)[] keys)
    {
        return new GlbTrack(node, false, keys.Select(key => key.Time).ToArray(), keys.SelectMany(key => new[] { key.Value.X, key.Value.Y, key.Value.Z }).ToArray(), step);
    }

    private static Quaternion AboutY(float degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * (float)Math.PI / 180f);

    private static void SameRotation(Quaternion actual, Quaternion expected)
    {
        Math.Abs(Quaternion.Dot(actual, expected)).Should().BeApproximately(1f, 1e-5f, $"{actual} should be {expected}");
    }

    [TestMethod]
    public void ARotationBetweenTwoKeysIsTheMiddleOfTheArc()
    {
        GlbTrack track = RotationTrack(1, false, (0f, Quaternion.Identity), (1f, AboutY(90f)));

        SameRotation(track.SampleRotation(0.5f), AboutY(45f));
        SameRotation(track.SampleRotation(0.25f), AboutY(22.5f));
    }

    [TestMethod]
    public void ARotationTakesTheShortWayRoundWhenTheSecondKeyIsTheNegatedQuaternion()
    {
        Quaternion negated = Quaternion.Negate(AboutY(90f));
        GlbTrack track = RotationTrack(1, false, (0f, Quaternion.Identity), (1f, negated));

        SameRotation(track.SampleRotation(0.5f), AboutY(45f));
    }

    [TestMethod]
    public void BeforeTheFirstKeyAndAfterTheLastTheKeysAtTheEndsHold()
    {
        GlbTrack track = RotationTrack(1, false, (1f, AboutY(10f)), (2f, AboutY(50f)));

        SameRotation(track.SampleRotation(0f), AboutY(10f));
        SameRotation(track.SampleRotation(-5f), AboutY(10f));
        SameRotation(track.SampleRotation(2f), AboutY(50f));
        SameRotation(track.SampleRotation(99f), AboutY(50f));
    }

    [TestMethod]
    public void AStepTrackStaysOnTheEarlierKeyUntilTheNextOne()
    {
        GlbTrack track = RotationTrack(1, true, (0f, Quaternion.Identity), (1f, AboutY(90f)), (2f, AboutY(180f)));

        SameRotation(track.SampleRotation(0.99f), Quaternion.Identity);
        SameRotation(track.SampleRotation(1f), AboutY(90f));
        SameRotation(track.SampleRotation(1.5f), AboutY(90f));
    }

    [TestMethod]
    public void ATranslationIsInterpolatedLinearlyBetweenTheKeysThatSurroundTheTime()
    {
        GlbTrack track = TranslationTrack(2, false, (0f, new Vector3(0f, 1f, 0f)), (1f, new Vector3(2f, 3f, 0f)), (3f, new Vector3(2f, 3f, 8f)));

        track.SampleTranslation(0.5f).Should().Be(new Vector3(1f, 2f, 0f));
        track.SampleTranslation(2f).Should().Be(new Vector3(2f, 3f, 4f));
        track.SampleTranslation(10f).Should().Be(new Vector3(2f, 3f, 8f));
        track.FirstTranslation().Should().Be(new Vector3(0f, 1f, 0f));
    }

    [TestMethod]
    public void ATrackWithOneKeyIsConstantAndATrackWithoutKeysIsTheIdentity()
    {
        RotationTrack(1, false, (0.7f, AboutY(30f))).SampleRotation(5f).Should().Be(AboutY(30f));
        new GlbTrack(1, true, [], [], false).SampleRotation(1f).Should().Be(Quaternion.Identity);
        new GlbTrack(1, false, [], [], false).SampleTranslation(1f).Should().Be(Vector3.Zero);
    }

    [TestMethod]
    public void ASampleFillsInOnlyTheNodesThatTheClipMovesAndSaysWhich()
    {
        GlbClipData clip = new("wave", 2f, [RotationTrack(1, false, (0f, Quaternion.Identity), (2f, AboutY(90f))), TranslationTrack(2, false, (0f, Vector3.Zero), (2f, Vector3.One))], 4);
        Quaternion[] rotations = new Quaternion[4];
        Vector3[] translations = new Vector3[4];
        bool[] hasRotation = [true, true, true, true];
        bool[] hasTranslation = [true, true, true, true];
        rotations[3] = AboutY(7f);

        clip.Sample(1f, rotations, hasRotation, translations, hasTranslation);

        hasRotation.Should().Equal(false, true, false, false);
        hasTranslation.Should().Equal(false, false, true, false);
        SameRotation(rotations[1], AboutY(45f));
        translations[2].Should().Be(new Vector3(0.5f, 0.5f, 0.5f));
        SameRotation(rotations[3], AboutY(7f));
    }

    [TestMethod]
    public void EveryClipCanBeRead()
    {
        GlbModel model = GlbModel.Read(GlbDocument.Parse(SyntheticGlb.Build()));

        for (int index = 0; index < model.Clips.Count; index++)
        {
            GlbClipData clip = model.ReadClipData(index);

            clip.Name.Should().Be(model.Clips[index].Name);
            clip.Duration.Should().BeApproximately(model.Clips[index].Duration, 1e-3f);
            clip.Tracks.Should().NotBeEmpty();
        }
    }

    [TestMethod]
    public void ANameThatIsNotAClipIsNotFoundAndAnIndexThatIsNotOneIsAnError()
    {
        GlbModel model = GlbModel.Read(GlbDocument.Parse(SyntheticGlb.Build()));

        model.FindClip("no_such_clip").Should().Be(-1);
        FluentActions.Invoking(() => model.ReadClipData(99)).Should().Throw<GlbException>();
        FluentActions.Invoking(() => model.ReadClipData(-1)).Should().Throw<GlbException>();
    }
}
