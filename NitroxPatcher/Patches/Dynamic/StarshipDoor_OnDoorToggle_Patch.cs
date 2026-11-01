using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class StarshipDoor_OnDoorToggle_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StarshipDoor t) => t.OnDoorToggle());

    public static void Postfix(StarshipDoor __instance)
    {
        // A laser-cut door (open method Sealed) has no state of its own: the toggle is the sound of the cut being done or a click on the door. Its state is the Sealed component's
        // (SealedDoorMetadata), and the server keeps one metadata per entity, so this would replace the cut progress / the open door of a joiner with a door that is sealed again
        if (StarshipDoorRules.ReportsOwnState((int)__instance.doorOpenMethod) && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            StarshipDoorMetadata starshipDoorMetadata = new(__instance.doorLocked, __instance.doorOpen);
            Resolve<Entities>().BroadcastMetadataUpdate(id, starshipDoorMetadata);
        }
    }
}
