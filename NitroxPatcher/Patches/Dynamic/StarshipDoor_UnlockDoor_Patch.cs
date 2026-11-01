using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class StarshipDoor_UnlockDoor_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StarshipDoor t) => t.UnlockDoor());

    public static void Prefix(StarshipDoor __instance)
    {
        // Not for a laser-cut door (open method Sealed): its state is the Sealed component's, see StarshipDoor_OnDoorToggle_Patch
        if (__instance.doorLocked && StarshipDoorRules.ReportsOwnState((int)__instance.doorOpenMethod) && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            // Bug 10 of the first play sessions: the line that pairs with "[LockedDoor] ... started" (the repaired panel next to the door, or a metadata change from the other player, unlocked it)
            Log.Info($"[{StarshipDoorRules.LOG_TAG_LOCKED}] {__instance.name} unlocked at {__instance.transform.position.ToString("F1")}: id {id}, open {__instance.doorOpen}");
            StarshipDoorMetadata starshipDoorMetadata = new(!__instance.doorLocked, __instance.doorOpen);
            Resolve<Entities>().BroadcastMetadataUpdate(id, starshipDoorMetadata);
        }
    }
}
