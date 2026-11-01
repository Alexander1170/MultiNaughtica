using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.DepthModuleRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     The crush depth of the six new depth modules and the way the best installed module is chosen.
/// </summary>
[TestClass]
public class DepthModuleRulesTest
{
    private const float DEFAULT_STEP = 300f;

    [TestMethod]
    [DataRow(CoopItems.SEAMOTH_DEPTH_MK4, 1200f)]
    [DataRow(CoopItems.SEAMOTH_DEPTH_MK5, 1500f)]
    [DataRow(CoopItems.PRAWN_DEPTH_MK3, 2000f)]
    [DataRow(CoopItems.PRAWN_DEPTH_MK4, 2300f)]
    [DataRow(CoopItems.CYCLOPS_DEPTH_MK4, 2000f)]
    [DataRow(CoopItems.CYCLOPS_DEPTH_MK5, 2300f)]
    public void TheSixModulesGiveTheDepthsOfTheDocumentationWithTheDefaultStep(int number, float depth)
    {
        CoopItemDefinition item = CoopItems.Get(number);

        DepthFor(item.Vehicle, item.Tier, DEFAULT_STEP).Should().Be(depth, item.Key);
    }

    [TestMethod]
    [DataRow(CoopItems.SEAMOTH_DEPTH_MK4, 1400f)]
    [DataRow(CoopItems.SEAMOTH_DEPTH_MK5, 1900f)]
    [DataRow(CoopItems.PRAWN_DEPTH_MK3, 2200f)]
    [DataRow(CoopItems.PRAWN_DEPTH_MK4, 2700f)]
    [DataRow(CoopItems.CYCLOPS_DEPTH_MK4, 2200f)]
    [DataRow(CoopItems.CYCLOPS_DEPTH_MK5, 2700f)]
    public void AStepOf500MetresMovesEveryModule(int number, float depth)
    {
        CoopItemDefinition item = CoopItems.Get(number);

        DepthFor(item.Vehicle, item.Tier, 500f).Should().Be(depth, item.Key);
    }

    [TestMethod]
    public void AStepOfZeroGivesTheDepthOfTheBestVanillaModule()
    {
        DepthFor(CoopVehicle.Seamoth, 2, 0f).Should().Be(900f);
        DepthFor(CoopVehicle.Prawn, 2, 0f).Should().Be(1700f);
        DepthFor(CoopVehicle.Cyclops, 2, 0f).Should().Be(1700f);
    }

    [TestMethod]
    public void TheExtrasAreTheOnesOfTheTopVanillaModulePlusTheSteps()
    {
        // The game adds the extra to the base depth of the vehicle (200, 900 and 500 metres), so the base cancels out of every comparison
        ExtraFor(CoopVehicle.Seamoth, 1, DEFAULT_STEP).Should().Be(1000f);
        ExtraFor(CoopVehicle.Seamoth, 2, DEFAULT_STEP).Should().Be(1300f);
        ExtraFor(CoopVehicle.Prawn, 1, DEFAULT_STEP).Should().Be(1100f);
        ExtraFor(CoopVehicle.Prawn, 2, DEFAULT_STEP).Should().Be(1400f);
        ExtraFor(CoopVehicle.Cyclops, 1, DEFAULT_STEP).Should().Be(1500f);
        ExtraFor(CoopVehicle.Cyclops, 2, DEFAULT_STEP).Should().Be(1800f);
    }

    [TestMethod]
    public void TheDepthIsTheExtraPlusTheBaseDepthOfTheVehicle()
    {
        (200f + ExtraFor(CoopVehicle.Seamoth, 1, DEFAULT_STEP)).Should().Be(DepthFor(CoopVehicle.Seamoth, 1, DEFAULT_STEP));
        (900f + ExtraFor(CoopVehicle.Prawn, 2, DEFAULT_STEP)).Should().Be(DepthFor(CoopVehicle.Prawn, 2, DEFAULT_STEP));
        (500f + ExtraFor(CoopVehicle.Cyclops, 2, DEFAULT_STEP)).Should().Be(DepthFor(CoopVehicle.Cyclops, 2, DEFAULT_STEP));
    }

    [TestMethod]
    public void TheBestInstalledModuleCountsAndTheModulesDoNotAddUp()
    {
        float best = Best(CoopVehicle.Seamoth, [100f, 300f, 700f], [1, 2], true, DEFAULT_STEP);

        best.Should().Be(1300f, "MK5 alone, not MK5 + MK4 + the vanilla modules");
    }

    [TestMethod]
    public void ANewModuleBeatsEveryVanillaModule()
    {
        Best(CoopVehicle.Seamoth, [700f], [1], true, DEFAULT_STEP).Should().Be(1000f);
        Best(CoopVehicle.Prawn, [800f], [1], true, DEFAULT_STEP).Should().Be(1100f);
        Best(CoopVehicle.Cyclops, [1200f], [1], true, DEFAULT_STEP).Should().Be(1500f);
    }

    [TestMethod]
    public void AVanillaModuleBeatsANewOneWhenTheStepIsZeroOrSmallerThanTheGap()
    {
        Best(CoopVehicle.Seamoth, [800f], [1], true, 0f).Should().Be(800f, "the 800 of the Seamoth's other reinforcement module is above the top depth module's 700");
    }

    [TestMethod]
    public void WithTheVehicleUpgradesSwitchedOffTheBestVanillaModuleCounts()
    {
        Best(CoopVehicle.Seamoth, [100f, 700f], [1, 2], false, DEFAULT_STEP).Should().Be(700f);
        Best(CoopVehicle.Seamoth, [], [2], false, DEFAULT_STEP).Should().Be(0f, "an installed new module does nothing, a Seamoth with only that has its base depth");
    }

    [TestMethod]
    public void NothingInstalledGivesNoExtra()
    {
        Best(CoopVehicle.Prawn, [], [], true, DEFAULT_STEP).Should().Be(0f);
    }

    [TestMethod]
    public void RemovingTheBestModuleFallsBackToTheNextBestOne()
    {
        Best(CoopVehicle.Seamoth, [300f], [1, 2], true, DEFAULT_STEP).Should().Be(1300f);
        Best(CoopVehicle.Seamoth, [300f], [1], true, DEFAULT_STEP).Should().Be(1000f);
        Best(CoopVehicle.Seamoth, [300f], [], true, DEFAULT_STEP).Should().Be(300f);
    }

    [TestMethod]
    public void ASmallStepLetsTheSeamothsReinforcementModuleBeatTheNewModule()
    {
        // The reinforcement module gives 800 m of extra depth, 100 m more than the best depth module of the game: a step of 50 gives MK4 750 m, which loses
        Best(CoopVehicle.Seamoth, [800f], [1], true, 50f).Should().Be(800f);
        Best(CoopVehicle.Seamoth, [800f], [2], true, 50f).Should().Be(800f, "MK5 with a step of 50 is 800 m, the same");
        Best(CoopVehicle.Seamoth, [800f], [2], true, 100f).Should().Be(900f);
        Best(CoopVehicle.Seamoth, [800f], [1], true, 150f).Should().Be(850f);
    }

    [TestMethod]
    public void TheTooltipsShowTheRealDepthOfTheServersStep()
    {
        CoopItemText.Tooltip(CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4), 300f, 2f, 5f, 15f, 10f, 0.25f, 0.5f).Should().Contain("1200 meters");
        CoopItemText.Tooltip(CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4), 500f, 2f, 5f, 15f, 10f, 0.25f, 0.5f).Should().Contain("1400 meters");
        CoopItemText.Tooltip(CoopItems.Get(CoopItems.PRAWN_DEPTH_MK4), 300f, 2f, 5f, 15f, 10f, 0.25f, 0.5f).Should().Contain("2300 meters");
        CoopItemText.Tooltip(CoopItems.Get(CoopItems.CYCLOPS_DEPTH_MK5), 300f, 2f, 5f, 15f, 10f, 0.25f, 0.5f).Should().Contain("2300 meters");
    }
}
