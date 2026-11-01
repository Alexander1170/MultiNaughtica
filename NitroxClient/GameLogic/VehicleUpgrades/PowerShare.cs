using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The Cyclops Power Share in the game (docs/coop/VEHICLE_UPGRADES.md).
/// <list type="bullet">
/// <item><b>Forward</b>: a Cyclops with the module and power charges the Seamoths and Prawn Suits within range that are not full, no docking needed. The vehicle's simulator asks about once a second
/// (<see cref="TickVehicle" />).</item>
/// <item><b>Emergency</b>: a Cyclops with the module below its threshold draws on the vehicle docked in it, down to that vehicle's reserve. The Cyclops' simulator asks (<see cref="TickCyclops" />), and the
/// game's own docking charge of that vehicle pauses meanwhile (<see cref="IsDockChargePaused" />).</item>
/// </list>
/// A vehicle's energy may only be changed by the player who simulates it, and the Cyclops and the vehicle usually have different simulators. Both directions are therefore the same request and grant
/// between the two simulators through the server (<see cref="PowerTransfer" />): the simulator of the <b>receiver</b> asks, the simulator of the <b>giver</b> takes the energy out of the giver first and
/// says how much (<see cref="OnOffer" />), and the receiver adds exactly that (<see cref="OnDelivery" />); what it could not use goes back (<see cref="OnRefund" />). When one client simulates both ends
/// the same steps run in one frame without a packet (<see cref="PowerTransfers.Move" />). The numbers and the arithmetic are in <see cref="PowerTransferRules" />.
/// </summary>
public static class PowerShare
{
    private static readonly PowerTransferTracker tracker = new();
    private static readonly TimedMarks chargingMarks = new();
    private static readonly TimedMarks emergencyMarks = new();
    private static readonly TimedMarks dockChargePause = new();
    private static readonly List<CyclopsPowerShare> cyclopses = [];
    private static bool subscribed;
    private static Totals stats;

    /// <summary>What the power share moved on this client since the session began: for the live check, which compares the two clients (what one gave is what the other received).</summary>
    public struct Totals
    {
        public int RequestsSent;
        public int OffersAnswered;
        public int DeliveriesApplied;
        public int DockChargesPaused;
        public float EnergyGiven;
        public float EnergyReceived;
        public float EnergyMovedLocally;
        public float EnergyReturned;
        public float EnergyRefunded;
    }

    public static Totals Stats => stats;

    private static TechType PowerShareTech => (TechType)CoopItems.CYCLOPS_POWER_SHARE;

    /// <summary>The share runs when the vehicle upgrades are on and this client has joined: until then the numbers are the defaults, and in Creative mode there is no energy to share.</summary>
    public static bool Active => VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted && GameModeUtils.RequiresPower();

    /// <summary>The Cyclops that have the share component (all of them, see <c>SubRoot_Start_Patch</c>).</summary>
    public static IReadOnlyList<CyclopsPowerShare> Cyclopses => cyclopses;

    public static void Register(CyclopsPowerShare share)
    {
        Subscribe();
        if (!cyclopses.Contains(share))
        {
            cyclopses.Add(share);
        }
    }

    public static void Unregister(CyclopsPowerShare share)
    {
        cyclopses.Remove(share);
    }

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: nothing of this session may be left over for the next one.</summary>
    public static void Reset()
    {
        tracker.Clear();
        chargingMarks.Clear();
        emergencyMarks.Clear();
        dockChargePause.Clear();
        cyclopses.Clear();
        PowerShareSwitch.Clear();
        stats = default;
    }

    /// <summary>
    /// True while the game's own docking charge of this vehicle (which would charge it from the Cyclops it is docked in) has to wait: an emergency draw on it ran lately, or the Cyclops with the Power Share is in an
    /// emergency or would be put into one by the charge (<see cref="PowerTransferRules.DockChargeShouldPause" />).
    /// </summary>
    public static bool IsDockChargePaused(Vehicle vehicle)
    {
        bool drawLately = dockChargePause.Count > 0 && vehicle.TryGetNitroxId(out NitroxId id) && dockChargePause.IsMarked(id, Time.time);
        bool paused = drawLately || DockedCyclopsIsInEmergency(vehicle);
        if (paused)
        {
            stats.DockChargesPaused++;
        }
        return paused;
    }

    private static bool DockedCyclopsIsInEmergency(Vehicle vehicle)
    {
        if (!Active || !vehicle.docked)
        {
            return false;
        }
        SubRoot cyclops = vehicle.GetComponentInParent<SubRoot>();
        if (!cyclops || !cyclops.isCyclops || !cyclops.powerRelay || !HasModule(cyclops))
        {
            return false;
        }
        vehicle.GetEnergyValues(out float charge, out float capacity);
        return PowerTransferRules.DockChargeShouldPause(cyclops.powerRelay.GetPower(), cyclops.powerRelay.GetMaxPower(), VehicleUpgradeSettings.EmergencyPowerThreshold, PowerTransferRules.VanillaDockCharge(charge, capacity), false);
    }

    /// <summary>For the live check: the name of the vehicle a Cyclops is drawing emergency power from right now (its mark has not run out), if any.</summary>
    public static bool TryGetEmergencyMark(NitroxId cyclopsId, out string vehicleName) => emergencyMarks.TryGet(cyclopsId, Time.time, out vehicleName);

    /// <summary>For the live check: a Cyclops charged this vehicle a moment ago.</summary>
    public static bool IsChargingMarked(NitroxId vehicleId) => chargingMarks.IsMarked(vehicleId, Time.time);

    /// <summary>The power share button of a Cyclops is shown: the vehicle upgrades are on, there is energy to share in this game mode and the Cyclops has the module.</summary>
    public static bool IsAvailable(SubRoot cyclops)
    {
        return VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && GameModeUtils.RequiresPower() && HasModule(cyclops);
    }

    /// <summary>Does the Cyclops have the module? One counts.</summary>
    public static bool HasModule(SubRoot cyclops)
    {
        return cyclops && cyclops.isCyclops && cyclops.upgradeConsole && cyclops.upgradeConsole.modules != null && cyclops.upgradeConsole.modules.GetCount(PowerShareTech) > 0;
    }

    // ---- the ticks (the requests of the receivers)

    /// <summary>
    /// Forward share, once a second per vehicle: the simulator of a Seamoth or Prawn Suit that is not full asks the nearest Cyclops with the module in range. A docked vehicle is left to the game's own
    /// docking charge and to the emergency power.
    /// </summary>
    public static bool TickVehicle(Vehicle vehicle, float elapsed)
    {
        // Returns false when the time of this tick is not used up: the last request of this pair is still on its way, and the next tick asks for the time since then as well
        if (!Active || !vehicle || vehicle.docked || !IsAlive(vehicle) || !vehicle.TryGetNitroxId(out NitroxId vehicleId) || !HoldsLock(vehicleId))
        {
            return true;
        }

        vehicle.GetEnergyValues(out float charge, out float capacity);
        float amount = PowerTransferRules.RequestAmount(VehicleUpgradeSettings.PowerShareRate, elapsed, capacity - charge);
        if (amount <= 0f)
        {
            return true;
        }

        CyclopsPowerShare nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (CyclopsPowerShare candidate in cyclopses)
        {
            if (!candidate || !CanShare(candidate.SubRoot))
            {
                continue;
            }
            float distance = candidate.DistanceToHull(vehicle.transform.position);
            if (distance < nearestDistance && PowerShareGeometry.InRange(distance, VehicleUpgradeSettings.PowerShareRange, 0f))
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }
        if (!nearest || !nearest.SubRoot.TryGetNitroxId(out NitroxId cyclopsId))
        {
            return true;
        }

        if (HoldsLock(cyclopsId))
        {
            float moved = PowerTransfers.Move(new CyclopsEnd(nearest.SubRoot), new VehicleEnd(vehicle), amount);
            if (moved > 0f)
            {
                stats.EnergyMovedLocally += moved;
                chargingMarks.Mark(vehicleId, Time.time, PowerTransferRules.DISPLAY_SECONDS);
            }
            return true;
        }
        if (tracker.TryBegin(cyclopsId, vehicleId, Time.time))
        {
            stats.RequestsSent++;
            Send(PowerTransfer.Request(cyclopsId, vehicleId, amount, PowerTransferMode.Forward));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Emergency power, once a second per Cyclops: the simulator of a Cyclops with the module whose power fell below the threshold asks for energy from the vehicle docked in it.
    /// </summary>
    public static bool TickCyclops(CyclopsPowerShare share, float elapsed)
    {
        // Returns false when the time of this tick is not used up (see TickVehicle)
        SubRoot cyclops = share.SubRoot;
        if (!Active || !HasModule(cyclops) || cyclops.subDestroyed || !cyclops.powerRelay || !cyclops.TryGetNitroxId(out NitroxId cyclopsId) || !HoldsLock(cyclopsId))
        {
            return true;
        }

        float power = cyclops.powerRelay.GetPower();
        float maxPower = cyclops.powerRelay.GetMaxPower();
        if (!PowerTransferRules.EmergencyActive(power, maxPower, VehicleUpgradeSettings.EmergencyPowerThreshold))
        {
            return true;
        }

        Vehicle docked = share.DockedVehicle();
        if (!docked || !docked.docked || !docked.TryGetNitroxId(out NitroxId vehicleId))
        {
            return true;
        }
        float amount = PowerTransferRules.RequestAmount(VehicleUpgradeSettings.EmergencyPowerRate, elapsed, maxPower - power);
        if (amount <= 0f)
        {
            return true;
        }

        if (HoldsLock(vehicleId))
        {
            float moved = PowerTransfers.Move(new VehicleEnd(docked), new CyclopsEnd(cyclops), amount);
            if (moved > 0f)
            {
                stats.EnergyMovedLocally += moved;
                dockChargePause.Mark(vehicleId, Time.time, PowerTransferRules.DOCK_CHARGE_PAUSE_SECONDS);
                emergencyMarks.Mark(cyclopsId, Time.time, PowerTransferRules.DISPLAY_SECONDS, docked.GetName());
            }
            return true;
        }
        if (tracker.TryBegin(vehicleId, cyclopsId, Time.time))
        {
            stats.RequestsSent++;
            Send(PowerTransfer.Request(vehicleId, cyclopsId, amount, PowerTransferMode.Emergency));
            return true;
        }
        return false;
    }

    // ---- the packets

    /// <summary>The server passed on a request: this client simulates the giver. Takes the energy out, as far as the giver may part with it, and says how much (also when it is nothing).</summary>
    public static void OnOffer(PowerTransfer offer)
    {
        PowerTransfer grant;
        try
        {
            IPowerEndpoint giver = GiverFor(offer, out float mostAllowed);
            grant = PowerTransfers.AnswerOffer(offer, giver, mostAllowed);
            stats.OffersAnswered++;
            stats.EnergyGiven += grant.Amount;
            if (giver != null && grant.Amount > 0f && offer.Mode == PowerTransferMode.Emergency)
            {
                dockChargePause.Mark(offer.GiverId, Time.time, PowerTransferRules.DOCK_CHARGE_PAUSE_SECONDS);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[VehicleUpgrades] power transfer {offer.TransferId}: could not take the energy out of the giver");
            grant = PowerTransfer.Grant(offer.TransferId, offer.GiverId, offer.ReceiverId, 0f, offer.Mode);
        }
        Send(grant);
    }

    /// <summary>
    /// The giver of an offer, when this client may take energy out of it for that offer (null when not), and the most one request may ask: a Cyclops gives to a vehicle in range that is not docked, a
    /// docked vehicle gives to the Cyclops it is docked in.
    /// </summary>
    private static IPowerEndpoint GiverFor(PowerTransfer offer, out float mostAllowed)
    {
        mostAllowed = 0f;
        if (!Active || !PowerTransferRules.IsValidAmount(offer.Amount) || !HoldsLock(offer.GiverId))
        {
            return null;
        }

        if (offer.Mode == PowerTransferMode.Forward)
        {
            // A Cyclops gives to a vehicle in range that is not docked, at the rate of the share
            if (!NitroxEntity.TryGetComponentFrom(offer.GiverId, out SubRoot cyclops) || !CanShare(cyclops) || !NitroxEntity.TryGetComponentFrom(offer.ReceiverId, out Vehicle vehicle) || !vehicle || vehicle.docked)
            {
                return null;
            }
            CyclopsPowerShare share = cyclops.GetComponent<CyclopsPowerShare>();
            if (!share || !PowerShareGeometry.InRange(share.DistanceToHull(vehicle.transform.position), VehicleUpgradeSettings.PowerShareRange, PowerShareGeometry.RANGE_TOLERANCE))
            {
                return null;
            }
            mostAllowed = VehicleUpgradeSettings.PowerShareRate * PowerTransferRules.MAX_STEP_SECONDS;
            return new CyclopsEnd(cyclops);
        }

        if (offer.Mode == PowerTransferMode.Emergency)
        {
            // A docked vehicle gives to the Cyclops it is docked in, down to its reserve
            if (!NitroxEntity.TryGetComponentFrom(offer.GiverId, out Vehicle vehicle) || !vehicle || !vehicle.docked || !NitroxEntity.TryGetComponentFrom(offer.ReceiverId, out SubRoot cyclops) ||
                !HasModule(cyclops) || vehicle.GetComponentInParent<SubRoot>() != cyclops || VehicleUpgradeSettings.EmergencyPowerThreshold <= 0f)
            {
                return null;
            }
            mostAllowed = VehicleUpgradeSettings.EmergencyPowerRate * PowerTransferRules.MAX_STEP_SECONDS;
            return new VehicleEnd(vehicle);
        }
        return null;
    }

    /// <summary>The answer to this client's request: the energy of a granted one is added to the receiver, what it cannot use goes back to the giver.</summary>
    public static void OnDelivery(PowerTransfer delivery)
    {
        tracker.Complete(delivery.GiverId, delivery.ReceiverId);
        if (delivery.Outcome != PowerTransferOutcome.Granted || !PowerTransferRules.IsValidAmount(delivery.Amount))
        {
            return;
        }
        if (!Active || !HoldsLock(delivery.ReceiverId))
        {
            // The lock of the receiver moved while the energy was on its way (or the share is off here): this client cannot use it, so all of it goes back to the giver. The server accepts the return
            // because it delivered the energy to this client
            Log.Debug($"[VehicleUpgrades] power transfer {delivery.TransferId}: {delivery.Amount:F2} energy arrived at a client that no longer simulates the receiver, returning it");
            stats.EnergyReturned += delivery.Amount;
            Send(PowerTransfer.Return(delivery.TransferId, delivery.GiverId, delivery.ReceiverId, delivery.Amount, delivery.Mode));
            return;
        }

        IPowerEndpoint receiver = EndOf(delivery.ReceiverId, delivery.Mode == PowerTransferMode.Forward);
        if (receiver == null)
        {
            // This client still holds the lock but the receiver is not here (it was destroyed or has not loaded): the energy goes back to the giver, all of it
            Log.Debug($"[VehicleUpgrades] power transfer {delivery.TransferId}: the receiver is not here, returning all {delivery.Amount:F2}");
            stats.EnergyReturned += delivery.Amount;
            Send(PowerTransfer.Return(delivery.TransferId, delivery.GiverId, delivery.ReceiverId, delivery.Amount, delivery.Mode));
            return;
        }
        PowerTransfer giveBack = PowerTransfers.ApplyDelivery(delivery, receiver, out float stored);
        if (giveBack != null)
        {
            stats.EnergyReturned += giveBack.Amount;
            Send(giveBack);
        }
        if (stored > 0f)
        {
            stats.DeliveriesApplied++;
            stats.EnergyReceived += stored;
            if (delivery.Mode == PowerTransferMode.Forward)
            {
                chargingMarks.Mark(delivery.ReceiverId, Time.time, PowerTransferRules.DISPLAY_SECONDS);
            }
            else
            {
                string vehicleName = NitroxEntity.TryGetComponentFrom(delivery.GiverId, out Vehicle giver) && giver ? giver.GetName() : "";
                emergencyMarks.Mark(delivery.ReceiverId, Time.time, PowerTransferRules.DISPLAY_SECONDS, vehicleName);
            }
        }
    }

    /// <summary>Energy comes back to the giver: it was delivered too late or the receiver could not use it.</summary>
    public static void OnRefund(PowerTransfer refund)
    {
        if (!HoldsLock(refund.GiverId))
        {
            return;
        }
        // The giver is the Cyclops in the forward mode and the vehicle in the emergency mode
        stats.EnergyRefunded += PowerTransfers.ApplyRefund(refund, EndOf(refund.GiverId, refund.Mode != PowerTransferMode.Forward));
    }

    // ---- the on-screen lines

    /// <summary>
    /// The line under "Press X to exit" while the local player pilots a vehicle that a Cyclops charges ("Charging from Cyclops") or the Cyclops helm of a Cyclops that draws on its docked vehicle
    /// ("Emergency power from docked X"). The delivery arrives on exactly the client of the pilot or of the helm, because the pilot simulates the vehicle and the helm the Cyclops.
    /// </summary>
    public static bool TryGetPilotLine(out string line)
    {
        line = null;
        Player player = Player.main;
        if (!player || (chargingMarks.Count == 0 && emergencyMarks.Count == 0))
        {
            return false;
        }

        double now = Time.time;
        Vehicle vehicle = player.GetVehicle();
        if (vehicle && vehicle.GetPilotingMode() && vehicle.TryGetNitroxId(out NitroxId vehicleId) && chargingMarks.IsMarked(vehicleId, now))
        {
            line = Language.main.Get("Nitrox_ChargingFromCyclops");
            return true;
        }

        if (player.GetMode() == Player.Mode.Piloting)
        {
            SubRoot sub = player.GetCurrentSub();
            if (sub && sub.isCyclops && sub.TryGetNitroxId(out NitroxId cyclopsId) && emergencyMarks.TryGet(cyclopsId, now, out string vehicleName))
            {
                line = string.Format(Language.main.Get("Nitrox_EmergencyPowerFrom"), vehicleName);
                return true;
            }
        }
        return false;
    }

    // ---- helpers

    /// <summary>A Cyclops can give: it has the module, its switch is on (a button on the lighting panel; off by default), it is not destroyed and has more power than its floor.</summary>
    private static bool CanShare(SubRoot cyclops)
    {
        return HasModule(cyclops) && PowerShareSwitch.IsOn(cyclops) && !cyclops.subDestroyed && cyclops.powerRelay &&
               PowerTransferRules.ForwardGrant(PowerTransferRules.MAX_AMOUNT, cyclops.powerRelay.GetPower(), cyclops.powerRelay.GetMaxPower(), VehicleUpgradeSettings.EmergencyPowerThreshold) > 0f;
    }

    private static bool IsAlive(Vehicle vehicle) => !vehicle.liveMixin || vehicle.liveMixin.IsAlive();

    /// <summary>True on the client that simulates the vehicle or the Cyclops.</summary>
    private static bool HoldsLock(NitroxId id)
    {
        return id != null && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);
    }

    private static void Send(PowerTransfer packet)
    {
        NitroxServiceLocator.LocateService<IPacketSender>().Send(packet);
    }

    /// <summary>The end for an id: the vehicle for a vehicle, the Cyclops for a Cyclops (or null when the object is not here).</summary>
    private static IPowerEndpoint EndOf(NitroxId id, bool isVehicle)
    {
        if (isVehicle)
        {
            return NitroxEntity.TryGetComponentFrom(id, out Vehicle vehicle) && vehicle ? new VehicleEnd(vehicle) : null;
        }
        return NitroxEntity.TryGetComponentFrom(id, out SubRoot cyclops) && cyclops ? new CyclopsEnd(cyclops) : null;
    }

    // ---- the ends

    /// <summary>
    /// A Cyclops as an end of a transfer: it gives from its power relay (the cells, one after the other) down to its floor, and takes into the relay.
    /// </summary>
    private sealed class CyclopsEnd(SubRoot cyclops) : IPowerEndpoint
    {
        public float Take(float offered)
        {
            if (!cyclops || !cyclops.powerRelay)
            {
                return 0f;
            }
            PowerRelay relay = cyclops.powerRelay;
            float granted = PowerTransferRules.ForwardGrant(offered, relay.GetPower(), relay.GetMaxPower(), VehicleUpgradeSettings.EmergencyPowerThreshold);
            if (!(granted > 0f))
            {
                return 0f;
            }
            PowerSystem.ConsumeEnergy(relay, granted, out float consumed);
            return consumed;
        }

        public float Give(float amount)
        {
            if (!cyclops || !cyclops.powerRelay)
            {
                return 0f;
            }
            PowerSystem.AddEnergy(cyclops.powerRelay, amount, out float stored);
            return stored;
        }
    }

    /// <summary>
    /// A Seamoth or Prawn Suit as an end of a transfer: it gives from its power cells down to its reserve (only a docked vehicle gives), and takes into them. The energy interface splits a delivery
    /// over the cells that are not full and returns what it really stored; the vehicle's own all-or-nothing methods are not used.
    /// </summary>
    private sealed class VehicleEnd(Vehicle vehicle) : IPowerEndpoint
    {
        public float Take(float offered)
        {
            if (!vehicle || !vehicle.energyInterface)
            {
                return 0f;
            }
            vehicle.GetEnergyValues(out float charge, out float capacity);
            float granted = PowerTransferRules.EmergencyGrant(offered, charge, capacity, VehicleUpgradeSettings.EmergencyPowerReserve);
            return granted > 0f ? vehicle.energyInterface.ConsumeEnergy(granted) : 0f;
        }

        public float Give(float amount)
        {
            return vehicle && vehicle.energyInterface ? vehicle.energyInterface.AddEnergy(amount) : 0f;
        }
    }
}
