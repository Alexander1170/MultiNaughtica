using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     Which blueprints of the new items are known: the two switches of the server decide.
/// </summary>
[TestClass]
public class VehicleUpgradeUnlockRulesTest
{
    [TestMethod]
    public void WithTheDefaultsEveryItemUnlocksWithItsPrerequisite()
    {
        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(true, false, CoopItems.All);

        plan.ShowNodes.Should().HaveCount(CoopItems.All.Count);
        plan.UnlockWithPrerequisite.Should().Equal(CoopItems.All);
        plan.UnlockNow.Should().BeEmpty();
    }

    [TestMethod]
    public void UnlockedFromStartKnowsEveryBlueprintAtOnce()
    {
        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(true, true, CoopItems.All);

        plan.ShowNodes.Should().Equal(CoopItems.All);
        plan.UnlockNow.Should().Equal(CoopItems.All);
        plan.UnlockWithPrerequisite.Should().BeEmpty("a rule for an item that is already known would only be noise");
    }

    [TestMethod]
    public void WithTheMasterSwitchOffNothingIsShownAndNothingUnlocks()
    {
        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(false, false, CoopItems.All);

        plan.ShowNodes.Should().BeEmpty();
        plan.UnlockNow.Should().BeEmpty();
        plan.UnlockWithPrerequisite.Should().BeEmpty();
    }

    [TestMethod]
    public void TheMasterSwitchBeatsUnlockedFromStart()
    {
        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(false, true, CoopItems.All);

        plan.ShowNodes.Should().BeEmpty();
        plan.UnlockNow.Should().BeEmpty();
    }

    [TestMethod]
    public void NoItemsGiveAnEmptyPlan()
    {
        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(true, false, []);

        plan.ShowNodes.Should().BeEmpty();
        plan.UnlockWithPrerequisite.Should().BeEmpty();
    }

    [TestMethod]
    public void OnceTheSeamothDepthModuleMk3IsKnownBothNewTiersAreKnown()
    {
        // The rule the game applies for a compound blueprint with one dependency: known as soon as the dependency is known
        TechType[] known = [TechType.VehicleHullModule3];
        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(true, false, CoopItems.All);

        IEnumerable<string> unlocked = plan.UnlockWithPrerequisite.Where(item => known.Contains(item.Prerequisite)).Select(item => item.Key);

        unlocked.Should().Equal("CoopSeamothDepthModuleMK4", "CoopSeamothDepthModuleMK5");
    }
}
