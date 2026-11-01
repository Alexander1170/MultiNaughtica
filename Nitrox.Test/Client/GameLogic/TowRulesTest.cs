using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.TowRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     The rules of the tow hooks (feature group 2): which vehicle a hook picks, how hard the cable pulls, when the tow is over, and the anchors of the cable on the hulls.
/// </summary>
[TestClass]
public class TowRulesTest
{
    private static TowCandidate Candidate(float distance, bool seamothOrPrawn = true, bool isTower = false, bool piloted = false, bool docked = false, bool alive = true, bool alreadyTowed = false, NitroxId? id = null)
    {
        return new TowCandidate(id ?? new NitroxId(), seamothOrPrawn, isTower, piloted, docked, alive, alreadyTowed, distance);
    }

    // ---- the range

    [TestMethod]
    public void AHookReachesHalfOfTheLongestCable()
    {
        AttachRange(25f).Should().Be(12.5f);
        AttachRange(100f).Should().Be(50f);
        AttachRange(5f).Should().Be(2.5f);
    }

    [TestMethod]
    public void TheCableHangsSlackUpToAThirdOfTheLongestCableBetweenFiveAndTwelveMetres()
    {
        RestLength(25f).Should().BeApproximately(8.75f, 0.001f);
        RestLength(5f).Should().Be(MIN_REST_LENGTH);
        RestLength(100f).Should().Be(MAX_REST_LENGTH);
        RestLength(25f).Should().BeLessThan(25f);
    }

    // ---- the choice of the vehicle

    [TestMethod]
    public void TheHookPicksTheNearestValidVehicle()
    {
        TowCandidate near = Candidate(4f);
        TowCandidate far = Candidate(9f);

        TryChooseTarget([far, near], 12.5f, out TowCandidate chosen).Should().BeTrue();

        chosen.Id.Should().Be(near.Id);
    }

    [TestMethod]
    public void AVehicleOutOfRangeIsNotPicked()
    {
        TryChooseTarget([Candidate(12.6f)], 12.5f, out _).Should().BeFalse();
        TryChooseTarget([Candidate(12.5f)], 12.5f, out _).Should().BeTrue("exactly at the range is in reach");
    }

    [TestMethod]
    public void OnlyASeamothOrAPrawnSuitCanBeTowed()
    {
        TryChooseTarget([Candidate(3f, seamothOrPrawn: false)], 12.5f, out _).Should().BeFalse("a Cyclops, a base piece or anything else is not a vehicle for the hook");
    }

    [TestMethod]
    public void TheTowerIsNeverItsOwnTarget()
    {
        TryChooseTarget([Candidate(0.5f, isTower: true)], 12.5f, out _).Should().BeFalse();
    }

    [TestMethod]
    public void OnlyAnUnpilotedVehicleCanBeTowed()
    {
        TryChooseTarget([Candidate(3f, piloted: true)], 12.5f, out _).Should().BeFalse("somebody sits in it");
        TryChooseTarget([Candidate(3f, piloted: true), Candidate(8f)], 12.5f, out TowCandidate chosen).Should().BeTrue();
        chosen.Piloted.Should().BeFalse("the farther empty vehicle is picked, not the nearer one with a pilot");
    }

    [TestMethod]
    public void ADockedOrDestroyedOrAlreadyTowedVehicleIsNotPicked()
    {
        TryChooseTarget([Candidate(3f, docked: true)], 12.5f, out _).Should().BeFalse();
        TryChooseTarget([Candidate(3f, alive: false)], 12.5f, out _).Should().BeFalse();
        TryChooseTarget([Candidate(3f, alreadyTowed: true)], 12.5f, out _).Should().BeFalse("a vehicle is towed by one tower");
    }

    [TestMethod]
    public void NothingToPickGivesNoTarget()
    {
        TryChooseTarget([], 12.5f, out TowCandidate chosen).Should().BeFalse();

        chosen.Id.Should().BeNull();
    }

    [TestMethod]
    public void ACandidateWithoutAnIdIsNeverValid()
    {
        IsValidTarget(new TowCandidate(null!, true, false, false, false, true, false, 1f), 12.5f).Should().BeFalse();
    }

    // ---- the pull

    [TestMethod]
    public void ASlackCablePullsWithNoForce()
    {
        PullAcceleration(5f, 8.75f, 3f, 0.02f).Should().Be(0f);
        PullAcceleration(8.75f, 8.75f, 3f, 0.02f).Should().Be(0f, "at the rest length the cable is not yet stretched");
    }

    [TestMethod]
    public void AStretchedCablePullsWithTheSpringAndTheDamper()
    {
        // 1 m of stretch, vehicles moving apart at 2 m/s: 4 * 1 + 3 * 2
        PullAcceleration(9.75f, 8.75f, 2f, 0.02f).Should().BeApproximately(SPRING * 1f + DAMPER * 2f, 0.001f);
    }

    [TestMethod]
    public void TheDamperOnlyActsWhileTheVehiclesMoveApart()
    {
        float closing = PullAcceleration(9.75f, 8.75f, -5f, 0.02f);
        float resting = PullAcceleration(9.75f, 8.75f, 0f, 0.02f);

        closing.Should().Be(resting).And.BeApproximately(SPRING, 0.001f, "a cable that is getting slack again does not push");
    }

    [TestMethod]
    public void ThePullHasACap()
    {
        PullAcceleration(40f, 8.75f, 30f, 0.02f).Should().Be(MAX_ACCELERATION);
    }

    [TestMethod]
    public void ALongPhysicsStepDoesNotThrowTheVehicle()
    {
        // The game steps by up to 0.1 s when nobody is near: the pull may not overshoot the rest length by more than a quarter of the stretch in one step
        float acceleration = PullAcceleration(9.75f, 8.75f, 10f, 0.2f);

        acceleration.Should().BeLessThanOrEqualTo(1f / (0.2f * 0.2f) * 0.25f);
        acceleration.Should().BeLessThan(MAX_ACCELERATION);
        PullAcceleration(9.75f, 8.75f, 10f, 0.02f).Should().BeGreaterThan(acceleration, "the usual short step pulls as hard as the cable says");
    }

    [TestMethod]
    [DataRow(float.NaN)]
    [DataRow(-3f)]
    public void ANonsenseLengthPullsNothing(float length)
    {
        PullAcceleration(length, 8.75f, 1f, 0.02f).Should().Be(0f);
    }

    [TestMethod]
    public void TheCableNeverPullsTheVehicleAwayFromTheTower()
    {
        for (float length = 0f; length < 80f; length += 0.7f)
        {
            for (float speed = -20f; speed < 40f; speed += 3f)
            {
                PullAcceleration(length, 8.75f, speed, 0.02f).Should().BeInRange(0f, MAX_ACCELERATION);
            }
        }
    }

    // ---- the end of a tow

    [TestMethod]
    public void ACableThatIsLongerThanTheLongestOneIsOnlyAProblemAfterAMoment()
    {
        IsTooLong(26f, 25f, 0f).Should().BeFalse("a gust of speed may stretch it for a moment");
        IsTooLong(26f, 25f, TOO_LONG_SECONDS - 0.01f).Should().BeFalse();
        IsTooLong(26f, 25f, TOO_LONG_SECONDS).Should().BeTrue();
    }

    [TestMethod]
    public void ACableThatIsWayTooLongEndsTheTowAtOnce()
    {
        IsTooLong(25f * SNAP_FACTOR + 0.1f, 25f, 0f).Should().BeTrue("a teleport or a respawn");
    }

    [TestMethod]
    public void ACableWithinTheLongestOneIsNeverTooLongHoweverLongItWaited()
    {
        IsTooLong(25f, 25f, 100f).Should().BeFalse();
        IsTooLong(10f, 25f, 100f).Should().BeFalse();
    }

    // ---- the slowing of the tower

    [TestMethod]
    public void ATowerMovesAtTheServersFactorAndNobodyElseIsSlowed()
    {
        SpeedFactor(towing: true, enabled: true, towSpeedFactor: 0.7f).Should().Be(0.7f);
        SpeedFactor(towing: false, enabled: true, towSpeedFactor: 0.7f).Should().Be(1f);
        SpeedFactor(towing: true, enabled: false, towSpeedFactor: 0.7f).Should().Be(1f, "with the vehicle upgrades off the hook does nothing");
    }

    [TestMethod]
    public void TheFactorIsKeptInsideSaneLimits()
    {
        SpeedFactor(true, true, 5f).Should().Be(1f, "a tow never makes a vehicle faster");
        SpeedFactor(true, true, 0f).Should().BeGreaterThan(0f, "a tower is never stopped dead by the factor");
        SpeedFactor(true, true, -3f).Should().BeGreaterThan(0f);
    }

    // ---- the warning

    [TestMethod]
    public void TheTowerIsWarnedNearAndBelowTheCrushDepth()
    {
        DepthWarning(100f, 200f).Should().BeNull();
        DepthWarning(190f, 200f).Should().Be("near");
        DepthWarning(185f, 200f).Should().Be("near");
        DepthWarning(200f, 200f).Should().Be("near");
        DepthWarning(201f, 200f).Should().Be("below");
    }

    [TestMethod]
    public void AVehicleWithoutACrushDepthIsNeverWarnedAbout()
    {
        DepthWarning(500f, 0f).Should().BeNull();
        DepthWarning(500f, -1f).Should().BeNull();
    }

    [TestMethod]
    public void TheCableOfEverySettingCanHoldAVehicleThatTheHookJustReached()
    {
        for (float distance = Nitrox.Model.Subnautica.DataStructures.GameLogic.VehicleUpgradeSettingsRules.MIN_TOW_MAX_DISTANCE; distance <= Nitrox.Model.Subnautica.DataStructures.GameLogic.VehicleUpgradeSettingsRules.MAX_TOW_MAX_DISTANCE; distance += 1f)
        {
            RestLength(distance).Should().BeLessThan(distance, "the cable begins to pull before it is as long as the longest cable ({0})", distance);
            // The hook reaches a vehicle within half of the cable; the far end of that vehicle's hull (a Seamoth or a Prawn Suit, under 5 m long) can be that much further
            (AttachRange(distance) + 5f + HullGeometry.ANCHOR_MARGIN).Should().BeLessThan(distance, "a tow that has just been hooked must not be too long already ({0})", distance);
        }
    }

    // ---- a vehicle that the game keeps still is carried

    [TestMethod]
    public void ACarriedVehicleBehindAStretchedCableIsPulledTowardTheTower()
    {
        // the towed anchor is 20 m behind the tower's (along -z), the cable points from the tower to the towed vehicle: the pull is along +z... toward the tower
        (float x, float y, float z) = CarryVelocity(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1f, 20f, 8.75f, 0.02f);

        z.Should().BeGreaterThan(0f, "toward the tower, which is ahead (+z)");
        Math.Abs(x).Should().Be(0f);
        Math.Abs(y).Should().Be(0f);
    }

    [TestMethod]
    public void ASlackCableDoesNotPullACarriedVehicleAndTheWaterSlowsItDown()
    {
        (float x, float y, float z) = CarryVelocity(0f, 0f, -4f, 0f, 0f, 0f, 0f, 0f, -1f, 5f, 8.75f, 0.1f);

        z.Should().BeInRange(-4f, -3f, "no pull, only the drag of the water");
        z.Should().BeApproximately(-4f * (1f - CARRY_DRAG * 0.1f), 0.001f);
    }

    [TestMethod]
    public void ACarriedVehicleFollowsATowerThatMovesAtASteadyPaceInsteadOfStayingBehind()
    {
        // 8 m/s tower, the towed vehicle starts at rest 8.75 m behind (the cable just taut): it catches up to about the speed of the tower and the cable settles
        float towedZ = -8.75f;
        float towerZ = 0f;
        float vz = 0f;
        for (int step = 0; step < 50 * 40; step++)
        {
            towerZ += 8f * 0.02f;
            float length = Math.Abs(towerZ - towedZ);
            (float _, float _, float newVz) = CarryVelocity(0f, 0f, vz, 0f, 0f, 8f, 0f, 0f, -1f, length, 8.75f, 0.02f);
            vz = newVz;
            towedZ += vz * 0.02f;
        }

        vz.Should().BeApproximately(8f, 0.5f, "after 40 s it keeps the pace of the tower");
        (towerZ - towedZ).Should().BeInRange(8.75f, 25f, "the cable stays within the longest cable");
    }

    [TestMethod]
    public void ACarriedVehicleIsNeverFasterThanTheLimit()
    {
        (float x, float y, float z) = CarryVelocity(0f, 0f, -100f, 0f, 0f, 0f, 0f, 0f, -1f, 3f, 8.75f, 0.02f);

        Math.Sqrt(x * x + y * y + z * z).Should().BeLessThanOrEqualTo(CARRY_MAX_SPEED + 0.001);
    }

    [TestMethod]
    public void ACarryStepWithoutTimeChangesNothing()
    {
        CarryVelocity(1f, 2f, 3f, 0f, 0f, 0f, 0f, 0f, -1f, 20f, 8.75f, 0f).Should().Be((1f, 2f, 3f));
    }
}
