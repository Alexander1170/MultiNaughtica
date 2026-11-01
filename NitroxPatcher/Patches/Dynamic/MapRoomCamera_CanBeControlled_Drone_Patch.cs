using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A scout drone can be controlled on the client of its operator only (the game's <c>CanBeControlled</c> says yes to any screen that asks, and its HUD uses the answer for "no signal"). On every other client the
/// copy of the drone is inert, and no scanner room screen can cycle to it and fly it.
/// </summary>
public sealed partial class MapRoomCamera_CanBeControlled_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCamera t) => t.CanBeControlled(default));

    public static void Postfix(MapRoomCamera __instance, ref bool __result)
    {
        if (__result && ScoutDrone.Of(__instance, out ScoutDrone drone))
        {
            __result = drone.LocalOperator;
        }
    }
}
