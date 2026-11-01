using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Makes ion cube pedestals (the ones with ion cubes on top that rise up) react to remote players.
/// </summary>
public sealed partial class PrecursorActivatedPillar_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorActivatedPillar t) => t.OnTriggerEnter(default));

    /// <summary>
    /// The vanilla test is "the entity root has a Player below it", which is also true for a vehicle the local player pilots (the Player is parented under it); the body of
    /// a remote player has no Player component and counts through the IsPlayer extension.
    /// </summary>
    public static bool IsPillarOccupant(GameObject entityRoot)
    {
        return entityRoot.IsPlayer || entityRoot.GetComponentInChildren<Player>();
    }

    public static bool Prefix(PrecursorActivatedPillar __instance, Collider col, bool ___active, ref bool ___extended, ref bool ___isFullyExtended, FMODAsset ___openSound, FMOD_CustomLoopingEmitter ___openedLoopingSound)
    {
        if (!___active)
        {
            return false;
        }

        GameObject entityRoot = UWE.Utils.GetEntityRoot(col.gameObject);
        if (!entityRoot)
        {
            entityRoot = col.gameObject;
        }

        if (!IsPillarOccupant(entityRoot))
        {
            return false;
        }

        // Track who is inside for proper exit handling (also when a player leaves the game in the zone)
        TriggerOccupancy.Enter(__instance, col, () => PrecursorActivatedPillar_OnTriggerExit_Patch.Retract(__instance));

        // Only play sounds and animate if not already extended
        if (!___extended)
        {
            if (___openSound)
            {
                Utils.PlayFMODAsset(___openSound, __instance.transform, 20f);
            }
            if (___openedLoopingSound)
            {
                ___openedLoopingSound.Play();
            }
            ___extended = true;
            ___isFullyExtended = false;
        }

        return false;
    }
}
