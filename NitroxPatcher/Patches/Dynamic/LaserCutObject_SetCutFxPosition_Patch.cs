using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The cutting beam of a laser-cut door and the look target of the cutter's arms come in from the world origin (upstream issue 2463). <c>LaserCutObject.OnEnable</c> stores the position of the
/// cutting node in <c>matchingNodePos</c> and every later <c>SetCutFxPosition</c> only lerps from there (2 per second); the entity spawner activates the clone before it puts it in place, so
/// the stored position is a node of a door that stood at the world origin. A stale position is reset to zero, which <c>SetCutFxPosition</c> replaces at once. See <see cref="LaserCutFxRules"/>.
/// </summary>
public sealed partial class LaserCutObject_SetCutFxPosition_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((LaserCutObject t) => t.SetCutFxPosition());

    public static void Prefix(LaserCutObject __instance)
    {
        Vector3 stored = __instance.matchingNodePos;
        if (LaserCutFxRules.IsStaleNodePosition(stored == Vector3.zero, (stored - __instance.transform.position).sqrMagnitude))
        {
            __instance.matchingNodePos = Vector3.zero;
        }
    }
}
