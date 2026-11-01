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
///     The gunner desk of a Cyclops (<see cref="CyclopsGunnerSeat" />): a player aboard asks to sit at it or to leave it. The first request for a free desk wins, and the server tells everybody (the gunner too, who
///     only then opens the console); a request that cannot be is answered with a denial. A client may only send <see cref="CyclopsGunnerSeatMessage.Take" /> and <see cref="CyclopsGunnerSeatMessage.Leave" />,
///     the rest is the server's and is dropped. The server does not look at the Cyclops' modules (nothing on the server models a Cyclops' upgrade console, like the navigator's packets): the client opens the
///     console only with the module, and the player who pays for a shot checks it.
/// </summary>
internal sealed class CyclopsGunnerSeatProcessor(
    CyclopsGunnerArbiter arbiter,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsGunnerSeatProcessor> logger) : IAuthPacketProcessor<CyclopsGunnerSeat>
{
    private readonly CyclopsGunnerArbiter arbiter = arbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsGunnerSeatProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CyclopsGunnerSeat packet)
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
            case CyclopsGunnerSeatMessage.Take:
                await Take(context, packet, sender, session, name);
                break;
            case CyclopsGunnerSeatMessage.Leave:
                if (arbiter.Leave(packet.CyclopsId, session))
                {
                    logger.ZLogInformation($"{name} left the gunner station of {packet.CyclopsId}");
                    await context.SendToAllAsync(new CyclopsGunnerSeat(CyclopsGunnerSeatMessage.Released, packet.CyclopsId, session, name, CyclopsGunnerSeatReason.Left));
                }
                break;
            default:
                logger.ZLogWarning($"{name} sent a gunner seat message of the kind {packet.Message}, which only the server may send; dropped");
                break;
        }
    }

    private async Task Take(AuthProcessorContext context, CyclopsGunnerSeat packet, Player sender, SessionId session, string name)
    {
        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(packet.CyclopsId))
        {
            await context.ReplyAsync(new CyclopsGunnerSeat(CyclopsGunnerSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsGunnerSeatReason.Unavailable));
            return;
        }
        // The server's idea of the sub root a player is in follows the client's (a SubRootChanged packet), so a player at the desk is aboard
        if (!sender.IsOnline || !sender.SubRootId.HasValue || !sender.SubRootId.Value.Equals(packet.CyclopsId))
        {
            await context.ReplyAsync(new CyclopsGunnerSeat(CyclopsGunnerSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsGunnerSeatReason.NotAboard));
            return;
        }

        CyclopsGunnerArbiter.SitResult result = arbiter.TrySit(packet.CyclopsId, session, name, out CyclopsGunnerArbiter.Seat holder);
        switch (result)
        {
            case CyclopsGunnerArbiter.SitResult.Seated:
            case CyclopsGunnerArbiter.SitResult.AlreadySeated:
                if (!sender.IsOnline)
                {
                    // The player left while this was being decided (the session cleaner has run already and did not find the seat): no seat is left behind for a session that is gone
                    arbiter.Leave(packet.CyclopsId, session);
                    break;
                }
                CyclopsGunnerSeat seated = new(CyclopsGunnerSeatMessage.Seated, packet.CyclopsId, session, name, CyclopsGunnerSeatReason.None);
                if (result == CyclopsGunnerArbiter.SitResult.AlreadySeated)
                {
                    // the gunner asks again (a stale client): only the gunner is told again, not everybody, so that a looping client cannot make the server send packets to all
                    await context.ReplyAsync(seated);
                    break;
                }
                logger.ZLogInformation($"{name} sits at the gunner station of {packet.CyclopsId}");
                await context.SendToAllAsync(seated);
                break;
            case CyclopsGunnerArbiter.SitResult.Occupied:
                await context.ReplyAsync(new CyclopsGunnerSeat(CyclopsGunnerSeatMessage.Denied, packet.CyclopsId, holder.Session, holder.Name, CyclopsGunnerSeatReason.Occupied));
                break;
            case CyclopsGunnerArbiter.SitResult.SeatedElsewhere:
                await context.ReplyAsync(new CyclopsGunnerSeat(CyclopsGunnerSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsGunnerSeatReason.SeatedElsewhere));
                break;
        }
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
