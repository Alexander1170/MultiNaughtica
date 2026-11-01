using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class SimulationOwnershipRequestProcessor(SimulationOwnershipData simulationOwnershipData, EntitySimulation entitySimulation, VehicleTowRegistry vehicleTowRegistry) : IAuthPacketProcessor<SimulationOwnershipRequest>
{
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly VehicleTowRegistry vehicleTowRegistry = vehicleTowRegistry;

    public async Task Process(AuthProcessorContext context, SimulationOwnershipRequest ownershipRequest)
    {
        bool aquiredLock = simulationOwnershipData.TryToAcquire(ownershipRequest.Id, context.Sender, ownershipRequest.LockType);

        if (aquiredLock)
        {
            bool shouldEntityMove = entitySimulation.ShouldSimulateEntityMovement(ownershipRequest.Id);
            SimulationOwnershipChange simulationOwnershipChange = new(ownershipRequest.Id, context.Sender.SessionId, ownershipRequest.LockType, shouldEntityMove);
            await context.SendToOthersAsync(simulationOwnershipChange);
            // The tow hooks: a player who gets the lock of a towed vehicle (gets into it) or of a tower ends the tow, unless it is the player who tows
            await vehicleTowRegistry.BroadcastEndedAsync(vehicleTowRegistry.LockTaken(ownershipRequest.Id, context.Sender.SessionId));
        }

        SimulationOwnershipResponse responseToPlayer = new(ownershipRequest.Id, aquiredLock, ownershipRequest.LockType);
        await context.ReplyAsync(responseToPlayer);
    }
}
