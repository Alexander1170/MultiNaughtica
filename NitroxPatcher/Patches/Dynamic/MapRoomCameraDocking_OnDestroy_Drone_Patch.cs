using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The dock of a scanner room destroys the camera in it when the room is deconstructed; the dock of a Cyclops lets the scout drone go instead (it is an item that cost something: the bay going out of the Cyclops, or
/// the Cyclops itself being destroyed, must not delete it).
/// </summary>
public sealed partial class MapRoomCameraDocking_OnDestroy_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCameraDocking t) => t.OnDestroy());

    public static bool Prefix(MapRoomCameraDocking __instance)
    {
        if (!__instance.GetComponent<DroneDockMarker>())
        {
            return true;
        }
        MapRoomCamera camera = __instance.camera;
        if (camera)
        {
            camera.transform.SetParent(null, true);
            if (camera.rigidBody)
            {
                camera.rigidBody.isKinematic = false;
            }
            __instance.camera = null;
        }
        return false;
    }
}
