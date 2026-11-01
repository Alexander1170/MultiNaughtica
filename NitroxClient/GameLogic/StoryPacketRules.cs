namespace NitroxClient.GameLogic;

/// <summary>
/// When a story packet (a goal executed, a goal scheduled) may be applied on this client. Contains no Unity types so that it can be tested without the game.
/// </summary>
/// <remarks>
/// The server sends such a packet to every connected player, a player who is still joining included. While the world scene of that player is loading, <c>StoryGoalScheduler.main</c>,
/// <c>StoryGoalManager.main</c> and <c>DayNightCycle.main</c> do not exist yet and the processor threw a NullReferenceException: the goal was lost for that player if his join snapshot had been
/// made before the goal ran (seen in a live two-player test after a world restart: "Error trying to process packet [StoryGoalExecuted Key:
/// Precursor_Prison_Aquarium_EnvironmentLog3 ...] NullReferenceException" on the client that was loading). The packet is now applied when the initial sync is done and the story systems exist.
/// What the snapshot already holds is applied a second time then, as for every packet that arrives just after the sync (a completed goal is not executed again, a scheduled one is known).
/// </remarks>
public static class StoryPacketRules
{
    /// <summary>How long a story packet waits for the join to finish before it is given up (the join itself times out long before).</summary>
    public const float WAIT_SECONDS = 300f;

    public static bool ShouldWait(bool initialSyncCompleted, bool storySystemsLoaded)
    {
        return !initialSyncCompleted || !storySystemsLoaded;
    }

    public static bool GiveUp(float waitedSeconds)
    {
        return waitedSeconds >= WAIT_SECONDS;
    }

    /// <summary>
    /// The multiplayer session that the packet arrived in is over (<see cref="StoryPacketSession.Generation" /> moved on): a player who leaves while joining and joins again within the wait must not get the
    /// old packet applied to the new session (the coroutine host outlives the session).
    /// </summary>
    public static bool SessionEnded(int generationAtArrival, int generationNow)
    {
        return generationAtArrival != generationNow;
    }
}
