using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Makes the gun disable terminal open when remote players approach.
/// </summary>
public sealed partial class PrecursorDisableGunTerminalArea_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorDisableGunTerminalArea t) => t.OnTriggerEnter(default));

    public static bool Prefix(PrecursorDisableGunTerminalArea __instance, Collider other)
    {
        if (!PrecursorDisableGunTerminalArea_OnTriggerExit_Patch.IsPlayerCollider(other, out GameObject entityRoot))
        {
            return false;
        }

        // Track who is inside for proper exit handling (also when a player leaves the game in the zone)
        int playersInside = TriggerOccupancy.Enter(__instance, other, () => PrecursorDisableGunTerminalArea_OnTriggerExit_Patch.Close(__instance));

        // Only open if this is the first player entering
        if (playersInside == 1)
        {
            __instance.terminal.OnTerminalAreaEnter();
        }

        return false;
    }
}
