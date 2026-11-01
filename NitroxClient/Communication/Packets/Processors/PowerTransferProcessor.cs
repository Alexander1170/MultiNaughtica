using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server's steps of a power transfer between vehicles that different players simulate (<see cref="PowerShare" />): an offer to the player who simulates the giver, the delivery to the player who
/// simulates the receiver, and the refund of energy nobody could use. The requests, grants and returns are what this client sends; one that arrives here is not the server's and is ignored.
/// </summary>
internal sealed class PowerTransferProcessor : IClientPacketProcessor<PowerTransfer>
{
    public Task Process(ClientProcessorContext context, PowerTransfer packet)
    {
        switch (packet.Kind)
        {
            case PowerTransferKind.Offer:
                PowerShare.OnOffer(packet);
                break;
            case PowerTransferKind.Delivery:
                PowerShare.OnDelivery(packet);
                break;
            case PowerTransferKind.Refund:
                PowerShare.OnRefund(packet);
                break;
            default:
                Log.Warn($"[VehicleUpgrades] ignored a power transfer packet of the kind {packet.Kind}");
                break;
        }
        return Task.CompletedTask;
    }
}
