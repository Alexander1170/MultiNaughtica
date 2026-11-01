using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PDAEncyclopediaEntryAddProcessor(PdaManager pdaManager) : IAuthPacketProcessor<PDAEncyclopediaEntryAdd>
{
    private readonly PdaManager pdaManager = pdaManager;

    public async Task Process(AuthProcessorContext context, PDAEncyclopediaEntryAdd packet)
    {
        // Both players report the same unlock: the others already got it from whoever reported it first
        if (pdaManager.AddEncyclopediaEntry(packet.Key, packet.Content))
        {
            await context.SendToOthersAsync(packet);
        }
    }
}
