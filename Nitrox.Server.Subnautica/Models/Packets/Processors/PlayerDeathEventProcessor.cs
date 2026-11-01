using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PlayerDeathEventProcessor(IOptions<SubnauticaServerOptions> config, CyclopsGunnerArbiter cyclopsGunnerArbiter, CyclopsEngineerArbiter cyclopsEngineerArbiter, CyclopsDroneArbiter cyclopsDroneArbiter) : IAuthPacketProcessor<PlayerDeathEvent>
{
    private readonly IOptions<SubnauticaServerOptions> options = config;
    private readonly CyclopsGunnerArbiter cyclopsGunnerArbiter = cyclopsGunnerArbiter;
    private readonly CyclopsEngineerArbiter cyclopsEngineerArbiter = cyclopsEngineerArbiter;
    private readonly CyclopsDroneArbiter cyclopsDroneArbiter = cyclopsDroneArbiter;

    public async Task Process(AuthProcessorContext context, PlayerDeathEvent packet)
    {
        if (options.Value.IsHardcore())
        {
            context.Sender.IsPermaDeath = true;
            await context.ReplyAsync(new PlayerKicked("Permanent death from hardcore mode"));
        }
        context.Sender.LastStoredPosition = packet.DeathPosition;
        context.Sender.LastStoredSubRootID = context.Sender.SubRootId;
        // A gunner who dies leaves the desk (the client closes the console too; this is for the seat that nobody would give up)
        await cyclopsGunnerArbiter.BroadcastEndedAsync(cyclopsGunnerArbiter.ReleaseSession(context.Sender.SessionId), CyclopsGunnerSeatReason.Died);
        // ... and so does an engineer
        await cyclopsEngineerArbiter.BroadcastEndedAsync(cyclopsEngineerArbiter.ReleaseSession(context.Sender.SessionId), CyclopsEngineerSeatReason.Died);
        // ... and an operator of the drone bay
        await cyclopsDroneArbiter.BroadcastEndedAsync(cyclopsDroneArbiter.ReleaseSession(context.Sender.SessionId), CyclopsDroneSeatReason.Died);
        if (context.Sender.Permissions > Perms.MODERATOR)
        {
            await context.ReplyAsync(new ChatMessage(SessionId.SERVER_ID, "You can use /back to go to your death location"));
        }
        await context.SendToOthersAsync(packet);
    }
}
