using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Broadcast the escape pod and sub root changes of a player respawning.
/// </summary>
public sealed partial class Player_MovePlayerToRespawnPoint_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Player t) => t.MovePlayerToRespawnPoint());

    public static void Postfix(Player __instance)
    {
        // Respawning in a lifepod ends with SetCurrentSub(null) and currentEscapePod = pod. The pod was reported by EscapePod_RespawnPlayer_Patch, and the server keeps ONE parent
        // per player: reporting "no sub root" now would overwrite the pod with open water
        if (!SubRootBroadcastRules.ShouldBroadcastSubRootAfterRespawn(__instance.currentSub, __instance.currentEscapePod))
        {
            return;
        }

        Optional<NitroxId> currentSubId = Optional.Empty;
        if (__instance.currentSub)
        {
            currentSubId = __instance.currentSub.GetId();
        }

        Resolve<LocalPlayer>().BroadcastSubrootChange(currentSubId);

        // BroadcastEscapePodChange() is handled by EscapePod_RespawnPlayer_Patch for cross-functionality with the warpme command
    }
}
