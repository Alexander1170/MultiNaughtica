namespace NitroxClient.GameLogic;

/// <summary>
///     The per-frame delta of the day/night clock (<c>DayNightCycle.deltaTime</c>, which the bioreactors, the reactors, the chargers, the solar panels and the flashlights integrate), measured on a
///     monotonic high resolution timestamp. Plain numbers, no game types. See <see cref="TimeManager.DeltaTime" />.
/// </summary>
/// <remarks>
///     The first versions took the difference of two readings of the UTC clock and fell back to <c>Time.deltaTime</c> when the readings were equal. The UTC clock only moves in steps of its
///     resolution (about 15.6 ms unless the process or the system asked for more): at a frame time below that, the frame that sees the step gets the whole step and the frames in between get their
///     own <c>Time.deltaTime</c> on top, so the sum of the deltas ran up to 1.5 times faster than real time (2 - frame time / resolution). A timestamp of <c>Stopwatch.GetTimestamp</c> has no
///     such steps: the deltas add up to the real time at every frame rate. A delta is not limited by <c>Time.maximumDeltaTime</c> either, a long frame gives its whole length (the reason the
///     game's own delta is not used).
/// </remarks>
public sealed class FrameDeltaMeter
{
    /// <summary>
    ///     A delta of zero ends up in a division by zero (NaN) in some scripts, so a delta is never smaller than this.
    /// </summary>
    public const float MINIMUM_DELTA_SECONDS = 0.0001f;

    private bool hasPrevious;
    private long previousTimestamp;

    /// <summary>
    ///     The delta since the previous call, in seconds. The first call has nothing to measure from and gives <paramref name="firstDeltaSeconds" />.
    /// </summary>
    /// <param name="timestamp">A monotonic timestamp (<c>Stopwatch.GetTimestamp()</c>).</param>
    /// <param name="frequency">The ticks per second of <paramref name="timestamp" /> (<c>Stopwatch.Frequency</c>).</param>
    /// <param name="firstDeltaSeconds">What to answer when there is no previous call (the game's own delta of this frame).</param>
    public float Next(long timestamp, long frequency, float firstDeltaSeconds)
    {
        float delta = hasPrevious ? (float)((timestamp - previousTimestamp) / (double)frequency) : firstDeltaSeconds;
        previousTimestamp = timestamp;
        hasPrevious = true;
        return delta < MINIMUM_DELTA_SECONDS ? MINIMUM_DELTA_SECONDS : delta;
    }
}
