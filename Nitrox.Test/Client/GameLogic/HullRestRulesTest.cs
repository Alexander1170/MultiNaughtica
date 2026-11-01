namespace NitroxClient.GameLogic;

[TestClass]
public class HullRestRulesTest
{
    // The numbers below are those of a replicated Cyclops: snapshots every 33 ms (30 Hz), the replicator compares the oldest and the newest of three (66 ms),
    // the sender sends a hull nobody steers only after 5 cm (or 0.05 degrees) and in a burst every 5 s.
    private const float TWO_INTERVALS = 0.066f;

    // What MovementReplicator.ReportHoldDisplacement passes to the tally
    private const float INTERVAL = HullRestRules.HOLD_REPORT_INTERVAL;
    private const float MIN_METERS = HullRestRules.HOLD_DEADBAND_DISTANCE;
    private const float MIN_DEGREES = HullRestRules.HOLD_DEADBAND_DEGREES;

    [TestMethod]
    public void IsAtRest_ANewTracker_StartsAtRest()
    {
        new HullRestRules.RestTracker().AtRest.Should().BeTrue();
    }

    [TestMethod]
    public void IsAtRest_AKeepAliveBurstOfIdenticalSnapshots_IsRest()
    {
        // A parked hull: the sender repeats the same pose for 0.2 s every 5 s
        new HullRestRules.RestTracker().IsAtRest(TWO_INTERVALS, 0f, 0f).Should().BeTrue();
    }

    [TestMethod]
    public void IsAtRest_ANoiseOfAFewMillimetersBetweenTwoFrames_IsRest()
    {
        // 1 mm in 66 ms is 1.5 cm/s: not worth an extrapolation of 0.3 s
        new HullRestRules.RestTracker().IsAtRest(TWO_INTERVALS, 0.001f, 0.01f).Should().BeTrue();
    }

    [TestMethod]
    public void IsAtRest_ASteadyDriveOfTheCyclops_IsNotRest()
    {
        // 1.25 m/s: 8.25 cm in 66 ms. The moving behaviour (extrapolation) must stay exactly as it was
        new HullRestRules.RestTracker().IsAtRest(TWO_INTERVALS, 0.0825f, 0f).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0.5f)]
    [DataRow(1f)]
    [DataRow(3f)]
    [DataRow(6f)]
    public void IsAtRest_EveryDrivingSpeed_IsNotRestWhateverTheWindow(float metersPerSecond)
    {
        foreach (float window in new[] { 0.034f, 0.066f, 0.1f, 0.2f, 0.25f })
        {
            new HullRestRules.RestTracker().IsAtRest(window, metersPerSecond * window, 0f).Should().BeFalse($"{metersPerSecond} m/s over {window} s is a drive");
        }
    }

    [TestMethod]
    public void IsAtRest_ATurnOnTheSpot_IsNotRest()
    {
        // A Cyclops turning at 30 degrees per second does not move its centre: 2 degrees in 66 ms
        new HullRestRules.RestTracker().IsAtRest(TWO_INTERVALS, 0f, 2f).Should().BeFalse();
    }

    [TestMethod]
    public void IsAtRest_ASpeedBetweenTheLimits_KeepsTheStateItHad()
    {
        const float between = (HullRestRules.REST_SPEED + HullRestRules.MOVE_SPEED) / 2f;

        HullRestRules.RestTracker atRest = new();
        atRest.IsAtRest(TWO_INTERVALS, between * TWO_INTERVALS, 0f).Should().BeTrue("a hull that stood still stays still until it is clearly moving");

        HullRestRules.RestTracker moving = new();
        moving.IsAtRest(TWO_INTERVALS, 1f * TWO_INTERVALS, 0f).Should().BeFalse();
        moving.IsAtRest(TWO_INTERVALS, between * TWO_INTERVALS, 0f).Should().BeFalse("a hull that moves keeps being extrapolated until it is clearly still");
    }

    [TestMethod]
    public void IsAtRest_ADriveThatSlowsDownAndStops_ReturnsToRestBelowTheLowLimit()
    {
        HullRestRules.RestTracker tracker = new();
        tracker.IsAtRest(TWO_INTERVALS, 2f * TWO_INTERVALS, 0f).Should().BeFalse();
        tracker.IsAtRest(TWO_INTERVALS, 0.5f * TWO_INTERVALS, 0f).Should().BeFalse();
        tracker.IsAtRest(TWO_INTERVALS, 0.08f * TWO_INTERVALS, 0f).Should().BeFalse();
        tracker.IsAtRest(TWO_INTERVALS, 0.04f * TWO_INTERVALS, 0f).Should().BeTrue();
        // and a creep that wakes it again needs to be clearly faster than the low limit
        tracker.IsAtRest(TWO_INTERVALS, 0.1f * TWO_INTERVALS, 0f).Should().BeTrue();
        tracker.IsAtRest(TWO_INTERVALS, 0.2f * TWO_INTERVALS, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void IsAtRest_ATurnRateAboveTheHighLimit_WakesAHullThatStoodStill()
    {
        HullRestRules.RestTracker tracker = new();
        tracker.IsAtRest(TWO_INTERVALS, 0f, 2f * TWO_INTERVALS).Should().BeFalse("2 degrees per second is above the 1.2 limit");
        tracker.IsAtRest(TWO_INTERVALS, 0f, 0.3f * TWO_INTERVALS).Should().BeTrue("0.3 degrees per second is below the 0.5 limit");
    }

    [TestMethod]
    public void IsAtRest_ATurnRateBetweenTheLimits_KeepsTheStateItHad()
    {
        const float between = (HullRestRules.REST_TURN_RATE + HullRestRules.MOVE_TURN_RATE) / 2f;

        new HullRestRules.RestTracker().IsAtRest(TWO_INTERVALS, 0f, between * TWO_INTERVALS).Should().BeTrue();

        HullRestRules.RestTracker turning = new();
        turning.IsAtRest(TWO_INTERVALS, 0f, 5f * TWO_INTERVALS).Should().BeFalse();
        turning.IsAtRest(TWO_INTERVALS, 0f, between * TWO_INTERVALS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAtRest_AMovingHull_IsStillWhileOneOfTheTwoRatesIsAboveTheLowLimit()
    {
        HullRestRules.RestTracker tracker = new();
        tracker.IsAtRest(TWO_INTERVALS, 1f * TWO_INTERVALS, 0f).Should().BeFalse();
        // Still, but turning at 0.8 degrees per second: not rest yet
        tracker.IsAtRest(TWO_INTERVALS, 0f, 0.8f * TWO_INTERVALS).Should().BeFalse();
        tracker.IsAtRest(TWO_INTERVALS, 0f, 0.2f * TWO_INTERVALS).Should().BeTrue();
    }

    [TestMethod]
    public void Reset_ReturnsToRest()
    {
        HullRestRules.RestTracker tracker = new();
        tracker.IsAtRest(TWO_INTERVALS, 1f * TWO_INTERVALS, 0f);
        tracker.AtRest.Should().BeFalse();

        tracker.Reset();

        tracker.AtRest.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(0f, 0.1f, 0f)]
    [DataRow(-0.066f, 0.1f, 0f)]
    [DataRow(float.NaN, 0.1f, 0f)]
    [DataRow(0.066f, float.NaN, 0f)]
    [DataRow(0.066f, 0.1f, float.NaN)]
    [DataRow(0.066f, -1f, 0f)]
    public void IsAtRest_NoUsableWindow_LeavesTheStateAlone(float window, float distance, float degrees)
    {
        HullRestRules.RestTracker moving = new();
        moving.IsAtRest(TWO_INTERVALS, 1f * TWO_INTERVALS, 0f);
        moving.IsAtRest(window, distance, degrees).Should().BeFalse();

        new HullRestRules.RestTracker().IsAtRest(window, distance, degrees).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(0f, 0f, HullRestRules.IdleHoldAction.None)]
    [DataRow(0.0003f, 0.001f, HullRestRules.IdleHoldAction.None)]
    [DataRow(0.001f, 0f, HullRestRules.IdleHoldAction.Place)]
    [DataRow(0f, 0.1f, HullRestRules.IdleHoldAction.Place)]
    [DataRow(0.02f, 0.2f, HullRestRules.IdleHoldAction.Place)]
    [DataRow(0.021f, 0f, HullRestRules.IdleHoldAction.Ease)]
    [DataRow(0f, 0.3f, HullRestRules.IdleHoldAction.Ease)]
    [DataRow(1f, 5f, HullRestRules.IdleHoldAction.Ease)]
    [DataRow(4.9f, 0f, HullRestRules.IdleHoldAction.Ease)]
    [DataRow(5.1f, 0f, HullRestRules.IdleHoldAction.Place)]
    [DataRow(50f, 90f, HullRestRules.IdleHoldAction.Place)]
    public void DecideIdleHold_ByHowFarTheHullStandsFromItsPose(float distance, float degrees, HullRestRules.IdleHoldAction expected)
    {
        HullRestRules.DecideIdleHold(distance, degrees).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(float.NaN, 0f)]
    [DataRow(0f, float.NaN)]
    [DataRow(float.PositiveInfinity, 0f)]
    [DataRow(-1f, 0f)]
    public void DecideIdleHold_ANonsenseOffset_PutsTheHullBack(float distance, float degrees)
    {
        // A broken measurement must not leave the copy wherever the rest of the game pushed it
        HullRestRules.DecideIdleHold(distance, degrees).Should().Be(HullRestRules.IdleHoldAction.Place);
    }

    [TestMethod]
    [DataRow(0f, false)]
    [DataRow(0.5f, false)]
    [DataRow(1.99f, false)]
    [DataRow(HullRestRules.HOLD_SETTLE_SECONDS, true)]
    [DataRow(60f, true)]
    [DataRow(float.PositiveInfinity, true)]
    [DataRow(float.NaN, false)]
    public void IsHoldSettled_OnlyAfterTheSettleTime(float secondsHeld, bool expected)
    {
        // Right after a drive the drawn hull is eased onto the last snapshot (an extrapolation overshoots a stop by 6 to 18 cm): that is not a push on the copy
        HullRestRules.IsHoldSettled(secondsHeld).Should().Be(expected);
    }

    // The hold repairs a displaced copy without a word. The tally is what turns that into one log line in a Release session, the only trace of a push on the copy.
    [TestMethod]
    public void TryTake_NothingAdded_ReportsNothing()
    {
        new HullRestRules.DisplacementTally().TryTake(1000f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryTake_SeveralCorrections_AreReportedWithTheirSumsAndMaxima()
    {
        HullRestRules.DisplacementTally tally = new();
        tally.Add(0.004f, 0.02f);
        tally.Add(0.01f, 0.1f);
        tally.Add(0.002f, 0f);

        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out HullRestRules.DisplacementSummary summary).Should().BeTrue();

        summary.Count.Should().Be(3);
        summary.Meters.Should().BeApproximately(0.016f, 1e-6f);
        summary.Degrees.Should().BeApproximately(0.12f, 1e-6f);
        summary.MaxMeters.Should().Be(0.01f);
        summary.MaxDegrees.Should().Be(0.1f);
    }

    [TestMethod]
    public void TryTake_AfterAReport_StartsFromNothing()
    {
        HullRestRules.DisplacementTally tally = new();
        tally.Add(0.01f, 0f);
        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeTrue();

        tally.Count.Should().Be(0);
        tally.TryTake(1000f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse("nothing happened since the report");
    }

    [TestMethod]
    public void TryTake_AtMostOncePerInterval_AndTheWaitingSumIsKept()
    {
        HullRestRules.DisplacementTally tally = new();
        tally.Add(0.01f, 0f);
        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeTrue();

        tally.Add(0.02f, 0f);
        tally.TryTake(100f + INTERVAL - 0.5f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse("30 s have not passed");
        tally.Add(0.03f, 0f);

        tally.TryTake(100f + INTERVAL, INTERVAL, MIN_METERS, MIN_DEGREES, out HullRestRules.DisplacementSummary summary).Should().BeTrue();
        summary.Meters.Should().BeApproximately(0.05f, 1e-6f, "what happened while the report was held back is not lost");
        summary.Count.Should().Be(2);
    }

    [TestMethod]
    public void TryTake_LessThanTheDeadBand_WaitsUntilItAddsUp()
    {
        const float part = 0.3f;
        HullRestRules.DisplacementTally tally = new();
        tally.Add(MIN_METERS * part, MIN_DEGREES * part);
        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse("a third of the dead band is noise");

        tally.Add(MIN_METERS * part, MIN_DEGREES * part);
        tally.Add(MIN_METERS * part, MIN_DEGREES * part);
        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse("0.9 of the dead band is still below it");
        tally.Add(MIN_METERS * part, MIN_DEGREES * part);

        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out HullRestRules.DisplacementSummary summary).Should().BeTrue("1.2 dead bands are news");
        summary.Count.Should().Be(4);
    }

    [TestMethod]
    public void TryTake_ATurnAloneIsNews()
    {
        HullRestRules.DisplacementTally tally = new();
        tally.Add(0f, 0.3f);

        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out HullRestRules.DisplacementSummary summary).Should().BeTrue();
        summary.Degrees.Should().BeApproximately(0.3f, 1e-6f);
        summary.Meters.Should().Be(0f);
    }

    [TestMethod]
    [DataRow(float.NaN, 0f)]
    [DataRow(0f, float.NaN)]
    [DataRow(float.PositiveInfinity, 0f)]
    [DataRow(0f, float.NegativeInfinity)]
    [DataRow(-1f, 0f)]
    [DataRow(0f, -1f)]
    [DataRow(0f, 0f)]
    public void Add_ABrokenMeasurement_CountsAsNothing(float distance, float degrees)
    {
        HullRestRules.DisplacementTally tally = new();

        tally.Add(distance, degrees);

        tally.Count.Should().Be(0);
        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Add_AGoodMeasurementNextToABrokenOne_KeepsTheGoodPart()
    {
        HullRestRules.DisplacementTally tally = new();

        tally.Add(float.NaN, 0.4f);

        tally.Count.Should().Be(1);
        tally.TryTake(100f, INTERVAL, MIN_METERS, MIN_DEGREES, out HullRestRules.DisplacementSummary summary).Should().BeTrue();
        summary.Meters.Should().Be(0f);
        summary.Degrees.Should().BeApproximately(0.4f, 1e-6f);
    }

    [TestMethod]
    public void TryTake_ABrokenClock_StaysSilent()
    {
        HullRestRules.DisplacementTally tally = new();
        tally.Add(0.01f, 0f);

        tally.TryTake(float.NaN, INTERVAL, MIN_METERS, MIN_DEGREES, out _).Should().BeFalse();
        tally.Count.Should().Be(1, "the displacement is kept for the next report");
    }
}
