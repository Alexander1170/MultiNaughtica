using System.Reflection;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Vanilla funnels almost every goal through <see cref="StoryGoal.Execute" />, which <see cref="StoryGoal_Execute_Patch" /> reports. A few callers complete a goal with
/// <see cref="StoryGoalManager.OnGoalComplete" /> directly and were never sent: the intro PDA triggers (IntroLifepodDirector), the global key of the Precursor doors
/// (PrecursorGlobalKeyActivator.ToggleDoor), and the "OnPlay..." goals completed when a radio message is played (StoryGoalManager.ExecutePendingRadioMessage,
/// which the Sunbeam countdown starts from). Those are sent here as plain story goals. A remote goal runs under the suppressor of its packet, so it is not sent back.
/// </summary>
public sealed partial class StoryGoalManager_OnGoalComplete_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalManager t) => t.OnGoalComplete(default));

    /// <param name="__result">True if the goal was not complete before</param>
    public static void Postfix(string key, bool __result)
    {
        if (!__result || StoryGoal_Execute_Patch.ExecuteDepth > 0 || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        Resolve<IPacketSender>().Send(new StoryGoalExecuted(key, Story.GoalType.Story.ToDto()));
    }
}
