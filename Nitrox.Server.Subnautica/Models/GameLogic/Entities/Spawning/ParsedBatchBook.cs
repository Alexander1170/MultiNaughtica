using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

/// <summary>
///     The record of which batches the server has generated, as <see cref="BatchEntitySpawner" /> keeps it (one lazy load task per batch) and as the save keeps it
///     (<c>WorldData.ParsedBatchCells</c>). Kept apart from the spawner so that the two directions of the conversion can be tested without a game install.
/// </summary>
/// <remarks>
///     A batch that is in the record is never generated again: its entities are in the entity data of the save. The record must therefore list every batch that has been generated,
///     including those that nobody visited since the server started. It used to list only the batches whose lazy task had been read in this run, so the first save after a start (the
///     server saves as soon as it hibernates with nobody connected) wrote a record without every restored batch, and the next visitor made the server generate them a second time.
/// </remarks>
internal static class ParsedBatchBook
{
    /// <returns>The batches that finished generating successfully (restored ones included), except those that came out empty.</returns>
    internal static List<NitroxInt3> ListParsed(IEnumerable<KeyValuePair<NitroxInt3, Lazy<Task<List<Entity>>>>> loadTasks, IEnumerable<NitroxInt3> emptyBatches)
    {
        HashSet<NitroxInt3> empty = [.. emptyBatches];
        // Only batches which finished loading: one still being generated (or one which failed) is not in the entity data of the save, so
        // listing it as parsed would leave its area permanently empty after a restart.
        return [.. loadTasks.Where(pair => pair.Value.IsValueCreated && pair.Value.Value.IsCompletedSuccessfully)
                            .Select(pair => pair.Key)
                            .Where(batchId => !empty.Contains(batchId))];
    }

    /// <summary>
    ///     Replaces the record by the batches of a save. Each one gets a task that is already complete (and an empty result: its entities are in the registry), so that
    ///     <see cref="ListParsed" /> keeps listing it although nobody asks for it.
    /// </summary>
    internal static void Restore(ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> loadTasks, IEnumerable<NitroxInt3>? parsedBatches)
    {
        loadTasks.Clear();
        MarkParsed(loadTasks, parsedBatches);
    }

    /// <returns>How many of the batches were not in the record yet.</returns>
    internal static int MarkParsed(ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> loadTasks, IEnumerable<NitroxInt3>? parsedBatches)
    {
        if (parsedBatches == null)
        {
            return 0;
        }

        int added = 0;
        foreach (NitroxInt3 batchId in parsedBatches)
        {
            // The value constructor makes IsValueCreated true: a lazy with a factory that nobody has run is invisible to ListParsed
            if (loadTasks.TryAdd(batchId, new Lazy<Task<List<Entity>>>(Task.FromResult(new List<Entity>()))))
            {
                added++;
            }
        }
        return added;
    }
}
