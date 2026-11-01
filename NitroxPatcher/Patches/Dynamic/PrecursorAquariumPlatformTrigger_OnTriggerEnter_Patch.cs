using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The trigger of the moving platform in the Prison Aquarium sets the animator of the Sea Emperor and the platform once a PLAYER steps on it. Vanilla ignores trigger colliders
/// (<c>other.isTrigger</c>) before it looks for the player, and the only collider of a remote player's body is a trigger, so the other player stepping on the platform did nothing on
/// this client and two players in the aquarium saw the Emperor and the platform in different phases. A remote body is handled here exactly like vanilla handles the local player
/// (the same lines of <c>OnTriggerEnter</c>); everything else runs the vanilla method.
/// </summary>
public sealed partial class PrecursorAquariumPlatformTrigger_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorAquariumPlatformTrigger t) => t.OnTriggerEnter(default));

    public static bool Prefix(PrecursorAquariumPlatformTrigger __instance, Collider other)
    {
        if (__instance.triggered || !other.isTrigger || !other.gameObject.IsRemotePlayer)
        {
            return true;
        }

        __instance.triggered = true;
        foreach (Animator animator in __instance.targetAnimators)
        {
            SafeAnimator.SetBool(animator, __instance.animationParameter, true);
        }

        __instance.Invoke("UpdateWalls", __instance.invisibleWallsDuration);
        __instance.Invoke("EndTrigger", __instance.maxStayDuration);
        return false;
    }
}
