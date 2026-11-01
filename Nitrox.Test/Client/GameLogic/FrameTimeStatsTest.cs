#if DEBUG
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>The frame-time numbers of the live check of the terrain hologram (<c>vuhelm perf</c>): average, median and the high percentiles of a run of frames.</summary>
[TestClass]
public class FrameTimeStatsTest
{
    [TestMethod]
    public void NoFramesGiveZeros()
    {
        FrameTimeSummary summary = FrameTimeStats.Summarize([]);

        summary.Frames.Should().Be(0);
        summary.AverageMs.Should().Be(0d);
        summary.Fps.Should().Be(0d);
        FrameTimeStats.Summarize(null).Frames.Should().Be(0);
    }

    [TestMethod]
    public void SteadyFramesGiveTheirTimeAndTheFrameRate()
    {
        float[] frames = new float[200];
        Array.Fill(frames, 0.01f);

        FrameTimeSummary summary = FrameTimeStats.Summarize(frames);

        summary.AverageMs.Should().BeApproximately(10d, 0.001d);
        summary.MedianMs.Should().BeApproximately(10d, 0.001d);
        summary.P99Ms.Should().BeApproximately(10d, 0.001d);
        summary.Fps.Should().BeApproximately(100d, 0.01d);
    }

    [TestMethod]
    public void APercentileIsTheNearestRankAndAHitchShowsInTheHighPercentilesAndTheMax()
    {
        // 98 frames of 10 ms, one of 20 ms and one of 100 ms: the 95th of 100 is still 10, the 99th is 20 and the maximum 100
        float[] frames = new float[100];
        Array.Fill(frames, 0.01f);
        frames[40] = 0.02f;
        frames[41] = 0.1f;

        FrameTimeSummary summary = FrameTimeStats.Summarize(frames);

        summary.P95Ms.Should().BeApproximately(10d, 0.001d);
        summary.P99Ms.Should().BeApproximately(20d, 0.001d);
        summary.MaxMs.Should().BeApproximately(100d, 0.001d);
        summary.AverageMs.Should().BeApproximately(11.0d, 0.001d, "98 x 10 + 20 + 100 over 100");
    }

    [TestMethod]
    public void NumbersThatAreNotFrameTimesCountAsZero()
    {
        FrameTimeSummary summary = FrameTimeStats.Summarize([float.NaN, 0.02f, float.PositiveInfinity, -1f]);

        summary.Frames.Should().Be(4);
        summary.MaxMs.Should().BeApproximately(20d, 0.001d);
        summary.AverageMs.Should().BeApproximately(5d, 0.001d);
    }

    [TestMethod]
    public void TheTextIsTheSameOnEveryMachine()
    {
        FrameTimeSummary summary = FrameTimeStats.Summarize([0.008f, 0.009f, 0.010f, 0.012f]);

        summary.ToString().Should().StartWith("frames=4 avg=9.75ms ").And.Contain(".").And.NotContain(",");
    }
}
#endif
