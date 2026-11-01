using Nitrox.Model.DataStructures;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal class PlayerSyncFinishedProcessor(SessionManager sessionManager, JoiningManager joiningManager, HibernateService hibernateService, EntitySimulation entitySimulation, VehicleTowRegistry vehicleTowRegistry, SeamothRiderArbiter seamothRiderArbiter, CyclopsNavRegistry cyclopsNavRegistry,
    CyclopsGunnerArbiter cyclopsGunnerArbiter, CyclopsEngineerArbiter cyclopsEngineerArbiter, CyclopsDroneArbiter cyclopsDroneArbiter, EntityRegistry entityRegistry, PlayerModelLedger playerModelLedger)
    : IAuthPacketProcessor<PlayerSyncFinished>
{
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly SessionManager sessionManager = sessionManager;
    private readonly JoiningManager joiningManager = joiningManager;
    private readonly HibernateService hibernateService = hibernateService;
    private readonly VehicleTowRegistry vehicleTowRegistry = vehicleTowRegistry;
    private readonly SeamothRiderArbiter seamothRiderArbiter = seamothRiderArbiter;
    private readonly CyclopsNavRegistry cyclopsNavRegistry = cyclopsNavRegistry;
    private readonly CyclopsGunnerArbiter cyclopsGunnerArbiter = cyclopsGunnerArbiter;
    private readonly CyclopsEngineerArbiter cyclopsEngineerArbiter = cyclopsEngineerArbiter;
    private readonly CyclopsDroneArbiter cyclopsDroneArbiter = cyclopsDroneArbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly PlayerModelLedger playerModelLedger = playerModelLedger;

    public async Task Process(AuthProcessorContext context, PlayerSyncFinished packet)
    {
        if (sessionManager.GetSessionCount() > 0)
        {
            await hibernateService.WakeAsync();
        }

        // Who holds which vehicle and base may have changed while this player was loading (see EntitySimulation.GetGlobalRootSimulationSnapshot)
        foreach (SimulatedEntity simulatedEntity in entitySimulation.GetGlobalRootSimulationSnapshot())
        {
            await context.SendAsync(new SimulationOwnershipChange(simulatedEntity), context.Sender.SessionId);
        }

        // The tows that are on (the tow hooks): the cable is drawn by every client, and this one has the vehicles now
        await vehicleTowRegistry.SendSnapshotAsync(context.Sender.SessionId);
        // ... and the riders who hold on to the bars of Seamoths
        await seamothRiderArbiter.SendSnapshotAsync(context.Sender.SessionId);
        // ... and the marks on the maps of the navigator consoles of the Cyclopses (the Cyclops Terrain Sonar)
        await cyclopsNavRegistry.SendSnapshotAsync(context.Sender.SessionId);
        // ... and who sits at the gunner desks of the Cyclopses (the Cyclops Gunner Station)
        await cyclopsGunnerArbiter.SendSnapshotAsync(context.Sender.SessionId);
        // ... and who sits at the engineer consoles and what the engineers have set (the Cyclops Engineer Console)
        await cyclopsEngineerArbiter.SendSnapshotAsync(context.Sender.SessionId);
        // ... and who flies the scout drones and which bays hold one (the Cyclops Drone Bay), the bays of a saved world rebuilt first
        cyclopsDroneArbiter.RestoreFrom(entityRegistry);
        await cyclopsDroneArbiter.SendSnapshotAsync(context.Sender.SessionId);
        // ... and which custom player models the others wear (docs/coop/PLAYER_MODELS.md); this player announces his own choice after his initial sync
        await playerModelLedger.SendSnapshotAsync(context.Sender.SessionId);

        joiningManager.SyncFinishedCallback?.Invoke();
    }
}
