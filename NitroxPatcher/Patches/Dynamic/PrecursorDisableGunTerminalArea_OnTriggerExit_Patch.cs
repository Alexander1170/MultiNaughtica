using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Prevents the gun disable terminal from closing when a player leaves if other players are still nearby.
/// </summary>
public sealed partial class PrecursorDisableGunTerminalArea_OnTriggerExit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorDisableGunTerminalArea t) => t.OnTriggerExit(default));

    /// <summary>
    /// Checks if the collider belongs to a player (local or remote) and returns the entity root.
    /// </summary>
    /// <param name="other">The collider to check</param>
    /// <param name="entityRoot">The entity root GameObject if it's a player, null otherwise</param>
    /// <returns>True if the collider belongs to a player, false otherwise</returns>
    public static bool IsPlayerCollider(Collider other, out GameObject entityRoot)
    {
        entityRoot = UWE.Utils.GetEntityRoot(other.gameObject);
        if (entityRoot == null)
        {
            entityRoot = other.gameObject;
        }
        return entityRoot.IsPlayer;
    }

    public static bool Prefix(PrecursorDisableGunTerminalArea __instance, Collider other)
    {
        if (!IsPlayerCollider(other, out GameObject entityRoot))
        {
            return false;
        }

        // Only close if no players remain
        if (TriggerOccupancy.Exit(__instance, other) <= 0)
        {
            Close(__instance);
        }

        return false;
    }

    /// <summary>
    /// Also run by <see cref="TriggerOccupancy" /> when the last player inside leaves the game.
    /// </summary>
    internal static void Close(PrecursorDisableGunTerminalArea trigger)
    {
        if (trigger && trigger.terminal)
        {
            trigger.terminal.OnTerminalAreaExit();
        }
    }
}
