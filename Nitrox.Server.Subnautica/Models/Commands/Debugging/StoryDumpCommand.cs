#if DEBUG
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Unlockables;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: writes the server's story state to <c>story-server.txt</c> in the Nitrox logs folder and asks every client to do the
///     same (<c>story-&lt;player&gt;.txt</c>). Same record format as the client dump, compared by a harness script that is not part of this repository.
/// </summary>
[Alias("story-dump")]
[RequiresPermission(Perms.ADMIN)]
internal sealed class StoryDumpCommand(StoryManager storyManager, StoryScheduler storyScheduler, PdaManager pdaManager, EntityRegistry entityRegistry, PlayerManager playerManager, ILogger<StoryDumpCommand> logger) : ICommandHandler<string>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly PlayerManager playerManager = playerManager;
    private readonly StoryManager storyManager = storyManager;
    private readonly StoryScheduler storyScheduler = storyScheduler;
    private readonly PdaManager pdaManager = pdaManager;
    private readonly ILogger<StoryDumpCommand> logger = logger;

    [Description("Test harness: writes the story state of the server and of every client to the logs folder")]
    public async Task Execute(ICommandContext context, [Description("label written into the dumps")] string label = "dump")
    {
        StringBuilder text = new();
        text.Append($"# player=server label={label} utc={DateTime.UtcNow:o}\n");
        foreach (string key in storyManager.StoryGoalData.CompletedGoals.OrderBy(key => key, StringComparer.Ordinal))
        {
            text.Append($"completed\t{key}\n");
        }
        int index = 0;
        foreach (string key in storyManager.StoryGoalData.RadioQueue.Clone())
        {
            text.Append($"radio\t{index++}\t{key}\n");
        }
        foreach (NitroxScheduledGoal goal in storyScheduler.GetScheduledStories().OrderBy(goal => goal.GoalKey, StringComparer.Ordinal))
        {
            text.Append($"scheduled\t{goal.GoalKey}\t{goal.GoalType}\t{goal.TimeExecute:F1}\n");
        }
        PdaStateData pdaState = pdaManager.GetPdaStateCopy();
        foreach (string key in pdaState.PdaLog.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal))
        {
            text.Append($"pdalog\t{key}\n");
        }
        foreach (string key in pdaState.EncyclopediaEntries.OrderBy(key => key, StringComparer.Ordinal))
        {
            text.Append($"encyclopedia\t{key}\n");
        }

        AppendEntityFlags(text);

        string path = Path.Combine(NitroxDirectory.LogsPath, "story-server.txt");
        await File.WriteAllTextAsync(path, text.ToString());
        logger.ZLogInformation($"[SyncHarness] story dump '{label}' written to {path}");

        await context.SendToAllAsync(new DebugStoryDump(label));
        await context.ReplyAsync($"Story dump '{label}' written by the server and requested from all clients");
    }

    /// <summary>
    ///     The state of entities that the server keeps, in the format and under the names of the client dump (StoryDumpFlags): what a joiner or a restarted world gets.
    ///     The infection of every player is what the server stores for him (<c>player.infection.&lt;name&gt;</c>), the leaks and the rocket are the metadata of their entities.
    /// </summary>
    private void AppendEntityFlags(StringBuilder text)
    {
        List<string> leaks = [];
        foreach (RadiationLeakEntity leak in entityRegistry.GetEntities<RadiationLeakEntity>())
        {
            leaks.Add($"{leak.Id.ToString().Substring(0, 8)}={(leak.Metadata as RadiationMetadata)?.Health ?? 0f:F0}");
        }
        if (leaks.Count > 0)
        {
            leaks.Sort(StringComparer.Ordinal);
            text.Append($"flag\tleakCount\t{leaks.Count}\n");
            text.Append($"flag\tleakHealth\t{string.Join(",", leaks)}\n");
        }

        foreach (Entity entity in entityRegistry.GetAllEntities())
        {
            if (entity.Metadata is RocketMetadata rocket)
            {
                text.Append($"flag\tent.rocketStage\t{rocket.CurrentStage}\n");
                text.Append($"flag\tent.rocketElevator\t{rocket.ElevatorState}@{rocket.ElevatorPosition:F1}\n");
                text.Append($"flag\tent.rocketPreflight\t{string.Join(",", rocket.PreflightChecks.Select(check => check.ToString()).OrderBy(check => check, StringComparer.Ordinal))}\n");
            }
        }

        foreach (Player player in playerManager.GetAllPlayers())
        {
            text.Append($"flag\tplayer.infection.{player.Name}\t{player.Stats?.InfectionAmount ?? 0f:F2}\n");
        }
    }
}
#endif
