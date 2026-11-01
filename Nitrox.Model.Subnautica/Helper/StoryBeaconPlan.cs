using System;
using System.Collections.Generic;

namespace Nitrox.Model.Subnautica.Helper;

/// <summary>
/// One beacon (a signal on the HUD and in the PDA list) that a story goal gives: the goal of an <c>OnGoalUnlock</c> of the game's unlock data and one of its
/// <c>UnlockSignalData</c> (description key and position). Plain data, no game types.
/// </summary>
public sealed class BeaconSpec
{
    public BeaconSpec(string goal, string descriptionKey, float x, float y, float z)
    {
        Goal = goal;
        DescriptionKey = descriptionKey;
        X = x;
        Y = y;
        Z = z;
    }

    public string Goal { get; }
    public string DescriptionKey { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    /// <returns>True if a ping with this description key at this position is the beacon of this spec</returns>
    public bool IsSameSpot(string descriptionKey, float x, float y, float z)
    {
        if (!string.Equals(DescriptionKey, descriptionKey, StringComparison.Ordinal))
        {
            return false;
        }

        float dx = X - x;
        float dy = Y - y;
        float dz = Z - z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) <= StoryBeaconPlan.SAME_SPOT_DISTANCE;
    }

    public override string ToString()
    {
        return $"{DescriptionKey} at {X:F0},{Y:F0},{Z:F0} (goal {Goal})";
    }
}

/// <summary>
/// Which beacons a client has to have. Vanilla creates the beacons of a goal in the one moment the goal completes (<c>OnGoalUnlockTracker.NotifyGoalComplete</c> takes the
/// unlock out of its table and instantiates the signals) and keeps them in the saved game afterwards; the table is never read again. A multiplayer client has no saved
/// pings, so a beacon that was missed in that moment (the unlock was not in the table on this client, the ping object was destroyed or never registered) is missed for
/// good, while the other player, whose moment went well, has it. The invariant that holds in vanilla is: <b>a goal that is complete has its beacons</b>. The plan states
/// it for every client from the data that is the same on all of them (the unlock table of the game and the completed goals, which the server keeps and relays).
/// </summary>
public static class StoryBeaconPlan
{
    /// <summary>
    /// Two pings with the same description key closer than this are the same beacon.
    /// </summary>
    public const float SAME_SPOT_DISTANCE = 2f;

    /// <param name="table">Every signal of the game's unlock data, with the goal that unlocks it</param>
    /// <param name="isGoalComplete">Whether a goal is complete on this client (the game's own set, case-insensitive)</param>
    /// <param name="isPresent">Whether this client has a ping for the beacon (created by the game, by an earlier plan or by the join)</param>
    /// <returns>The beacons that have to be created now: the goal is complete and there is no ping, each beacon once</returns>
    public static List<BeaconSpec> Missing(IEnumerable<BeaconSpec> table, Func<string, bool> isGoalComplete, Func<BeaconSpec, bool> isPresent)
    {
        List<BeaconSpec> missing = [];
        foreach (BeaconSpec spec in table)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Goal) || string.IsNullOrEmpty(spec.DescriptionKey))
            {
                continue;
            }

            if (!isGoalComplete(spec.Goal) || isPresent(spec))
            {
                continue;
            }

            // Two entries of the table can hand out the same beacon: it is created once
            if (missing.Exists(other => other.IsSameSpot(spec.DescriptionKey, spec.X, spec.Y, spec.Z)))
            {
                continue;
            }

            missing.Add(spec);
        }

        return missing;
    }

    /// <summary>
    /// A ping that is initialized registers itself with the ping manager (<c>PingInstance.Initialize</c> and <c>OnEnable</c>), which is what puts it on the HUD and in the PDA list.
    /// One that is not in the manager although it is initialized (it was unregistered by <c>OnDisable</c> and its object was never enabled again, or the registration was refused)
    /// is invisible for the player and has to be registered again. A ping that is not initialized yet will register itself when its <c>Start</c> runs.
    /// </summary>
    public static bool NeedsRegistering(bool initialized, bool registeredInManager)
    {
        return initialized && !registeredInManager;
    }

    /// <summary>
    /// <c>ExecutePendingRadioMessage</c> completes the played-goal only when <c>PDALog.Add</c> returned an entry, and it returns nothing when the PDA log already has the message.
    /// With two players the entry can be there before the player plays the message (the other player's entry is relayed to this client), and then the played-goal, and with it
    /// everything that hangs on it, would never complete on this client. The message that was taken out of the queue has been played whatever the PDA log said.
    /// </summary>
    /// <param name="poppedKey">The key of the message that was first in the queue before the call, null if the queue was empty</param>
    /// <param name="playedGoalComplete">Whether the played-goal of that message is complete after the call</param>
    public static bool ShouldCompletePlayedGoal(string poppedKey, bool playedGoalComplete)
    {
        return !string.IsNullOrEmpty(poppedKey) && !playedGoalComplete;
    }
}
