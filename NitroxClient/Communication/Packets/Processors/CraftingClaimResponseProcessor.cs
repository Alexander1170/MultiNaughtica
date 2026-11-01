using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The server's answer to a claim for crafting from lockers (<see cref="LockerCrafting.OnResponse" />): a granted answer starts the craft, any other cancels it. It arrives after the
///     <c>EntityDestroyed</c> packets of the items that were claimed.
/// </summary>
internal sealed class CraftingClaimResponseProcessor : IClientPacketProcessor<CraftingClaimResponse>
{
    public Task Process(ClientProcessorContext context, CraftingClaimResponse packet)
    {
        LockerCrafting.OnResponse(packet);
        return Task.CompletedTask;
    }
}
