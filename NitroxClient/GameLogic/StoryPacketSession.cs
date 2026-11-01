using NitroxClient.MonoBehaviours;

namespace NitroxClient.GameLogic;

/// <summary>
/// Counts the multiplayer sessions that ended, so that a story packet that waits for the end of a join (<see cref="StoryPacketRules" />) knows when the session it arrived in is over:
/// <c>CoroutineHost</c> outlives the session, and a player who leaves while joining and joins again within the wait must not get the old packet applied to the new session.
/// </summary>
public static class StoryPacketSession
{
    public static int Generation { get; private set; }

    static StoryPacketSession()
    {
        // The first read (the first story packet that has to wait) subscribes: only sessions that end after it matter
        Multiplayer.OnAfterMultiplayerEnd += () => Generation++;
    }
}
