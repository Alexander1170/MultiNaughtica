using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using static NitroxClient.GameLogic.SubRootBroadcastRules;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Reports the sub root the player is in after vanilla changed it. SetCurrentSub can refuse a request to enter a sub (CanChangeSub is false for the first three fixed updates
/// after a switch), so what is reported is what the call left in <c>Player.currentSub</c>, not what it was asked for.
/// </summary>
public sealed partial class Player_SetCurrentSub_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Player t) => t.SetCurrentSub(default, default));

    public static void Prefix(Player __instance, out SubRoot __state)
    {
        __state = __instance.currentSub;
    }

    public static void Postfix(Player __instance, SubRoot sub, SubRoot __state)
    {
        // We really want to avoid unnecessary packets giving false information
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        SubRoot currentSub = __instance.currentSub;
        Optional<NitroxId> currentSubId = currentSub.GetId();
        LocalPlayer localPlayer = Resolve<LocalPlayer>();

        // When in the water of the moonpool, it can happen that you hammer change requests
        // while the sub is not changed. This will prevent that
        SetCurrentSubOutcome outcome = ClassifySetCurrentSub(currentSub != __state, currentSub == sub);
        if (outcome == SetCurrentSubOutcome.Changed)
        {
            localPlayer.BroadcastSubrootChange(currentSubId);
            return;
        }

        if (outcome == SetCurrentSubOutcome.Rejected)
        {
            Log.Debug($"[{nameof(Player_SetCurrentSub_Patch)}] Vanilla refused to put the player into {(sub ? sub.name : "no sub")}, it stays in {(currentSub ? currentSub.name : "no sub")}");
        }

        if (IsOutOfSync(localPlayer.LastBroadcastSubRootId, currentSubId, __instance.currentEscapePod))
        {
            Log.WarnOnce($"[{nameof(Player_SetCurrentSub_Patch)}] The player is in {currentSubId} but the server was last told {localPlayer.LastBroadcastSubRootId}");
        }
    }
}
