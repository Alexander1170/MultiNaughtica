using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server asks this client (it simulates the Cyclops) to carry out an overdrive or a vent of the engineer, or tells the engineer what the answer was (<see cref="EngineerActions.OnPacket" />).
/// </summary>
internal sealed class CyclopsEngineerActionProcessor : IClientPacketProcessor<CyclopsEngineerAction>
{
    public Task Process(ClientProcessorContext context, CyclopsEngineerAction packet)
    {
        EngineerActions.OnPacket(packet);
        return Task.CompletedTask;
    }
}
