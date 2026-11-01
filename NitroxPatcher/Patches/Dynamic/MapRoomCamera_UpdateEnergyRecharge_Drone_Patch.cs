using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The dock's recharge of a scout drone is the mod's (<see cref="ScoutDrone.RechargeStep" />): the game's takes a tenth of the battery from the Cyclops' power relay every frame, on every client, which would
/// drain each client's copy of the relay. A scanner room's camera keeps the game's.
/// </summary>
public sealed partial class MapRoomCamera_UpdateEnergyRecharge_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCamera t) => t.UpdateEnergyRecharge());

    public static bool Prefix(MapRoomCamera __instance)
    {
        if (!ScoutDrone.Of(__instance, out ScoutDrone drone))
        {
            return true;
        }
        drone.RechargeStep();
        return false;
    }
}
