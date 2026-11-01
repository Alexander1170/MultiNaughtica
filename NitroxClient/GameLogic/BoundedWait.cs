using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// Gives a wait a deadline. A player is frozen (<c>Player.cinematicModeActive</c>) while the game loads something he needs (the terrain under him, his escape pod, the entities
/// around him), and he used to stay frozen for good when that never finished. Every such wait now keeps one of these and lets go of the player when it says to stop.
/// Holds no Unity type, the clock is injected (<c>() =&gt; Time.unscaledTime</c> in the game), so the decision can be unit tested.
/// </summary>
/// <example>
/// <code>
/// BoundedWait wait = new(10f, () => Time.unscaledTime);
/// while (wait.KeepWaiting(LargeWorldStreamer.main.IsWorldSettled)) { yield return null; }
/// if (wait.TimedOut) { Log.Warn($"Gave up after {wait.Waited} s"); }
/// </code>
/// </example>
public sealed class BoundedWait
{
    private readonly Func<float> clock;
    private readonly float startTime;
    private readonly float timeoutSeconds;

    /// <param name="timeoutSeconds">How long the wait may last, measured from this call</param>
    /// <param name="clock">Seconds, in a unit that keeps running whatever the game does with its time scale</param>
    public BoundedWait(float timeoutSeconds, Func<float> clock)
    {
        this.timeoutSeconds = timeoutSeconds;
        this.clock = clock;
        startTime = clock();
    }

    /// <summary>
    /// True once <see cref="KeepWaiting" /> stopped the wait because the deadline passed before the condition was met
    /// </summary>
    public bool TimedOut { get; private set; }

    /// <summary>
    /// Seconds between the creation of this wait and the last <see cref="KeepWaiting" /> call
    /// </summary>
    public float Waited { get; private set; }

    /// <summary>
    /// Call it once per frame. The condition is tested before the deadline, so that a long frame (a hitch while loading) that lets the deadline pass but also
    /// finishes the loading counts as done, not as a timeout. It is a plain method instead of a nested coroutine so that the caller's own try/finally stays the one
    /// place that releases the player, also when the condition throws.
    /// </summary>
    /// <returns>True if the caller should wait another frame, false once the condition is met or the deadline passed (see <see cref="TimedOut" />)</returns>
    public bool KeepWaiting(Func<bool> isDone)
    {
        Waited = clock() - startTime;
        if (isDone())
        {
            return false;
        }

        if (Waited >= timeoutSeconds)
        {
            TimedOut = true;
            return false;
        }

        return true;
    }
}
