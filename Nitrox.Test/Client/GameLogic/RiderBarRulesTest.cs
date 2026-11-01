using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.RiderBarRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     The rules of the Seamoth Rider Bar (feature group 2): when a player can hold on, when they let go, how the look is throttled, and where the bar sits on a hull.
/// </summary>
[TestClass]
public class RiderBarRulesTest
{
    private static RiderGrabContext Context(bool enabled = true, bool barInstalled = true, bool vehicleAlive = true, bool vehicleDocked = false, bool playerNormalMode = true, bool playerInSubOrVehicle = false,
                                            bool playerAlive = true, bool playerRiding = false, bool requestPending = false, bool someoneElseRides = false)
    {
        return new RiderGrabContext(enabled, barInstalled, vehicleAlive, vehicleDocked, playerNormalMode, playerInSubOrVehicle, playerAlive, playerRiding, requestPending, someoneElseRides);
    }

    // ---- holding on

    [TestMethod]
    public void ASwimmingPlayerCanHoldOnToAFreeBarOfAFreeSeamoth()
    {
        CanGrab(Context()).Should().Be(RiderGrabDecision.Yes);
    }

    [TestMethod]
    public void WhenSomebodyElseHoldsOnTheBarSaysOccupied()
    {
        CanGrab(Context(someoneElseRides: true)).Should().Be(RiderGrabDecision.Occupied);
    }

    [TestMethod]
    public void OneRiderPerSeamothMeansTheFirstRequestInFlightBlocksTheSecondPressOfTheSamePlayer()
    {
        CanGrab(Context(requestPending: true)).Should().Be(RiderGrabDecision.No);
    }

    [TestMethod]
    public void NothingIsShownWhenThePlayerCannotUseTheBarAtAll()
    {
        CanGrab(Context(enabled: false)).Should().Be(RiderGrabDecision.No, "the vehicle upgrades are off");
        CanGrab(Context(barInstalled: false)).Should().Be(RiderGrabDecision.No);
        CanGrab(Context(vehicleAlive: false)).Should().Be(RiderGrabDecision.No);
        CanGrab(Context(vehicleDocked: true)).Should().Be(RiderGrabDecision.No);
        CanGrab(Context(playerNormalMode: false)).Should().Be(RiderGrabDecision.No, "piloting, sitting or already locked");
        CanGrab(Context(playerInSubOrVehicle: true)).Should().Be(RiderGrabDecision.No);
        CanGrab(Context(playerAlive: false)).Should().Be(RiderGrabDecision.No);
        CanGrab(Context(playerRiding: true)).Should().Be(RiderGrabDecision.No, "a player holds on to one bar at a time");
    }

    [TestMethod]
    public void AnOccupiedBarOfADockedSeamothShowsNothingNotOccupied()
    {
        CanGrab(Context(vehicleDocked: true, someoneElseRides: true)).Should().Be(RiderGrabDecision.No);
    }

    // ---- letting go

    [TestMethod]
    public void TheUseKeyOrTheExitKeyLetsGoAfterTheDebounce()
    {
        ShouldLetGo(1f, true, false, false, true).Should().BeTrue();
        ShouldLetGo(1f, false, true, false, true).Should().BeTrue();
        ShouldLetGo(1f, false, false, false, true).Should().BeFalse("no key");
    }

    [TestMethod]
    public void TheKeyThatGrabbedDoesNotLetGoInTheSameFrame()
    {
        ShouldLetGo(0f, true, false, false, true).Should().BeFalse();
        ShouldLetGo(LET_GO_DEBOUNCE_SECONDS - 0.01f, true, true, false, true).Should().BeFalse();
        ShouldLetGo(LET_GO_DEBOUNCE_SECONDS, true, false, false, true).Should().BeTrue();
    }

    [TestMethod]
    public void ThePdaAndTakenInputKeepTheRiderOn()
    {
        ShouldLetGo(5f, true, true, true, true).Should().BeFalse("while the PDA is open the keys belong to it");
        ShouldLetGo(5f, true, true, false, false).Should().BeFalse("the console or the chat has the keyboard");
    }

    [TestMethod]
    public void ARequestWithoutAnAnswerMayBeAskedAgainAfterTheTimeout()
    {
        RequestTimedOut(REQUEST_TIMEOUT_SECONDS - 0.1f).Should().BeFalse();
        RequestTimedOut(REQUEST_TIMEOUT_SECONDS).Should().BeTrue();
    }

    // ---- the look

    [TestMethod]
    public void TheLookIsSentAtMostFifteenTimesASecond()
    {
        ShouldSendLook(LOOK_INTERVAL_SECONDS * 0.5f, 45f).Should().BeFalse();
        ShouldSendLook(LOOK_INTERVAL_SECONDS, 45f).Should().BeTrue();
    }

    [TestMethod]
    public void TheLookIsSentWhenItChangedAndOtherwiseOnceASecond()
    {
        ShouldSendLook(0.2f, LOOK_MIN_CHANGE_DEGREES - 0.01f).Should().BeFalse("a look that did not change is not worth a packet");
        ShouldSendLook(0.2f, LOOK_MIN_CHANGE_DEGREES).Should().BeTrue();
        ShouldSendLook(LOOK_KEEP_ALIVE_SECONDS, 0f).Should().BeTrue("the keep-alive tells a client that joined where the rider looks");
    }

    [TestMethod]
    public void TheHeadTurnsOnlyAsFarAsASwimmersCan()
    {
        ClampYaw(200f).Should().Be(MAX_YAW_DEGREES);
        ClampYaw(-200f).Should().Be(-MAX_YAW_DEGREES);
        ClampYaw(30f).Should().Be(30f);
        ClampPitch(120f).Should().Be(MAX_PITCH_DEGREES);
        ClampPitch(-120f).Should().Be(-MAX_PITCH_DEGREES);
        ClampPitch(-10f).Should().Be(-10f);
    }

    // ---- the layout

    [TestMethod]
    public void TheRodSitsAcrossTheRearOfTheHullInTheUpperHalf()
    {
        // A box 2.4 wide, 1.6 high and 4.2 long around the origin (half sizes 1.2, 0.8, 2.1)
        (float x, float y, float z) = RiderBarLayout.RodCenter(0f, 0f, 0f, 0.8f, 2.1f);

        x.Should().Be(0f);
        y.Should().BeApproximately(0.8f * RiderBarLayout.HEIGHT_SHARE, 0.0001f).And.BeGreaterThan(0f);
        z.Should().BeApproximately(-2.1f - RiderBarLayout.REAR_GAP, 0.0001f, "just behind the rear end (forward is +z)");
    }

    [TestMethod]
    public void TheRiderHangsBehindAndBelowTheRod()
    {
        (float rodX, float rodY, float rodZ) = RiderBarLayout.RodCenter(0.2f, 0.1f, 0.3f, 0.8f, 2.1f);
        (float x, float y, float z) = RiderBarLayout.RiderRoot(0.2f, 0.1f, 0.3f, 0.8f, 2.1f);

        x.Should().Be(rodX);
        y.Should().BeApproximately(rodY - RiderBarLayout.RIDER_BELOW, 0.0001f);
        z.Should().BeApproximately(rodZ - RiderBarLayout.RIDER_BEHIND, 0.0001f);
    }

    [TestMethod]
    public void TheRodIsAboutHalfAsWideAsTheHullButBetweenThirtyAndSixtyCentimetres()
    {
        RiderBarLayout.RodHalfLength(1.2f).Should().BeApproximately(0.6f, 0.0001f).And.BeLessThanOrEqualTo(RiderBarLayout.MAX_HALF_LENGTH);
        RiderBarLayout.RodHalfLength(0.1f).Should().Be(RiderBarLayout.MIN_HALF_LENGTH);
        RiderBarLayout.RodHalfLength(5f).Should().Be(RiderBarLayout.MAX_HALF_LENGTH);
    }

    // ---- the trigger the hand finds (a play-test found the thin bar fiddly to aim at)

    [TestMethod]
    public void TheHandTriggerIsAtLeastTenTimesAsThickAsTheRodAndWellOverHalfAMetreAcross()
    {
        (RiderBarLayout.GRAB_RADIUS * 2f).Should().BeGreaterThanOrEqualTo(RiderBarLayout.ROD_DIAMETER * 10f);
        (RiderBarLayout.GRAB_RADIUS * 2f).Should().BeGreaterThanOrEqualTo(0.6f, "it was 0.4 m across and still fiddly");
    }

    [TestMethod]
    public void TheHandTriggerIsALittleLongerThanTheRodAtEachEnd()
    {
        foreach (float halfLength in new[] { RiderBarLayout.MIN_HALF_LENGTH, 0.45f, RiderBarLayout.MAX_HALF_LENGTH })
        {
            float rod = halfLength * 2f;
            float trigger = RiderBarLayout.GrabHeight(halfLength);

            trigger.Should().BeApproximately(rod + RiderBarLayout.GRAB_EXTRA_LENGTH, 0.0001f);
            ((trigger - rod) / 2f).Should().BeInRange(0.3f, 0.6f, "a hand's width of play at each end, not a second bar");
        }
    }

    [TestMethod]
    public void TheBiggerTriggerGrowsToTheOutsideAndReachesNoFartherIntoTheHullThanTheOldOneDid()
    {
        const float OLD_RADIUS = 0.2f;
        float oldReach = OLD_RADIUS - RiderBarLayout.REAR_GAP;

        RiderBarLayout.GrabReachIntoHull().Should().BeLessThanOrEqualTo(oldReach + 0.0001f, "the Seamoth's own hand target (getting in) has to go on working on the hull");
        RiderBarLayout.GRAB_RADIUS.Should().BeGreaterThan(OLD_RADIUS);
    }

    // ---- a grant that comes late, and a look that is not a rotation

    [TestMethod]
    public void AGrantIsHonouredWhileTheRequestIsFreshAndThePlayerIsNearTheBar()
    {
        GrantStillWanted(0.5f, 1.5f).Should().BeTrue();
        GrantStillWanted(GRANT_MAX_AGE_SECONDS, GRANT_MAX_DISTANCE).Should().BeTrue("the limits are inclusive");
    }

    [TestMethod]
    public void AStaleGrantOrAGrantForAPlayerWhoSwamAwayIsNotHonoured()
    {
        GrantStillWanted(GRANT_MAX_AGE_SECONDS + 0.1f, 1f).Should().BeFalse("a server stall of 20 seconds");
        GrantStillWanted(1f, GRANT_MAX_DISTANCE + 0.1f).Should().BeFalse("the player moved 30 m away: they would be snapped onto the Seamoth");
        GrantStillWanted(-1f, 1f).Should().BeFalse();
        GrantStillWanted(float.NaN, 1f).Should().BeFalse();
        GrantStillWanted(1f, float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void TheAnswerTimeoutIsShorterThanTheTimeALateGrantIsHonoured()
    {
        REQUEST_TIMEOUT_SECONDS.Should().BeLessThan(GRANT_MAX_AGE_SECONDS, "a player who asks again after the timeout can still be granted the first request");
    }

    [TestMethod]
    [DataRow(0f, 0f, 0f, 1f)]
    [DataRow(0f, 0.7071f, 0f, 0.7071f)]
    [DataRow(0.5f, 0.5f, 0.5f, 0.5f)]
    public void ARotationIsALook(float x, float y, float z, float w)
    {
        IsRotation(x, y, z, w).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(float.NaN, 0f, 0f, 1f)]
    [DataRow(float.PositiveInfinity, 0f, 0f, 1f)]
    [DataRow(0f, 0f, 0f, 0f)]
    [DataRow(5f, 5f, 5f, 5f)]
    [DataRow(0.1f, 0f, 0f, 0.1f)]
    public void ANaNInfiniteZeroOrFarFromUnitQuaternionIsNotALook(float x, float y, float z, float w)
    {
        IsRotation(x, y, z, w).Should().BeFalse();
    }
}
