using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class StoryGoalExecutedProcessor(IPacketSender packetSender, StoryManager storyManager, StoryScheduler storyScheduler, PdaManager pdaManager, ILogger<StoryGoalExecutedProcessor> logger)
    : IAuthPacketProcessor<StoryGoalExecuted>
{
    private readonly IPacketSender packetSender = packetSender;
    private readonly StoryManager storyManager = storyManager;
    private readonly StoryScheduler storyScheduler = storyScheduler;
    private readonly PdaManager pdaManager = pdaManager;
    private readonly ILogger<StoryGoalExecutedProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, StoryGoalExecuted packet)
    {
        logger.ZLogDebug($"Processing packet: {packet}");
        // The switch is structure is similar to StoryGoal.Execute()
        bool added = storyManager.AddCompletedStory(packet.Key);

        // Vanilla StoryGoalCustomEventHandler.NotifyGoalComplete removes this message from the radio queue of every client when the gun gets disabled
        if (string.Equals(packet.Key, "Goal_Disable_Gun", StringComparison.OrdinalIgnoreCase))
        {
            storyManager.StoryGoalData.RadioQueue.RemoveWhere(message => string.Equals(message, "RadioSunbeam4", StringComparison.OrdinalIgnoreCase));
        }

        switch (packet.Type)
        {
            case StoryGoalExecuted.EventType.RADIO:
                if (added)
                {
                    storyManager.QueueRadioStory(packet.Key);
                }
                break;
            case StoryGoalExecuted.EventType.PDA:
                if (packet.Timestamp.HasValue)
                {
                    pdaManager.AddPDALogEntry(new(packet.Key, packet.Timestamp.Value));
                }
                break;
        }
        await storyScheduler.UnscheduleStory(packet.Key);

        // Both players run the same scheduled goals: the other players already got this goal from whoever reported it first
        // (except the goal of a played radio message, which the play itself completed on the server first: StoryRelayRules)
        if (StoryRelayRules.ShouldRelay(packet.Key, packet.Type, added))
        {
            await context.SendToOthersAsync(packet);
        }
    }
}
