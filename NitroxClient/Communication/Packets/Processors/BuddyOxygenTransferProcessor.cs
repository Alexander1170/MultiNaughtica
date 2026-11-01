using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server's half of the buddy oxygen (docs/coop/BUDDY_OXYGEN.md): a giver's step arrives for this player's tank (<see cref="BuddyOxygenKind.Deliver" />), or the server says how this player's own step
/// ended (<see cref="BuddyOxygenKind.Settle" />). The work is in <see cref="BuddyOxygenShare" />.
/// </summary>
internal sealed class BuddyOxygenTransferProcessor : IClientPacketProcessor<BuddyOxygenTransfer>
{
    public Task Process(ClientProcessorContext context, BuddyOxygenTransfer packet)
    {
        BuddyOxygenShare.OnPacket(packet);
        return Task.CompletedTask;
    }
}
