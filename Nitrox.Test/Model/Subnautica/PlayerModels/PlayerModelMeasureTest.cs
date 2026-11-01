using System.Numerics;
using Newtonsoft.Json.Linq;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The measurements of a skeleton (docs/coop/PLAYER_MODELS.md, "Tuning"): the diver's from its bind pose and a model's from its rest pose (the <c>measured</c> block of a profile is written by the Debug command <c>vufp calibrate</c>).
/// </summary>
[TestClass]
public class PlayerModelMeasureTest
{
    private static float Of(IReadOnlyList<KeyValuePair<string, float>> values, string name) => values.Single(value => value.Key == name).Value;

    [TestMethod]
    public void TheDiverIsMeasuredFromItsBindPose()
    {
        IReadOnlyList<KeyValuePair<string, float>> diver = PlayerModelMeasure.OfVanilla(VanillaBindReader.Bind());

        diver.Select(value => value.Key).Should().Equal(PlayerModelMeasure.Names);
        Of(diver, "hipsHeight").Should().BeApproximately(0.984f, 0.001f);
        Of(diver, "shoulderHeight").Should().BeApproximately(1.430f, 0.001f);
        Of(diver, "shoulderWidth").Should().BeApproximately(0.390f, 0.001f);
        Of(diver, "upperArm").Should().BeApproximately(0.2837f, 0.001f);
        Of(diver, "foreArm").Should().BeApproximately(0.2023f, 0.001f);
        Of(diver, "hand").Should().BeApproximately(0.131f, 0.001f);
        Of(diver, "armRestDegrees").Should().BeApproximately(65f, 1f, "the arms of the diver hang about 25 degrees off the vertical at bind");
    }

    [TestMethod]
    public void TheFixtureRigsAreMeasuredFromTheirRestPose()
    {
        IReadOnlyList<KeyValuePair<string, float>> reference = PlayerModelMeasure.OfModel(RigFixture.ReferenceRest(), PlayerModelBoneNames.Mixamo);
        IReadOnlyList<KeyValuePair<string, float>> extras = PlayerModelMeasure.OfModel(RigFixture.ExtrasRest(), PlayerModelBoneNames.Mixamo);
        IReadOnlyList<KeyValuePair<string, float>> alternate = PlayerModelMeasure.OfModel(RigFixture.AlternateRest(), PlayerModelBoneNames.Mixamo);

        foreach (IReadOnlyList<KeyValuePair<string, float>> model in new[] { reference, extras, alternate })
        {
            model.Select(value => value.Key).Should().Equal(PlayerModelMeasure.Names);
        }
        Of(reference, "hipsHeight").Should().BeApproximately(0.969f, 0.001f);
        Of(reference, "upperArm").Should().BeApproximately(0.2416f, 0.001f);
        Of(reference, "armRestDegrees").Should().BeApproximately(37.8f, 0.2f);
        Of(extras, "hipsHeight").Should().BeApproximately(0.9487f, 0.001f);
        Of(extras, "upperArm").Should().BeApproximately(0.1702f, 0.001f, "the upper arm is a third shorter than the reference's");
        Of(extras, "armRestDegrees").Should().BeApproximately(50.8f, 0.2f);
        Of(alternate, "hipsHeight").Should().BeApproximately(0.858f, 0.001f);
        Of(alternate, "armRestDegrees").Should().BeApproximately(0f, 0.1f, "it stands in a T");
        Of(alternate, "hand").Should().BeApproximately(0.1273f, 0.001f, "the mean of two hands that are 8.8 and 16.7 cm");
    }

    [TestMethod]
    public void ARigWithoutToesHasNoFootMeasure()
    {
        PlayerModelRest reference = RigFixture.ReferenceRest();
        PlayerModelRest noToes = new(Enumerable.Range(0, reference.Count).Select(node => reference.Name(node).Contains("ToeBase") ? $"gone_{node}" : reference.Name(node)).ToArray(), Enumerable.Range(0, reference.Count).Select(reference.Parent).ToArray(),
                                     Enumerable.Range(0, reference.Count).Select(reference.Position).ToArray(), Enumerable.Range(0, reference.Count).Select(reference.Rotation).ToArray());

        IReadOnlyList<KeyValuePair<string, float>> measured = PlayerModelMeasure.OfModel(noToes, PlayerModelBoneNames.Mixamo);

        measured.Select(value => value.Key).Should().NotContain("foot");
        measured.Select(value => value.Key).Should().Contain("upperArm");
    }

    [TestMethod]
    public void AnAngleBelowTheHorizonIsReadFromADirection()
    {
        PlayerModelMeasure.DegreesBelowHorizontal(new Vector3(1f, 0f, 0f)).Should().BeApproximately(0f, 1e-4f);
        PlayerModelMeasure.DegreesBelowHorizontal(new Vector3(0f, -1f, 0f)).Should().BeApproximately(90f, 1e-4f);
        PlayerModelMeasure.DegreesBelowHorizontal(new Vector3(1f, -1f, 0f)).Should().BeApproximately(45f, 1e-3f);
        PlayerModelMeasure.DegreesBelowHorizontal(new Vector3(0f, 1f, 0f)).Should().BeApproximately(-90f, 1e-4f);
        PlayerModelMeasure.DegreesBelowHorizontal(Vector3.Zero).Should().Be(0f);
    }

    [TestMethod]
    public void TheMeasuresAreComparedWithTheDiverAfterTheModelIsSized()
    {
        IReadOnlyList<KeyValuePair<string, float>> model = [new("upperArm", 0.17f), new("armRestDegrees", 50f)];
        IReadOnlyList<KeyValuePair<string, float>> diver = [new("upperArm", 0.2837f), new("armRestDegrees", 64f)];

        string text = PlayerModelMeasure.Compare(model, diver, 1.037f);

        text.Should().Contain("upperArm: model 0.170 m, sized 0.176 m, diver 0.284 m, model/diver 0.62");
        text.Should().Contain("armRestDegrees: model 50.000 deg, sized 50.000 deg, diver 64.000 deg");
    }

    [TestMethod]
    public void WritingTheMeasuredBlockChangesNothingElseInAProfile()
    {
        const string existing = """
            {
              "displayName": "Example",
              "notes": "by hand",
              "firstPerson": { "trims": { "LeftShoulder": 0.8 } },
              "somethingNew": [1, 2, 3],
              "measured": { "stale": 1 }
            }
            """;
        IReadOnlyList<KeyValuePair<string, float>> measured = [new("hipsHeight", 0.94871f), new("upperArm", 0.17f)];

        string text = PlayerModelProfileFile.WithMeasured(existing, measured);

        JObject written = JObject.Parse(text);
        ((string)written["displayName"]!).Should().Be("Example");
        ((string)written["notes"]!).Should().Be("by hand");
        ((double)written["firstPerson"]!["trims"]!["LeftShoulder"]!).Should().Be(0.8);
        written["somethingNew"]!.Count().Should().Be(3, "a field that this game does not know is kept");
        ((int)written["schema"]!).Should().Be(PlayerModelProfile.SCHEMA);
        ((JObject)written["measured"]!).Properties().Select(property => property.Name).Should().Equal("hipsHeight", "upperArm");
        ((double)written["measured"]!["hipsHeight"]!).Should().Be(0.9487, "the numbers are rounded to a tenth of a millimetre");
        text.Should().Contain("\n  \"displayName\"", "the file is indented");

        // a profile that is empty or is not JSON starts again
        JObject fresh = JObject.Parse(PlayerModelProfileFile.WithMeasured("", measured));
        fresh.Properties().Select(property => property.Name).Should().Equal("schema", "measured");
        JObject broken = JObject.Parse(PlayerModelProfileFile.WithMeasured("{ \"displayName\": ", measured));
        broken.Properties().Select(property => property.Name).Should().Equal("schema", "measured");
        // and what is written reads back as a profile with the same measured block
        PlayerModelProfile.Parse(text, out IReadOnlyList<string> problems).Measured!.Properties().Should().HaveCount(2);
        problems.Should().BeEmpty();
    }
}
