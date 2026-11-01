using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The grip of a Reaper hurts the held vehicle every second without a dealer. Names the Reaper for the duration of the damage (<see cref="CreatureDamageSource" />) so that the
/// hit on a vehicle another player simulates is relayed (<see cref="LiveMixin_TakeDamage_Patch" />) instead of being lost, and so that the owner of the vehicle does not apply it
/// twice when its own copy of the Reaper holds the vehicle as well.
/// </summary>
public sealed partial class ReaperLeviathan_DamageVehicle_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((ReaperLeviathan t) => t.DamageVehicle());

    public static void Prefix(ReaperLeviathan __instance, out GameObject __state)
    {
        __state = CreatureDamageSource.Enter(__instance.gameObject);
    }

    public static void Finalizer(GameObject __state)
    {
        CreatureDamageSource.Exit(__state);
    }
}
