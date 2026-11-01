using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The route of a Cyclops (<see cref="CyclopsRouteChanged" />): a navigator appends and removes waypoints, the player at the helm says that the Cyclops reached the next one. The server checks that it can be true (the
///     vehicle upgrades are on, the entity is a Cyclops, the waypoint is in the world, far enough from the last one and the route has room; an advance is forward only, by one, and comes from the player whose client
///     simulates the Cyclops and names the waypoint it reached), keeps the books in <see cref="CyclopsNavRegistry" /> and passes it on to everybody else. The server's order is the route's order: an added waypoint is
///     relayed with its slot, and a removal with the index of the next waypoint that results. A change that cannot be is answered to the sender alone with the opposite change (a waypoint that was refused with a
///     "removed" whose index is -1, which says that there is no next waypoint to take over; a refused advance with the "advanced" the server has), so that the sender's own copy follows the server's.
/// </summary>
internal sealed class CyclopsRouteChangedProcessor(
    CyclopsNavRegistry registry,
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsRouteChangedProcessor> logger) : IAuthPacketProcessor<CyclopsRouteChanged>
{
    private readonly CyclopsNavRegistry registry = registry;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsRouteChangedProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CyclopsRouteChanged packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        Player sender = context.Sender;
        bool allowed = options.Value.VehicleUpgrades && sender.IsOnline && IsCyclops(packet.CyclopsId);
        switch (packet.Change)
        {
            case CyclopsRouteChange.WaypointAdded:
                await ProcessAdded(context, packet, allowed);
                break;
            case CyclopsRouteChange.WaypointRemoved:
                if (allowed && packet.WaypointId != null && registry.TryRemoveWaypoint(packet.CyclopsId, packet.WaypointId, out int next))
                {
                    logger.ZLogInformation($"{sender.Name} removed a waypoint of the route of Cyclops {packet.CyclopsId}");
                    await context.SendToOthersAsync(new CyclopsRouteChanged(CyclopsRouteChange.WaypointRemoved, packet.CyclopsId, packet.WaypointId, packet.Position, next, sender.SessionId, sender.Name));
                    // The remover worked out the next waypoint on its own while an advance may have been on its way: the server's is the one that counts
                    await context.ReplyAsync(new CyclopsRouteChanged(CyclopsRouteChange.Advanced, packet.CyclopsId, new NitroxId(), packet.Position, next, sender.SessionId, sender.Name));
                }
                break;
            case CyclopsRouteChange.RouteCleared:
                if (allowed && registry.ClearRoute(packet.CyclopsId) > 0)
                {
                    logger.ZLogInformation($"{sender.Name} cleared the route of Cyclops {packet.CyclopsId}");
                    await context.SendToOthersAsync(new CyclopsRouteChanged(CyclopsRouteChange.RouteCleared, packet.CyclopsId, packet.WaypointId ?? new NitroxId(), packet.Position, 0, sender.SessionId, sender.Name));
                }
                break;
            case CyclopsRouteChange.Advanced:
                await ProcessAdvanced(context, packet, allowed);
                break;
        }
    }

    private async Task ProcessAdded(AuthProcessorContext context, CyclopsRouteChanged packet, bool allowed)
    {
        Player sender = context.Sender;
        NitroxId? waypointId = packet.WaypointId;
        if (waypointId == null)
        {
            return;
        }

        CyclopsNavRegistry.Waypoint waypoint = new(waypointId, packet.Position, sender.SessionId, sender.Name);
        bool isNew = false;
        int slot = 0;
        if (!allowed || !registry.TryAddWaypoint(packet.CyclopsId, waypoint, out isNew, out slot))
        {
            logger.ZLogInformation($"{sender.Name}'s waypoint on Cyclops {packet.CyclopsId} was refused");
            await context.ReplyAsync(new CyclopsRouteChanged(CyclopsRouteChange.WaypointRemoved, packet.CyclopsId, waypointId, packet.Position, -1, sender.SessionId, sender.Name));
            return;
        }
        if (!isNew)
        {
            return;
        }
        logger.ZLogInformation($"{sender.Name} added a waypoint to the route of Cyclops {packet.CyclopsId}");
        await context.SendToOthersAsync(new CyclopsRouteChanged(CyclopsRouteChange.WaypointAdded, packet.CyclopsId, waypointId, packet.Position, slot, sender.SessionId, sender.Name));
    }

    private async Task ProcessAdvanced(AuthProcessorContext context, CyclopsRouteChanged packet, bool allowed)
    {
        Player sender = context.Sender;
        // Only the player whose client simulates the Cyclops decides that it reached a waypoint: if several clients decided, they would disagree
        bool holds = simulationOwnershipData.GetPlayerForLock(packet.CyclopsId) == sender;
        if (allowed && holds && packet.WaypointId != null && registry.TryAdvance(packet.CyclopsId, packet.Index, packet.WaypointId))
        {
            await context.SendToOthersAsync(new CyclopsRouteChanged(CyclopsRouteChange.Advanced, packet.CyclopsId, packet.WaypointId, packet.Position, packet.Index, sender.SessionId, sender.Name));
            return;
        }
        // A repeated or late advance (or one for a waypoint that was removed on its way) is not an error; tell the sender where the route really is
        await context.ReplyAsync(new CyclopsRouteChanged(CyclopsRouteChange.Advanced, packet.CyclopsId, new NitroxId(), packet.Position, registry.NextOf(packet.CyclopsId), sender.SessionId, sender.Name));
    }

    private bool IsCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "");
    }
}
