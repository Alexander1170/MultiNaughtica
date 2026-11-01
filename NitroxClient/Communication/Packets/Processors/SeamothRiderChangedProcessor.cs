using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>The server says who holds on to the bar of which Seamoth (<see cref="RiderBars.OnPacket" />).</summary>
internal sealed class SeamothRiderChangedProcessor : IClientPacketProcessor<SeamothRiderChanged>
{
    public Task Process(ClientProcessorContext context, SeamothRiderChanged packet)
    {
        RiderBars.OnPacket(packet);
        return Task.CompletedTask;
    }
}
