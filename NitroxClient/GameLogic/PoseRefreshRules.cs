namespace NitroxClient.GameLogic;

/// <summary>
/// When the local player announces his underwater state again (bug 6, PLAYERS.md PL9 (6)). Plain numbers, no game types, so that it can be tested; <c>UnderwaterStateTracker</c> sends.
/// </summary>
/// <remarks>
/// The state used to be sent when it changed, and only then. The copy of the player on another client takes the pose from it (<see cref="RemotePoseTracker" />), so a state that went
/// missing once (the event came before the body of the player existed on that client, the player joined with a state that is not his) stayed wrong until the next change, and the next change
/// does not come while the player walks from the lifepod into a base and on into a docked Cyclops: all of them are dry. The same event, sent again every <see cref="REFRESH_INTERVAL" />
/// seconds, costs one small packet per player and heals every such state. It is the packet that exists (the server keeps and relays it as it does the first one), no new one, the wire is unchanged.
/// </remarks>
public static class PoseRefreshRules
{
    /// <summary>
    /// A state that did not change is announced again after this many seconds.
    /// </summary>
    public const float REFRESH_INTERVAL = 5f;

    /// <summary>
    /// A copy that walks in a Cyclops and still has the swimming state after this many seconds (<see cref="REFRESH_INTERVAL" /> plus 2 s for the transit of the repeat and the one second
    /// step of the watch that looks) has had every chance of a correction: the sender announced a state again in the meantime. What is left is not a boarding race.
    /// </summary>
    public const float SETTLED_AFTER = REFRESH_INTERVAL + 2f;

    /// <summary>
    /// A report that ends the swimming state of a walker this many seconds or more after his first pawn packet is not the boarding race (the pawn packet and the state change are sent by the
    /// same client within a frame or two of each other, in either order): it is a state that had to be corrected, by a late event or by the next announcement.
    /// </summary>
    public const float LATE_AFTER = 1f;

    /// <summary>
    /// The state is sent now: it changed, or the last announcement is <see cref="REFRESH_INTERVAL" /> seconds old (or older).
    /// </summary>
    public static bool IsDue(bool changed, float secondsSinceLastSend)
    {
        return changed || secondsSinceLastSend >= REFRESH_INTERVAL;
    }

    /// <summary>
    /// The copy has walked for <paramref name="secondsWalking" /> seconds (<see cref="float.NaN" /> when he does not): long enough that a swimming state that is still reported is no race
    /// of the boarding (<see cref="SETTLED_AFTER" />).
    /// </summary>
    public static bool IsSettled(float secondsWalking)
    {
        return !float.IsNaN(secondsWalking) && secondsWalking >= SETTLED_AFTER;
    }

    /// <summary>
    /// A report ended the swimming state of a walker <paramref name="secondsWalking" /> seconds after he began to walk (<see cref="float.NaN" /> when it did not end one): late enough to be
    /// worth a line (<see cref="LATE_AFTER" />), not the boarding race.
    /// </summary>
    public static bool IsLateCorrection(float secondsWalking)
    {
        return !float.IsNaN(secondsWalking) && secondsWalking >= LATE_AFTER;
    }
}
