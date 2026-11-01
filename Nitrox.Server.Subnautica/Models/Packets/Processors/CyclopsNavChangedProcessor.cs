using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The marks of a Cyclops' navigator (<see cref="CyclopsNavChanged" />): a player says that they placed, removed or cleared marks on the map of the navigator console; the server checks that it can be true
///     (the vehicle upgrades are on, the entity is a Cyclops, the mark is in the world and a known kind, the Cyclops has room, and a mark of the Scout Drone comes from its seated operator), keeps the books in <see cref="CyclopsNavRegistry" /> and passes it on to everybody
///     else, who draw the marks. A placement that cannot be is answered with a "removed" to the sender alone, who takes its own copy back.
/// </summary>
internal sealed class CyclopsNavChangedProcessor(
    CyclopsNavRegistry registry,
    EntityRegistry entityRegistry,
    CyclopsDroneArbiter droneArbiter,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsNavChangedProcessor> logger) : IAuthPacketProcessor<CyclopsNavChanged>
{
    private readonly CyclopsNavRegistry registry = registry;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly CyclopsDroneArbiter droneArbiter = droneArbiter;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsNavChangedProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CyclopsNavChanged packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        Player sender = context.Sender;
        bool allowed = options.Value.VehicleUpgrades && sender.IsOnline && IsCyclops(packet.CyclopsId);
        switch (packet.Change)
        {
            case CyclopsNavChange.MarkAdded:
                await ProcessAdded(context, packet, allowed);
                break;
            case CyclopsNavChange.MarkRemoved:
                if (allowed && packet.MarkId != null && registry.TryRemove(packet.CyclopsId, packet.MarkId, out CyclopsNavRegistry.Mark? removed) && removed != null)
                {
                    logger.ZLogInformation($"{sender.Name} removed the mark '{removed.Label}' of Cyclops {packet.CyclopsId}");
                    await context.SendToOthersAsync(new CyclopsNavChanged(CyclopsNavChange.MarkRemoved, packet.CyclopsId, packet.MarkId, removed.Position, removed.Kind, removed.Label, removed.Owner, removed.OwnerName));
                }
                break;
            case CyclopsNavChange.MarksCleared:
                if (allowed)
                {
                    int cleared = registry.Clear(packet.CyclopsId);
                    if (cleared > 0)
                    {
                        logger.ZLogInformation($"{sender.Name} cleared the {cleared} marks of Cyclops {packet.CyclopsId}");
                        await context.SendToOthersAsync(new CyclopsNavChanged(CyclopsNavChange.MarksCleared, packet.CyclopsId, packet.MarkId ?? new NitroxId(), packet.Position, 0, string.Empty, sender.SessionId, sender.Name));
                    }
                }
                break;
        }
    }

    private async Task ProcessAdded(AuthProcessorContext context, CyclopsNavChanged packet, bool allowed)
    {
        Player sender = context.Sender;
        NitroxId? markId = packet.MarkId;
        if (markId == null)
        {
            return;
        }

        string label = CyclopsNavRules.SanitizeLabel(packet.Label);
        bool valid = allowed && CyclopsNavRules.IsKnownKind(packet.MarkKind) && CyclopsNavRules.IsValidPosition(packet.Position.X, packet.Position.Y, packet.Position.Z);
        // The marks of the Scout Drone (a resource, a threat) are the seated operator's of that Cyclops: nobody else places them
        if (valid && CyclopsNavRules.IsDroneKind(packet.MarkKind) && !droneArbiter.IsOperator(sender.SessionId, packet.CyclopsId))
        {
            valid = false;
        }
        // The owner is the sender, whatever the packet said
        CyclopsNavRegistry.Mark mark = new(markId, packet.CyclopsId, packet.Position, packet.MarkKind, label, sender.SessionId, sender.Name);
        bool isNew = false;
        if (!valid || !registry.TryAdd(mark, out isNew))
        {
            logger.ZLogInformation($"{sender.Name}'s mark on Cyclops {packet.CyclopsId} was refused");
            await context.ReplyAsync(new CyclopsNavChanged(CyclopsNavChange.MarkRemoved, packet.CyclopsId, markId, packet.Position, packet.MarkKind, label, sender.SessionId, sender.Name));
            return;
        }
        if (!isNew)
        {
            return;
        }
        logger.ZLogInformation($"{sender.Name} placed the mark '{label}' on Cyclops {packet.CyclopsId}");
        await context.SendToOthersAsync(new CyclopsNavChanged(CyclopsNavChange.MarkAdded, packet.CyclopsId, markId, packet.Position, packet.MarkKind, label, sender.SessionId, sender.Name));
    }

    private bool IsCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "");
    }
}
