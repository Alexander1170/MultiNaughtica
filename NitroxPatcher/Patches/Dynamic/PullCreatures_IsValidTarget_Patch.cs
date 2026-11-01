using System.Reflection;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// K18, Mesmer: the hypnosis of a Mesmer (<see cref="PullCreatures" />) is made of effects on the screen, the controls and the position of <c>Player.main</c>, so it can only
/// work on the local player; for any other target the code that runs on the client that simulates the Mesmer is a stray half of it (the swirl on that client's screen and the
/// loop sound start if the target looks like a player, nothing is pulled, and the real victim, a remote player, feels nothing). A remote player's body is therefore never a
/// valid hypnosis target. The Mesmer's bite on a remote player is relayed to them like any other creature's (<c>MeleeAttack_CanDealDamageTo_Patch</c>).
/// </summary>
public sealed partial class PullCreatures_IsValidTarget_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PullCreatures t) => t.IsValidTarget(default(GameObject)));

    public static bool Prefix(GameObject target, ref bool __result)
    {
        if (target && target.GetComponentInParent<RemotePlayerIdentifier>())
        {
            __result = false;
            return false;
        }
        return true;
    }
}
