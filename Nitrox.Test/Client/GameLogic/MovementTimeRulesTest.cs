namespace NitroxClient.GameLogic;

/// <summary>
///     The timestamps of the movement snapshots (<see cref="MovementTimeRules" />, bug 7). They are the server's active play time, 28 800 s at 8 hours and growing, and a float does not hold
///     them to the millisecond: the world "Test" of the play session was at 5.2 h when it started and is at about 8 h now, past 9.1 h the step of a float is 3.9 ms, and every stamp and every
///     "now" used to be rounded to it, which is timing noise on each snapshot of each vehicle and each remote player.
/// </summary>
[TestClass]
public class MovementTimeRulesTest
{
    // the real play time of a world at 1 h, 5.2 h (the first play session), 8 h (now), 9.2 h (the step doubles), 18.3 h, 36.5 h and 100 h
    private static readonly double[] worldAges = [3600.0, 18655.0, 28800.0, 33100.0, 65800.0, 131400.0, 360000.0];

    [TestMethod]
    public void AgeKeepsTheMillisecondAtEveryAgeOfAWorld()
    {
        const double SNAPSHOT_AGE = 0.0333; // a snapshot of a 30 Hz stream, as old as one period
        foreach (double worldAge in worldAges)
        {
            double snapshotTime = worldAge + 0.123456;
            double now = snapshotTime + SNAPSHOT_AGE;

            MovementTimeRules.Age(now, snapshotTime).Should().BeApproximately((float)SNAPSHOT_AGE, 1e-6f, $"world of {worldAge} s");
        }
    }

    [TestMethod]
    public void TheOldFloatArithmeticWasOffByMillisecondsFromNineHoursOfPlay()
    {
        // what the replicators did: (float)now - (float)snapshotTime. The premise of the change: this is wrong by more than a millisecond for some snapshot from 5 h of play on, and by more than 4 ms at
        // 36 h (measured worst cases over these 200 snapshots: 1.9 ms, 2.0 ms and 13.6 ms), while the double arithmetic of MovementTimeRules is right to a millionth of a second
        foreach ((double worldAge, float atLeast) in new[] { (18655.0, 0.001f), (33100.0, 0.001f), (131400.0, 0.004f) })
        {
            float worstFloatError = 0f;
            float worstDoubleError = 0f;
            for (int i = 0; i < 200; i++)
            {
                double snapshotTime = worldAge + i * 0.0517;
                double now = snapshotTime + 0.0333;
                float floatAge = (float)now - (float)snapshotTime;
                worstFloatError = Math.Max(worstFloatError, Math.Abs(floatAge - 0.0333f));
                worstDoubleError = Math.Max(worstDoubleError, Math.Abs(MovementTimeRules.Age(now, snapshotTime) - 0.0333f));
            }

            worstFloatError.Should().BeGreaterThan(atLeast, $"float, world of {worldAge} s");
            worstDoubleError.Should().BeLessThan(1e-6f, $"double, world of {worldAge} s");
        }
    }

    [TestMethod]
    public void TheStepOfAFloatDoublesEveryTimeTheWorldDoublesItsAge()
    {
        // the reason that it gets worse as the world gets older (this is the premise written down: the step of a float at the age of the world)
        FloatStep(18655f).Should().Be(0.001953125f); // 5.2 h: 1.95 ms
        FloatStep(28800f).Should().Be(0.001953125f); // 8 h: 1.95 ms
        FloatStep(33100f).Should().Be(0.00390625f); // 9.2 h: 3.9 ms
        FloatStep(65800f).Should().Be(0.0078125f); // 18.3 h: 7.8 ms
        FloatStep(131400f).Should().Be(0.015625f); // 36.5 h: 15.6 ms
    }

    [TestMethod]
    public void AgeIsNegativeForATimeInTheFuture()
    {
        MovementTimeRules.Age(100000.5, 100000.75).Should().BeApproximately(-0.25f, 1e-6f);
    }

    [TestMethod]
    public void TheSpanOfTwoSnapshotsIsTheirDistanceInTime()
    {
        // the window of the velocity estimate: newest minus oldest
        MovementTimeRules.Age(28800.2, 28800.1334).Should().BeApproximately(0.0666f, 1e-6f);
    }

    [TestMethod]
    public void TheInterpolationFactorIsExactAtEveryAgeOfAWorld()
    {
        foreach (double worldAge in worldAges)
        {
            double previous = worldAge + 10.0;
            double next = previous + 0.1332; // two periods of a 30 Hz stream
            MovementTimeRules.InterpolationFactor(previous, previous, next).Should().Be(0f);
            MovementTimeRules.InterpolationFactor(next, previous, next).Should().Be(1f);
            MovementTimeRules.InterpolationFactor(previous + 0.0666, previous, next).Should().BeApproximately(0.5f, 1e-4f, $"world of {worldAge} s");
            MovementTimeRules.InterpolationFactor(previous + 0.0333, previous, next).Should().BeApproximately(0.25f, 1e-4f, $"world of {worldAge} s");
        }
    }

    [TestMethod]
    public void TheInterpolationFactorIsNotClamped()
    {
        // the replicator asks only while the two snapshots surround the time; outside that the factor says how far outside
        MovementTimeRules.InterpolationFactor(28800.0 - 0.05, 28800.0, 28800.1).Should().BeApproximately(-0.5f, 1e-4f);
        MovementTimeRules.InterpolationFactor(28800.0 + 0.15, 28800.0, 28800.1).Should().BeApproximately(1.5f, 1e-4f);
    }

    [TestMethod]
    public void AFloatFactorStillComesOutOfTheDoubleDifferencesForAHundredHoursOfPlay()
    {
        // the precision of the factor itself: both differences are small numbers, so 24 bits are plenty
        double previous = 360000.0;
        double next = previous + 0.0666;
        for (int i = 0; i <= 10; i++)
        {
            double now = previous + i * 0.00666;
            MovementTimeRules.InterpolationFactor(now, previous, next).Should().BeApproximately(i / 10f, 1e-4f);
        }
    }

    private static float FloatStep(float seconds) => MathF.BitIncrement(seconds) - seconds;
}
