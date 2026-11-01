#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>What a run of frame times came to (milliseconds), for the live check of the terrain hologram (<c>vuhelm perf</c>).</summary>
public readonly struct FrameTimeSummary
{
    public FrameTimeSummary(int frames, double averageMs, double medianMs, double p95Ms, double p99Ms, double maxMs)
    {
        Frames = frames;
        AverageMs = averageMs;
        MedianMs = medianMs;
        P95Ms = p95Ms;
        P99Ms = p99Ms;
        MaxMs = maxMs;
    }

    public int Frames { get; }
    public double AverageMs { get; }
    public double MedianMs { get; }
    public double P95Ms { get; }
    public double P99Ms { get; }
    public double MaxMs { get; }

    /// <summary>Frames per second of the average frame; 0 for no frames.</summary>
    public double Fps => AverageMs > 0d ? 1000d / AverageMs : 0d;

    /// <summary><c>frames=1200 avg=8.31ms p50=8.20ms p95=9.80ms p99=12.40ms max=31.00ms fps=120.3</c>, the same format on every client.</summary>
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "frames={0} avg={1:F2}ms p50={2:F2}ms p95={3:F2}ms p99={4:F2}ms max={5:F2}ms fps={6:F1}", Frames, AverageMs, MedianMs, P95Ms, P99Ms, MaxMs, Fps);
    }
}

/// <summary>The arithmetic of the frame-time sampling of the live check: the average, the median and the high percentiles (nearest rank) of the frame times in seconds.</summary>
public static class FrameTimeStats
{
    public static FrameTimeSummary Summarize(IReadOnlyList<float> frameSeconds)
    {
        if (frameSeconds == null || frameSeconds.Count == 0)
        {
            return new FrameTimeSummary(0, 0d, 0d, 0d, 0d, 0d);
        }
        double[] ms = new double[frameSeconds.Count];
        double sum = 0d;
        for (int i = 0; i < ms.Length; i++)
        {
            double value = float.IsNaN(frameSeconds[i]) || float.IsInfinity(frameSeconds[i]) ? 0d : Math.Max(0d, frameSeconds[i]) * 1000d;
            ms[i] = value;
            sum += value;
        }
        Array.Sort(ms);
        return new FrameTimeSummary(ms.Length, sum / ms.Length, Percentile(ms, 0.5d), Percentile(ms, 0.95d), Percentile(ms, 0.99d), ms[ms.Length - 1]);
    }

    /// <summary>The value at the nearest rank of <paramref name="fraction" /> in an ascending list (the 95th percentile of 100 frames is the 95th slowest-but-five).</summary>
    private static double Percentile(double[] ascending, double fraction)
    {
        int rank = (int)Math.Ceiling(fraction * ascending.Length);
        return ascending[Math.Min(ascending.Length - 1, Math.Max(0, rank - 1))];
    }
}
#endif
