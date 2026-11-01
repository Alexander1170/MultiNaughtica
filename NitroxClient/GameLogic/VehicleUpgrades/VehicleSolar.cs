using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The solar chargers in the game (docs/coop/VEHICLE_UPGRADES.md). The Seamoth module is the game's own (SeamothSolarCharge): its code adds energy once a second for every client that has the
/// Seamoth loaded, and the co-op build replaces the amount with the server's setting. The Cyclops module is new: it adds energy every frame, like the Cyclops thermal reactor module.
/// <para>
/// Both charge on <b>one client only</b>, the one that holds the simulation lock of the vehicle. The charge lives in the power cell's battery, which every client may change and which is then
/// sent to the others as its new value: two clients that both charge would overwrite each other's values and the charge would hang at the first whole number. The game's energy methods are
/// already stopped on every other client (<c>Vehicle_AddEnergy_Patch</c>), and this class asks for the lock itself so that the other clients do not even calculate.
/// </para>
/// </summary>
public static class VehicleSolar
{
    private static TechType CyclopsSolarTech => (TechType)CoopItems.CYCLOPS_SOLAR;

    /// <summary>
    /// The game's once-a-second charge of the Seamoth's solar charger modules. Returns whether the game's own method should run: it does when the vehicle upgrades are switched off, so that
    /// the module behaves exactly like in the game.
    /// </summary>
    public static bool OnSeamothSolarTick(SeaMoth seaMoth)
    {
        if (!VehicleUpgradeSettings.Enabled)
        {
            return true;
        }
        if (!seaMoth || seaMoth.modules == null || !HoldsLock(seaMoth))
        {
            return false;
        }

        int modules = seaMoth.modules.GetCount(TechType.SeamothSolarCharge);
        if (modules <= 0)
        {
            return false;
        }

        // The modules add up, as they do in the game; one tick of the game's InvokeRepeating is one second
        float perModule = SolarChargeRules.EnergyFor(VehicleUpgradeSettings.SolarChargeSeamoth, Ocean.GetDepthOf(seaMoth.gameObject), DayNightCycle.main.GetLocalLightScalar(), 1f);
        float energy = modules * perModule;
        if (energy > 0f)
        {
            seaMoth.AddEnergy(energy);
        }
        return false;
    }

    /// <summary>Called in every frame by the component on each Cyclops (<c>CyclopsSolarCharger</c>); charges only on the client that holds the Cyclops' lock.</summary>
    public static void OnCyclopsTick(SubRoot subRoot)
    {
        if (!VehicleUpgradeSettings.Enabled || !subRoot || !subRoot.upgradeConsole || !subRoot.powerRelay)
        {
            return;
        }
        Equipment modules = subRoot.upgradeConsole.modules;
        if (modules == null || SolarChargeRules.CountingModules(modules.GetCount(CyclopsSolarTech)) == 0 || !HoldsLock(subRoot) || !GameModeUtils.RequiresPower())
        {
            return;
        }

        float energy = SolarChargeRules.EnergyFor(VehicleUpgradeSettings.SolarChargeCyclops, Ocean.GetDepthOf(subRoot.gameObject), DayNightCycle.main.GetLocalLightScalar(), DayNightCycle.main.deltaTime);
        if (energy > 0f)
        {
            PowerSystem.AddEnergy(subRoot.powerRelay, energy, out _);
        }
    }

    /// <summary>
    /// Whether the Cyclops' Solar Charger is adding power now, for the "+" next to the bolt of the helm HUD. The same rule as <see cref="OnCyclopsTick" /> without the part that only the simulating client
    /// can do (holding the lock): every client works it out from what every client knows (the module, the depth, the time of day and the charge of the cells), so everybody aboard sees the same.
    /// </summary>
    public static bool IsCyclopsCharging(SubRoot subRoot)
    {
        if (!subRoot || !subRoot.upgradeConsole || !subRoot.powerRelay || !DayNightCycle.main)
        {
            return false;
        }
        Equipment modules = subRoot.upgradeConsole.modules;
        int counting = modules != null ? SolarChargeRules.CountingModules(modules.GetCount(CyclopsSolarTech)) : 0;
        // the energy for one second: above zero when it is light enough and not too deep
        float rate = SolarChargeRules.EnergyFor(VehicleUpgradeSettings.SolarChargeCyclops, Ocean.GetDepthOf(subRoot.gameObject), DayNightCycle.main.GetLocalLightScalar(), 1f);
        return SolarIndicatorRules.IsCharging(VehicleUpgradeSettings.Enabled, counting, GameModeUtils.RequiresPower(), rate, subRoot.powerRelay.GetPower(), subRoot.powerRelay.GetMaxPower());
    }

    /// <summary>
    /// True on the client that simulates the vehicle (the pilot's, or the one the server gave the vehicle to). A vehicle that is not an entity of the world yet, or a client that is still
    /// joining, does not charge: the battery has no id to send its charge under before that.
    /// </summary>
    private static bool HoldsLock(Component vehicle)
    {
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted || !vehicle.TryGetNitroxId(out NitroxId id))
        {
            return false;
        }
        return NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);
    }
}
