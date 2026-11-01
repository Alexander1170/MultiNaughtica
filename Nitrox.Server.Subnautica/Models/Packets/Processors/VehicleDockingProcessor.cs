using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

sealed class VehicleDockingProcessor : IAuthPacketProcessor<VehicleDocking>
{
    private readonly IPacketSender packetSender;
    private readonly EntityRegistry entityRegistry;
    private readonly ILogger<VehicleDockingProcessor> logger;
    private readonly SeamothRiderArbiter seamothRiderArbiter;
    private readonly VehicleTowRegistry vehicleTowRegistry;

    public VehicleDockingProcessor(IPacketSender packetSender, EntityRegistry entityRegistry, ILogger<VehicleDockingProcessor> logger, SeamothRiderArbiter seamothRiderArbiter, VehicleTowRegistry vehicleTowRegistry)
    {
        this.packetSender = packetSender;
        this.entityRegistry = entityRegistry;
        this.logger = logger;
        this.seamothRiderArbiter = seamothRiderArbiter;
        this.vehicleTowRegistry = vehicleTowRegistry;
    }

    public async Task Process(AuthProcessorContext context, VehicleDocking packet)
    {
        // A Seamoth that docks sheds the player who holds on to its bar (the rider bar) and a vehicle that docks lets go of its tow: before anything below can return early, because the clients dock
        // the vehicle whatever the server finds
        await seamothRiderArbiter.BroadcastEndedAsync(seamothRiderArbiter.VehicleGone(packet.VehicleId));
        await vehicleTowRegistry.BroadcastEndedAsync(vehicleTowRegistry.EntityDestroyed(packet.VehicleId));

        if (!entityRegistry.TryGetEntityById(packet.VehicleId, out Entity vehicleEntity))
        {
            logger.ZLogError($"Unable to find vehicle to dock {packet.VehicleId}");
            return;
        }

        if (!entityRegistry.TryGetEntityById(packet.DockId, out Entity dockEntity))
        {
            logger.ZLogError($"Unable to find dock {packet.DockId} for docking vehicle {packet.VehicleId}");
            return;
        }

        entityRegistry.ReparentEntity(vehicleEntity, dockEntity);

        await context.SendToOthersAsync(packet);
    }
}
