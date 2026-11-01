using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>Where a remote rider looks (<see cref="RiderBars.OnLook" />).</summary>
internal sealed class SeamothRiderLookProcessor : IClientPacketProcessor<SeamothRiderLook>
{
    public Task Process(ClientProcessorContext context, SeamothRiderLook packet)
    {
        RiderBars.OnLook(packet);
        return Task.CompletedTask;
    }
}
