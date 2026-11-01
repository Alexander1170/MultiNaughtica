using System.Collections;
using System.Linq;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Story;
using UnityEngine;
using UWE;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class ScheduleProcessor : IClientPacketProcessor<Schedule>
{
    public Task Process(ClientProcessorContext context, Schedule schedulePacket)
    {
        // A client that is still joining gets the packet while its world scene loads (the scheduler, the story manager and the day night cycle do not exist yet): wait for the end of the join
        if (StoryPacketRules.ShouldWait(IsInitialSyncCompleted(), StorySystemsLoaded()))
        {
            CoroutineHost.StartCoroutine(ApplyWhenLoaded(schedulePacket, StoryPacketSession.Generation));
            return Task.CompletedTask;
        }

        Apply(schedulePacket);
        return Task.CompletedTask;
    }

    private static bool IsInitialSyncCompleted() => Multiplayer.Main != null && Multiplayer.Main.InitialSyncCompleted;

    private static bool StorySystemsLoaded() => StoryGoalScheduler.main != null && StoryGoalManager.main != null && DayNightCycle.main != null;

    private static IEnumerator ApplyWhenLoaded(Schedule schedulePacket, int generation)
    {
        float waited = 0f;
        while (StoryPacketRules.ShouldWait(IsInitialSyncCompleted(), StorySystemsLoaded()))
        {
            if (StoryPacketRules.SessionEnded(generation, StoryPacketSession.Generation))
            {
                Log.Debug($"[{nameof(ScheduleProcessor)}] The goal {schedulePacket.Key} that waited for the end of the join is dropped: its session ended");
                yield break;
            }

            if (StoryPacketRules.GiveUp(waited))
            {
                Log.Warn($"[{nameof(ScheduleProcessor)}] The goal {schedulePacket.Key} was not scheduled: the client did not finish joining within {StoryPacketRules.WAIT_SECONDS:F0} s");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.25f);
            waited += 0.25f;
        }

        Apply(schedulePacket);
    }

    private static void Apply(Schedule schedulePacket)
    {
        ScheduledGoal goal = new()
        {
            goalKey = schedulePacket.Key,
            goalType = (Story.GoalType)schedulePacket.Type,
            timeExecute = schedulePacket.TimeExecute
        };
        if (ShouldSchedule(goal))
        {
            StoryGoalScheduler.main.schedule.Add(goal);
        }
        Log.Debug($"Processed a Schedule packet [Key: {goal.goalKey}, Type: {goal.goalType}, TimeExecute: {goal.timeExecute}]");
    }

    private static bool ShouldSchedule(ScheduledGoal goal)
    {
        return goal.timeExecute >= DayNightCycle.main.timePassedAsDouble && !IsAlreadyKnown(goal.goalKey);
    }

    private static bool IsAlreadyKnown(string goalKey)
    {
        return StoryGoalScheduler.main.schedule.Any(g => g.goalKey == goalKey) || //  Scheduled
               StoryGoalManager.main.completedGoals.Contains(goalKey); // Completed
    }
}
