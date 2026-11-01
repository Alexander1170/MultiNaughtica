using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says that the navigator of a Cyclops asked for a sonar ping (this client simulates the Cyclops: it fires it and pays for it), or the result of a ping (<see cref="NavigatorSonar.OnPacket" />).
/// </summary>
internal sealed class CyclopsSonarPingProcessor : IClientPacketProcessor<CyclopsSonarPing>
{
    public Task Process(ClientProcessorContext context, CyclopsSonarPing packet)
    {
        NavigatorSonar.OnPacket(packet);
        return Task.CompletedTask;
    }
}
