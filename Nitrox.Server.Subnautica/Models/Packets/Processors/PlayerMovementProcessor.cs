using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PlayerMovementProcessor(EntityRegistry entityRegistry, EntitySimulation entitySimulation) : IAuthPacketProcessor<PlayerMovement>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly EntitySimulation entitySimulation = entitySimulation;

    public async Task Process(AuthProcessorContext context, PlayerMovement packet)
    {
        Optional<PlayerEntity> playerEntity = entityRegistry.GetEntityById<PlayerEntity>(context.Sender.PlayerContext.PlayerNitroxId);

        if (playerEntity.HasValue)
        {
            playerEntity.Value.Transform.Position = packet.Position;
            playerEntity.Value.Transform.Rotation = packet.BodyRotation;
        }

        context.Sender.Position = packet.Position;
        context.Sender.Rotation = packet.BodyRotation;
        await context.SendToOthersAsync(packet);

        // The simulation lock of a base follows the players who are in or near it
        List<SimulatedEntity> baseChanges = entitySimulation.RebalanceBaseLocks();
        if (baseChanges.Count > 0)
        {
            await context.SendToAllAsync(new SimulationOwnershipChange(baseChanges));
        }
    }
}
