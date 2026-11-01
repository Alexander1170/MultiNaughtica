using System.Reflection;
using HarmonyLib;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Prevents ion cube pedestals from retracting when a player leaves if other players are still nearby.
/// </summary>
public sealed partial class PrecursorActivatedPillar_OnTriggerExit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorActivatedPillar t) => t.OnTriggerExit(default));

    public static bool Prefix(PrecursorActivatedPillar __instance, Collider col)
    {
        GameObject entityRoot = UWE.Utils.GetEntityRoot(col.gameObject);
        if (!entityRoot)
        {
            entityRoot = col.gameObject;
        }
        // A collider that Enter admitted leaves although it no longer carries a player (a vehicle whose pilot got out inside): otherwise it stays registered for good and the
        // pillar never retracts
        if (!TriggerOccupancy.Contains(__instance, col) && !PrecursorActivatedPillar_OnTriggerEnter_Patch.IsPillarOccupant(entityRoot))
        {
            return false;
        }

        // Only retract if no players remain
        if (TriggerOccupancy.Exit(__instance, col) <= 0)
        {
            Retract(__instance);
        }

        return false;
    }

    /// <summary>
    /// Also run by <see cref="TriggerOccupancy" /> when the last player inside leaves the game.
    /// </summary>
    internal static void Retract(PrecursorActivatedPillar pillar)
    {
        if (!pillar)
        {
            return;
        }

        // The same as the vanilla OnTriggerExit does: close sound, stop the loop, extended = false, isFullyExtended = true
        Traverse.Create(pillar).Method("Retract").GetValue();
    }
}
