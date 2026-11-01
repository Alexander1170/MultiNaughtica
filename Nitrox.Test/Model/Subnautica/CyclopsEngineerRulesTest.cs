using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica;

/// <summary>The arithmetic of the engineer console (docs/coop/VEHICLE_UPGRADES.md, station 3): routes, the overdrive and vent state machines, the heat.</summary>
[TestClass]
public class CyclopsEngineerRulesTest
{
    private const float BONUS = 0.15f;

    // ---- routes

    [TestMethod]
    public void ARouteThatIsNoRouteIsBalanced()
    {
        CyclopsEngineerRules.RouteFrom(0).Should().Be(EngineerRoute.Balanced);
        CyclopsEngineerRules.RouteFrom(1).Should().Be(EngineerRoute.Engines);
        CyclopsEngineerRules.RouteFrom(2).Should().Be(EngineerRoute.Shields);
        CyclopsEngineerRules.RouteFrom(3).Should().Be(EngineerRoute.Systems);
        CyclopsEngineerRules.RouteFrom(4).Should().Be(EngineerRoute.Balanced);
        CyclopsEngineerRules.RouteFrom(-1).Should().Be(EngineerRoute.Balanced);
        CyclopsEngineerRules.IsKnownRoute(3).Should().BeTrue();
        CyclopsEngineerRules.IsKnownRoute(4).Should().BeFalse();
        CyclopsEngineerRules.IsKnownRoute(-1).Should().BeFalse();
    }

    [TestMethod]
    public void OnlyTheEnginesRouteMakesTheCyclopsFaster()
    {
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, false, false, BONUS).Should().BeApproximately(1.15f, 0.0001f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Balanced, false, false, BONUS).Should().Be(1f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Shields, false, true, BONUS).Should().Be(1f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Systems, true, true, BONUS).Should().Be(1f, "an overdrive without the engines route does not exist");
    }

    [TestMethod]
    public void TheOverdriveBoostsOnlyAtFlankSpeed()
    {
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, true, true, BONUS).Should().BeApproximately(1.15f * 1.35f, 0.0001f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, true, false, BONUS).Should().BeApproximately(1.15f, 0.0001f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, false, true, BONUS).Should().BeApproximately(1.15f, 0.0001f);
    }

    [TestMethod]
    public void ASpeedBonusThatIsNoNumberOrNegativeChangesNothing()
    {
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, false, false, float.NaN).Should().Be(1f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, false, false, float.PositiveInfinity).Should().Be(1f);
        CyclopsEngineerRules.SpeedFactor(EngineerRoute.Engines, false, false, -0.5f).Should().Be(1f);
    }

    [TestMethod]
    public void OnlyTheShieldsRouteSavesShieldPower()
    {
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Shields, 0.4f).Should().BeApproximately(0.6f, 0.0001f);
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Balanced, 0.4f).Should().Be(1f);
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Engines, 0.4f).Should().Be(1f);
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Systems, 0.4f).Should().Be(1f);
    }

    [TestMethod]
    public void TheShieldAlwaysCostsSomething()
    {
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Shields, 5f).Should().BeApproximately(0.05f, 0.0001f);
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Shields, float.NaN).Should().Be(1f);
        CyclopsEngineerRules.ShieldCostFactor(EngineerRoute.Shields, -1f).Should().Be(1f);
    }

    [TestMethod]
    public void OnlyTheSystemsRouteSeesFartherAndRunsDeeperAndPaysForIt()
    {
        CyclopsEngineerRules.SonarFactor(EngineerRoute.Systems).Should().Be(1.5f);
        CyclopsEngineerRules.SonarFactor(EngineerRoute.Balanced).Should().Be(1f);
        CyclopsEngineerRules.SonarFactor(EngineerRoute.Shields).Should().Be(1f);
        CyclopsEngineerRules.SilentRunningCostFactor(EngineerRoute.Systems).Should().Be(1.5f);
        CyclopsEngineerRules.SilentRunningCostFactor(EngineerRoute.Engines).Should().Be(1f);
    }

    [TestMethod]
    public void TheNoiseIsOnlyLowerWhileSilentRunningIsOnAndThePowerGoesToTheSystems()
    {
        CyclopsEngineerRules.NoiseFactor(EngineerRoute.Systems, true, 0.5f).Should().Be(0.5f);
        CyclopsEngineerRules.NoiseFactor(EngineerRoute.Systems, false, 0.5f).Should().Be(1f);
        CyclopsEngineerRules.NoiseFactor(EngineerRoute.Engines, true, 0.5f).Should().Be(1f);
        CyclopsEngineerRules.NoiseFactor(EngineerRoute.Systems, true, 5f).Should().BeApproximately(0.05f, 0.0001f);
        CyclopsEngineerRules.NoiseFactor(EngineerRoute.Systems, true, float.NaN).Should().Be(1f);
    }

    // ---- the overdrive

    [TestMethod]
    public void AnOverdriveRunsFromItsStartUntilItsStampAndOnlyOnTheEnginesRoute()
    {
        CyclopsEngineerRules.IsOverdriveActive(EngineerRoute.Engines, 105d, 112d).Should().BeTrue();
        CyclopsEngineerRules.IsOverdriveActive(EngineerRoute.Engines, 112d, 112d).Should().BeFalse();
        CyclopsEngineerRules.IsOverdriveActive(EngineerRoute.Engines, 200d, 112d).Should().BeFalse();
        CyclopsEngineerRules.IsOverdriveActive(EngineerRoute.Shields, 105d, 112d).Should().BeFalse("changing the route suspends the overdrive");
    }

    [TestMethod]
    public void TheCooldownOfAnOverdriveStartsWhenItEnds()
    {
        (double until, double readyAt) = CyclopsEngineerRules.StartOverdrive(100d, 12f, 45f);

        until.Should().Be(112d);
        readyAt.Should().Be(157d);
    }

    [TestMethod]
    public void TheServerLetsAnOverdriveThroughOnTheEnginesRouteWithoutACooldown()
    {
        CyclopsEngineerRules.ServerCheckOverdrive(EngineerRoute.Engines, 100d, 0d).Should().Be(CyclopsEngineerFailure.None);
        CyclopsEngineerRules.ServerCheckOverdrive(EngineerRoute.Balanced, 100d, 0d).Should().Be(CyclopsEngineerFailure.WrongRoute);
        CyclopsEngineerRules.ServerCheckOverdrive(EngineerRoute.Shields, 100d, 0d).Should().Be(CyclopsEngineerFailure.WrongRoute);
        CyclopsEngineerRules.ServerCheckOverdrive(EngineerRoute.Engines, 100d, 130d).Should().Be(CyclopsEngineerFailure.Cooldown);
        CyclopsEngineerRules.ServerCheckOverdrive(EngineerRoute.Engines, 130d, 130d).Should().Be(CyclopsEngineerFailure.None, "the cooldown is over at its stamp");
    }

    [TestMethod]
    public void TheSimulatorNeedsTheEngineOnFlankSpeedAndPowerForAnOverdrive()
    {
        CyclopsEngineerRules.SimulatorCheckOverdrive(true, true, true).Should().Be(CyclopsEngineerFailure.None);
        CyclopsEngineerRules.SimulatorCheckOverdrive(false, true, true).Should().Be(CyclopsEngineerFailure.EngineOff);
        CyclopsEngineerRules.SimulatorCheckOverdrive(true, false, true).Should().Be(CyclopsEngineerFailure.NotFlank);
        CyclopsEngineerRules.SimulatorCheckOverdrive(true, true, false).Should().Be(CyclopsEngineerFailure.NoPower);
        CyclopsEngineerRules.SimulatorCheckOverdrive(false, false, false).Should().Be(CyclopsEngineerFailure.EngineOff, "the first thing that is wrong is told");
    }

    // ---- the vent

    [TestMethod]
    public void AVentNeedsNoCooldownOnTheServerAndPowerAndEnergyOnTheSimulator()
    {
        CyclopsEngineerRules.ServerCheckVent(100d, 0d).Should().Be(CyclopsEngineerFailure.None);
        CyclopsEngineerRules.ServerCheckVent(100d, 110d).Should().Be(CyclopsEngineerFailure.Cooldown);
        CyclopsEngineerRules.SimulatorCheckVent(true, true, 3).Should().Be(CyclopsEngineerFailure.None);
        CyclopsEngineerRules.SimulatorCheckVent(false, true, 3).Should().Be(CyclopsEngineerFailure.NoPower);
        CyclopsEngineerRules.SimulatorCheckVent(true, false, 3).Should().Be(CyclopsEngineerFailure.NoPower);
        CyclopsEngineerRules.SimulatorCheckVent(true, true, 0).Should().Be(CyclopsEngineerFailure.AlreadyCool, "a cool engine has nothing to vent: no energy is paid and no cooldown starts");
        CyclopsEngineerRules.SimulatorCheckVent(false, true, 0).Should().Be(CyclopsEngineerFailure.NoPower);
        CyclopsEngineerRules.VentReadyAt(100d, 30f).Should().Be(130d);
    }

    [TestMethod]
    public void TheSecondsLeftOfACooldownNeverGoBelowZero()
    {
        CyclopsEngineerRules.Remaining(100d, 130d).Should().Be(30f);
        CyclopsEngineerRules.Remaining(100d, 100d).Should().Be(0f);
        CyclopsEngineerRules.Remaining(100d, 50d).Should().Be(0f);
        CyclopsEngineerRules.Remaining(100d, double.NaN).Should().Be(0f);
    }

    // ---- the heat

    [TestMethod]
    public void VentingTakesFourAwayAndNeverGoesBelowZero()
    {
        CyclopsEngineerRules.HeatAfterVent(9).Should().Be(5);
        CyclopsEngineerRules.HeatAfterVent(4).Should().Be(0);
        CyclopsEngineerRules.HeatAfterVent(1).Should().Be(0);
        CyclopsEngineerRules.HeatAfterVent(0).Should().Be(0);
    }

    [TestMethod]
    public void StartingAnOverdriveAddsTwoAndNeverGoesAboveTen()
    {
        CyclopsEngineerRules.HeatAfterOverdriveStart(0).Should().Be(2);
        CyclopsEngineerRules.HeatAfterOverdriveStart(9).Should().Be(CyclopsEngineerRules.MAX_HEAT);
        CyclopsEngineerRules.HeatAfterOverdriveStart(10).Should().Be(CyclopsEngineerRules.MAX_HEAT);
    }

    [TestMethod]
    public void TheOverdriveAddsItsExtraHeatOnlyWhileTheGameIsHeating()
    {
        CyclopsEngineerRules.HeatBeforeStep(3, true, true).Should().Be(4);
        CyclopsEngineerRules.HeatBeforeStep(3, true, false).Should().Be(3, "the game cools the engine when it does not flank, the extra heat must not cancel that");
        CyclopsEngineerRules.HeatBeforeStep(3, false, true).Should().Be(3);
        CyclopsEngineerRules.HeatBeforeStep(10, true, true).Should().Be(10);
    }

    [TestMethod]
    public void TheHeatBandsAndTheFireChancesAreTheGamesOwn()
    {
        // SubFire.EngineOverheatSimulation: after its step the value is compared: greater than 5 is one in three, greater than 3 is one in five
        CyclopsEngineerRules.BandOf(3).Should().Be(EngineerHeatBand.Cool);
        CyclopsEngineerRules.BandOf(4).Should().Be(EngineerHeatBand.Warm);
        CyclopsEngineerRules.BandOf(5).Should().Be(EngineerHeatBand.Warm);
        CyclopsEngineerRules.BandOf(6).Should().Be(EngineerHeatBand.Hot);
        CyclopsEngineerRules.BandOf(10).Should().Be(EngineerHeatBand.Hot);
        CyclopsEngineerRules.FireChancePerStep(3).Should().Be(0f);
        CyclopsEngineerRules.FireChancePerStep(4).Should().Be(0.2f);
        CyclopsEngineerRules.FireChancePerStep(5).Should().Be(0.2f);
        CyclopsEngineerRules.FireChancePerStep(6).Should().BeApproximately(1f / 3f, 0.0001f);
    }

    [TestMethod]
    public void ClampingTheHeatKeepsItInsideTheGamesRange()
    {
        CyclopsEngineerRules.ClampHeat(-3).Should().Be(0);
        CyclopsEngineerRules.ClampHeat(7).Should().Be(7);
        CyclopsEngineerRules.ClampHeat(99).Should().Be(10);
    }

    // ---- the server's floor

    [TestMethod]
    public void TheHoldBackOfAPressIsLongerThanTheEngineersOwnTimeoutAndTheStatusFloorIsShorterThanTheHeartbeat()
    {
        CyclopsEngineerRules.PENDING_FORWARD_MILLISECONDS.Should().BeGreaterThan((long)(CyclopsEngineerRules.REQUEST_TIMEOUT_SECONDS * 1000f), "a client that gave up must not be able to press again while the first press may still run");
        CyclopsEngineerRules.LATE_RESULT_MILLISECONDS.Should().BeGreaterThan(CyclopsEngineerRules.PENDING_FORWARD_MILLISECONDS, "an answer after the wait is still taken: the simulator has paid by then");
        CyclopsEngineerRules.MIN_STATUS_INTERVAL_MILLISECONDS.Should().BeLessThan(1000, "the simulator sends about once a second");
    }
}
