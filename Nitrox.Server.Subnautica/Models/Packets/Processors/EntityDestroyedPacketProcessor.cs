using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityDestroyedPacketProcessor(PlayerManager playerManager, EntitySimulation entitySimulation, WorldEntityManager worldEntityManager, VehicleTowRegistry vehicleTowRegistry, SeamothRiderArbiter seamothRiderArbiter, CyclopsNavRegistry cyclopsNavRegistry, CyclopsGunnerArbiter cyclopsGunnerArbiter, CyclopsEngineerArbiter cyclopsEngineerArbiter, CyclopsDroneArbiter cyclopsDroneArbiter) : IAuthPacketProcessor<EntityDestroyed>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly VehicleTowRegistry vehicleTowRegistry = vehicleTowRegistry;
    private readonly SeamothRiderArbiter seamothRiderArbiter = seamothRiderArbiter;
    private readonly CyclopsNavRegistry cyclopsNavRegistry = cyclopsNavRegistry;
    private readonly CyclopsGunnerArbiter cyclopsGunnerArbiter = cyclopsGunnerArbiter;
    private readonly CyclopsEngineerArbiter cyclopsEngineerArbiter = cyclopsEngineerArbiter;
    private readonly CyclopsDroneArbiter cyclopsDroneArbiter = cyclopsDroneArbiter;

    public async Task Process(AuthProcessorContext context, EntityDestroyed packet)
    {
        entitySimulation.EntityDestroyed(packet.Id);
        // A tow that a destroyed vehicle was part of is over (the tow hooks)
        await vehicleTowRegistry.BroadcastEndedAsync(vehicleTowRegistry.EntityDestroyed(packet.Id));
        // ... and so is the ride of a destroyed Seamoth (the rider bar)
        await seamothRiderArbiter.BroadcastEndedAsync(seamothRiderArbiter.VehicleGone(packet.Id));
        bool destroyed = worldEntityManager.TryDestroyEntity(packet.Id, out Entity? entity);
        // ... and the marks and the route of a destroyed Cyclops (the clients drop theirs when the Cyclops is destroyed on their side). After the entity is out of the registry, so that a mark that arrives
        // in between is refused (it is not a Cyclops any more) instead of being kept for a Cyclops that is gone
        cyclopsNavRegistry.EntityDestroyed(packet.Id);
        // ... and the gunner of a destroyed Cyclops is out of the seat
        await cyclopsGunnerArbiter.BroadcastEndedAsync(cyclopsGunnerArbiter.CyclopsGone(packet.Id), CyclopsGunnerSeatReason.CyclopsDestroyed);
        // ... and so is the engineer, and what was set at the engineer console
        await cyclopsEngineerArbiter.BroadcastEndedAsync(cyclopsEngineerArbiter.CyclopsGone(packet.Id), CyclopsEngineerSeatReason.CyclopsDestroyed);
        // ... and the operator of the drone bay of a destroyed Cyclops; and a destroyed drone empties its bay and ends the seat that flew it
        await cyclopsDroneArbiter.BroadcastEndedAsync(cyclopsDroneArbiter.CyclopsGone(packet.Id), CyclopsDroneSeatReason.CyclopsDestroyed);
        (NitroxId? bayCyclopsId, IReadOnlyList<CyclopsDroneArbiter.Seat> flew) = cyclopsDroneArbiter.DroneGone(packet.Id);
        if (bayCyclopsId != null)
        {
            await cyclopsDroneArbiter.BroadcastEndedAsync(flew, CyclopsDroneSeatReason.DroneDestroyed);
            await context.SendToAllAsync(cyclopsDroneArbiter.StateOf(bayCyclopsId));
        }

        if (destroyed && entity != null)
        {
            if (entity is VehicleEntity vehicleEntity)
            {
                worldEntityManager.MovePlayerChildrenToRoot(vehicleEntity);
            }

            foreach (Player player in playerManager.GetConnectedPlayers())
            {
                bool isOtherPlayer = player != context.Sender;
                if (isOtherPlayer && player.CanSee(entity))
                {
                    await context.SendAsync(packet, player.SessionId);
                }
            }
        }
    }
}
