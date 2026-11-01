namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     Makes the outcome of the Sunbeam range check (in range of the landing site or not) a single decision for all players (ST1h). Every client proposes the outcome when the
///     range check goal completes on it, the first proposal wins and every later one gets the same answer, so the two mutually exclusive PDA goals can not both be played.
/// </summary>
/// <remarks>
///     Nothing here depends on who proposed: if the proposing player disconnects right after, the decision stands, and if it disconnects before, the proposal of any other
///     client decides. The decision lives in memory only. It is cleared when the Sunbeam event restarts (<see cref="Reset" />) and, after a server restart, replaced by the
///     outcome goal that is already completed (<see cref="Decide" />).
/// </remarks>
internal sealed class SunbeamRangeArbiter
{
    private readonly Lock padlock = new();
    private bool? decidedInRange;

    /// <summary>
    ///     The accepted outcome, or null while nothing was decided.
    /// </summary>
    public bool? DecidedInRange
    {
        get
        {
            lock (padlock)
            {
                return decidedInRange;
            }
        }
    }

    /// <summary>
    ///     Takes a proposal and returns the outcome that counts for everybody.
    /// </summary>
    /// <param name="proposedInRange">True if the proposing client found a player at the landing site</param>
    /// <param name="inRangeGoalCompleted">True if the in range PDA goal is already completed on the server</param>
    /// <param name="outOfRangeGoalCompleted">True if the out of range PDA goal is already completed on the server</param>
    /// <param name="isNewDecision">True only for the call that made the decision, the one whose outcome has to be sent to everybody</param>
    /// <returns>True if the Sunbeam shoot-down counts as seen from the landing site</returns>
    public bool Decide(bool proposedInRange, bool inRangeGoalCompleted, bool outOfRangeGoalCompleted, out bool isNewDecision)
    {
        lock (padlock)
        {
            if (decidedInRange is { } alreadyDecided)
            {
                isNewDecision = false;
                return alreadyDecided;
            }

            // A goal that is already completed (a server restart wiped the decision, or someone completed it by console) must not be contradicted by the other one
            if (inRangeGoalCompleted || outOfRangeGoalCompleted)
            {
                decidedInRange = inRangeGoalCompleted;
                isNewDecision = false;
                return inRangeGoalCompleted;
            }

            decidedInRange = proposedInRange;
            isNewDecision = true;
            return proposedInRange;
        }
    }

    /// <summary>
    ///     Forgets the decision. For when the Sunbeam event restarts and the range check runs again.
    /// </summary>
    public void Reset()
    {
        lock (padlock)
        {
            decidedInRange = null;
        }
    }
}
