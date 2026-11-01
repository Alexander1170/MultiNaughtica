#if DEBUG
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness (bases audit): writes to the SERVER LOG every base the server knows (<c>/bases</c>) with all its descendants, one line per entity, so that
///     a harness script that is not part of this repository can compare them with the clients' <c>bases</c> console command.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class BasesCommand(EntityRegistry entityRegistry, SimulationOwnershipData simulationOwnershipData, ILogger<BasesCommand> logger) : ICommandHandler
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly ILogger<BasesCommand> logger = logger;

    [Description("Test harness: logs every base the server knows with all its descendants")]
    public async Task Execute(ICommandContext context)
    {
        List<BuildEntity> bases = entityRegistry.GetEntities<BuildEntity>();
        foreach (BuildEntity buildEntity in bases)
        {
            string baseId = buildEntity.Id.ToString()[..8];
            string holder = simulationOwnershipData.TryGetLock(buildEntity.Id, out SimulationOwnershipData.PlayerLock playerLock) ? $"{playerLock.Player.Name} {playerLock.LockType}" : "nobody";
            logger.ZLogInformation($"[SyncHarness] serverbases: base {baseId} at {buildEntity.Transform.Position} operation {buildEntity.OperationId} children {buildEntity.ChildEntities.Count} simulated by {holder}");
            Log(buildEntity, baseId, 0);
        }
        logger.ZLogInformation($"[SyncHarness] serverbases: {bases.Count} bases");
        await context.ReplyAsync("see the server log");
    }

    private void Log(Entity parent, string baseId, int depth)
    {
        foreach (Entity child in parent.ChildEntities)
        {
            string position = child is WorldEntity worldEntity ? worldEntity.Transform.Position.ToString() : "-";
            string metadata = child.Metadata?.ToString() ?? "none";
            if (metadata.Length > 160)
            {
                metadata = $"{metadata[..160]}...";
            }
            logger.ZLogInformation($"[SyncHarness] serverbases: base {baseId} {new string(' ', depth)}{child.GetType().Name} {child.TechType} id {child.Id.ToString()[..8]} parent {child.ParentId?.ToString()[..8]} at {position} children {child.ChildEntities.Count} metadata {metadata}");
            Log(child, baseId, depth + 1);
        }
    }
}
#endif
