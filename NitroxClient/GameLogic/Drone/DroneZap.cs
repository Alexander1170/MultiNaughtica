using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Extensions;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.GameLogic.Drone;

/// <summary>
/// The zapper of the Scout Drone (docs/coop/VEHICLE_UPGRADES.md, station 4; <see cref="CyclopsDroneZap" />). The operator presses the key: this asks the server (which keeps the cooldown, the seat and the drone's place).
/// When the server tells everybody that a zap happens, every client plays the effect, and applies it to what it simulates: the Lava Larvae within the radius that sit on a vehicle or a Cyclops are knocked off, and the
/// creatures within the radius that can be scared (they have the game's <c>FleeOnDamage</c>, the reaction of the Seamoth's electrical defence) flee. Nothing is damaged. A creature that gets angry when it is hurt
/// (the leviathans) is left alone: it would not flee, it would come. The operator's client pays the energy from the drone's battery when it hears its own zap. Each creature is simulated by one client, so each
/// is scared once.
/// </summary>
public static class DroneZap
{
    /// <summary>The damage the scare is worked out from; <c>FleeOnDamage</c> multiplies an electrical hit by 35 and compares it with the creature's threshold, so this is far past any threshold.</summary>
    private const float SCARE_DAMAGE = 40f;

    private static int serial;
    private static float lastRequestTime = -100f;

    /// <summary>The operator pressed the zap key: ask the server. Returns whether a request was sent; <paramref name="message" /> says why not, for the player.</summary>
    public static bool TryRequest(DroneBay bay, out string message)
    {
        message = null;
        ScoutDrone drone = bay ? bay.Drone : null;
        MapRoomCamera camera = drone ? drone.Camera : null;
        NitroxId cyclopsId = bay ? bay.CyclopsId : null;
        if (!camera || cyclopsId == null || !bay.IsOperating || !drone.TryGetNitroxId(out NitroxId droneId))
        {
            return false;
        }
        if (drone.IsDocked)
        {
            message = "The drone has to be out of its dock to zap";
            return false;
        }
        // a key held down repeats; the server keeps the real cooldown
        if (Time.time - lastRequestTime < 1f)
        {
            return false;
        }
        EnergyMixin energy = camera.energyMixin;
        if (!ScoutDroneRules.HasZapEnergy(energy ? energy.charge : 0f, VehicleUpgradeSettings.ZapEnergy, GameModeUtils.RequiresPower()))
        {
            message = "Not enough energy to zap";
            return false;
        }
        lastRequestTime = Time.time;
        serial++;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsDroneZap.Request(cyclopsId, droneId, camera.transform.position.ToDto(), serial));
        Log.Info($"[VehicleUpgrades] drone zap: asked the server (serial {serial})");
        return true;
    }

    /// <summary>The server says that a zap happens, or refuses this client's request.</summary>
    public static void OnPacket(CyclopsDroneZap packet)
    {
        switch (packet.Kind)
        {
            case CyclopsDroneZapKind.Apply:
                OnApply(packet);
                break;
            case CyclopsDroneZapKind.Refused:
                OnRefused(packet);
                break;
        }
    }

    private static void OnRefused(CyclopsDroneZap packet)
    {
        if (packet.OperatorSession != DroneSeats.LocalSession)
        {
            return;
        }
        string text = packet.Failure switch
        {
            CyclopsDroneZapFailure.TooSoon => "The zapper is recharging",
            CyclopsDroneZapFailure.NotOut => "The drone has to be out of its dock to zap",
            CyclopsDroneZapFailure.TooFar => "The zap lost the drone",
            _ => "The zapper cannot be used now"
        };
        ErrorMessage.AddMessage(text);
        Log.Info($"[VehicleUpgrades] drone zap: refused ({packet.Failure})");
    }

    private static void OnApply(CyclopsDroneZap packet)
    {
        Vector3 centre = packet.Position.ToUnity();
        float radius = Mathf.Max(1f, packet.Radius);
        if (packet.OperatorSession == DroneSeats.LocalSession)
        {
            PayEnergy();
        }
        DroneZapEffect.Play(centre, radius);
        int larvae = KnockOffLarvae(centre, radius);
        int scared = ScareCreatures(centre, radius);
        Log.Info($"[VehicleUpgrades] drone zap: at {centre.ToString("F1")} radius {radius:F1}: {larvae} lava larvae knocked off, {scared} creatures scared (of those this client simulates)");
    }

    /// <summary>The operator's client pays: it is the one that flies the drone and so changes its battery.</summary>
    private static void PayEnergy()
    {
        ScoutDrone drone = DroneBay.Current ? DroneBay.Current.Drone : null;
        EnergyMixin energy = drone && drone.Camera ? drone.Camera.energyMixin : null;
        if (energy && GameModeUtils.RequiresPower())
        {
            energy.ConsumeEnergy(VehicleUpgradeSettings.ZapEnergy);
        }
    }

    /// <summary>A creature (or anything that has an id) is simulated by this client, or is not shared at all (then every client acts on its own copy).</summary>
    private static bool Simulates(GameObject gameObject)
    {
        return !gameObject.TryGetNitroxId(out NitroxId id) || NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);
    }

    private static int KnockOffLarvae(Vector3 centre, float radius)
    {
        int knockedOff = 0;
        float squared = radius * radius;
        foreach (LavaLarvaAttachPoint point in UnityEngine.Object.FindObjectsOfType<LavaLarvaAttachPoint>())
        {
            if (!point || !point.occupied || !point.lavaLarva || (point.transform.position - centre).sqrMagnitude > squared)
            {
                continue;
            }
            AttachToVehicle attach = point.lavaLarva.GetComponent<AttachToVehicle>();
            if (attach && Simulates(attach.gameObject))
            {
                attach.SetDetached();
                knockedOff++;
            }
        }
        return knockedOff;
    }

    private static int ScareCreatures(Vector3 centre, float radius)
    {
        int scared = 0;
        HashSet<Creature> seen = [];
        foreach (Collider collider in Physics.OverlapSphere(centre, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            Creature creature = collider ? collider.GetComponentInParent<Creature>() : null;
            if (!creature || !seen.Add(creature) || !creature.isActiveAndEnabled)
            {
                continue;
            }
            // what gets angry when it is hurt (the leviathans) would come instead of fleeing
            if (creature.GetComponent<AggressiveOnDamage>() || !Simulates(creature.gameObject))
            {
                continue;
            }
            FleeOnDamage flee = creature.GetComponent<FleeOnDamage>();
            if (!flee)
            {
                continue;
            }
            DamageInfo info = new();
            info.damage = SCARE_DAMAGE;
            info.originalDamage = SCARE_DAMAGE;
            info.type = DamageType.Electrical;
            info.position = centre;
            flee.OnTakeDamage(info);
            scared++;
        }
        return scared;
    }
}
