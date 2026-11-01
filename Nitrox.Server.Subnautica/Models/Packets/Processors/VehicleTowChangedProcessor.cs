using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The tow hooks (<see cref="VehicleTowChanged" />): a player says that their Seamoth or Cyclops hooked a vehicle or let go; the server checks that it can be true, keeps the books in
///     <see cref="VehicleTowRegistry" /> and passes it on to everybody else, who draw the cable. When a tow cannot be (the vehicle upgrades are off, somebody sits in the towed vehicle, somebody else
///     tows it, the sender does not simulate both vehicles) the sender is told with a "detached" and lets go.
/// </summary>
internal sealed class VehicleTowChangedProcessor(
    VehicleTowRegistry registry,
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<VehicleTowChangedProcessor> logger) : IAuthPacketProcessor<VehicleTowChanged>
{
    private readonly VehicleTowRegistry registry = registry;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<VehicleTowChangedProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, VehicleTowChanged packet)
    {
        if (packet.TowerId == null || packet.TowedId == null || packet.TowerId.Equals(packet.TowedId))
        {
            return;
        }

        if (!packet.Attached)
        {
            // Only the tow of this tower, this vehicle and this player is ended: a late "let go" of an earlier tow must not end a newer one
            if (registry.TryDetach(packet.TowerId, packet.TowedId, context.Sender.SessionId))
            {
                await context.SendToOthersAsync(new VehicleTowChanged(packet.TowerId, packet.TowedId, false, context.Sender.SessionId));
            }
            return;
        }

        SessionId session = context.Sender.SessionId;
        if (!CanTow(context.Sender, packet) || !registry.TryAttach(packet.TowerId, packet.TowedId, session))
        {
            logger.ZLogInformation($"{context.Sender.Name}'s tow of {packet.TowedId} by {packet.TowerId} was refused");
            await context.ReplyAsync(new VehicleTowChanged(packet.TowerId, packet.TowedId, false, context.Sender.SessionId));
            return;
        }
        if (!context.Sender.IsOnline)
        {
            // The player left while this was being decided (the session cleaner has run already and did not find the tow): no tow is left behind for a session that is gone
            registry.TryDetach(packet.TowerId, packet.TowedId, session);
            return;
        }

        logger.ZLogInformation($"{context.Sender.Name} tows {packet.TowedId} with {packet.TowerId}");
        await context.SendToOthersAsync(new VehicleTowChanged(packet.TowerId, packet.TowedId, true, context.Sender.SessionId));
    }

    private bool CanTow(Player sender, VehicleTowChanged packet)
    {
        if (!options.Value.VehicleUpgrades || !sender.IsOnline)
        {
            return false;
        }

        // A Seamoth or a Cyclops tows a Seamoth or a Prawn Suit
        if (!entityRegistry.TryGetEntityById(packet.TowerId, out Entity? tower) || !entityRegistry.TryGetEntityById(packet.TowedId, out Entity? towed) ||
            !VehicleTowRules.RolesFit(tower.TechType?.Name ?? "", towed.TechType?.Name ?? ""))
        {
            return false;
        }

        // The sender simulates both vehicles (the towed one is simulated by the tower's client, which is what makes it follow). Whoever sits in a vehicle holds its lock, so nobody else can sit in the
        // towed one; the sender itself must not
        if (simulationOwnershipData.GetPlayerForLock(packet.TowerId) != sender || simulationOwnershipData.GetPlayerForLock(packet.TowedId) != sender)
        {
            return false;
        }
        return sender.PlayerContext?.DrivingVehicle == null || !sender.PlayerContext.DrivingVehicle.Equals(packet.TowedId);
    }
}
