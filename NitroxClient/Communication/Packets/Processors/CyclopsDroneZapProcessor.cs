using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Drone;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says that a zap of a Scout Drone happens (everybody plays it and applies it to what they simulate) or refuses this client's request (<see cref="DroneZap.OnPacket" />).
/// </summary>
internal sealed class CyclopsDroneZapProcessor : IClientPacketProcessor<CyclopsDroneZap>
{
    public Task Process(ClientProcessorContext context, CyclopsDroneZap packet)
    {
        DroneZap.OnPacket(packet);
        return Task.CompletedTask;
    }
}
