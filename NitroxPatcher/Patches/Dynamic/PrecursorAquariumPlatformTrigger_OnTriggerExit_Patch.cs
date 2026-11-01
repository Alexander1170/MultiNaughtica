using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The other half of <see cref="PrecursorAquariumPlatformTrigger_OnTriggerEnter_Patch" />: a remote player's body leaving the platform ends the trigger like the local player
/// leaving it does (<c>OnTriggerExit</c> of vanilla), so that every client's Emperor and platform go through the same phases.
/// </summary>
public sealed partial class PrecursorAquariumPlatformTrigger_OnTriggerExit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorAquariumPlatformTrigger t) => t.OnTriggerExit(default));

    public static bool Prefix(PrecursorAquariumPlatformTrigger __instance, Collider other)
    {
        if (!__instance.triggered || !other.isTrigger || !other.gameObject.IsRemotePlayer)
        {
            return true;
        }

        if (!__instance.invisibleWalls.activeSelf)
        {
            __instance.EndTrigger();
        }

        return false;
    }
}
