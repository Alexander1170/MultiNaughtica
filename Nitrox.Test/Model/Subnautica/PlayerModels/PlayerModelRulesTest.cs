using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>The rules of the custom player models (docs/coop/PLAYER_MODELS.md): what an id may be, how a file gives one, how it is shown, and which clips are emotes.</summary>
[TestClass]
public class PlayerModelRulesTest
{
    [TestMethod]
    [DataRow("player-example")]
    [DataRow("Model_2")]
    [DataRow("a")]
    [DataRow("v1.2")]
    [DataRow("0123456789012345678901234567890123456789")]
    public void AnIdIsLettersDigitsDotsDashesAndUnderscores(string id)
    {
        PlayerModelRules.IsValidId(id).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("a/b")]
    [DataRow("a\\b")]
    [DataRow("..\\..\\x")]
    [DataRow("my model")]
    [DataRow("caf\u00e9")]
    [DataRow("name:stream")]
    [DataRow("01234567890123456789012345678901234567890")]
    public void AnythingElseIsNotAnIdAndNeverAPath(string? id)
    {
        PlayerModelRules.IsValidId(id).Should().BeFalse();
    }

    [TestMethod]
    public void AFileNameGivesTheIdWithoutTheExtension()
    {
        PlayerModelRules.IdFromFileName("player-example.glb").Should().Be("player-example");
        PlayerModelRules.IdFromFileName("Sample.GLB").Should().Be("Sample");
        PlayerModelRules.IdFromFileName("C:\\models\\a_b.glb").Should().Be("a_b");
    }

    [TestMethod]
    public void AFileThatIsNotAModelOrWhoseNameCannotBeAnIdGivesNothing()
    {
        PlayerModelRules.IdFromFileName("readme.txt").Should().BeNull();
        PlayerModelRules.IdFromFileName("model.glb.bak").Should().BeNull();
        PlayerModelRules.IdFromFileName("my model.glb").Should().BeNull();
        PlayerModelRules.IdFromFileName("..glb").Should().BeNull();
        PlayerModelRules.IdFromFileName(".glb").Should().BeNull();
        PlayerModelRules.IdFromFileName("").Should().BeNull();
        PlayerModelRules.IdFromFileName(null).Should().BeNull();
    }

    [TestMethod]
    public void TheDisplayNameWithoutAProfileIsTheFileNameMadePresentable()
    {
        PlayerModelRules.DisplayName("player-example").Should().Be("Example", "the profile of the model may give another name");
        PlayerModelRules.DisplayName("a_b-c").Should().Be("A B C");
        PlayerModelRules.DisplayName("").Should().Be("Default");
        PlayerModelRules.DisplayName(null).Should().Be("Default");
        PlayerModelRules.VANILLA.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("Gangnam_Groove", true)]
    [DataRow("ymca_dance", true)]
    [DataRow("Gentlemans_Bow", true)]
    [DataRow("Jazz_Hands_inplace", true)]
    [DataRow("Breakdance_1990", true)]
    [DataRow("Backflip_and_Rise", true)]
    [DataRow("Backflip_Sweep_Kick", true)]
    [DataRow("Indoor_Swing", true)]
    [DataRow("Shot_and_Fall_Forward", true)]
    [DataRow("dying_backwards", true)]
    [DataRow("Running", false)]
    [DataRow("Walking", false)]
    [DataRow("Confident_Walk", false)]
    [DataRow("Crawl_Backward", false)]
    [DataRow("Idle", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void TheClipsOfMovementAreNotEmotes(string? clip, bool emote)
    {
        PlayerModelRules.IsEmoteClip(clip).Should().Be(emote);
    }

    [TestMethod]
    public void TheWheelShowsTheNameOfAClipWithBlanks()
    {
        PlayerModelRules.EmoteLabel("Gangnam_Groove").Should().Be("Gangnam Groove");
        PlayerModelRules.EmoteLabel("ymca_dance").Should().Be("ymca dance");
        PlayerModelRules.EmoteLabel(null).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("player-example", "player-example")]
    [DataRow("A.b_c-1", "A.b_c-1")]
    [DataRow("", "")]
    [DataRow(null, "")]
    [DataRow("..", "")]
    [DataRow("a/b", "")]
    [DataRow("a\\b", "")]
    [DataRow("<size=99>", "")]
    [DataRow("has space", "")]
    public void AnIdFromTheNetworkIsAModelNameOrTheVanillaBody(string? id, string expected)
    {
        PlayerModelRules.SanitizeId(id).Should().Be(expected);
    }

    [TestMethod]
    public void AnIdThatIsTooLongIsTheVanillaBody()
    {
        PlayerModelRules.SanitizeId(new string('x', PlayerModelRules.MAX_ID_LENGTH)).Should().HaveLength(PlayerModelRules.MAX_ID_LENGTH);
        PlayerModelRules.SanitizeId(new string('x', PlayerModelRules.MAX_ID_LENGTH + 1)).Should().BeEmpty();
    }

    [TestMethod]
    public void TheLengthOfTheVanillaBodyIsNothingAndNeverNegative()
    {
        PlayerModelRules.SanitizeBytes("", 500).Should().Be(0);
        PlayerModelRules.SanitizeBytes("model", -1).Should().Be(0);
        PlayerModelRules.SanitizeBytes("model", 21741924).Should().Be(21741924);
    }

    [TestMethod]
    public void TwoFilesAreTheSameModelWhenTheirLengthsAreTheSameAndNotNothing()
    {
        PlayerModelRules.SameFile(21741924, 21741924).Should().BeTrue();
        PlayerModelRules.SameFile(21741924, 21741925).Should().BeFalse("another file of the same name");
        PlayerModelRules.SameFile(0, 0).Should().BeFalse("a file that is not there has no length");
        PlayerModelRules.SameFile(-1, -1).Should().BeFalse();
    }

    [TestMethod]
    public void TheClientKeepsLongerThanTheServerAsksFor()
    {
        PlayerModelRules.SERVER_INTERVAL_SECONDS.Should().Be(2.0);
        PlayerModelRules.CLIENT_INTERVAL_SECONDS.Should().BeGreaterThan(PlayerModelRules.SERVER_INTERVAL_SECONDS);
    }
}
