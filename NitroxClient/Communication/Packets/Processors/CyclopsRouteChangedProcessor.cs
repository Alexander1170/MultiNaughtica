using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says that the route of a Cyclops changed: a waypoint was appended or taken out, the route was cleared, or the Cyclops reached a waypoint (<see cref="NavRoute.OnPacket" />); it also sends the route as it
/// stands to a client that joins, and answers a refused waypoint with a "removed" and a refused advance with the "advanced" the server has.
/// </summary>
internal sealed class CyclopsRouteChangedProcessor : IClientPacketProcessor<CyclopsRouteChanged>
{
    public Task Process(ClientProcessorContext context, CyclopsRouteChanged packet)
    {
        NavRoute.OnPacket(packet);
        return Task.CompletedTask;
    }
}
