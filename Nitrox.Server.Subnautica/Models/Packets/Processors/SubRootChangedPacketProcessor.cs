using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class SubRootChangedPacketProcessor(EntityRegistry entityRegistry, CyclopsGunnerArbiter cyclopsGunnerArbiter, CyclopsEngineerArbiter cyclopsEngineerArbiter, CyclopsDroneArbiter cyclopsDroneArbiter) : IAuthPacketProcessor<SubRootChanged>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly CyclopsGunnerArbiter cyclopsGunnerArbiter = cyclopsGunnerArbiter;
    private readonly CyclopsEngineerArbiter cyclopsEngineerArbiter = cyclopsEngineerArbiter;
    private readonly CyclopsDroneArbiter cyclopsDroneArbiter = cyclopsDroneArbiter;

    public async Task Process(AuthProcessorContext context, SubRootChanged packet)
    {
        PlayerParentRules.TryGetParent(entityRegistry, context.Sender.GameObjectId, out Entity? currentParent);
        if (!PlayerParentRules.KeepsEscapePodParent(packet.SubRootId, currentParent))
        {
            entityRegistry.ReparentEntity(context.Sender.GameObjectId, packet.SubRootId.OrNull());
            context.Sender.SubRootId = packet.SubRootId;
        }
        // A gunner who is not in the Cyclops any more does not sit at its desk
        await cyclopsGunnerArbiter.BroadcastEndedAsync(cyclopsGunnerArbiter.PlayerMoved(context.Sender.SessionId, packet.SubRootId), CyclopsGunnerSeatReason.LeftCyclops);
        // ... and neither does an engineer
        await cyclopsEngineerArbiter.BroadcastEndedAsync(cyclopsEngineerArbiter.PlayerMoved(context.Sender.SessionId, packet.SubRootId), CyclopsEngineerSeatReason.LeftCyclops);
        // ... and neither does an operator of the drone bay
        await cyclopsDroneArbiter.BroadcastEndedAsync(cyclopsDroneArbiter.PlayerMoved(context.Sender.SessionId, packet.SubRootId), CyclopsDroneSeatReason.LeftCyclops);
        await context.SendToOthersAsync(packet);
    }
}
