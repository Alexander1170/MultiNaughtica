using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Drone;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says which drone the bay of a Cyclops holds and whether it is in the dock (<see cref="DroneBayState.OnPacket" />); the bay carries it out.
/// </summary>
internal sealed class CyclopsDroneStateProcessor : IClientPacketProcessor<CyclopsDroneState>
{
    public Task Process(ClientProcessorContext context, CyclopsDroneState packet)
    {
        DroneBayState.OnPacket(packet);
        return Task.CompletedTask;
    }
}
