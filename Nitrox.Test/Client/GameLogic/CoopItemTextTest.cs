using System.Globalization;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The names and tooltips of the new items with the numbers of the server's settings.
/// </summary>
[TestClass]
public class CoopItemTextTest
{
    [TestMethod]
    [DataRow(2f, "2")]
    [DataRow(0.25f, "0.25")]
    [DataRow(1200f, "1200")]
    [DataRow(2.5f, "2.5")]
    public void NumbersShowNoTrailingZeros(float value, string text)
    {
        CoopItemText.Number(value).Should().Be(text);
    }

    [TestMethod]
    public void NumbersUseADotWhateverTheCultureOfThePlayer()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            CoopItemText.Number(0.25f).Should().Be("0.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    public void TheAfterburnerTooltipShowsTheServersNumbers()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.SEAMOTH_AFTERBURNER);

        string text = CoopItemText.Tooltip(item, 300f, 3f, 8f, 20f, 12.5f, 0.25f, 0.5f);

        text.Should().Contain("3 times").And.Contain("8 seconds").And.Contain("12.5 energy").And.Contain("20 seconds to recover");
    }

    [TestMethod]
    public void TheCyclopsSolarTooltipShowsTheCyclopsRate()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.CYCLOPS_SOLAR);

        CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 0.25f, 1.5f).Should().Contain("up to 1.5 energy per second");
    }

    [TestMethod]
    public void WithTheVehicleUpgradesOffTheNewModulesSayThatTheyDoNothing()
    {
        string text = CoopItemText.Tooltip(CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4), 300f, 2f, 5f, 15f, 10f, 3f, 1.25f, false);

        text.Should().EndWith("Switched off on this server: it does nothing.");
        CoopItemText.Tooltip(CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4), 300f, 2f, 5f, 15f, 10f, 3f, 1.25f, true).Should().NotContain("Switched off");
    }

    [TestMethod]
    public void WithTheVehicleUpgradesOffTheGamesSolarChargerShowsTheGamesRateNotTheServers()
    {
        CoopItemDefinition solar = CoopItems.Get(CoopItems.SEAMOTH_SOLAR);

        CoopItemText.Tooltip(solar, 300f, 2f, 5f, 15f, 10f, 3f, 1.25f, false).Should().Contain("up to 1 energy per second per module").And.NotContain("Switched off");
        CoopItemText.Tooltip(solar, 300f, 2f, 5f, 15f, 10f, 3f, 1.25f, true).Should().Contain("up to 3 energy per second per module");
    }

    [TestMethod]
    public void NoTooltipKeepsAPlaceholder()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 0.25f, 0.5f).Should().NotContain("{", item.Key).And.NotContain("}", item.Key);
        }
    }

    [TestMethod]
    public void TheCurrentSettingsAreUsedWhenNoneArePassed()
    {
        try
        {
            VehicleUpgradeSettings.Apply(new Nitrox.Model.Subnautica.DataStructures.GameLogic.SessionSettings { ExtraDepthPerModule = 500f });

            CoopItemText.Tooltip(CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4)).Should().Contain("1400 meters");
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
    }

    [TestMethod]
    public void ApplyWritesTheNameAndTheTooltipOfEveryItem()
    {
        Dictionary<string, string> strings = new() { ["Existing"] = "kept" };

        CoopItemText.Apply(strings);

        strings.Should().HaveCount(1 + CoopItems.All.Count * 4);
        strings["Existing"].Should().Be("kept");
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            strings[item.Key].Should().Be(item.DisplayName);
            strings[item.TooltipKey].Should().Be(CoopItemText.Tooltip(item));
        }
    }

    [TestMethod]
    public void TheNamesInTheIngredientListAndTheBlueprintListAreGivenSoNoKeyIsLookedUpInVain()
    {
        // The game reads TooltipFactory.techTypeIngredientStrings and uGUI_BlueprintsTab.blueprintEntryStrings to find a key, and falls back to the plain name for a key that is missing
        // (logging it once as a missing key). The registry points both caches at these keys.
        Dictionary<string, string> strings = new();

        CoopItemText.Apply(strings);

        CoopItemDefinition mk5 = CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK5);
        mk5.IngredientKey.Should().Be("CoopSeamothDepthModuleMK5.TooltipIngredient");
        mk5.BlueprintKey.Should().Be("CoopSeamothDepthModuleMK5.BlueprintsTab");
        strings[mk5.IngredientKey].Should().Be("Seamoth Depth Module MK5");
        strings[mk5.BlueprintKey].Should().Be("Seamoth Depth Module MK5");
    }

    [TestMethod]
    public void AnAdoptedItemKeepsTheIngredientAndBlueprintNamesTheGameHas()
    {
        Dictionary<string, string> strings = new()
        {
            ["SeamothSolarCharge.TooltipIngredient"] = "Solar (game)",
            ["SeamothSolarCharge.BlueprintsTab"] = "Solar blueprint (game)"
        };

        CoopItemText.Apply(strings);

        strings["SeamothSolarCharge.TooltipIngredient"].Should().Be("Solar (game)");
        strings["SeamothSolarCharge.BlueprintsTab"].Should().Be("Solar blueprint (game)");
    }

    [TestMethod]
    public void AnAdoptedItemKeepsTheNameTheGameGivesItButGetsTheTooltipOfTheTable()
    {
        Dictionary<string, string> strings = new()
        {
            ["SeamothSolarCharge"] = "Seamoth Solar Charge (game)",
            ["Tooltip_SeamothSolarCharge"] = "The text of the game"
        };

        CoopItemText.Apply(strings);

        strings["SeamothSolarCharge"].Should().Be("Seamoth Solar Charge (game)");
        strings["Tooltip_SeamothSolarCharge"].Should().Contain("up to 1 energy per second per module").And.Contain("Several modules add up");
    }

    [TestMethod]
    public void AnAdoptedItemWithoutANameGetsTheNameOfTheTable()
    {
        Dictionary<string, string> strings = new();

        CoopItemText.Apply(strings);

        strings["SeamothSolarCharge"].Should().Be("Seamoth Solar Charger");
    }

    [TestMethod]
    public void ApplyOverwritesTheTextsOfAnEarlierSettings()
    {
        Dictionary<string, string> strings = new();
        try
        {
            CoopItemText.Apply(strings);
            string before = strings["Tooltip_CoopSeamothDepthModuleMK4"];

            VehicleUpgradeSettings.Apply(new Nitrox.Model.Subnautica.DataStructures.GameLogic.SessionSettings { ExtraDepthPerModule = 100f });
            CoopItemText.Apply(strings);

            before.Should().Contain("1200 meters");
            strings["Tooltip_CoopSeamothDepthModuleMK4"].Should().Contain("1000 meters");
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
    }
}
