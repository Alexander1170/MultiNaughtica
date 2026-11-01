using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Vehicle_OnPilotModeBegin_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.OnPilotModeBegin());

    public static void Prefix(Vehicle __instance)
    {
        Resolve<Vehicles>().BroadcastOnPilotModeChanged(__instance.gameObject, true);
        // Somebody on this client got into a vehicle that it tows (the tower's own player included, who keeps the lock): the tow is over
        VehicleTows.OnPilotEntered(__instance);
    }
}
