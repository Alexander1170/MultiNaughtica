using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.GameLogic.PlayerLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Vehicle_OnKill_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.OnKill());

    public static void Prefix(Vehicle __instance)
    {
        // A player who holds on to the Seamoth's bar is its child: it lets go before the Seamoth is destroyed, or the player object would go with it (first, whatever below fails)
        RiderBars.OnVehicleGone(__instance, RiderReleaseReason.Destroyed);
        VehicleTows.OnVehicleKilled(__instance);
        if (__instance.TryGetIdOrWarn(out NitroxId id) && Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            Resolve<SimulationOwnership>().StopSimulatingEntity(id);
            Resolve<Vehicles>().BroadcastDestroyedVehicle(id);
        }
        foreach (RemotePlayerIdentifier identifier in __instance.GetComponentsInChildren<RemotePlayerIdentifier>(true))
        {
            identifier.RemotePlayer.ResetStates();
        }
    }
}
