using System.Globalization;
using System.Text.RegularExpressions;
using Nitrox.Model.Configuration;
using Nitrox.Model.Serialization;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic;

/// <summary>
///     The player page docs/coop/BUDDY_OXYGEN.md says what the code does: the key, the three settings with their defaults and ranges, the numbers of the rules. A number that changes in the code and not in the
///     page fails here.
/// </summary>
[TestClass]
public class BuddyOxygenDocsTest
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

    private static string Text(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [TestMethod]
    public void ThePageHasTheDefaultAndTheRangeOfEverySetting()
    {
        string page = ReadFile("docs", "coop", "BUDDY_OXYGEN.md");
        SubnauticaServerOptions options = new();

        (string key, float expectedDefault, float min, float max)[] settings =
        [
            (nameof(options.BuddyOxygenRate), options.BuddyOxygenRate, BuddyOxygenRules.MIN_RATE, BuddyOxygenRules.MAX_RATE),
            (nameof(options.BuddyOxygenRange), options.BuddyOxygenRange, BuddyOxygenRules.MIN_RANGE, BuddyOxygenRules.MAX_RANGE),
            (nameof(options.BuddyOxygenFloor), options.BuddyOxygenFloor, BuddyOxygenRules.MIN_FLOOR, BuddyOxygenRules.MAX_FLOOR)
        ];

        foreach ((string key, float expectedDefault, float min, float max) in settings)
        {
            Match row = Regex.Match(page, $@"^\| `{key}` \|(?<default>[^|]*)\|(?<range>[^|]*)\|", RegexOptions.Multiline);
            row.Success.Should().BeTrue($"the settings table must have a row for {key}");
            row.Groups["default"].Value.Trim().Should().Be(Text(expectedDefault), $"the default of {key}");
            row.Groups["range"].Value.Trim().Should().Be($"{Text(min)} to {Text(max)}", $"the range of {key}");
        }
    }

    [TestMethod]
    public void TheOptionDescriptionsNameTheRangesOfTheNumbers()
    {
        // The text the host reads in server.cfg: "Allowed 0 to 20" must be the limits of the rules
        foreach ((string property, float min, float max) in new[]
                 {
                     (nameof(SubnauticaServerOptions.BuddyOxygenRate), BuddyOxygenRules.MIN_RATE, BuddyOxygenRules.MAX_RATE),
                     (nameof(SubnauticaServerOptions.BuddyOxygenRange), BuddyOxygenRules.MIN_RANGE, BuddyOxygenRules.MAX_RANGE),
                     (nameof(SubnauticaServerOptions.BuddyOxygenFloor), BuddyOxygenRules.MIN_FLOOR, BuddyOxygenRules.MAX_FLOOR)
                 })
        {
            string description = typeof(SubnauticaServerOptions).GetProperty(property)!.GetCustomAttributes(typeof(PropertyDescriptionAttribute), false).Cast<PropertyDescriptionAttribute>().Single().Description;

            description.Should().Contain($"Allowed {Text(min)} to {Text(max)}", $"the description of {property}");
        }
    }

    [TestMethod]
    public void ThePageNamesTheKeyAndTheNumbersOfTheRules()
    {
        string page = ReadFile("docs", "coop", "BUDDY_OXYGEN.md");

        page.Should().Contain("Hold the share key (B)");
        page.Should().Contain("Share air with a teammate (hold)");
        page.Should().Contain($"**{Text(BuddyOxygenRules.DEFAULT_RANGE)} m**");
        page.Should().Contain($"**{Text(BuddyOxygenRules.DEFAULT_RATE)} seconds of air per second**");
        page.Should().Contain($"**You keep {Text(BuddyOxygenRules.DEFAULT_FLOOR)} seconds of air for yourself**");
        page.Should().Contain($"about {Text(BuddyOxygenRules.CONE_DEGREES)} degrees");
        page.Should().Contain($"{Text(BuddyOxygenRules.DEFAULT_RANGE + BuddyOxygenRules.RANGE_SLACK)} m for the server");
        page.Should().Contain($"at most {BuddyOxygenRules.MAX_OPEN_PER_GIVER} steps waiting");
        page.Should().Contain($"{Text(BuddyOxygenRules.STALE_SECONDS)} s is given back too");
        page.Should().Contain($"a bucket of {Text(BuddyOxygenRules.SERVER_RATE_FACTOR)} times the rate");
    }

    [TestMethod]
    public void TheLanguageFileHasTheTextsOfTheKeyAndTheHud()
    {
        string english = ReadFile("Nitrox.Assets.Subnautica", "LanguageFiles", "en.json");

        foreach (string key in new[]
                 {
                     "Nitrox_Settings_Keybind_BuddyOxygen", "Nitrox_BuddyOxygenHint", "Nitrox_BuddyOxygenSharing", "Nitrox_BuddyOxygenReceiving", "Nitrox_BuddyOxygenFull", "Nitrox_BuddyOxygenCannot",
                     "Nitrox_BuddyOxygenTooFar", "Nitrox_BuddyOxygenFloor"
                 })
        {
            english.Should().Contain($"\"{key}\":", $"the text {key} is used by the buddy oxygen");
        }
        english.Should().Contain("\"Nitrox_BuddyOxygenReceiving\": \"{0} is giving you air\"");
    }
}
