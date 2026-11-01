using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Prevents precursor key terminal pedestals from closing when a player leaves if other players are still nearby.
/// </summary>
public sealed partial class PrecursorKeyTerminalTrigger_OnTriggerExit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorKeyTerminalTrigger t) => t.OnTriggerExit(default));

    public static bool Prefix(PrecursorKeyTerminalTrigger __instance, Collider col)
    {
        if (col.gameObject.AliveOrNull() is { IsPlayer: false })
        {
            return false;
        }

        // Only close if no players remain
        if (TriggerOccupancy.Exit(__instance, col) <= 0)
        {
            Close(__instance);
        }

        return false;
    }

    /// <summary>
    /// Also run by <see cref="TriggerOccupancy" /> when the last player inside leaves the game.
    /// </summary>
    internal static void Close(PrecursorKeyTerminalTrigger trigger)
    {
        if (trigger)
        {
            trigger.SendMessageUpwards("CloseDeck");
        }
    }
}
