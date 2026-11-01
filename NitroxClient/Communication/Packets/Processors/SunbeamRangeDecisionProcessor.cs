using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server decided the outcome of the Sunbeam range check (ST1h): triggers the one PDA goal everybody plays, which is what vanilla does at the end of the range check but
/// with the local player's position replaced by the decision. It then travels like any goal (every client runs it, the first to finish reports it). A decision the client
/// already acted on (it also gets the answer to its own late proposal) changes nothing, the scheduler refuses a goal that is scheduled or completed.
/// </summary>
internal sealed class SunbeamRangeDecisionProcessor : IClientPacketProcessor<SunbeamRangeDecision>
{
    public Task Process(ClientProcessorContext context, SunbeamRangeDecision packet)
    {
        // A joining client gets the goals that already ran from the initial sync
        StoryGoalCustomEventHandler eventHandler = StoryGoalCustomEventHandler.main;
        if (!eventHandler || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return Task.CompletedTask;
        }

        StoryGoal goal = packet.InRange ? eventHandler.sunbeamDestroyEventInRange : eventHandler.sunbeamDestroyEventOutOfRange;
        Log.Debug($"The Sunbeam range check was decided: {(packet.InRange ? "in range" : "out of range")}, triggering {goal.key}");
        goal.Trigger();
        return Task.CompletedTask;
    }
}
