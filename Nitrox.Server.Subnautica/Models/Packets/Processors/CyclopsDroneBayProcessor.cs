using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     What a client reports about the drone bay of a Cyclops (<see cref="CyclopsDroneBay" />): a drone was loaded, taken out, docked or undocked. The server decides: a load needs a player aboard the
///     Cyclops (the console is inside), an unload any player (the dock is on the hull outside, and a drone is picked up from it by a swimmer as well as by a crew member), a dock and an undock the operator of it; a load needs a Scout Drone that exists (an entity of the item, which is how the bay can be sure it is not asked to hold a Seamoth); the bay holds one drone, the
///     one it was loaded with. What it accepts it keeps (<see cref="CyclopsDroneArbiter" />) and tells everybody (<see cref="CyclopsDroneState" />); what it does not accept changes nothing and is not answered, the
///     state it tells next is the truth. An unload of a drone that is out flying, and an undock when the drone is out already, are no-ops.
/// </summary>
internal sealed class CyclopsDroneBayProcessor(
    CyclopsDroneArbiter arbiter,
    EntityRegistry entityRegistry,
    WorldEntityManager worldEntityManager,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsDroneBayProcessor> logger) : IAuthPacketProcessor<CyclopsDroneBay>
{
    private readonly CyclopsDroneArbiter arbiter = arbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsDroneBayProcessor> logger = logger;

    /// <summary>How far from the drone (or from its Cyclops) a player has to be to pick the drone up: the use distance of the game's hand is 2 m, this leaves room for the delay of the player's position.</summary>
    private const float UNLOAD_DISTANCE = 60f;

    public async Task Process(AuthProcessorContext context, CyclopsDroneBay packet)
    {
        if (packet.CyclopsId == null || packet.DroneId == null || !options.Value.VehicleUpgrades || !IsLiveCyclops(packet.CyclopsId))
        {
            return;
        }
        Player sender = context.Sender;
        if (!sender.IsOnline || !sender.SessionId.IsPlayer)
        {
            return;
        }

        bool changed;
        switch (packet.Kind)
        {
            case CyclopsDroneBayKind.Load:
                // A drone that the bay holds already and that sits out (a swimmer picked up the drone that was left adrift, and put it into the dock again at the console) is docked again; a drone that somebody flies is not
                changed = IsAboard(sender, packet.CyclopsId) && IsScoutDrone(packet.DroneId) &&
                          (arbiter.Load(packet.CyclopsId, packet.DroneId) || (!arbiter.TryGetOperator(packet.CyclopsId, out _) && arbiter.SetDocked(packet.CyclopsId, packet.DroneId, true)));
                break;
            case CyclopsDroneBayKind.Unload:
                // Picked up by hand: from the dock (a swimmer or a crew member), or out in the water where it was left adrift. Not while somebody flies it, and only by a player who is near it or near its Cyclops
                changed = IsNearDroneOrCyclops(sender, packet.CyclopsId, packet.DroneId) && arbiter.Unload(packet.CyclopsId, packet.DroneId, allowOut: !arbiter.TryGetOperator(packet.CyclopsId, out _));
                break;
            case CyclopsDroneBayKind.Dock:
                changed = arbiter.IsOperator(sender.SessionId, packet.CyclopsId) && arbiter.SetDocked(packet.CyclopsId, packet.DroneId, true);
                break;
            case CyclopsDroneBayKind.Undock:
                changed = arbiter.IsOperator(sender.SessionId, packet.CyclopsId) && arbiter.SetDocked(packet.CyclopsId, packet.DroneId, false);
                break;
            default:
                logger.ZLogWarning($"{sender.Name} sent a drone bay request of the kind {packet.Kind}, which is none; dropped");
                return;
        }
        if (!changed)
        {
            // Not accepted: nothing changes, and the sender is told how the bay is, so that a client that did something the server does not know of (a drone put into the dock in its own copy) is set right
            logger.ZLogDebug($"{sender.Name}'s drone bay request ({packet.Kind}) for {packet.CyclopsId} changes nothing");
            await context.ReplyAsync(arbiter.StateOf(packet.CyclopsId));
            return;
        }
        logger.ZLogInformation($"{sender.Name}: drone bay of {packet.CyclopsId}: {packet.Kind} {packet.DroneId}");
        Store(packet.CyclopsId, packet.DroneId);
        if (packet.Kind == CyclopsDroneBayKind.Unload)
        {
            // Nothing to fly any more: the seat, if somebody still had it, ends
            await arbiter.BroadcastEndedAsync(arbiter.EndSeatOf(packet.CyclopsId), CyclopsDroneSeatReason.DroneDestroyed);
        }
        await context.SendToAllAsync(arbiter.StateOf(packet.CyclopsId));
    }

    /// <summary>The player is within <see cref="UNLOAD_DISTANCE" /> of the drone's place or of the Cyclops' place, as the server knows them.</summary>
    private bool IsNearDroneOrCyclops(Player player, NitroxId cyclopsId, NitroxId droneId)
    {
        return IsNear(player, droneId) || IsNear(player, cyclopsId);
    }

    private bool IsNear(Player player, NitroxId entityId)
    {
        if (!entityRegistry.TryGetEntityById(entityId, out WorldEntity? entity))
        {
            return false;
        }
        float dx = player.Position.X - entity.Transform.Position.X;
        float dy = player.Position.Y - entity.Transform.Position.Y;
        float dz = player.Position.Z - entity.Transform.Position.Z;
        return dx * dx + dy * dy + dz * dz <= UNLOAD_DISTANCE * UNLOAD_DISTANCE;
    }

    /// <summary>
    ///     The bay is saved on the drone's entity (<see cref="ScoutDroneMetadata" />, whose bay part only the server writes), so that a world that is saved with a drone in the bay loads with the bay holding it. A drone
    ///     that is in no bay keeps only its health (or has no metadata when it has not been hurt). A drone in the dock is where its Cyclops is, so that the cells it is loaded in are the Cyclops' (the Cyclops carries it, nobody reports the drone's position while it sits there).
    /// </summary>
    private void Store(NitroxId cyclopsId, NitroxId droneId)
    {
        if (!entityRegistry.TryGetEntityById(droneId, out Entity? drone))
        {
            return;
        }
        bool held = arbiter.TryGetDrone(cyclopsId, out NitroxId bayDrone, out bool docked) && bayDrone.Equals(droneId);
        float health = (drone.Metadata as ScoutDroneMetadata)?.Health ?? ScoutDroneMetadata.HEALTH_UNKNOWN;
        if (held)
        {
            drone.Metadata = new ScoutDroneMetadata(cyclopsId, docked, health);
        }
        else
        {
            drone.Metadata = health == ScoutDroneMetadata.HEALTH_UNKNOWN ? null : new ScoutDroneMetadata(null, false, health);
        }
        if (held && docked && entityRegistry.TryGetEntityById(cyclopsId, out WorldEntity? cyclops))
        {
            worldEntityManager.TryUpdateEntityPosition(droneId, cyclops.Transform.Position, cyclops.Transform.Rotation, out _, out _);
        }
    }

    private static bool IsAboard(Player player, NitroxId cyclopsId)
    {
        return player.SubRootId.HasValue && player.SubRootId.Value.Equals(cyclopsId);
    }

    /// <summary>The entity is a Scout Drone (the item the bay is for).</summary>
    private bool IsScoutDrone(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && entity.TechType?.Name == ScoutDroneRules.ITEM_TECH_TYPE_NAME;
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
