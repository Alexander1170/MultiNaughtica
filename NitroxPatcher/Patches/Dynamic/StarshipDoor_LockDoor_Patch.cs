using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class StarshipDoor_LockDoor_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StarshipDoor t) => t.LockDoor());

    public static void Prefix(StarshipDoor __instance)
    {
        // Not for a laser-cut door (open method Sealed): its state is the Sealed component's, see StarshipDoor_OnDoorToggle_Patch
        if (!__instance.doorLocked && StarshipDoorRules.ReportsOwnState((int)__instance.doorOpenMethod) && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            // This runs before LockDoor sets the flag: the door becomes locked (the old code sent the old state, "unlocked", and unlocked the door of the other players)
            StarshipDoorMetadata starshipDoorMetadata = new(!__instance.doorLocked, __instance.doorOpen);
            Resolve<Entities>().BroadcastMetadataUpdate(id, starshipDoorMetadata);
        }
    }
}
