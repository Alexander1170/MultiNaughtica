using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The HUD's resource tracker lists what the scanner room of the screen of the drone has scanned; the drone bay's screen has no scanner room (the game would fail on the empty one), so while its drone is flown the
/// tracker lists nothing.
/// </summary>
public sealed partial class uGUI_ResourceTracker_GatherScanned_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_ResourceTracker t) => t.GatherScanned());

    public static bool Prefix(uGUI_ResourceTracker __instance)
    {
        MapRoomScreen screen = uGUI_CameraDrone.main ? uGUI_CameraDrone.main.GetScreen() : null;
        if (!screen || !screen.GetComponent<DroneScreenMarker>())
        {
            return true;
        }
        __instance.nodes.Clear();
        __instance.mapRooms.Clear();
        return false;
    }
}
