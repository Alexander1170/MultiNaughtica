namespace NitroxClient.GameLogic;

/// <summary>
/// The decisions of the Bleeder claim (K18, <see cref="BleederClaims" />), free of Unity types so that they can be tested. A Bleeder attaches to the player whose own copy of it
/// bumps into them, and that player's client has to simulate it for the attack to run (the actions of a creature only run on the client that holds its lock), so the player
/// whose copy accepted the contact asks for an exclusive lock on the Bleeder and gives it back to the normal hand-over once the Bleeder let go.
/// </summary>
public static class BleederClaimRules
{
    /// <summary>
    /// Seconds after which a claim whose answer never came is asked for again (a lost answer must not block the Bleeder for good).
    /// </summary>
    public const float CLAIM_ANSWER_TIMEOUT = 10f;

    public enum ClaimOutcome
    {
        /// <summary>The server refused (somebody else holds the Bleeder exclusively, i.e. it is attached to them): undo the contact the copy accepted.</summary>
        Detach,
        /// <summary>The lock is ours but the Bleeder let go or died while the answer was on its way: give the lock back.</summary>
        ReleaseLock,
        /// <summary>The lock is ours and the Bleeder is still on its target: it is simulated here from now on.</summary>
        KeepLock
    }

    /// <param name="holdsExclusiveLock">We already hold the Bleeder exclusively, there is nothing to ask for.</param>
    /// <param name="secondsSinceRequest">Seconds since an unanswered request, or null if none is waiting.</param>
    public static bool ShouldRequestClaim(bool holdsExclusiveLock, float? secondsSinceRequest)
    {
        if (holdsExclusiveLock)
        {
            return false;
        }
        return secondsSinceRequest == null || secondsSinceRequest.Value >= CLAIM_ANSWER_TIMEOUT;
    }

    public static ClaimOutcome JudgeClaimResponse(bool lockAcquired, bool stillHasTarget)
    {
        if (!lockAcquired)
        {
            return ClaimOutcome.Detach;
        }
        return stillHasTarget ? ClaimOutcome.KeepLock : ClaimOutcome.ReleaseLock;
    }

    /// <summary>
    /// Whether the exclusive lock goes back to the normal (transient) hand-over after the Bleeder let go of its target. Not for a Bleeder that is leaving the world: the
    /// component is disabled in <c>OnDisable</c>, and a dead Bleeder loses its lock when its entity is destroyed.
    /// </summary>
    public static bool ShouldReleaseLockAfterDetach(bool holdsExclusiveLock, bool componentActive, bool bleederAlive)
    {
        return holdsExclusiveLock && componentActive && bleederAlive;
    }

    /// <summary>
    /// Whether the contact is undone some seconds after the lock was granted: we hold the Bleeder and it wrote the contact into its state, but the attack never started (vanilla
    /// chooses the action with a rotating check, and a more urgent action can keep it from running), so the lock would stay exclusive and the player occupied for good.
    /// </summary>
    public static bool ShouldAbandonClaim(bool holdsExclusiveLock, bool hasTarget, bool attackActive)
    {
        return holdsExclusiveLock && hasTarget && !attackActive;
    }

    /// <summary>
    /// Whether the Bleeder still shows the state of an attack after the server took its lock from us. Vanilla ends the attack in <c>StopPerform</c>, which does not run for a
    /// creature we do not simulate, so the attack has to be ended by hand or the Bleeder stays pinned to a player who no longer simulates it.
    /// </summary>
    public static bool NeedsDetachAfterLockLoss(bool attackActive, bool hasTarget)
    {
        return attackActive || hasTarget;
    }
}
