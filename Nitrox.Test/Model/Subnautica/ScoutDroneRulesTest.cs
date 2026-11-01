using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The numbers that the scout drone changes about the game's camera drone (feature group 3, station 4): the thrust for a top speed that is a multiple of the Seamoth's, the static and the black fade of a weak signal,
///     the leash past the range, the battery.
/// </summary>
[TestClass]
public class ScoutDroneRulesTest
{
    // the game's step: 50 physics steps per second
    private const float STEP = 0.02f;

    [TestMethod]
    public void TheServersNameOfTheItemIsTheNumberOfTheClientsItem()
    {
        ScoutDroneRules.ITEM_TECH_TYPE_NAME.Should().Be(NitroxClient.GameLogic.VehicleUpgrades.CoopItems.SCOUT_DRONE.ToString(System.Globalization.CultureInfo.InvariantCulture), "the server knows the item as its number: the game's enum has no name for it");
    }

    [TestMethod]
    public void TheThrustSettlesAtTheSpeedOfTheSeamothTimesTheFactor()
    {
        // simulate what Unity does each step: push by the acceleration, then take the drag off
        foreach (float factor in new[] { 1f, 2f, 3.5f })
        {
            float drag = 2f;
            float thrust = ScoutDroneRules.ThrustAcceleration(factor, drag, STEP);
            float speed = 0f;
            for (int i = 0; i < 2000; i++)
            {
                speed = (speed + thrust * STEP) * (1f - drag * STEP);
            }

            speed.Should().BeApproximately(factor * ScoutDroneRules.SEAMOTH_TOP_SPEED, 0.05f);
        }
    }

    [TestMethod]
    public void TheThrustOfTheVanillaDroneSettlesNearTenMetresPerSecond()
    {
        // the drone's prefab: underwater drag 2, thrust 20: the factor that matches the game's own speed is a little under the Seamoth's
        float drag = 2f;
        float speed = 0f;
        for (int i = 0; i < 2000; i++)
        {
            speed = (speed + ScoutDroneRules.VANILLA_THRUST * STEP) * (1f - drag * STEP);
        }

        speed.Should().BeApproximately(9.6f, 0.05f);
        (speed / ScoutDroneRules.SEAMOTH_TOP_SPEED).Should().BeLessThan(1f);
    }

    [TestMethod]
    public void ANonsenseThrustInputFallsBackToTheGamesOwnThrust()
    {
        ScoutDroneRules.ThrustAcceleration(float.NaN, 2f, STEP).Should().Be(ScoutDroneRules.VANILLA_THRUST);
        ScoutDroneRules.ThrustAcceleration(2f, 0f, STEP).Should().Be(ScoutDroneRules.VANILLA_THRUST);
        ScoutDroneRules.ThrustAcceleration(2f, 2f, -1f).Should().Be(ScoutDroneRules.VANILLA_THRUST);
        ScoutDroneRules.ThrustAcceleration(-3f, 2f, STEP).Should().Be(ScoutDroneRules.VANILLA_THRUST);
        float huge = ScoutDroneRules.ThrustAcceleration(2f, 100f, STEP);
        float.IsInfinity(huge).Should().BeFalse("a drag that would eat the whole step keeps a floor under the divisor");
    }

    [TestMethod]
    public void TheStaticBeginsAtTheStartAndIsFullAtTheRange()
    {
        ScoutDroneRules.Noise(0f, 1000f, 0.5f).Should().Be(0f);
        ScoutDroneRules.Noise(500f, 1000f, 0.5f).Should().Be(0f);
        ScoutDroneRules.Noise(750f, 1000f, 0.5f).Should().BeApproximately(0.5f, 0.0001f);
        ScoutDroneRules.Noise(1000f, 1000f, 0.5f).Should().Be(1f);
        ScoutDroneRules.Noise(5000f, 1000f, 0.5f).Should().Be(1f, "the static does not go beyond full, unlike the game's, which has no end");
    }

    [TestMethod]
    public void TheStaticHandlesTheEdgesOfItsInputs()
    {
        ScoutDroneRules.Noise(100f, 1000f, 0f).Should().BeApproximately(0.1f, 0.0001f, "a start of zero means static from the dock on");
        ScoutDroneRules.Noise(990f, 1000f, 5f).Should().BeLessThan(1f, "a start beyond the range is held just inside it");
        ScoutDroneRules.Noise(float.NaN, 1000f, 0.5f).Should().Be(0f);
        ScoutDroneRules.Noise(500f, 0f, 0.5f).Should().Be(0f);
        ScoutDroneRules.Noise(500f, float.PositiveInfinity, 0.5f).Should().Be(0f);
    }

    [TestMethod]
    public void ThePictureGoesBlackOnlyPastTheRangeAndNeverCompletelyBlack()
    {
        ScoutDroneRules.Fader(0f, 1000f).Should().Be(0f);
        ScoutDroneRules.Fader(1000f, 1000f).Should().Be(0f);
        ScoutDroneRules.Fader(1050f, 1000f).Should().BeApproximately(0.5f, 0.0001f);
        ScoutDroneRules.Fader(1100f, 1000f).Should().Be(ScoutDroneRules.MAX_BLACK);
        ScoutDroneRules.Fader(9000f, 1000f).Should().Be(ScoutDroneRules.MAX_BLACK);
        ScoutDroneRules.Fader(float.NaN, 1000f).Should().Be(0f);
    }

    [TestMethod]
    public void PastTheRangeTheLeashDropsTheOutwardPartOfTheWish()
    {
        // home is straight "up" from the dock (0,1,0); the drone is 1200 m out of a 1000 m range
        (float x, float y, float z) = ScoutDroneRules.Leash(1200f, 1000f, 0f, 1f, 0f, 0.6f, 0.8f, 0f);

        x.Should().BeApproximately(0.6f, 0.0001f, "sideways is kept");
        y.Should().BeApproximately(0f, 0.0001f, "outward is dropped");
        z.Should().Be(0f);
    }

    [TestMethod]
    public void TheLeashLetsTheDroneComeBackAndLeavesTheRangeAlone()
    {
        ScoutDroneRules.Leash(1200f, 1000f, 0f, 1f, 0f, 0f, -1f, 0f).Should().Be((0f, -1f, 0f), "coming back is whole");
        ScoutDroneRules.Leash(900f, 1000f, 0f, 1f, 0f, 0f, 1f, 0f).Should().Be((0f, 1f, 0f), "inside the range nothing changes");
        ScoutDroneRules.Leash(1000f, 1000f, 0f, 1f, 0f, 0f, 1f, 0f).Should().Be((0f, 1f, 0f), "at the range exactly the drone is still free");
        ScoutDroneRules.Leash(1200f, 1000f, 0f, 1f, 0f, 0f, 0f, 0f).Should().Be((0f, 0f, 0f));
        ScoutDroneRules.IsPastRange(1000.1f, 1000f).Should().BeTrue();
        ScoutDroneRules.IsPastRange(1000f, 1000f).Should().BeFalse();
        ScoutDroneRules.IsPastRange(float.NaN, 1000f).Should().BeFalse();
    }

    [TestMethod]
    public void TheBatteryIsUsedOnlyWhileTheDroneMovesFastEnough()
    {
        ScoutDroneRules.Drain(1.9f, 1f).Should().Be(0f);
        ScoutDroneRules.Drain(2f, 1f).Should().Be(0f, "at the threshold exactly");
        ScoutDroneRules.Drain(20f, 1f).Should().BeApproximately(0.06666f, 0.00001f, "the game's rate, whatever the speed");
        ScoutDroneRules.Drain(20f, 0.5f).Should().BeApproximately(0.03333f, 0.00001f);
        ScoutDroneRules.Drain(20f, -1f).Should().Be(0f);
        ScoutDroneRules.Drain(float.NaN, 1f).Should().Be(0f);
        (ScoutDroneRules.BATTERY_CAPACITY / ScoutDroneRules.DRAIN_PER_SECOND / 60f).Should().BeApproximately(25f, 0.1f, "about 25 minutes of flight on a full battery");
    }

    [TestMethod]
    [DataRow(true, false, true, true)]
    [DataRow(true, true, true, false)]
    [DataRow(true, false, false, false)]
    [DataRow(false, false, true, false)]
    public void ACreatureBitesADroneThatIsOutAndAliveWithAMouthThatBitesVehicles(bool attackBitesVehicles, bool docked, bool alive, bool expected)
    {
        ScoutDroneRules.CanBeBitten(attackBitesVehicles, docked, alive).Should().Be(expected);
    }

    [TestMethod]
    public void CreaturesHuntTheDroneOnlyWhileItIsOutOfItsDock()
    {
        ScoutDroneRules.IsHunted(false).Should().BeTrue();
        ScoutDroneRules.IsHunted(true).Should().BeFalse();
    }

    [TestMethod]
    public void TheServerLetsAZapPassAtEightyPercentOfTheCooldown()
    {
        ScoutDroneRules.ZapServerIntervalMilliseconds(12f).Should().Be(9600L);
        ScoutDroneRules.ZapServerIntervalMilliseconds(2f).Should().Be(1600L);
        ScoutDroneRules.ZapServerIntervalMilliseconds(0f).Should().Be(0L);
        ScoutDroneRules.ZapServerIntervalMilliseconds(-5f).Should().Be(0L);
        ScoutDroneRules.ZapServerIntervalMilliseconds(float.NaN).Should().Be(0L);
    }

    [TestMethod]
    public void AZapIsAcceptedOnlyNearTheDrone()
    {
        ScoutDroneRules.IsNearDrone(0f, 0f, 0f).Should().BeTrue();
        ScoutDroneRules.IsNearDrone(ScoutDroneRules.ZAP_POSITION_TOLERANCE, 0f, 0f).Should().BeTrue("exactly at the tolerance");
        ScoutDroneRules.IsNearDrone(ScoutDroneRules.ZAP_POSITION_TOLERANCE + 1f, 0f, 0f).Should().BeFalse();
        ScoutDroneRules.IsNearDrone(50f, 50f, 50f).Should().BeFalse("86 m");
        ScoutDroneRules.IsNearDrone(float.NaN, 0f, 0f).Should().BeFalse();
        ScoutDroneRules.IsNearDrone(0f, float.PositiveInfinity, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void AZapNeedsTheEnergyUnlessTheGameModeNeedsNoPower()
    {
        ScoutDroneRules.HasZapEnergy(15f, 15f, true).Should().BeTrue();
        ScoutDroneRules.HasZapEnergy(14.9f, 15f, true).Should().BeFalse();
        ScoutDroneRules.HasZapEnergy(0f, 15f, false).Should().BeTrue("creative mode");
        ScoutDroneRules.HasZapEnergy(float.NaN, 15f, true).Should().BeFalse();
    }

    [TestMethod]
    public void TheDockRechargesAtAFixedRateAndNeverPastFull()
    {
        ScoutDroneRules.Recharge(0f, 100f, 1f).Should().Be(4f);
        ScoutDroneRules.Recharge(0f, 100f, 0.02f).Should().BeApproximately(0.08f, 0.0001f);
        ScoutDroneRules.Recharge(98f, 100f, 1f).Should().Be(2f, "only what is missing");
        ScoutDroneRules.Recharge(100f, 100f, 1f).Should().Be(0f);
        ScoutDroneRules.Recharge(120f, 100f, 1f).Should().Be(0f);
        ScoutDroneRules.Recharge(0f, 100f, -1f).Should().Be(0f);
        ScoutDroneRules.Recharge(float.NaN, 100f, 1f).Should().Be(0f);
        (ScoutDroneRules.BATTERY_CAPACITY / ScoutDroneRules.RECHARGE_PER_SECOND).Should().Be(25f, "a flat battery is full in 25 seconds");
    }
}
