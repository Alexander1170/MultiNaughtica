using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Helper;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

/// <summary>
///     Puts the record of generated batches of a save right when it is shorter than the entity data: coop.3 saved only the batches that someone visited since the server started (see
///     <see cref="ParsedBatchBook" />), so every save written after a restart lost the batches generated earlier. A batch that is not in the record is generated again when someone visits it,
///     and what it generates is not what the world holds any more (ids shifted by a databox, states reset).
/// </summary>
/// <remarks>
///     A batch counts as generated when the entities say so, and the evidence must be hard: a batch wrongly counted as generated is never generated and stays empty. Three kinds of evidence:
///     <list type="number">
///         <item>
///             An id of the batch's own generator. The ids of a batch are the first <c>NextId()</c> calls of <c>DeterministicGenerator(seed, batchId.ToString())</c>; a registered entity
///             whose id is among the first <see cref="ID_STREAM_DEPTH" /> of them was generated for that batch, wherever it stands now. Exact (a 128-bit id), checked for every batch that
///             holds an entity. In the world "Test" it recognises 340 of the 346 batches that the two server logs show generated and none of the 25 batches that hold only creatures that swam over a
///             border. All three kinds together put 344 of the 346 into the record (the 2 others hold no entity any more), and no batch that no log shows generated.
///         </item>
///         <item>A <see cref="SerializedWorldEntity" /> names the batch it was generated for.</item>
///         <item>
///             A batch that holds at least <see cref="MIN_SERVER_ENTITIES" /> top-level entities that the server spawned there. A fallback for a seed that does not match (a count is not proof:
///             creatures swim over the border of an unexplored batch, in the world "Test" 22 batches that no server log shows generated hold between 1 and 5 such strays and one holds 11).
///         </item>
///     </list>
///     A small batch that is missed is only generated again when it is visited, which is harmless: the registry keeps what it holds (see <c>EntityRegistry.AddEntitiesKeepingExisting</c>) and the
///     repair of duplicates takes out the copies that the shifted ids of a second run leave.
/// </remarks>
internal static class ParsedBatchRepair
{
    internal const int MIN_SERVER_ENTITIES = 16;

    /// <summary>
    ///     How many ids of the generator of a batch are looked at. The largest index seen in the world "Test" is 5407 (a batch of about 5400 entities).
    /// </summary>
    internal const int ID_STREAM_DEPTH = 32768;

    internal sealed record Derivation(List<NitroxInt3> All, List<NitroxInt3> Added);

    /// <param name="entities">The flat list of the entity data of the save.</param>
    /// <param name="alreadyParsed">The record of the save; its batches stay in it, in their order.</param>
    /// <param name="generatedIds">The ids the generator of a batch produces, in order (<see cref="BatchIdStream" />); null when the seed of the world is not known.</param>
    internal static Derivation Derive(IEnumerable<Entity> entities, IEnumerable<NitroxInt3>? alreadyParsed, Func<NitroxInt3, IEnumerable<NitroxId>>? generatedIds = null)
    {
        List<NitroxInt3> all = [];
        HashSet<NitroxInt3> known = [];
        if (alreadyParsed != null)
        {
            foreach (NitroxInt3 batchId in alreadyParsed)
            {
                if (known.Add(batchId))
                {
                    all.Add(batchId);
                }
            }
        }

        List<Entity> flat = [.. entities];
        HashSet<NitroxInt3> evidence = [];
        HashSet<NitroxInt3> candidates = [];
        Dictionary<NitroxInt3, int> serverSpawned = [];
        foreach (Entity entity in flat)
        {
            if (entity.ParentId != null || entity is not WorldEntity world || world.Transform == null || world is CellRootEntity or GlobalRootEntity)
            {
                continue;
            }

            if (world is SerializedWorldEntity serialized)
            {
                evidence.Add(serialized.BatchId);
                candidates.Add(serialized.BatchId);
                continue;
            }

            NitroxInt3 batchId = BatchOf(world);
            candidates.Add(batchId);
            if (world.SpawnedByServer)
            {
                serverSpawned.TryGetValue(batchId, out int count);
                serverSpawned[batchId] = count + 1;
            }
        }

        foreach (KeyValuePair<NitroxInt3, int> pair in serverSpawned)
        {
            if (pair.Value >= MIN_SERVER_ENTITIES)
            {
                evidence.Add(pair.Key);
            }
        }

        if (generatedIds != null)
        {
            HashSet<NitroxId> registered = [.. flat.Select(entity => entity.Id)];
            foreach (NitroxInt3 candidate in candidates)
            {
                if (known.Contains(candidate) || evidence.Contains(candidate))
                {
                    continue;
                }

                int taken = 0;
                foreach (NitroxId generated in generatedIds(candidate))
                {
                    if (registered.Contains(generated))
                    {
                        evidence.Add(candidate);
                        break;
                    }
                    if (++taken >= ID_STREAM_DEPTH)
                    {
                        break;
                    }
                }
            }
        }

        List<NitroxInt3> added = evidence.Where(batchId => !known.Contains(batchId))
                                         .OrderBy(batchId => batchId.X)
                                         .ThenBy(batchId => batchId.Y)
                                         .ThenBy(batchId => batchId.Z)
                                         .ToList();
        all.AddRange(added);
        return new Derivation(all, added);
    }

    /// <summary>
    ///     The ids that the batch generator of the server produces for a batch, in the order it produces them, without end (the caller takes what it needs).
    /// </summary>
    internal static IEnumerable<NitroxId> BatchIdStream(string seed, NitroxInt3 batchId)
    {
        DeterministicGenerator generator = new(seed, batchId.ToString());
        while (true)
        {
            yield return generator.NextId();
        }
    }

    /// <summary>
    ///     The batch a world entity stands in (the level of the cell does not change it).
    /// </summary>
    private static NitroxInt3 BatchOf(WorldEntity entity)
    {
        return new AbsoluteEntityCell(entity.Transform.LocalPosition, 0).BatchId;
    }
}
