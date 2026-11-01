using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace NitroxClient.GameLogic;

/// <summary>The guide docs/coop/PLAYER_MODELS.md says what the code does: the folders, the shape of an id, the emote numbers and the texts of the Options list.</summary>
[TestClass]
public class PlayerModelsDocsTest
{
    private static string ReadFile(params string[] path)
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine([directory, .. path]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.Inconclusive($"{Path.Combine(path)} was not found above the test assembly");
        return "";
    }

    [TestMethod]
    public void TheGuideNamesTheFoldersTheIdRulesAndTheOptionsEntries()
    {
        string page = ReadFile("docs", "coop", "PLAYER_MODELS.md");
        string english = ReadFile("Nitrox.Assets.Subnautica", "LanguageFiles", "en.json");

        page.Should().Contain($"Resources\\{PlayerModelRules.SHIPPED_FOLDER}");
        page.Should().Contain($"%APPDATA%\\Nitrox\\{PlayerModelRules.USER_FOLDER}");
        page.Should().Contain($"1 to {PlayerModelRules.MAX_ID_LENGTH} letters, digits, dots, dashes and underscores");
        page.Should().Contain("`.glb`");
        foreach (string key in new[] { "Nitrox_Settings_PlayerModel", "Nitrox_Settings_PlayerModels", "Nitrox_Settings_ShowOwnModel", "Nitrox_Settings_Keybind_Emote" })
        {
            english.Should().Contain($"\"{key}\": \"");
        }
        page.Should().Contain("Model the others see");
        page.Should().Contain("Show my model in first person");
        page.Should().Contain("Emote wheel (hold)");
    }

    [TestMethod]
    public void TheGuideSaysWhichClipsAreEmotesAndTheNumbersAreTheCodes()
    {
        string page = ReadFile("docs", "coop", "PLAYER_MODELS.md");

        page.Should().Contain("**G**");
        page.Should().Contain($"shorter than {PlayerEmoteRules.LOOPING_BELOW_SECONDS:0.0} seconds repeats, for {PlayerEmoteRules.LOOP_PLAY_SECONDS:0} seconds in all");
        page.Should().Contain($"no emote plays longer than {PlayerEmoteRules.MAX_PLAY_SECONDS:0} seconds");
        page.Should().Contain($"holds up to {PlayerEmoteRules.MAX_WHEEL_ENTRIES} entries");
        page.Should().Contain($"At least {PlayerModelEmoteRetarget.MIN_MAPPED_BONES} bones must be shared");
        foreach (string movement in new[] { "walk", "run", "crawl", "idle" })
        {
            page.Should().Contain($"`{movement}`");
            PlayerModelRules.IsEmoteClip($"My_{movement}_cycle").Should().BeFalse();
        }
        PlayerModelRules.IsEmoteClip("Wave_Hello").Should().BeTrue();
    }

    [TestMethod]
    public void TheGuideListsTheFieldsOfTheProfileAndNoEmoteSourceIsNamedByDefault()
    {
        string page = ReadFile("docs", "coop", "PLAYER_MODELS.md");

        foreach (string field in new[] { "displayName", "notes", "scale.source", "scale.factor", "eyeHeight", "bones.prefix", "bones.map", "emotes.shared", "emotes.source", "firstPerson.hiddenBones", "firstPerson.trims", "firstPerson.offset", "hands.*", "measured" })
        {
            page.Should().Contain($"`{field}`");
        }
        PlayerModelProfile.DEFAULT_EMOTE_SOURCE.Should().BeEmpty("the game ships no model, so there is no source of shared emotes unless a profile names one");
        PlayerModelProfile.Default.SharedEmotes.Should().BeFalse();
        PlayerModelProfile.Parse("""{ "emotes": { "source": "player-other" } }""", out _).SharedEmotes.Should().BeTrue();
        PlayerModelProfile.Parse("""{ "emotes": { "source": "player-other", "shared": false } }""", out _).SharedEmotes.Should().BeFalse();
    }

    [TestMethod]
    public void TheGuideDoesNotNameAnyModelThatShips()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Resources", PlayerModelRules.SHIPPED_FOLDER);

        (Directory.Exists(folder) ? Directory.GetFiles(folder, "*.glb") : []).Should().BeEmpty("this build ships no model");
    }
}
