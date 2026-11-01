using Nitrox.Server.Subnautica.Models.Packets.Processors.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class ReaperGrabVehicleProcessor(
    PlayerManager playerManager,
    EntityRegistry entityRegistry
) : TransmitIfCanSeePacketProcessor<ReaperGrabVehicle>(playerManager, entityRegistry)
{
    public override async Task Process(AuthProcessorContext context, ReaperGrabVehicle packet) => await TransmitIfCanSeeEntitiesAsync(context, packet, [packet.ReaperId, packet.VehicleId]);
}
