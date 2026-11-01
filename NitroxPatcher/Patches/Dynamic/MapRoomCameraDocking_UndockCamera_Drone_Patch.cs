using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A scout drone that leaves the dock of a Cyclops is let go of the hull and given the hull's speed (the game puts it at the undock pose and makes it a free body with no speed at all, which a moving Cyclops
/// would leave behind in a jolt).
/// </summary>
public sealed partial class MapRoomCameraDocking_UndockCamera_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCameraDocking t) => t.UndockCamera());

    public static void Prefix(MapRoomCameraDocking __instance, out MapRoomCamera __state)
    {
        __state = __instance.camera;
    }

    public static void Postfix(MapRoomCameraDocking __instance, MapRoomCamera __state)
    {
        DroneDockMarker marker = __instance.GetComponent<DroneDockMarker>();
        if (!__state || !marker || !ScoutDrone.Of(__state, out ScoutDrone drone))
        {
            return;
        }
        __state.transform.SetParent(null, true);
        // on the client that flies it, a free body that physics moves and that cannot pass through a wall at twice a Seamoth's speed (the copies on the other clients are placed by the movement replicator)
        if (drone.LocalOperator)
        {
            drone.ConfigureFlightBody();
        }
        Rigidbody hull = __instance.GetComponentInParent<Rigidbody>();
        if (hull && __state.rigidBody && !__state.rigidBody.isKinematic)
        {
            __state.rigidBody.velocity = hull.velocity;
        }
        // Flown out by its operator, picked up, or let go because the module went out: told to the server (not when the bay carries out what the server said)
        if (marker.Bay)
        {
            marker.Bay.ReportDock(drone, false);
        }
    }
}
