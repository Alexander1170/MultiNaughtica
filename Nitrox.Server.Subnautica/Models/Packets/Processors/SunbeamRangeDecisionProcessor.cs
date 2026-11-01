using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Every client proposes the outcome of the Sunbeam range check when the goal completes on it. The first proposal is the decision and goes to everybody (the proposer included, it
///     has not triggered a goal itself), a later one is answered with the decision to its sender only, in case that client missed it (<see cref="SunbeamRangeArbiter" />).
/// </summary>
internal sealed class SunbeamRangeDecisionProcessor(StoryManager storyManager, ILogger<SunbeamRangeDecisionProcessor> logger) : IAuthPacketProcessor<SunbeamRangeDecision>
{
    private readonly StoryManager storyManager = storyManager;
    private readonly ILogger<SunbeamRangeDecisionProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, SunbeamRangeDecision packet)
    {
        bool inRange = storyManager.SunbeamRange.Decide(packet.InRange,
                                                        storyManager.ContainsCompletedStory(SunbeamRangeDecision.IN_RANGE_GOAL),
                                                        storyManager.ContainsCompletedStory(SunbeamRangeDecision.OUT_OF_RANGE_GOAL),
                                                        out bool isNewDecision);
        SunbeamRangeDecision decision = new(inRange);
        if (!isNewDecision)
        {
            logger.ZLogDebug($"Answering {context.Sender.Name} with the Sunbeam range decision that was already made: {decision}");
            await context.ReplyAsync(decision);
            return;
        }

        logger.ZLogInformation($"Sunbeam range check decided by the proposal of {context.Sender.Name}: {(inRange ? "a player is at the landing site" : "nobody is at the landing site")}");
        await context.SendToAllAsync(decision);
    }
}
