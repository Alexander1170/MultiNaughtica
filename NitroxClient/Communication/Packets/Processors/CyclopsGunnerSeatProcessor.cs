using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Gunner;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says who sits at the gunner desk of a Cyclops, or refuses this client's request (<see cref="GunnerSeats.OnPacket" />).
/// </summary>
internal sealed class CyclopsGunnerSeatProcessor : IClientPacketProcessor<CyclopsGunnerSeat>
{
    public Task Process(ClientProcessorContext context, CyclopsGunnerSeat packet)
    {
        GunnerSeats.OnPacket(packet);
        return Task.CompletedTask;
    }
}
