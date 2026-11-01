using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says that a mark was placed on the map of a Cyclops' navigator console, or removed, or that the marks were cleared (<see cref="NavMarks.OnPacket" />); it also sends the marks that are on to a
/// client that joins, and answers a refused placement with a "removed".
/// </summary>
internal sealed class CyclopsNavChangedProcessor : IClientPacketProcessor<CyclopsNavChanged>
{
    public Task Process(ClientProcessorContext context, CyclopsNavChanged packet)
    {
        NavMarks.OnPacket(packet);
        return Task.CompletedTask;
    }
}
