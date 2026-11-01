using System.Reflection;
using NitroxClient.GameLogic;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Every goal that completes on this client (its own scheduler, a click on the radio, or a goal that the other player reported and this client runs) goes through
/// <see cref="StoryGoalManager.OnGoalComplete" />, and that is where the game hands out the beacons of the goal. <see cref="StoryBeacons" /> writes the beacons that the goal carries
/// to the log and, a moment later, makes sure that they stand (vanilla keeps them in the saved game, a multiplayer client has no saved pings).
/// </summary>
public sealed partial class StoryGoalManager_OnGoalComplete_Beacons_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalManager t) => t.OnGoalComplete(default));

    /// <param name="__result">True if the goal was not complete before</param>
    public static void Postfix(string key, bool __result)
    {
        StoryBeacons.OnGoalCompleted(key, __result);
    }
}
