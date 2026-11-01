using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The explosion of a Crashfish damages everything around it through <c>DamageSystem.RadiusDamage</c> which passes no dealer. Names the Crashfish for the duration of the
/// explosion (<see cref="CreatureDamageSource" />), see <see cref="ReaperLeviathan_DamageVehicle_Patch" />.
/// </summary>
public sealed partial class Crash_Detonate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Crash t) => t.Detonate());

    public static void Prefix(Crash __instance, out GameObject __state)
    {
        __state = CreatureDamageSource.Enter(__instance.gameObject);
    }

    public static void Finalizer(GameObject __state)
    {
        CreatureDamageSource.Exit(__state);
    }
}
