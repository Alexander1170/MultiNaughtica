using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>A Seamoth that docks (in a moon pool or a Cyclops) ends its afterburner burn, a vehicle that docks ends the tow it is part of, and a Seamoth that docks sheds its rider. The Seamoth's own override calls this method first.</summary>
public sealed partial class Vehicle_OnDockedChanged_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.OnDockedChanged(default, default));

    public static void Postfix(Vehicle __instance, bool docked)
    {
        if (docked)
        {
            RiderBars.OnVehicleGone(__instance, RiderReleaseReason.Docked);
            VehicleAfterburners.End(__instance, AfterburnerState.EndReason.Docked);
            VehicleTows.OnDocking(__instance);
        }
    }
}
