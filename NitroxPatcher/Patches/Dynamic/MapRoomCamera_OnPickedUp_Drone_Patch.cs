using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A Scout Drone that a player picks up while it is out (adrift where its operator left it, or where its battery ran out) leaves its bay for good: the server is told (the pickup of a drone from the dock is told by
/// the dock's own patch, which sees the undock). Nothing is told when the drone is picked up by the bay itself (the module went out) or while somebody flies it.
/// </summary>
public sealed partial class MapRoomCamera_OnPickedUp_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCamera t) => t.OnPickedUp(default));

    public static void Prefix(MapRoomCamera __instance, out bool __state)
    {
        // the game's own OnPickedUp undocks the camera; whether it was in a dock is read before
        __state = __instance.dockingPoint;
    }

    public static void Postfix(MapRoomCamera __instance, bool __state)
    {
        if (!__state && ScoutDrone.Of(__instance, out ScoutDrone drone) && drone.Bay)
        {
            drone.Bay.ReportPickedUp(drone);
        }
    }
}
