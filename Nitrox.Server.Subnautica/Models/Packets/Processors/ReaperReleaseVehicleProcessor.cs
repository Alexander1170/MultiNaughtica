using Nitrox.Server.Subnautica.Models.Packets.Processors.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class ReaperReleaseVehicleProcessor(
    PlayerManager playerManager,
    EntityRegistry entityRegistry
) : TransmitIfCanSeePacketProcessor<ReaperReleaseVehicle>(playerManager, entityRegistry)
{
    public override async Task Process(AuthProcessorContext context, ReaperReleaseVehicle packet) => await TransmitIfCanSeeEntitiesAsync(context, packet, [packet.ReaperId]);
}
