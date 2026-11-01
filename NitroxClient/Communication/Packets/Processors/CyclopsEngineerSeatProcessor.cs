using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says who sits at the engineer console of a Cyclops, or refuses this client's request (<see cref="EngineerSeats.OnPacket" />).
/// </summary>
internal sealed class CyclopsEngineerSeatProcessor : IClientPacketProcessor<CyclopsEngineerSeat>
{
    public Task Process(ClientProcessorContext context, CyclopsEngineerSeat packet)
    {
        EngineerSeats.OnPacket(packet);
        return Task.CompletedTask;
    }
}
