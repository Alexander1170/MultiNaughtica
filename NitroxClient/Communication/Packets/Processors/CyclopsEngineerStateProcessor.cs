using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says what the engineer has set on a Cyclops: the route and the seconds that are left of the cooldowns (<see cref="EngineerState.Apply" />).
/// </summary>
internal sealed class CyclopsEngineerStateProcessor : IClientPacketProcessor<CyclopsEngineerState>
{
    public Task Process(ClientProcessorContext context, CyclopsEngineerState packet)
    {
        EngineerState.Apply(packet);
        return Task.CompletedTask;
    }
}
