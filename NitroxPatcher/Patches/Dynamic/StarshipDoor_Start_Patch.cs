using System.Linq;
using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// One log line for every laser-cut door (open method Sealed, tag <c>[SealedDoor]</c>) and for every manual door that is locked (tag <c>[LockedDoor]</c>) the moment it starts in a session. The
/// first play sessions (bug 10) reported doors that looked like plain closed doors with no interaction and nothing in the logs said which doors had been in the world or in what state; the
/// two kinds that can be meant are a laser-cut door (it must say "Sealed door") and a locked manual door (it says nothing until the damaged panel next to it is repaired). The line gives the
/// id, the place, the state, the lock flag and whether the door has the colliders its hand text needs (<see cref="StarshipDoorRules"/>). Nothing is changed.
/// </summary>
public sealed partial class StarshipDoor_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StarshipDoor t) => t.Start());

    public static void Postfix(StarshipDoor __instance)
    {
        if (!__instance)
        {
            return;
        }

        int openMethod = (int)__instance.doorOpenMethod;
        string? tag = StarshipDoorRules.StartLogTag(openMethod, __instance.doorLocked);
        if (tag == null)
        {
            return;
        }

        int solidColliders = __instance.GetComponentsInChildren<Collider>(false).Count(collider => collider.enabled && !collider.isTrigger);
        string id = __instance.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "none";
        string position = __instance.transform.position.ToString("F1");
        if (openMethod == StarshipDoorRules.SEALED)
        {
            Sealed sealedComponent = __instance.sealedComponent;
            string state = sealedComponent ? $"sealed {sealedComponent._sealed} opened {sealedComponent.openedAmount}/{sealedComponent.maxOpenedAmount}" : "no Sealed component";
            Log.Info($"[{tag}] {__instance.name} started at {position}: id {id}, {state}, locked {__instance.doorLocked}, solid colliders {solidColliders}, enabled {__instance.enabled}");
            return;
        }

        Log.Info($"[{tag}] {__instance.name} started at {position}: id {id}, open {__instance.doorOpen}, solid colliders {solidColliders}, enabled {__instance.enabled}");
    }
}
