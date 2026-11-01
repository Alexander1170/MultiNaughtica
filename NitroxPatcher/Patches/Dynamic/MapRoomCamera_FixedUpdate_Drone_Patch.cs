using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The physics step of a scout drone is the mod's (<see cref="ScoutDrone.PhysicsStep" />): the thrust of the server's speed factor instead of the game's literal 20, and the leash past the range. The
/// game's own step is skipped for it; a scanner room's camera keeps it.
/// </summary>
public sealed partial class MapRoomCamera_FixedUpdate_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCamera t) => t.FixedUpdate());

    public static bool Prefix(MapRoomCamera __instance)
    {
        if (!ScoutDrone.Of(__instance, out ScoutDrone drone))
        {
            return true;
        }
        drone.PhysicsStep();
        return false;
    }
}
