using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server says a tow was hooked or ended (<see cref="VehicleTows.OnPacket" />): every client draws the cable of the tows that are on, and the towing player's client lets go when the server ended
/// its tow.
/// </summary>
internal sealed class VehicleTowChangedProcessor : IClientPacketProcessor<VehicleTowChanged>
{
    public Task Process(ClientProcessorContext context, VehicleTowChanged packet)
    {
        VehicleTows.OnPacket(packet);
        return Task.CompletedTask;
    }
}
