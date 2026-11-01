using System.Globalization;
using System.Text.RegularExpressions;
using Nitrox.Model.Configuration;
using Nitrox.Model.Serialization;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic;

/// <summary>
///     The player page docs/coop/QUICK_PING.md says what the code does: the key, the setting with its default and range, the numbers of the rules. A number that changes in the code and not in the page fails here.
/// </summary>
[TestClass]
public class QuickPingDocsTest
{
    private static string ReadPage()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, "docs", "coop", "QUICK_PING.md");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.Inconclusive("docs/coop/QUICK_PING.md was not found above the test assembly");
        return "";
    }

    private static string Text(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [TestMethod]
    public void ThePageHasTheDefaultAndTheRangeOfTheSetting()
    {
        string page = ReadPage();
        SubnauticaServerOptions options = new();

        Match row = Regex.Match(page, $@"^\| `{nameof(options.QuickPingSeconds)}` \|(?<default>[^|]*)\|(?<range>[^|]*)\|", RegexOptions.Multiline);

        row.Success.Should().BeTrue("the settings table must have a row for QuickPingSeconds");
        row.Groups["default"].Value.Trim().Should().Be(Text(options.QuickPingSeconds));
        row.Groups["range"].Value.Trim().Should().Be($"{Text(QuickPingRules.MIN_SECONDS)} to {Text(QuickPingRules.MAX_SECONDS)}");
    }

    [TestMethod]
    public void TheOptionDescriptionNamesTheRangeOfTheNumber()
    {
        // The text the host reads in server.cfg: "Allowed 5 to 120" must be the limits of the rules
        string description = typeof(SubnauticaServerOptions).GetProperty(nameof(SubnauticaServerOptions.QuickPingSeconds))!.GetCustomAttributes(typeof(PropertyDescriptionAttribute), false).Cast<PropertyDescriptionAttribute>().Single().Description;

        description.Should().Contain($"Allowed {Text(QuickPingRules.MIN_SECONDS)} to {Text(QuickPingRules.MAX_SECONDS)}");
    }

    [TestMethod]
    public void ThePageNamesTheKeyAndTheNumbersOfTheRules()
    {
        string page = ReadPage();

        page.Should().Contain("The middle mouse button");
        page.Should().Contain("Ping what you look at");
        page.Should().Contain($"**{Text(QuickPingRules.AIM_RANGE)} m**");
        page.Should().Contain($"{Text(QuickPingRules.SERVER_INTERVAL_SECONDS)} s");
        page.Should().Contain($"the client keeps {Text(QuickPingRules.CLIENT_INTERVAL_SECONDS)} s");
        page.Should().Contain($"at most {CyclopsNavRules.MAX_LABEL_LENGTH} characters");
        page.Should().Contain($"\"{QuickPingRules.LOOK_HERE}\"");
        page.Should().Contain("SenderName", "the wire change has to be written down");
    }

    [TestMethod]
    public void TheLanguageFileLabelsTheKeyBinding()
    {
        string directory = AppContext.BaseDirectory;
        string? file = null;
        while (!string.IsNullOrEmpty(directory) && file == null)
        {
            string candidate = Path.Combine(directory, "Nitrox.Assets.Subnautica", "LanguageFiles", "en.json");
            file = File.Exists(candidate) ? candidate : null;
            directory = Path.GetDirectoryName(directory) ?? "";
        }
        if (file == null)
        {
            Assert.Inconclusive("Nitrox.Assets.Subnautica/LanguageFiles/en.json was not found above the test assembly");
            return;
        }

        File.ReadAllText(file).Should().Contain("\"Nitrox_Settings_Keybind_Ping\": \"Ping what you look at\"");
    }
}
