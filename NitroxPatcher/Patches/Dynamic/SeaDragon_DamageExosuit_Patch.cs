using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Sea Dragon's hold on an exosuit hurts it at an interval without a dealer. Names the Sea Dragon for the duration of the damage (<see cref="CreatureDamageSource" />), see
/// <see cref="ReaperLeviathan_DamageVehicle_Patch" />.
/// </summary>
public sealed partial class SeaDragon_DamageExosuit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaDragon t) => t.DamageExosuit());

    public static void Prefix(SeaDragon __instance, out GameObject __state)
    {
        __state = CreatureDamageSource.Enter(__instance.gameObject);
    }

    public static void Finalizer(GameObject __state)
    {
        CreatureDamageSource.Exit(__state);
    }
}
