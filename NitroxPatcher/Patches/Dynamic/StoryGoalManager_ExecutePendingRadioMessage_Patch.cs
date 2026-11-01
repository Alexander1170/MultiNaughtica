using System;
using System.Reflection;
using Nitrox.Model.Subnautica.Helper;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Vanilla <c>ExecutePendingRadioMessage</c> (what a click on the radio does, and what the play of the other player does on this client) takes the first pending message, adds it
/// to the PDA log and completes "OnPlay" plus its key <b>only if the PDA log did not have the message yet</b> (<c>PDALog.Add</c> returns nothing for a known key). The signals of the
/// lifepods, the "no signal" PDA lines and the Sunbeam countdown hang on that goal. With two players the entry can already be in this client's PDA log (the other player's entry is
/// relayed), and then the goal would never complete here. The message that was taken out of the queue has been played whatever the PDA log said: the goal is completed after the call if the
/// game left it open. Nothing changes when the game completed it (the log said so in the real play session: every message of the first evening completed it on both clients).
/// </summary>
public sealed partial class StoryGoalManager_ExecutePendingRadioMessage_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalManager t) => t.ExecutePendingRadioMessage());

    /// <param name="__state">The key of the message that is first in the queue, null if the queue is empty (the game returns at once then)</param>
    public static void Prefix(StoryGoalManager __instance, out string __state)
    {
        __state = __instance.pendingRadioMessages.Count > 0 ? __instance.pendingRadioMessages[0] : null;
    }

    public static void Postfix(StoryGoalManager __instance, string __state)
    {
        if (string.IsNullOrEmpty(__state))
        {
            return;
        }

        try
        {
            string playedGoal = RadioGoalKeys.PlayedGoalOf(__state);
            bool completedByTheGame = __instance.IsGoalComplete(playedGoal);
            if (StoryBeaconPlan.ShouldCompletePlayedGoal(__state, completedByTheGame))
            {
                bool completedNow = __instance.OnGoalComplete(playedGoal);
                Log.Info($"[Beacons] the radio message {__state} was played here and the game left {playedGoal} open (its PDA log entry was there before the play): completed it now: {completedNow}");
            }
            else
            {
                Log.Info($"[Beacons] the radio message {__state} was played here, {playedGoal} is complete");
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[Beacons] radio message {__state}");
        }
    }
}
