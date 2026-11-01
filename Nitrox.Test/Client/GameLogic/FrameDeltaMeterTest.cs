namespace NitroxClient.GameLogic;

/// <summary>
///     Bug 8: the per-frame delta of the day/night clock (<c>DayNightCycle.deltaTime</c>) must add up to the real time at every frame rate. The first version took the difference of two readings of
///     the UTC clock, which only moves in steps of its resolution, and gave <c>Time.deltaTime</c> on top on the frames in between.
/// </summary>
[TestClass]
public class FrameDeltaMeterTest
{
    /// <summary>
    ///     Ticks per second of the fake monotonic timestamp (a nanosecond; a Stopwatch ticks at 10 MHz on Windows).
    /// </summary>
    private const long FREQUENCY = 1_000_000_000L;

    /// <summary>
    ///     The resolution of the UTC clock when nothing asked the system for a finer timer.
    /// </summary>
    private const double UTC_RESOLUTION_SECONDS = 0.015625d;

    /// <summary>
    ///     The moments of the frames of a run at the given frame rate, each frame +-30 % around the average (deterministic).
    /// </summary>
    private static List<double> FrameMoments(double framesPerSecond, double seconds)
    {
        Random random = new(12345);
        List<double> moments = [0d];
        double t = 0d;
        while (t < seconds)
        {
            t += 1d / framesPerSecond * (0.7d + 0.6d * random.NextDouble());
            moments.Add(t);
        }
        return moments;
    }

    private static long Timestamp(double seconds)
    {
        return (long)(seconds * FREQUENCY);
    }

    [TestMethod]
    [DataRow(20d)]
    [DataRow(30d)]
    [DataRow(60d)]
    [DataRow(100d)]
    [DataRow(144d)]
    [DataRow(240d)]
    public void TheDeltasAddUpToTheRealTimeOfFiveMinutesAtEveryFrameRate(double framesPerSecond)
    {
        List<double> moments = FrameMoments(framesPerSecond, 300d);
        FrameDeltaMeter meter = new();

        double sum = 0d;
        for (int i = 0; i < moments.Count; i++)
        {
            float delta = meter.Next(Timestamp(moments[i]), FREQUENCY, 0.016f);
            if (i > 0)
            {
                sum += delta;
            }
        }

        double real = moments[moments.Count - 1] - moments[0];
        GameClockRules.Rate(sum, real).Should().BeApproximately(1d, 0.0005);
    }

    [TestMethod]
    public void TheFirstFrameGetsTheDeltaOfTheGameAndNeverZero()
    {
        FrameDeltaMeter meter = new();

        meter.Next(Timestamp(5d), FREQUENCY, 0.02f).Should().Be(0.02f);
        meter.Next(Timestamp(5.016d), FREQUENCY, 0.02f).Should().BeApproximately(0.016f, 1e-6f);

        FrameDeltaMeter fresh = new();
        fresh.Next(Timestamp(5d), FREQUENCY, 0f).Should().Be(FrameDeltaMeter.MINIMUM_DELTA_SECONDS);
    }

    [TestMethod]
    public void TwoReadingsOfTheSameInstantGiveTheMinimumNotZero()
    {
        FrameDeltaMeter meter = new();
        meter.Next(Timestamp(1d), FREQUENCY, 0.016f);

        // A delta of 0 ends up in a division by zero (NaN) in some scripts
        meter.Next(Timestamp(1d), FREQUENCY, 0.016f).Should().Be(FrameDeltaMeter.MINIMUM_DELTA_SECONDS);
        meter.Next(Timestamp(0.9d), FREQUENCY, 0.016f).Should().Be(FrameDeltaMeter.MINIMUM_DELTA_SECONDS);
    }

    [TestMethod]
    public void ALongFrameGivesItsWholeLengthNotTheMaximumDeltaTimeOfTheGame()
    {
        FrameDeltaMeter meter = new();
        meter.Next(Timestamp(10d), FREQUENCY, 0.016f);

        // A loading hitch of 5 s: Time.deltaTime would stop at Time.maximumDeltaTime (a third of a second), the world's time must not
        meter.Next(Timestamp(15d), FREQUENCY, 0.016f).Should().BeApproximately(5f, 1e-4f);
    }

    [TestMethod]
    public void ADifferentTimestampFrequencyGivesTheSameSeconds()
    {
        FrameDeltaMeter meter = new();
        const long stopwatchFrequency = 10_000_000L;
        meter.Next(123_456_789L, stopwatchFrequency, 0.016f);

        meter.Next(123_456_789L + stopwatchFrequency / 60, stopwatchFrequency, 0.016f).Should().BeApproximately(1f / 60f, 1e-6f);
    }

    /// <summary>
    ///     What the first version did (UTC clock difference, <c>Time.deltaTime</c> when the difference is 0), played against a UTC clock that only moves in steps of its resolution. Kept as the
    ///     reason the meter exists: above the frame rate that matches the resolution the deltas ran fast.
    /// </summary>
    private static double OldRuleRate(double framesPerSecond)
    {
        List<double> moments = FrameMoments(framesPerSecond, 300d);
        double sum = 0d;
        double previousClock = Math.Floor(moments[0] / UTC_RESOLUTION_SECONDS) * UTC_RESOLUTION_SECONDS;
        for (int i = 1; i < moments.Count; i++)
        {
            double clock = Math.Floor(moments[i] / UTC_RESOLUTION_SECONDS) * UTC_RESOLUTION_SECONDS;
            double delta = clock - previousClock;
            if (Math.Abs(delta) < 1e-9)
            {
                delta = Math.Max(moments[i] - moments[i - 1], 0.0001d);
            }
            sum += delta;
            previousClock = clock;
        }
        return GameClockRules.Rate(sum, moments[moments.Count - 1] - moments[0]);
    }

    [TestMethod]
    public void TheUtcClockDifferenceRanFastAtHighFrameRatesAndRightAtLowOnes()
    {
        OldRuleRate(144d).Should().BeGreaterThan(1.3d, "the frames between two steps of the UTC clock were counted at their own length on top of the step");
        OldRuleRate(240d).Should().BeGreaterThan(1.5d);
        OldRuleRate(30d).Should().BeApproximately(1d, 0.02, "a frame longer than the resolution of the UTC clock always sees a step");
    }
}
