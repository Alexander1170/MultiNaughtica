using Nitrox.Model.Subnautica.Helper;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
/// Which story goals the server passes on to the other players.
/// </summary>
internal static class StoryRelayRules
{
    /// <summary>
    /// A goal is passed on when the server learns it from this packet (every client runs the same scheduled goals, the first report is enough). The goal of a played radio message
    /// is the exception: <see cref="StoryManager.RemovedLatestRadioMessage" /> completes it on the server in the moment the play arrives, before the player's own report of the goal
    /// can, so that report always looks like a repeat, and a client that did not complete the goal itself (its own copy of the queue was empty or ran differently, the play
    /// reached it as a packet only) would never get it. The signals of the lifepods, the "no signal" lines and the Sunbeam countdown hang on this goal. Passing it on costs
    /// nothing: a client that already completed it ignores it.
    /// </summary>
    public static bool ShouldRelay(string goalKey, StoryGoalExecuted.EventType type, bool addedToCompletedGoals)
    {
        return addedToCompletedGoals || (type == StoryGoalExecuted.EventType.STORY && RadioGoalKeys.IsPlayedGoal(goalKey));
    }
}
