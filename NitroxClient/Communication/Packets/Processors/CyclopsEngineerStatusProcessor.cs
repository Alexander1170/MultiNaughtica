using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The simulator of a Cyclops tells the engineer at its console the engine heat (<see cref="EngineerHeat.OnStatus" />).
/// </summary>
internal sealed class CyclopsEngineerStatusProcessor : IClientPacketProcessor<CyclopsEngineerStatus>
{
    public Task Process(ClientProcessorContext context, CyclopsEngineerStatus packet)
    {
        EngineerHeat.OnStatus(packet);
        return Task.CompletedTask;
    }
}
