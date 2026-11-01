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
///     The drone console of a Cyclops (<see cref="CyclopsDroneSeat" />): a player aboard asks to sit at it or to leave it. The first request for a free console wins, and the server tells everybody (the operator too, who
///     only then opens the console); a request that cannot be is answered with a denial. A client may only send <see cref="CyclopsDroneSeatMessage.Take" /> and <see cref="CyclopsDroneSeatMessage.Leave" />,
///     the rest is the server's and is dropped. The server does not look at the Cyclops' modules (nothing on the server models a Cyclops' upgrade console, like the navigator's packets): the client opens the
///     console only with the module, and the simulator, which can see the upgrade console, checks it when it carries out a press that needs it.
/// </summary>
internal sealed class CyclopsDroneSeatProcessor(
    CyclopsDroneArbiter arbiter,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsDroneSeatProcessor> logger) : IAuthPacketProcessor<CyclopsDroneSeat>
{
    private readonly CyclopsDroneArbiter arbiter = arbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsDroneSeatProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CyclopsDroneSeat packet)
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
            case CyclopsDroneSeatMessage.Take:
                await Take(context, packet, sender, session, name);
                break;
            case CyclopsDroneSeatMessage.Leave:
                if (arbiter.Leave(packet.CyclopsId, session))
                {
                    logger.ZLogInformation($"{name} left the drone console of {packet.CyclopsId}");
                    await context.SendToAllAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Released, packet.CyclopsId, session, name, CyclopsDroneSeatReason.Left));
                }
                break;
            default:
                logger.ZLogWarning($"{name} sent an operator seat message of the kind {packet.Message}, which only the server may send; dropped");
                break;
        }
    }

    private async Task Take(AuthProcessorContext context, CyclopsDroneSeat packet, Player sender, SessionId session, string name)
    {
        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(packet.CyclopsId))
        {
            await context.ReplyAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsDroneSeatReason.Unavailable));
            return;
        }
        // The server's idea of the sub root a player is in follows the client's (a SubRootChanged packet), so a player at the console is aboard
        if (!sender.IsOnline || !sender.SubRootId.HasValue || !sender.SubRootId.Value.Equals(packet.CyclopsId))
        {
            await context.ReplyAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsDroneSeatReason.NotAboard));
            return;
        }

        // A new seat needs the bay's drone to fly (the bay is the server's: it holds the drone, in the dock or out; whoever sits down flies it from where it is); the operator who asks again for the seat they have is answered below
        if (!arbiter.IsOperator(session, packet.CyclopsId) && !arbiter.TryGetDrone(packet.CyclopsId, out _, out _))
        {
            await context.ReplyAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsDroneSeatReason.Unavailable));
            return;
        }

        CyclopsDroneArbiter.SitResult result = arbiter.TrySit(packet.CyclopsId, session, name, out CyclopsDroneArbiter.Seat holder);
        switch (result)
        {
            case CyclopsDroneArbiter.SitResult.Seated:
            case CyclopsDroneArbiter.SitResult.AlreadySeated:
                if (!sender.IsOnline)
                {
                    // The player left while this was being decided (the session cleaner has run already and did not find the seat): no seat is left behind for a session that is gone
                    arbiter.Leave(packet.CyclopsId, session);
                    break;
                }
                CyclopsDroneSeat seated = new(CyclopsDroneSeatMessage.Seated, packet.CyclopsId, session, name, CyclopsDroneSeatReason.None);
                if (result == CyclopsDroneArbiter.SitResult.AlreadySeated)
                {
                    // the operator asks again (a stale client): only the operator is told again, not everybody, so that a looping client cannot make the server send packets to all
                    await context.ReplyAsync(seated);
                    break;
                }
                logger.ZLogInformation($"{name} sits at the drone console of {packet.CyclopsId}");
                await context.SendToAllAsync(seated);
                break;
            case CyclopsDroneArbiter.SitResult.Occupied:
                await context.ReplyAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Denied, packet.CyclopsId, holder.Session, holder.Name, CyclopsDroneSeatReason.Occupied));
                break;
            case CyclopsDroneArbiter.SitResult.SeatedElsewhere:
                await context.ReplyAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Denied, packet.CyclopsId, session, name, CyclopsDroneSeatReason.SeatedElsewhere));
                break;
        }
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
