using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Drone;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says who sits at the drone console of a Cyclops, or refuses this client's request (<see cref="DroneSeats.OnPacket" />).
/// </summary>
internal sealed class CyclopsDroneSeatProcessor : IClientPacketProcessor<CyclopsDroneSeat>
{
    public Task Process(ClientProcessorContext context, CyclopsDroneSeat packet)
    {
        DroneSeats.OnPacket(packet);
        return Task.CompletedTask;
    }
}
