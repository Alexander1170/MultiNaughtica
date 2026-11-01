using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The dock of a Cyclops takes scout drones only and the docks of the scanner rooms take no scout drone (the game's dock takes any camera that enters its trigger). A drone in the dock of a Cyclops is parented to the
/// hull, so that it rides along with the Cyclops (the game only puts it at the dock's pose).
/// </summary>
public sealed partial class MapRoomCameraDocking_DockCamera_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCameraDocking t) => t.DockCamera(default));

    public static bool Prefix(MapRoomCameraDocking __instance, MapRoomCamera camera)
    {
        bool droneDock = __instance.GetComponent<DroneDockMarker>();
        bool drone = ScoutDrone.Of(camera, out _);
        return droneDock == drone;
    }

    public static void Postfix(MapRoomCameraDocking __instance, MapRoomCamera camera)
    {
        DroneDockMarker marker = __instance.GetComponent<DroneDockMarker>();
        if (marker && ScoutDrone.Of(camera, out ScoutDrone drone))
        {
            camera.transform.SetParent(__instance.transform, true);
            // Flown home by its operator, or put there: told to the server (not when the bay carries out what the server said)
            if (marker.Bay)
            {
                marker.Bay.ReportDock(drone, true);
            }
        }
    }
}
