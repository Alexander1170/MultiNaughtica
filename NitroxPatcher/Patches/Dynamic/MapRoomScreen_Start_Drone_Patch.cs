using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The screen that the drone bay flies its drone from (<see cref="DroneScreenMarker" />) is a <c>MapRoomScreen</c> only because the game's drone control wants one: it has no scanner room, no text for the list of
/// cameras and no preview, and the game's Start would look for a base and subscribe it to the list of cameras. It is left unstarted.
/// </summary>
public sealed partial class MapRoomScreen_Start_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomScreen t) => t.Start());

    public static bool Prefix(MapRoomScreen __instance)
    {
        return !__instance.GetComponent<DroneScreenMarker>();
    }
}
