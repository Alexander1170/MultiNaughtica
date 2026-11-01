using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Ensures vehicle charging from moonpool only occurs on the simulating player, and pauses it while a Cyclops with the power share is in an emergency or draws emergency power from the vehicle (the Cyclops power share).
/// </summary>
public sealed partial class Vehicle_UpdateEnergyRecharge_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.UpdateEnergyRecharge());

    public static bool Prefix(Vehicle __instance)
    {
        PatchProbe.Fired("Vehicle.UpdateEnergyRecharge");
        if (!__instance.TryGetNitroxId(out NitroxId vehicleId) || !Resolve<SimulationOwnership>().HasAnyLockType(vehicleId))
        {
            return false;
        }
        if (PowerShare.IsDockChargePaused(__instance))
        {
            // The game stops its charging loop itself when a tick does not charge; this tick does not run, so the loop is stopped here (0 = allow fade out)
            __instance.chargingSound.Stop((FMOD.Studio.STOP_MODE)0);
            return false;
        }
        return true;
    }
}
