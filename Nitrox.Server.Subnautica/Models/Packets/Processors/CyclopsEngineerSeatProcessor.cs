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
///     The engineer console of a Cyclops (<see cref="CyclopsEngineerSeat" />): a player aboard asks to sit at it or to leave it. The first request for a free console wins, and the server tells everybody (the engineer too, who
///     only then opens the console); a request that cannot be is answered with a denial. A client may only send <see cref="CyclopsEngineerSeatMessage.Take" /> and <see cref="CyclopsEngineerSeatMessage.Leave" />,
///     the rest is the server's and is dropped. The server does not look at the Cyclops' modules (nothing on the server models a Cyclops' upgrade console, like the navigator's packets): the client opens the
///     console only with the module, and the simulator, which can see the upgrade console, checks it when it carries out a press that needs it.
/// </summary>
internal sealed class CyclopsEngineerSeatProcessor(
    CyclopsEngineerArbiter arbiter,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsEngineerSeatProcessor> logger) : IAuthPacketProcessor<CyclopsEngineerSeat>
{
    private readonly CyclopsEngineerArbiter arbiter = arbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsEngineerSeatProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CyclopsEngineerSeat packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        // Read once: the player may leave while this is being decided
        Player sender = context.Sender;
        SessionId session = sender.SessionId;
        string name = sender.Name;
        if (!session.IsPlayer)
        {
            return;
        }

        switch (packet.Message)
        {
            case CyclopsEngineerSeatMessage.Take:
                await Take(context, packet, sender, session, name);
                break;
            case CyclopsEngineerSeatMessage.Leave:
                if (arbiter.Leave(packet.CyclopsId, session))
                {
                    logger.ZLogInformation($"{name} left the engineer console of {packet.CyclopsId}");
                    await context.SendToAllAsync(new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Released, packet.CyclopsId, session, name, CyclopsEngineerSeatReason.Left));
                }
                break;
            default:
                logger.ZLogWarning($"{name} sent an engineer seat message of the kind {packet.Message}, which only the server may send; dropped");
                break;
        }
    }

    private async Task Take(AuthProcessorContext context, CyclopsEngineerSeat packet, Player sender, SessionId session, string name)
    {
        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(packet.CyclopsId))
        {
            await context.ReplyAsync(new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsEngineerSeatReason.Unavailable));
            return;
        }
        // The server's idea of the sub root a player is in follows the client's (a SubRootChanged packet), so a player at the console is aboard
        if (!sender.IsOnline || !sender.SubRootId.HasValue || !sender.SubRootId.Value.Equals(packet.CyclopsId))
        {
            await context.ReplyAsync(new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsEngineerSeatReason.NotAboard));
            return;
        }

        CyclopsEngineerArbiter.SitResult result = arbiter.TrySit(packet.CyclopsId, session, name, out CyclopsEngineerArbiter.Seat holder);
        switch (result)
        {
            case CyclopsEngineerArbiter.SitResult.Seated:
            case CyclopsEngineerArbiter.SitResult.AlreadySeated:
                if (!sender.IsOnline)
                {
                    // The player left while this was being decided (the session cleaner has run already and did not find the seat): no seat is left behind for a session that is gone
                    arbiter.Leave(packet.CyclopsId, session);
                    break;
                }
                CyclopsEngineerSeat seated = new(CyclopsEngineerSeatMessage.Seated, packet.CyclopsId, session, name, CyclopsEngineerSeatReason.None);
                if (result == CyclopsEngineerArbiter.SitResult.AlreadySeated)
                {
                    // the engineer asks again (a stale client): only the engineer is told again, not everybody, so that a looping client cannot make the server send packets to all
                    await context.ReplyAsync(seated);
                    break;
                }
                logger.ZLogInformation($"{name} sits at the engineer console of {packet.CyclopsId}");
                await context.SendToAllAsync(seated);
                break;
            case CyclopsEngineerArbiter.SitResult.Occupied:
                await context.ReplyAsync(new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Denied, packet.CyclopsId, holder.Session, holder.Name, CyclopsEngineerSeatReason.Occupied));
                break;
            case CyclopsEngineerArbiter.SitResult.SeatedElsewhere:
                await context.ReplyAsync(new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsEngineerSeatReason.SeatedElsewhere));
                break;
        }
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
