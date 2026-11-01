#if DEBUG
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;
using Nitrox.Server.Subnautica.Models.Helper;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Live check "batch regeneration" (the design notes (not part of this repository)): writes to the SERVER LOG what the registry holds and whether a batch was generated twice. <c>/registryaudit</c> logs
///     <c>[WorldAudit] registry entities: N, duplicate ids: D, doubled top-level: T (...), generated batches in the record: B</c>; <c>/registryaudit batches</c> also logs one
///     <c>[WorldAudit] batch x,y,z top-level=N</c> line per batch, which a script compares before and after a restart (a number that moved says the batch was generated again);
///     <c>/registryaudit idstream</c> logs, per generated batch, how many of the first 64 ids of its generator are in the registry.
///     The same first line is logged by every world load.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class RegistryAuditCommand(EntityRegistry entityRegistry, BatchEntitySpawner batchEntitySpawner, IOptions<SubnauticaServerOptions> options, ILogger<RegistryAuditCommand> logger) : ICommandHandler<string>
{
    private const int ID_STREAM_SAMPLE = 64;
    private readonly BatchEntitySpawner batchEntitySpawner = batchEntitySpawner;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<RegistryAuditCommand> logger = logger;

    [Description("Test harness: logs the registry audit (entities, duplicate ids, doubled top-level entities); 'batches' also logs the top-level entity count of every batch, 'idstream' how many ids of each batch's generator are registered")]
    public async Task Execute(ICommandContext context, [Description("batches | idstream")] string detail = "")
    {
        List<Entity> all = entityRegistry.GetAllEntities();
        WorldAuditResult audit = WorldAudit.Run(all, DuplicateEntityRepair.IsMobileCreature);
        int parsedBatches = batchEntitySpawner.SerializableParsedBatches.Count;
        string summary = $"[WorldAudit] {audit}, generated batches in the record: {parsedBatches}";
        logger.ZLogInformation($"{summary}");

        if (detail.Equals("batches", StringComparison.OrdinalIgnoreCase))
        {
            foreach (KeyValuePair<NitroxInt3, int> batch in WorldAudit.CountTopLevelByBatch(all))
            {
                logger.ZLogInformation($"[WorldAudit] batch {batch.Key.X},{batch.Key.Y},{batch.Key.Z} top-level={batch.Value}");
            }
        }
        else if (detail.Equals("idstream", StringComparison.OrdinalIgnoreCase))
        {
            // Of every generated batch: how many of the first ids that its generator produces are registered. A batch that was generated here has most of them; a batch with none means that the
            // registry holds ids that did not come from that batch's generator (the open question of the design notes (not part of this repository))
            HashSet<NitroxId> registered = [.. all.Select(entity => entity.Id)];
            int withIds = 0;
            int withoutIds = 0;
            foreach (NitroxInt3 batchId in batchEntitySpawner.SerializableParsedBatches)
            {
                DeterministicGenerator generator = new(options.Value.Seed, batchId.ToString());
                int found = 0;
                for (int i = 0; i < ID_STREAM_SAMPLE; i++)
                {
                    if (registered.Contains(generator.NextId()))
                    {
                        found++;
                    }
                }
                logger.ZLogInformation($"[WorldAudit] idstream batch {batchId.X},{batchId.Y},{batchId.Z} registered={found} of {ID_STREAM_SAMPLE}");
                if (found > 0)
                {
                    withIds++;
                }
                else
                {
                    withoutIds++;
                }
            }
            logger.ZLogInformation($"[WorldAudit] idstream: batches with registered ids of their generator={withIds}, without={withoutIds}");
        }
        await context.ReplyAsync(summary);
    }
}
#endif
