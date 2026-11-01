using System.Runtime.CompilerServices;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The Seamoth afterburner in the game (docs/coop/VEHICLE_UPGRADES.md): one <see cref="AfterburnerState" /> per Seamoth, fed by Harmony patches. The game calls the use of an instant quick slot
/// module through <c>Vehicle.SlotKeyDown</c> (cooldown must be 1, then the energy of the module's TechData entry is spent, then <c>OnUpgradeModuleUse</c>); the TechData cost of the afterburner is 0 because
/// the server's setting decides the cost, so this class spends it itself and keeps the cooldown in its own state, which survives the game clearing the quick slot arrays when the pilot gets out.
/// Runs on the client of the pilot only: the other players see the faster Seamoth because its movement is what the game syncs already.
/// </summary>
public static class VehicleAfterburners
{
    private static readonly ConditionalWeakTable<Vehicle, AfterburnerState> states = new();

    /// <summary>The factors of a Seamoth's engine as they were before the afterburner scaled them, so that they can be put back.</summary>
    public struct ForceSnapshot
    {
        public bool Scaled;
        public float Forward;
        public float Backward;
        public float Sideward;
        public float Vertical;
    }

    private static TechType AfterburnerTech => (TechType)CoopItems.SEAMOTH_AFTERBURNER;

    public static AfterburnerState StateOf(Vehicle vehicle) => states.GetOrCreateValue(vehicle);

    public static bool IsInstalled(Vehicle vehicle) => vehicle && vehicle.modules != null && vehicle.modules.GetCount(AfterburnerTech) > 0;

    /// <summary>The quick slot of the afterburner was pressed (after the game's own handling of the key, which writes a cooldown of 0 for a module it does not know).</summary>
    public static void OnUse(SeaMoth seaMoth, TechType techType, int slotID)
    {
        if (techType != AfterburnerTech)
        {
            return;
        }

        AfterburnerState state = StateOf(seaMoth);
        seaMoth.GetEnergyValues(out float charge, out _);
        AfterburnerSettings settings = AfterburnerSettings.Current;
        bool allowed = VehicleUpgradeSettings.Enabled && seaMoth.GetPilotingMode();

        AfterburnerState.PressResult result = state.Press(Time.time, allowed, charge, settings);
        if (result != AfterburnerState.PressResult.Started)
        {
            return;
        }

        seaMoth.ConsumeEnergy(settings.Energy);
        if (slotID >= 0 && slotID < seaMoth.quickSlotTimeUsed.Length)
        {
            seaMoth.quickSlotTimeUsed[slotID] = state.SlotTimeUsed;
            seaMoth.quickSlotCooldown[slotID] = state.SlotCooldown;
        }
        Log.Info($"[VehicleUpgrades] afterburner on: x{settings.Speed} for {settings.Seconds} s, {settings.Energy} energy, {settings.CooldownSeconds} s to recover (slot {slotID}, {charge:F1} energy before)");
    }

    /// <summary>
    /// Makes the quick slot show, and the game obey, the cooldown of the afterburner: <c>Vehicle.GetQuickSlotCooldown</c> is what the key press asks first and what the slot's wheel draws.
    /// </summary>
    public static void AdjustCooldown(Vehicle vehicle, int slotID, ref float result)
    {
        if (!states.TryGetValue(vehicle, out AfterburnerState state) || vehicle.modules == null)
        {
            return;
        }
        string[] slots = vehicle.slotIDs;
        if (slotID < 0 || slotID >= slots.Length || vehicle.modules.GetTechTypeInSlot(slots[slotID]) != AfterburnerTech)
        {
            return;
        }

        float now = Time.time;
        if (state.PhaseAt(now) == AfterburnerState.Phase.Ready || state.SlotCooldown <= 0f)
        {
            // Ready is ready: a burn that ended early (power lost, docked, module taken out) with the pilot still seated left the game's arrays at the length of the full burn and cooldown, which would
            // keep the slot shut after the cooldown is over
            result = 1f;
            return;
        }
        result = Mathf.Clamp01((now - state.SlotTimeUsed) / state.SlotCooldown);
    }

    /// <summary>Called before the Seamoth's engine applies its forces for a physics step: scales them while the afterburner burns.</summary>
    public static void ScaleForces(Vehicle vehicle, out ForceSnapshot snapshot)
    {
        snapshot = default;
        if (vehicle is not SeaMoth)
        {
            return;
        }

        // The afterburner and a tow hook both change the speed: the factors multiply (a burn of 2 while towing at 0.7 is 1.4)
        float factor = 1f;
        if (states.TryGetValue(vehicle, out AfterburnerState state))
        {
            float now = Time.time;
            vehicle.GetEnergyValues(out float charge, out _);
            state.Tick(now, charge);
            factor = Mathf.Max(1f, state.SpeedFactor(now));
        }
        factor *= VehicleTows.SpeedFactor(vehicle);
        if (factor == 1f)
        {
            return;
        }

        snapshot = new ForceSnapshot
        {
            Scaled = true,
            Forward = vehicle.forwardForce,
            Backward = vehicle.backwardForce,
            Sideward = vehicle.sidewardForce,
            Vertical = vehicle.verticalForce
        };
        vehicle.forwardForce *= factor;
        vehicle.backwardForce *= factor;
        vehicle.sidewardForce *= factor;
        vehicle.verticalForce *= factor;
    }

    public static void RestoreForces(Vehicle vehicle, in ForceSnapshot snapshot)
    {
        if (!snapshot.Scaled || !vehicle)
        {
            return;
        }
        vehicle.forwardForce = snapshot.Forward;
        vehicle.backwardForce = snapshot.Backward;
        vehicle.sidewardForce = snapshot.Sideward;
        vehicle.verticalForce = snapshot.Vertical;
    }

    /// <summary>The burn ends for another reason than its time: the pilot got out, the Seamoth docked, its power failed, or the module is gone.</summary>
    public static void End(Vehicle vehicle, AfterburnerState.EndReason reason)
    {
        if (vehicle is SeaMoth && states.TryGetValue(vehicle, out AfterburnerState state) && state.IsBurning)
        {
            state.End(Time.time, reason);
            Log.Info($"[VehicleUpgrades] afterburner off: {reason}");
        }
    }

    /// <summary>A module of the Seamoth changed: a burn without its module is over.</summary>
    public static void OnModulesChanged(SeaMoth seaMoth)
    {
        if (!IsInstalled(seaMoth) || !VehicleUpgradeSettings.Enabled)
        {
            End(seaMoth, AfterburnerState.EndReason.Disabled);
        }
    }
}
