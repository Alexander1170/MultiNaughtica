using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Gunner;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server asks this client (it simulates the Cyclops) to pay for a shot of the gunner, or tells the gunner what the answer was (<see cref="GunnerFire.OnPacket" />).
/// </summary>
internal sealed class CyclopsGunnerFireProcessor : IClientPacketProcessor<CyclopsGunnerFire>
{
    public Task Process(ClientProcessorContext context, CyclopsGunnerFire packet)
    {
        GunnerFire.OnPacket(packet);
        return Task.CompletedTask;
    }
}
