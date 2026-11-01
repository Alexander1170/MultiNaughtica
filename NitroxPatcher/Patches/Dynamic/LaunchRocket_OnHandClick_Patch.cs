using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LaunchRocket_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((LaunchRocket t) => t.OnHandClick(default));

    /*
     * if (!LaunchRocket_OnHandClick_Patch.IsEveryoneAboard(this)) return;   <--- [INSERTED LINES, after the vanilla IsRocketReady / launchStarted / gunDisabled checks]
     * LaunchRocket.SetLaunchStarted();
     * LaunchRocket_OnHandClick_Patch.BroadcastRocketLaunch(this);   <--- [INSERTED LINE]
     * [Removed all lines after]
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        CodeMatcher matcher = new(instructions);

        // The checks above jump straight to SetLaunchStarted, so the labels pointing at it move to the first inserted instruction
        matcher.MatchStartForward([new CodeMatch(OpCodes.Call, Reflect.Method(() => LaunchRocket.SetLaunchStarted()))]);
        Label launchAllowed = generator.DefineLabel();
        List<Label> labelsToSetLaunchStarted = matcher.Instruction.ExtractLabels();
        matcher.Instruction.labels.Add(launchAllowed);
        matcher.Insert([
            new CodeInstruction(OpCodes.Ldarg_0).WithLabels(labelsToSetLaunchStarted),
            new CodeInstruction(OpCodes.Call, Reflect.Method(() => IsEveryoneAboard(default))),
            new CodeInstruction(OpCodes.Brtrue, launchAllowed),
            new CodeInstruction(OpCodes.Ret)
        ]);

        return matcher.MatchEndForward([
                                                new CodeMatch(OpCodes.Call, Reflect.Method(() => LaunchRocket.SetLaunchStarted()))
                                            ])
                                            .Advance(1) // Skip the SetLaunchStarted because we don't want to manage all the jump labels that forward to it
                                            // Also this instruction doesn't block Rockets.RocketLaunch(GameObject) since that function doesn't check for LaunchRocket.launchStarted
                                            .RemoveInstructions(10)
                                            .InsertAndAdvance([
                                                new CodeInstruction(OpCodes.Ldarg_0),
                                                new CodeInstruction(OpCodes.Call, Reflect.Method(() => BroadcastRocketLaunch(default)))
                                            ]).InstructionEnumeration();
    }

    /// <summary>
    /// The rocket launches only when every connected player is inside it, otherwise the clicking player is told who is missing (ST1i).
    /// Alone on the server it launches like in singleplayer.
    /// </summary>
    private static bool IsEveryoneAboard(LaunchRocket launchRocket)
    {
        Rocket rocket = launchRocket.RequireComponentInParent<Rocket>();

        // The launching player first: the launch is latched right after this returns, and a launch that his own client then refuses would leave him with a latched launch and no ending
        if (!Rockets.IsLocalPlayerAboard(rocket.gameObject))
        {
            Log.InGame(Language.main.Get("Nitrox_RocketMoveInside"));
            return false;
        }

        List<string> missing = Resolve<Rockets>().GetPlayersNotAboard(rocket.gameObject);
        if (missing.Count == 0)
        {
            return true;
        }

        Log.InGame(Language.main.GetFormat("Nitrox_RocketWaitingForPlayers", string.Join(", ", missing)));
        return false;
    }

    private static void BroadcastRocketLaunch(LaunchRocket launchRocket)
    {
        Rocket rocket = launchRocket.RequireComponentInParent<Rocket>();
        Rockets rockets = Resolve<Rockets>();
        rockets.RequestRocketLaunch(rocket);
        rockets.RocketLaunch(rocket.gameObject);
    }
}
