using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Makes precursor key terminal pedestals (tablet pedestals) react to remote players.
/// </summary>
public sealed partial class PrecursorKeyTerminalTrigger_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorKeyTerminalTrigger t) => t.OnTriggerEnter(default));

    public static bool Prefix(PrecursorKeyTerminalTrigger __instance, Collider col)
    {
        if (col.gameObject.AliveOrNull() is { IsPlayer: false })
        {
            return false;
        }

        // Track who is inside for proper exit handling (also when a player leaves the game in the zone)
        int playersInside = TriggerOccupancy.Enter(__instance, col, () => PrecursorKeyTerminalTrigger_OnTriggerExit_Patch.Close(__instance));

        // Only send OpenDeck if this is the first player entering
        if (playersInside == 1)
        {
            __instance.SendMessageUpwards("OpenDeck");
        }

        return false;
    }
}
