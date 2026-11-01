using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using static Nitrox.Model.Subnautica.DataStructures.GameLogic.PowerTransferRules;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The arithmetic of the Cyclops power share: the amounts a vehicle asks for, what a Cyclops and a docked vehicle may part with, when the emergency power starts, and which kinds of vehicle fit which
///     mode. Plain numbers, shared by the server and the client.
/// </summary>
[TestClass]
public class PowerTransferRulesTest
{
    // ---- amounts

    [TestMethod]
    [DataRow(1f, true)]
    [DataRow(0.05f, true)]
    [DataRow(MAX_AMOUNT, true)]
    [DataRow(0f, false)]
    [DataRow(-1f, false)]
    [DataRow(MAX_AMOUNT + 0.01f, false)]
    [DataRow(float.NaN, false)]
    [DataRow(float.PositiveInfinity, false)]
    [DataRow(float.NegativeInfinity, false)]
    public void AnAmountIsValidWhenItIsPositiveAndWithinTheCap(float amount, bool valid)
    {
        IsValidAmount(amount).Should().Be(valid);
    }

    [TestMethod]
    public void AGrantIsNeverMoreThanWasOfferedAndNeverNegative()
    {
        SanitizeGrant(1.5f, 2f).Should().Be(1.5f);
        SanitizeGrant(2f, 2f).Should().Be(2f);
        SanitizeGrant(9f, 2f).Should().Be(2f, "a giver cannot make energy by granting more than it was asked for");
        SanitizeGrant(-1f, 2f).Should().Be(0f);
        SanitizeGrant(0f, 2f).Should().Be(0f);
        SanitizeGrant(float.NaN, 2f).Should().Be(0f);
        SanitizeGrant(float.PositiveInfinity, 2f).Should().Be(2f, "infinity is more than offered, so it is the offer");
        SanitizeGrant(1f, 0f).Should().Be(0f);
        SanitizeGrant(1f, float.NaN).Should().Be(0f);
    }

    [TestMethod]
    public void ARequestAsksForTheRateOfTheTimeSinceTheLastOne()
    {
        RequestAmount(2f, 1f, 100f).Should().Be(2f);
        RequestAmount(2f, 0.5f, 100f).Should().Be(1f);
        RequestAmount(5f, 1f, 100f).Should().Be(5f);
    }

    [TestMethod]
    public void ARequestAsksForNoMoreThanTheRoomOfTheReceiver()
    {
        RequestAmount(2f, 1f, 0.7f).Should().Be(0.7f);
        RequestAmount(2f, 1f, 0f).Should().Be(0f, "a full vehicle asks for nothing");
        RequestAmount(2f, 1f, -3f).Should().Be(0f);
    }

    [TestMethod]
    public void AHitchOfTheGameDoesNotBecomeOneBigRequest()
    {
        RequestAmount(2f, 30f, 100f).Should().Be(2f * MAX_STEP_SECONDS, "a pause of half a minute asks for at most the longest step");
    }

    [TestMethod]
    public void ASmallAmountWaitsUntilItAddsUpAndANullRateAsksForNothing()
    {
        RequestAmount(2f, 1f, MIN_AMOUNT * 0.9f).Should().Be(0f);
        RequestAmount(2f, 1f, MIN_AMOUNT).Should().Be(MIN_AMOUNT);
        RequestAmount(0f, 1f, 100f).Should().Be(0f, "a rate of 0 switches the share off");
        RequestAmount(2f, 0f, 100f).Should().Be(0f);
        RequestAmount(float.NaN, 1f, 100f).Should().Be(0f);
        RequestAmount(2f, float.NaN, 100f).Should().Be(0f);
        RequestAmount(2f, 1f, float.NaN).Should().Be(0f);
    }

    // ---- the Cyclops gives (forward)

    [TestMethod]
    public void TheCyclopsKeepsTheEmergencyThresholdForItselfButAtLeastFivePercent()
    {
        ForwardFloorEnergy(600f, 10f).Should().BeApproximately(60f, 0.001f);
        ForwardFloorEnergy(600f, 0f).Should().BeApproximately(30f, 0.001f, "even without the emergency power a Cyclops does not give itself away");
        ForwardFloorEnergy(600f, 3f).Should().BeApproximately(30f, 0.001f);
        ForwardFloorEnergy(600f, 50f).Should().BeApproximately(300f, 0.001f);
        ForwardFloorEnergy(0f, 10f).Should().Be(0f);
    }

    [TestMethod]
    public void TheCyclopsGivesWhatItHasAboveItsFloorUpToTheOffer()
    {
        ForwardGrant(2f, 300f, 600f, 10f).Should().Be(2f);
        ForwardGrant(2f, 61f, 600f, 10f).Should().BeApproximately(1f, 0.001f);
        ForwardGrant(2f, 60f, 600f, 10f).Should().Be(0f, "at the floor it gives nothing");
        ForwardGrant(2f, 10f, 600f, 10f).Should().Be(0f);
        ForwardGrant(2f, 0f, 0f, 10f).Should().Be(0f, "a Cyclops without cells has nothing to give");
        ForwardGrant(0f, 300f, 600f, 10f).Should().Be(0f);
        ForwardGrant(-1f, 300f, 600f, 10f).Should().Be(0f);
    }

    // ---- the emergency (reverse)

    [TestMethod]
    public void TheEmergencyStartsStrictlyBelowTheThreshold()
    {
        EmergencyActive(59f, 600f, 10f).Should().BeTrue();
        EmergencyActive(60f, 600f, 10f).Should().BeFalse("exactly at the threshold is not below it");
        EmergencyActive(61f, 600f, 10f).Should().BeFalse();
        EmergencyActive(0f, 600f, 10f).Should().BeTrue();
    }

    [TestMethod]
    public void AThresholdOfZeroOrACyclopsWithoutCellsSwitchesTheEmergencyOff()
    {
        EmergencyActive(0f, 600f, 0f).Should().BeFalse();
        EmergencyActive(0f, 0f, 10f).Should().BeFalse();
        EmergencyActive(float.NaN, 600f, 10f).Should().BeFalse();
    }

    // ---- the game's own docking charge and the pause of it

    [TestMethod]
    public void TheGamesDockingChargeIsAQuarterOfAPercentOfTheCapacityAtMostWhatIsMissing()
    {
        VanillaDockCharge(50f, 200f).Should().Be(0.5f, "a Seamoth: 0.5 energy a second");
        VanillaDockCharge(100f, 400f).Should().Be(1f, "a Prawn Suit or a bigger cell");
        VanillaDockCharge(199.8f, 200f).Should().BeApproximately(0.2f, 0.0001f, "only what is missing");
        VanillaDockCharge(200f, 200f).Should().Be(0f, "a full vehicle is not charged");
        VanillaDockCharge(50f, 0f).Should().Be(0f);
    }

    [TestMethod]
    public void TheDockingChargeWaitsWhileACyclopsIsInAnEmergencyOrWouldBePutIntoOneByIt()
    {
        DockChargeShouldPause(110f, 1200f, 10f, 0.5f, false).Should().BeTrue("9.2 percent: in an emergency");
        DockChargeShouldPause(120f, 1200f, 10f, 0.5f, false).Should().BeTrue("exactly at the threshold the emergency has not started, but the charge would take the Cyclops below it");
        DockChargeShouldPause(120.5f, 1200f, 10f, 0.5f, false).Should().BeFalse("the charge leaves it exactly at the threshold, not below");
        DockChargeShouldPause(400f, 1200f, 10f, 0.5f, false).Should().BeFalse("far above: the game's charge goes on");
        DockChargeShouldPause(120f, 1200f, 10f, 0f, false).Should().BeFalse("a full vehicle takes nothing, so nothing would be taken from the Cyclops");
    }

    [TestMethod]
    public void AnEmergencyThatIsSwitchedOffNeverPausesTheDockingCharge()
    {
        DockChargeShouldPause(10f, 1200f, 0f, 0.5f, false).Should().BeFalse("a threshold of 0 switches the emergency power off");
        DockChargeShouldPause(10f, 0f, 10f, 0.5f, false).Should().BeFalse("a Cyclops without cells");
        DockChargeShouldPause(float.NaN, 1200f, 10f, 0.5f, false).Should().BeFalse();
    }

    [TestMethod]
    public void ARecentDrawPausesTheDockingChargeWhateverTheCyclopsHolds()
    {
        DockChargeShouldPause(1000f, 1200f, 10f, 0.5f, true).Should().BeTrue("the energy that went out of the vehicle a moment ago is not to be put back by the game's charge");
    }

    [TestMethod]
    public void ACyclopsHeldAtItsThresholdByTheEmergencyIsNeverEmptiedBelowItByTheGamesOwnCharge()
    {
        // The second live run of the second group: the Cyclops at 120.00 of 1200 (exactly 10 percent), the docked Seamoth at its reserve, 50.00 of 200. Once a second the game calls its charge and the
        // Cyclops calls its emergency draw; the draw refreshes a pause of three seconds. Every call of the game's charge that is not paused moves 0.5 from the Cyclops to the Seamoth.
        int Run(bool stateBased)
        {
            float cyclops = 120f;
            float vehicle = 50f;
            int unpaused = 0;
            double pausedUntil = double.NegativeInfinity;
            for (int second = 0; second < 120; second++)
            {
                float charge = VanillaDockCharge(vehicle, 200f);
                bool paused = stateBased ? DockChargeShouldPause(cyclops, 1200f, 10f, charge, second < pausedUntil) : second < pausedUntil;
                if (!paused)
                {
                    vehicle += charge;
                    cyclops -= charge;
                    unpaused++;
                }
                if (EmergencyActive(cyclops, 1200f, 10f))
                {
                    float grant = EmergencyGrant(5f, vehicle, 200f, 25f);
                    vehicle -= grant;
                    cyclops += grant;
                    if (grant > 0f)
                    {
                        pausedUntil = second + DOCK_CHARGE_PAUSE_SECONDS;
                    }
                }
            }
            return unpaused;
        }

        Run(false).Should().BeGreaterThan(20, "with only the mark of a draw one charge slips through after every pause: the cycle that the live run showed (0.5 energy every 4.3 s, for ever)");
        Run(true).Should().Be(0, "with the state of the Cyclops the game's charge never runs and nothing moves");
    }

    [TestMethod]
    public void WhenTheCyclopsRecoversAboveItsThresholdTheDockingChargeResumes()
    {
        float cyclops = 100f;
        float vehicle = 50f;
        int charges = 0;
        for (int second = 0; second < 30; second++)
        {
            cyclops += 5f; // solar charger, thermal reactor, a power cell put in
            float charge = VanillaDockCharge(vehicle, 200f);
            if (!DockChargeShouldPause(cyclops, 1200f, 10f, charge, false))
            {
                vehicle += charge;
                cyclops -= charge;
                charges++;
            }
        }

        charges.Should().BeGreaterThan(0, "above 10 percent (120) the vehicle docked in it is charged again");
        vehicle.Should().BeGreaterThan(50f);
    }

    [TestMethod]
    public void ADockedVehicleKeepsItsReserve()
    {
        Drawable(200f, 200f, 25f).Should().Be(150f);
        Drawable(60f, 200f, 25f).Should().Be(10f);
        Drawable(50f, 200f, 25f).Should().Be(0f, "at the reserve it gives nothing");
        Drawable(20f, 200f, 25f).Should().Be(0f);
        Drawable(100f, 0f, 25f).Should().Be(0f);
        Drawable(200f, 200f, 0f).Should().Be(200f, "a reserve of 0 lets the vehicle be emptied");
    }

    [TestMethod]
    public void ADockedVehicleGivesWhatIsAboveItsReserveUpToTheOffer()
    {
        EmergencyGrant(5f, 200f, 200f, 25f).Should().Be(5f);
        EmergencyGrant(5f, 52f, 200f, 25f).Should().Be(2f);
        EmergencyGrant(5f, 50f, 200f, 25f).Should().Be(0f);
        EmergencyGrant(0f, 200f, 200f, 25f).Should().Be(0f);
    }

    // ---- who is who

    [TestMethod]
    public void TheShareRunsBetweenACyclopsAndASeamothOrAPrawnSuit()
    {
        RolesFit(PowerTransferMode.Forward, "Cyclops", "Seamoth").Should().BeTrue();
        RolesFit(PowerTransferMode.Forward, "Cyclops", "Exosuit").Should().BeTrue();
        RolesFit(PowerTransferMode.Emergency, "Seamoth", "Cyclops").Should().BeTrue();
        RolesFit(PowerTransferMode.Emergency, "Exosuit", "Cyclops").Should().BeTrue();
    }

    [TestMethod]
    public void NoOtherPairingFits()
    {
        RolesFit(PowerTransferMode.Forward, "Seamoth", "Cyclops").Should().BeFalse("in the forward mode the Cyclops gives");
        RolesFit(PowerTransferMode.Emergency, "Cyclops", "Seamoth").Should().BeFalse("in the emergency mode the vehicle gives");
        RolesFit(PowerTransferMode.Forward, "Cyclops", "Cyclops").Should().BeFalse();
        RolesFit(PowerTransferMode.Forward, "Seamoth", "Exosuit").Should().BeFalse();
        RolesFit(PowerTransferMode.Forward, "Cyclops", "").Should().BeFalse();
        RolesFit(PowerTransferMode.Forward, "Cyclops", "Peeper").Should().BeFalse();
        RolesFit(PowerTransferMode.Unknown, "Cyclops", "Seamoth").Should().BeFalse();
    }

    // ---- the numbers the documentation names

    [TestMethod]
    public void TheDocumentedNumbersAreTheOnesInTheCode()
    {
        // docs/coop/VEHICLE_UPGRADES.md: one request a second, a Cyclops never shares below 5 percent, the lines stay on for a moment after the last delivery
        REQUEST_INTERVAL_SECONDS.Should().Be(1f);
        FORWARD_FLOOR_PERCENT.Should().Be(5f);
        MAX_STEP_SECONDS.Should().BeGreaterThan(REQUEST_INTERVAL_SECONDS, "a request that is a little late must still be able to ask for its whole second");
        ANSWER_TIMEOUT_SECONDS.Should().BeGreaterThan(REQUEST_INTERVAL_SECONDS);
        LEDGER_EXPIRY_SECONDS.Should().BeGreaterThan(ANSWER_TIMEOUT_SECONDS, "the server must not forget a transfer before the requester gave up on it");
        DISPLAY_SECONDS.Should().BeGreaterThan(REQUEST_INTERVAL_SECONDS, "the line must not blink between two deliveries");
        DOCK_CHARGE_PAUSE_SECONDS.Should().BeGreaterThan(REQUEST_INTERVAL_SECONDS, "the pause must outlast the gap between two emergency draws");
    }

    [TestMethod]
    public void TheDefaultSettingsGiveACyclopsThatCanKeepUpWithOneSeamothAndAnEmergencyThatCanMoveIt()
    {
        // A Seamoth holds 200: at 2 per second it charges in 100 seconds; the emergency rate (5) is more than the share (2): a low Cyclops gets what it needs from a docked vehicle faster than it gives
        VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RATE.Should().Be(2f);
        VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RATE.Should().BeGreaterThan(VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RATE);
        VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_THRESHOLD.Should().BeGreaterThan(FORWARD_FLOOR_PERCENT, "the share stops before the emergency starts");
    }
}
