using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The next-camera and previous-camera keys of the game's drone control call the screen's <c>CycleCamera</c>; the drone bay's screen has exactly one camera and must not switch to a scanner room's.
/// </summary>
public sealed partial class MapRoomScreen_CycleCamera_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomScreen t) => t.CycleCamera(default));

    public static bool Prefix(MapRoomScreen __instance)
    {
        return !__instance.GetComponent<DroneScreenMarker>();
    }
}
