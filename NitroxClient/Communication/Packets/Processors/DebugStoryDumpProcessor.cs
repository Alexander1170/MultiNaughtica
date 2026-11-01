using System;
using System.IO;
using System.Linq;
using System.Text;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors.Core;
using Story;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness: writes this client's story state to <c>story-&lt;player&gt;.txt</c> on request of the server (/storydump).
///     One tab separated record per line: <c>completed key</c>, <c>radio index key</c>, <c>scheduled key type time</c>, <c>pdalog key</c>,
///     <c>encyclopedia key</c>, <c>flag name value</c>. Only acts in Debug builds.
/// </summary>
internal sealed class DebugStoryDumpProcessor(IMultiplayerSession multiplayerSession) : IClientPacketProcessor<DebugStoryDump>
{
    private readonly IMultiplayerSession multiplayerSession = multiplayerSession;

    public Task Process(ClientProcessorContext context, DebugStoryDump packet)
    {
#if DEBUG
        string player = multiplayerSession.AuthenticationContext?.Username ?? "unknown";
        StringBuilder text = new();
        text.Append($"# player={player} label={packet.Label} utc={DateTime.UtcNow:o}\n");

        StoryGoalManager goalManager = StoryGoalManager.main;
        if (goalManager)
        {
            foreach (string key in goalManager.completedGoals.OrderBy(key => key, StringComparer.Ordinal))
            {
                text.Append($"completed\t{key}\n");
            }
            for (int i = 0; i < goalManager.pendingRadioMessages.Count; i++)
            {
                text.Append($"radio\t{i}\t{goalManager.pendingRadioMessages[i]}\n");
            }
        }

        StoryGoalScheduler scheduler = StoryGoalScheduler.main;
        if (scheduler)
        {
            foreach (ScheduledGoal goal in scheduler.schedule.OrderBy(goal => goal.goalKey, StringComparer.Ordinal))
            {
                text.Append($"scheduled\t{goal.goalKey}\t{(int)goal.goalType}\t{goal.timeExecute:F1}\n");
            }
        }

        foreach (string key in PDALog.entries.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            text.Append($"pdalog\t{key}\n");
        }
        foreach (string key in PDAEncyclopedia.entries.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            text.Append($"encyclopedia\t{key}\n");
        }

        StoryGoalCustomEventHandler eventHandler = StoryGoalCustomEventHandler.main;
        if (eventHandler)
        {
            text.Append($"flag\tgunDisabled\t{eventHandler.gunDisabled}\n");
            text.Append($"flag\tcountdownActive\t{eventHandler.countdownActive}\n");
        }

        // Lifepod and its radio (ST1d): a player who joined after they were repaired must not see them damaged again
        EscapePod pod = EscapePod.main;
        if (pod)
        {
            text.Append($"flag\tpodHealth\t{pod.liveMixin.health:F0}\n");
            if (pod.radioSpawner && pod.radioSpawner.spawnedObj && pod.radioSpawner.spawnedObj.TryGetComponent(out Radio radio))
            {
                text.Append($"flag\tradioHealth\t{radio.liveMixin.health:F0}\n");
            }
        }

        // The rest of the observable story state (Aurora and its leaks, infection, doors, teleporters, the rocket, ...), see StoryDumpFlags
        StoryDumpFlags.Append(text);

        string path = Path.Combine(Log.LogDirectory, $"story-{player}.txt");
        File.WriteAllText(path, text.ToString());
        Log.Info($"[SyncHarness] story state written to {path}");
#endif
        return Task.CompletedTask;
    }
}
