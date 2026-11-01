using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Two fixes in the vanilla reaction of the story to a completed goal, which every client runs for every goal (its own and the ones other players report).<br />
/// - <see cref="StoryGoalCustomEventHandler.gunDisabled"/> is only set by <see cref="StoryGoalCustomEventHandler.DisableGun"/>, which runs on the player who used the
/// Precursor gun terminal. The other players only get the "Goal_Disable_Gun" goal, so for them the gun stayed enabled: the rocket refused to launch, the terminal
/// still opened and the Sunbeam event was not cancelled. Goals executed for other players call NotifyGoalComplete too, so the flag is set from there (Postfix).<br />
/// - "SunbeamCheckPlayerRange" triggers one of two mutually exclusive PDA goals depending on whether the LOCAL player is at the landing site. Every client ran its own branch
/// and both goals were reported, so everybody got both contradictory logs. The branch no longer triggers anything by itself: the outcome is decided once for all players by
/// the server (ST1h, <see cref="SunbeamRangeDecision"/>), see <see cref="RequestSunbeamRangeOutcome"/> (Transpiler).
/// </summary>
public sealed partial class StoryGoalCustomEventHandler_NotifyGoalComplete_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalCustomEventHandler t) => t.NotifyGoalComplete(default));

    private const string GUN_DEACTIVATE_GOAL = "Goal_Disable_Gun";

    /*
     * if (key == "SunbeamCheckPlayerRange")
     * {
     *     this.countdownActive = false;
     *     base.Invoke("StartSunbeamShootdownFX", 7f);
     *     bool flag = this.IsPlayerAtLandingSite();
     *     VFXSunbeam.main.PlaySFX(flag);
     *     (flag ? this.sunbeamDestroyEventInRange : this.sunbeamDestroyEventOutOfRange).Trigger();   <--- [Trigger replaced by RequestSunbeamRangeOutcome(goal)]
     * }
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchStartForward([
                                                new CodeMatch(OpCodes.Ldfld, Reflect.Field((StoryGoalCustomEventHandler t) => t.sunbeamDestroyEventInRange)),
                                                new CodeMatch(OpCodes.Callvirt, Reflect.Method((StoryGoal t) => t.Trigger()))
                                            ])
                                            .ThrowIfInvalid("Could not find the Trigger of the Sunbeam range goal in StoryGoalCustomEventHandler.NotifyGoalComplete")
                                            .Advance(1)
                                            // Set keeps the labels: the branch of the out of range goal jumps here
                                            .Set(OpCodes.Call, Reflect.Method(() => RequestSunbeamRangeOutcome(default)))
                                            .InstructionEnumeration();
    }

    public static void Postfix(StoryGoalCustomEventHandler __instance, string key)
    {
        if (!__instance.gunDisabled && string.Equals(key, GUN_DEACTIVATE_GOAL, StringComparison.OrdinalIgnoreCase))
        {
            __instance.gunDisabled = true;
            CloseGunTerminals();
        }
    }

    /// <summary>
    /// The terminal of the player who used it closes at the end of his cinematic. Everyone else's terminal was opened by the area trigger and, with the gun disabled, neither
    /// leaving nor entering the area closes it any more (both return early): it stayed open with its looping hum.
    /// </summary>
    private static void CloseGunTerminals()
    {
        foreach (PrecursorDisableGunTerminal terminal in UnityEngine.Object.FindObjectsOfType<PrecursorDisableGunTerminal>())
        {
            if (terminal.opened && !terminal.usingPlayer)
            {
                terminal.SetOpen(false);
            }
        }
    }

    /// <summary>
    /// Replaces triggering the goal the local player's position selected. The local player is one voter (the sound effect keeps following it): this client proposes the outcome
    /// to the server, which accepts the first proposal of all clients and answers everybody with it, and the goal is triggered when that answer arrives
    /// (SunbeamRangeDecisionProcessor). Outside of a running session there is nobody to agree with and the vanilla goal is triggered.
    /// </summary>
    /// <param name="localChoice">The goal vanilla picked from the local player's position</param>
    public static void RequestSunbeamRangeOutcome(StoryGoal localChoice)
    {
        StoryGoalCustomEventHandler eventHandler = StoryGoalCustomEventHandler.main;
        if (!eventHandler || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            localChoice.Trigger();
            return;
        }

        // The decision of another client got here first
        if (IsGoalKnown(SunbeamRangeDecision.IN_RANGE_GOAL) || IsGoalKnown(SunbeamRangeDecision.OUT_OF_RANGE_GOAL))
        {
            return;
        }

        List<NitroxVector3> playerPositions = [];
        if (Player.main)
        {
            playerPositions.Add(Player.main.transform.position.ToDto());
        }
        foreach (RemotePlayer remotePlayer in Resolve<PlayerManager>().GetAll())
        {
            if (remotePlayer.Body)
            {
                playerPositions.Add(remotePlayer.Body.transform.position.ToDto());
            }
        }

        bool inRange = SunbeamRange.IsAnyoneAtLandingSite(playerPositions, eventHandler.landingSiteLocation.ToDto(), eventHandler.landingSiteRadius);
        Log.Debug($"Proposing that the Sunbeam range check is {(inRange ? "in range" : "out of range")} ({playerPositions.Count} players counted)");
        Resolve<IPacketSender>().Send(new SunbeamRangeDecision(inRange));
    }

    private static bool IsGoalKnown(string goalKey)
    {
        return StoryGoalManager.main.completedGoals.Contains(goalKey) || StoryGoalScheduler.main.schedule.Any(scheduledGoal => scheduledGoal.goalKey == goalKey);
    }
}
