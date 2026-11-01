using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     What <see cref="WorldEntityManager.GetEntities" /> may hand out of the cell index (bug 9, the ghost fish in the base, 2026-10-24).
/// </summary>
/// <remarks>
///     The cell index keeps one object per id and cell. Registering an entity with an id that the registry already held (the second generation of a batch, coop.3) replaced the object in the
///     registry but not the one in the index: the index kept the old object under the cell it was in, and <see cref="WorldEntityManager.GetEntities" /> only asked whether the id was
///     registered. Every player who loaded that cell was then sent a copy of an object that the registry no longer knows: it stands where it was on the day before (a creature that swam in
///     the base on the first night, for example), nobody updates it (the updates of the id go to the registry's object), nobody can pick it up or hit it (that is by id, which is somebody else's now), the
///     cleanup of copies that nobody updates (K4e) never looks at it (it follows the moves of the registry's object), and which of the two objects of the id a client keeps depends on
///     the order it spawned them in.
/// </remarks>
internal static class CellIndexRules
{
    internal enum EntryState
    {
        /// <summary>The registry holds this very object under its id: it is handed out.</summary>
        Current,

        /// <summary>The id left the registry (destroyed, picked up): never handed out, left in the index as before.</summary>
        Hidden,

        /// <summary>The registry holds another object under the id: this one can never be right again and is removed from the index.</summary>
        Stale
    }

    /// <param name="indexed">The object the cell index holds.</param>
    /// <param name="registered">The object the registry holds under the id of <paramref name="indexed" />, null when the id is not registered.</param>
    internal static EntryState Classify(WorldEntity indexed, Entity? registered)
    {
        if (registered == null)
        {
            return EntryState.Hidden;
        }

        return ReferenceEquals(registered, indexed) ? EntryState.Current : EntryState.Stale;
    }

    /// <summary>
    ///     The first few removed entries as "tech id at position", for the log line.
    /// </summary>
    internal static string Describe(IReadOnlyList<WorldEntity> staleEntries)
    {
        List<string> parts = [];
        for (int i = 0; i < staleEntries.Count && i < MAX_DESCRIBED; i++)
        {
            WorldEntity entity = staleEntries[i];
            parts.Add($"{entity.TechType} {entity.Id} at {entity.Transform?.Position}");
        }

        return staleEntries.Count > MAX_DESCRIBED ? $"{string.Join(", ", parts)} and {staleEntries.Count - MAX_DESCRIBED} more" : string.Join(", ", parts);
    }

    private const int MAX_DESCRIBED = 5;
}
