using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     ShockerMeleeAttack (the Ampeel) is the one melee attack that calls LiveMixin.TakeDamage without a dealer, so the relay of creature hits to the hit player
///     (<see cref="LiveMixin_TakeDamage_Patch" />) could not tell which creature shocked them. Remembers the shocking creature for the duration of the touch.
/// </summary>
public sealed partial class ShockerMeleeAttack_OnTouch_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((ShockerMeleeAttack t) => t.OnTouch(default));

    /// <summary>
    ///     The creature whose electric attack is dealing damage right now, null otherwise.
    /// </summary>
    public static GameObject CurrentShocker { get; private set; }

    public static void Prefix(ShockerMeleeAttack __instance, out GameObject __state)
    {
        __state = CurrentShocker;
        CurrentShocker = __instance.gameObject;
    }

    public static void Finalizer(GameObject __state)
    {
        CurrentShocker = __state;
    }
}
