namespace NitroxClient.GameLogic;

/// <summary>
/// The arithmetic on the timestamps of movement snapshots (<c>MovementReplicator</c>, <c>RemotePlayerMovementReplicator</c> and the broadcasters). Contains no Unity types so that it can be tested
/// without the game (bug 7, the passenger's wiggle).
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is stamped with the server's active play time (<c>TimeManager.RealTimeElapsed</c>, seconds since the world was created), and the receiver compares it with its own reading of the same
/// clock. These times used to be a <c>float</c>. A float has 24 bits of mantissa, so the step between two of them grows with the age of the world: 1.95 ms from 4.5 to 9.1 h of play, <b>3.9 ms from
/// 9.1 h</b>, 7.8 ms from 18.2 h, 15.6 ms from 36.4 h (the world "Test" of the first play session was at 5.2 h when it started and at about 8 h by the second). Every stamp and every "now" was rounded
/// to that step, which is timing noise on every snapshot: at 5 m/s the age of a snapshot is wrong by up to one step, i.e. the drawn position by up to 2 cm at 9 h and 8 cm at 36 h, in the direction of
/// travel, and it differs from one snapshot to the next.
/// </para>
/// <para>
/// The times are now <c>double</c> (an exact reading to well below a microsecond for any age a world reaches), on the wire and in the replicators. Only what is small is a float again: the DIFFERENCE
/// of two times (an age, the span of the snapshots, a latency), computed from the two doubles first, so nothing is lost in the subtraction.
/// </para>
/// </remarks>
public static class MovementTimeRules
{
    /// <summary>
    /// Seconds by which <paramref name="now" /> is after <paramref name="earlier" /> (negative when it is before), both server active times.
    /// </summary>
    public static float Age(double now, double earlier)
    {
        return (float)(now - earlier);
    }

    /// <summary>
    /// How far <paramref name="now" /> is on its way from <paramref name="previousTime" /> (0) to <paramref name="nextTime" /> (1): the interpolation factor of two buffered snapshots.
    /// Not clamped (the caller only asks while the two snapshots surround the time).
    /// </summary>
    public static float InterpolationFactor(double now, double previousTime, double nextTime)
    {
        return (float)((now - previousTime) / (nextTime - previousTime));
    }
}
