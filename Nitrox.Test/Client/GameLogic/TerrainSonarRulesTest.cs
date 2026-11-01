using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.CoopItemText;

namespace NitroxClient.GameLogic;

/// <summary>
///     The arithmetic of the terrain hologram at the helm of a Cyclops with the Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md): its scale against the pings of the sonar display, the radius it loads, when
///     it is shown, and the numbers of its tooltip.
/// </summary>
[TestClass]
public class TerrainSonarRulesTest
{
    // ---- the scale of the display

    [TestMethod]
    public void AMetreIsTheDisplaySizeOverTheScanSizeOfThePings()
    {
        TerrainSonarRules.UnitsPerMetre(0.3f, 100f).Should().BeApproximately(0.003f, 0.000001f);
        TerrainSonarRules.UnitsPerMetre(1f, 250f).Should().BeApproximately(0.004f, 0.000001f);
    }

    [TestMethod]
    [DataRow(0f, 100f)]
    [DataRow(-0.3f, 100f)]
    [DataRow(0.3f, 0f)]
    [DataRow(0.3f, -5f)]
    [DataRow(float.NaN, 100f)]
    [DataRow(0.3f, float.NaN)]
    [DataRow(float.PositiveInfinity, 100f)]
    [DataRow(0.3f, float.PositiveInfinity)]
    public void ASizeThatIsMissingOrNotANumberGivesNoScaleSoTheCallerUsesItsFallback(float displaySize, float scanSize)
    {
        TerrainSonarRules.UnitsPerMetre(displaySize, scanSize).Should().Be(0f);
    }

    [TestMethod]
    public void TheWorldScaleOfAMetreIsTheDisplayScaleTimesTheDisplaysOwnUnits()
    {
        TerrainSonarRules.WorldUnitsPerMetre(0.003f, 0.5f).Should().BeApproximately(0.0015f, 0.0000001f);
        TerrainSonarRules.WorldUnitsPerMetre(0.003f, -1f).Should().Be(0f, "a display that is mirrored or has no size shows nothing sensible");
    }

    // ---- what the game's hologram is asked to load

    [TestMethod]
    public void TheLoadRadiusIsTheRangeAndTheDistanceFromThePivotToTheDisplayAndAMargin()
    {
        TerrainSonarRules.LoadRadius(200f, 24f).Should().Be(232, "200 m around the pivot, 24 m to the display, 8 m margin");
        TerrainSonarRules.LoadRadius(200.2f, 0f).Should().Be(209, "a fraction is rounded up, never down");
        TerrainSonarRules.LoadRadius(200f, -5f).Should().Be(208, "a display that is measured as behind the pivot by a mistake does not shrink the radius");
    }

    [TestMethod]
    public void TheLoadRadiusNeverExceedsWhatTheGameItselfLoads()
    {
        TerrainSonarRules.LoadRadius(500f, 60f).Should().BeLessThanOrEqualTo(TerrainSonarRules.MAX_LOAD_RADIUS);
        TerrainSonarRules.LoadRadius(100000f, 0f).Should().Be(TerrainSonarRules.MAX_LOAD_RADIUS);
        TerrainSonarRules.LoadRadius(0f, 0f).Should().BeGreaterThan(0);
        TerrainSonarRules.MAX_LOAD_RADIUS.Should().BeGreaterThanOrEqualTo((int)(TheLargestRange + 60f + TerrainSonarRules.LOAD_MARGIN_METRES), "the largest range of the options with a long Cyclops still fits");
    }

    private static float TheLargestRange => Nitrox.Model.Subnautica.DataStructures.GameLogic.VehicleUpgradeSettingsRules.MAX_TERRAIN_SONAR_RANGE;

    // ---- the size of the game's hologram

    [TestMethod]
    public void AMeshUnitOfTheGamesHologramIsFourMetresAndAMetreTakesTheDisplaysUnitsPerMetre()
    {
        // The game scales a mesh unit by hologramRadius / mapWorldRadius, so one metre (a quarter of a mesh unit) takes hologramRadius / (4 * mapWorldRadius) display units
        float unitsPerMetre = 0.003f;
        int loadRadius = 232;

        float hologramRadius = TerrainSonarRules.HologramRadius(unitsPerMetre, loadRadius);

        (hologramRadius / loadRadius / TerrainSonarRules.METRES_PER_MESH_UNIT).Should().BeApproximately(unitsPerMetre, 0.0000001f);
    }

    [TestMethod]
    public void TheHologramFadesAtTheRangeAsTheDisplayShowsIt()
    {
        // 200 m at 0.003 display units per metre and a display scaled 0.5 in the world: 0.3 world units
        TerrainSonarRules.FadeRadius(200f, 0.003f, 0.5f).Should().BeApproximately(0.3f, 0.00001f, "the range only: the terrain around the pivot is drawn around the display's own place");
        TerrainSonarRules.FadeRadius(-5f, 0.003f, 0.5f).Should().Be(0f);
    }

    [TestMethod]
    public void TheHelmShowsAtMostWhatItsDisplayShowsAndTheConsoleMapTheWholeSetting()
    {
        TerrainSonarRules.HelmRange(200f, 130f).Should().Be(130f, "the display's pings and pad are made for 130 m");
        TerrainSonarRules.HelmRange(100f, 130f).Should().Be(100f, "a smaller setting is kept");
        TerrainSonarRules.HelmRange(500f, 130f).Should().Be(130f);
        TerrainSonarRules.HelmRange(200f, 0f).Should().Be(200f, "a display whose scan radius is not known gives the setting");
        TerrainSonarRules.HelmRange(200f, float.NaN).Should().Be(200f);
    }

    // ---- when it is shown

    [TestMethod]
    public void TheHologramIsShownOnlyWhileTheModuleWorksAndThePlayerIsAboard()
    {
        TerrainSonarRules.ShouldShow(true, true).Should().BeTrue();
        TerrainSonarRules.ShouldShow(true, false).Should().BeFalse("nobody pays for the hologram of a Cyclops they are not in");
        TerrainSonarRules.ShouldShow(false, true).Should().BeFalse("no module, or the vehicle upgrades are off");
        TerrainSonarRules.ShouldShow(false, false).Should().BeFalse();
    }

    [TestMethod]
    public void AHologramIsBuiltAgainOnlyForAnotherRange()
    {
        TerrainSonarRules.NeedsRebuild(200f, 200f).Should().BeFalse();
        TerrainSonarRules.NeedsRebuild(200f, 200.3f).Should().BeFalse("the game's scale is frozen at its start; a hair is not worth a rebuild");
        TerrainSonarRules.NeedsRebuild(200f, 250f).Should().BeTrue();
        TerrainSonarRules.NeedsRebuild(250f, 200f).Should().BeTrue();
    }

    // ---- the tooltip and the table

    [TestMethod]
    public void TheTooltipOfTheModuleShowsTheNumbersOfTheServer()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.CYCLOPS_TERRAIN_SONAR);

        string text = CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 1f, 1.25f, GroupTwoNumbers.Defaults, new GroupThreeNumbers(350f, 33f));

        text.Should().Contain("within 350 meters").And.Contain("within 33 meters").And.NotContain("{").And.NotContain("}");
    }

    [TestMethod]
    public void TheTooltipOfTheGunnerStationShowsTheNumbersOfTheServerAndTheDefaultsWithoutThem()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.CYCLOPS_GUNNER_STATION);

        string told = CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 1f, 1.25f, GroupTwoNumbers.Defaults, new GroupThreeNumbers(350f, 33f, 12.5f, 4.5f, 80f));
        string defaults = CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 1f, 1.25f);

        told.Should().Contain("costs 12.5 energy").And.Contain("waits 4.5 seconds").And.Contain("flies 80 meters").And.NotContain("{").And.NotContain("}");
        defaults.Should().Contain("costs 10 energy").And.Contain("waits 3 seconds").And.Contain("flies 60 meters").And.NotContain("{");
    }

    [TestMethod]
    public void TheTooltipOfTheEngineerConsoleShowsTheNumbersOfTheServerAndTheDefaultsWithoutThem()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.CYCLOPS_ENGINEER_CONSOLE);

        string told = CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 1f, 1.25f, GroupTwoNumbers.Defaults, new GroupThreeNumbers(350f, 33f, 12.5f, 4.5f, 80f, 0.3f, 20f, 90f, 0.6f, 15f, 0.75f));
        string defaults = CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 1f, 1.25f);

        told.Should().Contain("30 percent faster").And.Contain("20 second overdrive").And.Contain("uses 60 percent less").And.Contain("75 percent quieter").And.NotContain("{").And.NotContain("}");
        defaults.Should().Contain("15 percent faster").And.Contain("12 second overdrive").And.Contain("uses 40 percent less").And.Contain("50 percent quieter").And.NotContain("{");
    }

    [TestMethod]
    public void TheTooltipWithoutNumbersUsesTheDefaults()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.CYCLOPS_TERRAIN_SONAR);

        CoopItemText.Tooltip(item, 300f, 2f, 5f, 15f, 10f, 1f, 1.25f).Should().Contain("within 200 meters").And.Contain("within 20 meters");
    }

    [TestMethod]
    public void TheDisplayIsSwitchedOnForTheTerrainSonarAloneAndOnlyWhenTheGameLeftItOff()
    {
        TerrainSonarRules.DisplayStep(true, false, false, false).Should().Be(TerrainSonarRules.DisplayAction.SwitchOn, "the game shows the display only for its own Cyclops Sonar Module");
        TerrainSonarRules.DisplayStep(true, false, true, true).Should().Be(TerrainSonarRules.DisplayAction.None, "already on: nothing to do on every poll");
        TerrainSonarRules.DisplayStep(true, false, false, true).Should().Be(TerrainSonarRules.DisplayAction.SwitchOn, "the game set it off again on a change of a module: on again");
    }

    [TestMethod]
    public void TheDisplayIsLeftToTheGameWhenTheCyclopsSonarModuleIsThere()
    {
        TerrainSonarRules.DisplayStep(true, true, true, false).Should().Be(TerrainSonarRules.DisplayAction.None);
        TerrainSonarRules.DisplayStep(true, true, false, false).Should().Be(TerrainSonarRules.DisplayAction.None, "the game decides, and sets it on by itself");
    }

    [TestMethod]
    public void TheDisplayIsGivenBackWhenTheTerrainSonarIsOutOrAGameModuleCameIn()
    {
        TerrainSonarRules.DisplayStep(false, false, true, true).Should().Be(TerrainSonarRules.DisplayAction.Restore, "the module was taken out (or the server switched the upgrades off): back to what the game has");
        TerrainSonarRules.DisplayStep(true, true, true, true).Should().Be(TerrainSonarRules.DisplayAction.Restore, "the game's own sonar module came in: the display is the game's again");
        TerrainSonarRules.DisplayStep(false, false, false, false).Should().Be(TerrainSonarRules.DisplayAction.None, "never touched a display that we did not switch on");
        TerrainSonarRules.DisplayStep(false, true, true, false).Should().Be(TerrainSonarRules.DisplayAction.None);
    }

    [TestMethod]
    public void TheTerrainSonarIsAPassiveCyclopsModuleOfTheCyclopsFabricator()
    {
        CoopItemDefinition item = CoopItems.Get(CoopItems.CYCLOPS_TERRAIN_SONAR);

        item.Kind.Should().Be(CoopItemKind.TerrainSonar);
        item.Vehicle.Should().Be(CoopVehicle.Cyclops);
        item.QuickSlot.Should().Be(QuickSlotType.Passive);
        item.Equipment.Should().Be(EquipmentType.CyclopsModule);
        item.Tree.Should().Be(CraftTree.Type.CyclopsFabricator);
        TerrainSonar.Tech.Should().Be((TechType)20013);
    }
}
