using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace NitroxClient.GameLogic;

/// <summary>
/// The entities that reached the spawner before the object of their parent exists, held until the parent is spawned.
/// </summary>
/// <remarks>
/// The same entity is sent again every time its cell is replayed, so an entity is held once (the newest copy wins). An entry is dropped as soon as its entity has been
/// spawned by another route (the parent spawned it with its own children), when its entity or its parent is destroyed, when the cell of its entity goes to sleep (the
/// replay of the cell sends it again), and after <see cref="MAX_AGE_SECONDS"/> as a last resort. Without this a warp storm of 30 cell changes left 1149 entries of
/// 194 entities in the table.
/// </remarks>
public sealed class PendingParentEntities
{
    /// <summary>
    /// How long an entry may wait for its parent. A parent that is in the same answer is spawned within seconds, one that is not is not coming.
    /// </summary>
    public const float MAX_AGE_SECONDS = 300f;

    private readonly Dictionary<NitroxId, Dictionary<NitroxId, Entry>> entriesByParentId = [];
    private readonly Dictionary<NitroxId, NitroxId> parentIdByEntityId = [];

    public int Count => parentIdByEntityId.Count;

    public int ParentCount => entriesByParentId.Count;

    public IEnumerable<(NitroxId ParentId, Entity Entity)> Entries => entriesByParentId.SelectMany(pair => pair.Value.Values.Select(entry => (pair.Key, entry.Entity)));

    public bool HasWaitersFor(NitroxId parentId) => parentId != null && entriesByParentId.ContainsKey(parentId);

    /// <summary>
    /// The entity that waits under this id, if any.
    /// </summary>
    public bool TryGet(NitroxId entityId, out Entity entity)
    {
        entity = null;
        if (entityId != null && parentIdByEntityId.TryGetValue(entityId, out NitroxId parentId) &&
            entriesByParentId.TryGetValue(parentId, out Dictionary<NitroxId, Entry> entries) && entries.TryGetValue(entityId, out Entry entry))
        {
            entity = entry.Entity;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Holds the entity until its parent is spawned. An entity that is already held (under any parent) is replaced by this newer copy.
    /// </summary>
    public void Add(Entity entity, float now)
    {
        if (entity.ParentId == null)
        {
            return;
        }

        Remove(entity.Id);

        if (!entriesByParentId.TryGetValue(entity.ParentId, out Dictionary<NitroxId, Entry> entries))
        {
            entries = [];
            entriesByParentId[entity.ParentId] = entries;
        }

        entries[entity.Id] = new Entry(entity, now, CellOf(entity));
        parentIdByEntityId[entity.Id] = entity.ParentId;
    }

    /// <summary>
    /// The cell a waiting entity belongs to, worked out once when it is added. Only entities that sit in the world by their position have one: a global root entity
    /// (vehicle, module, base piece) is not part of any cell, and a level outside 0-3 has no cell size (AbsoluteEntityCell throws for it).
    /// </summary>
    private static AbsoluteEntityCell CellOf(Entity entity)
    {
        if (entity is WorldEntity worldEntity and not GlobalRootEntity && worldEntity.Level is >= 0 and <= 3)
        {
            return worldEntity.AbsoluteEntityCell;
        }
        return null;
    }

    /// <summary>
    /// Takes every entity that waits for this parent, except the ones in <paramref name="alreadyHandled"/> (the parent brings those itself).
    /// </summary>
    public List<Entity> Take(NitroxId parentId, ICollection<NitroxId> alreadyHandled)
    {
        List<Entity> taken = [];
        if (parentId == null || !entriesByParentId.TryGetValue(parentId, out Dictionary<NitroxId, Entry> entries))
        {
            return taken;
        }

        entriesByParentId.Remove(parentId);
        foreach (Entry entry in entries.Values)
        {
            parentIdByEntityId.Remove(entry.Entity.Id);
            if (!alreadyHandled.Contains(entry.Entity.Id))
            {
                taken.Add(entry.Entity);
            }
        }
        return taken;
    }

    /// <summary>
    /// The entity does not wait any more: it was spawned, or it is gone.
    /// </summary>
    public bool Remove(NitroxId entityId)
    {
        if (entityId == null || !parentIdByEntityId.TryGetValue(entityId, out NitroxId parentId))
        {
            return false;
        }

        parentIdByEntityId.Remove(entityId);
        if (entriesByParentId.TryGetValue(parentId, out Dictionary<NitroxId, Entry> entries))
        {
            entries.Remove(entityId);
            if (entries.Count == 0)
            {
                entriesByParentId.Remove(parentId);
            }
        }
        return true;
    }

    /// <summary>
    /// The parent is gone, nobody will ever adopt its entries.
    /// </summary>
    public int RemoveParent(NitroxId parentId)
    {
        if (parentId == null || !entriesByParentId.TryGetValue(parentId, out Dictionary<NitroxId, Entry> entries))
        {
            return 0;
        }

        entriesByParentId.Remove(parentId);
        foreach (NitroxId entityId in entries.Keys)
        {
            parentIdByEntityId.Remove(entityId);
        }
        return entries.Count;
    }

    /// <summary>
    /// The cell went to sleep: the next time it loads the server sends everything in it again, so what waited here for a parent is outdated.
    /// </summary>
    public int RemoveCell(AbsoluteEntityCell cell)
    {
        return RemoveWhere(entry => entry.Cell != null && entry.Cell.Equals(cell));
    }

    public int RemoveOlderThan(float oldestAllowedTime)
    {
        return RemoveWhere(entry => entry.Since < oldestAllowedTime);
    }

    /// <summary>
    /// What <see cref="RemoveOlderThan"/> would drop, for the log: the number of distinct parents and the most common kinds of entity, then (bug 10 of the first play sessions: the
    /// logs could not say whether laser-cut doors, which are TechType None placeholder children of a wreck, were among the dropped) the number of laser-cut doors among them and the
    /// parents that lose most, by the start of their id.
    /// </summary>
    public string DescribeOlderThan(float oldestAllowedTime)
    {
        Dictionary<string, int> kinds = [];
        Dictionary<NitroxId, int> countByParent = [];
        int sealedDoors = 0;
        foreach (KeyValuePair<NitroxId, Dictionary<NitroxId, Entry>> pair in entriesByParentId)
        {
            foreach (Entry entry in pair.Value.Values)
            {
                if (entry.Since >= oldestAllowedTime)
                {
                    continue;
                }

                string kind = $"{entry.Entity.GetType().Name}/{entry.Entity.TechType?.Name ?? "?"}";
                kinds[kind] = kinds.TryGetValue(kind, out int count) ? count + 1 : 1;
                countByParent[pair.Key] = countByParent.TryGetValue(pair.Key, out int parentCount) ? parentCount + 1 : 1;
                if (entry.Entity is WorldEntity { ClassId: StarshipDoorRules.SEALED_DOOR_CLASS_ID })
                {
                    sealedDoors++;
                }
            }
        }

        string doors = sealedDoors > 0 ? $"; laser-cut doors x{sealedDoors}" : "";
        string biggest = string.Join(", ", countByParent.OrderByDescending(pair => pair.Value).Take(3).Select(pair => $"{pair.Key.ToString().Substring(0, 8)} x{pair.Value}"));
        return $"{countByParent.Count} parents; {string.Join(", ", kinds.OrderByDescending(pair => pair.Value).Take(5).Select(pair => $"{pair.Key} x{pair.Value}"))}{doors}{(biggest.Length > 0 ? $"; parents {biggest}" : "")}";
    }

    private int RemoveWhere(System.Func<Entry, bool> predicate)
    {
        List<NitroxId> toRemove = [];
        foreach (Dictionary<NitroxId, Entry> entries in entriesByParentId.Values)
        {
            toRemove.AddRange(entries.Values.Where(predicate).Select(entry => entry.Entity.Id));
        }

        foreach (NitroxId entityId in toRemove)
        {
            Remove(entityId);
        }
        return toRemove.Count;
    }

    private readonly record struct Entry(Entity Entity, float Since, AbsoluteEntityCell Cell);
}
