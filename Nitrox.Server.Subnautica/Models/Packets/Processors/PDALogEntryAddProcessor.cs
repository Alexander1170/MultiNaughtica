using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PDALogEntryAddProcessor(PdaManager pdaManager, StoryScheduler storyScheduler) : IAuthPacketProcessor<PDALogEntryAdd>
{
    private readonly PdaManager pdaManager = pdaManager;
    private readonly StoryScheduler storyScheduler = storyScheduler;

    public async Task Process(AuthProcessorContext context, PDALogEntryAdd packet)
    {
        bool isNewEntry = pdaManager.AddPDALogEntry(new PDALogEntry(packet.Key, packet.Timestamp));
        if (storyScheduler.ContainsScheduledStory(packet.Key))
        {
            await storyScheduler.UnscheduleStory(packet.Key);
        }
        // Both players report the same unlock: the others already got it from whoever reported it first
        if (isNewEntry)
        {
            await context.SendToOthersAsync(packet);
        }
    }
}
