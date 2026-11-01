using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Gives a new id to a player's item that shares its id with a different entity in a base, a locker or an inventory. Pure: it plans, <see cref="Apply" /> changes the ids.
/// </summary>
/// <remarks>
///     <para>
///         How it happens (the world "Test"): a batch that is generated a second time puts a generated entity under the id of an item that a player picked up and stored (the registry replaced the
///         item). The item stays in the child list of its locker, and the next move of "that id" by a client moves the generated entity instead. The save then holds two different entities with one
///         id inside the trees of the global root: 4 ids in the world "Test" (a Quartz and a Sandshark egg, a Salt and a Peeper, a Salt and a Hoopfish, two Quartz). A flat entity list cannot
///         hold an id twice, so these only exist in the global root data.
///     </para>
///     <para>
///         The rule deletes nothing. For each id that two different entities carry, the entity that matches the entry of the flat entity list (type, tech type, parent) keeps the id; every other
///         occurrence that is a childless <see cref="InventoryItemEntity" /> gets a new id. Left alone and reported: an id that a player's equipment or quick slots point at, an id for which no
///         single occurrence matches the flat entry, an occurrence that is not an inventory item or has children, and the same entity listed twice under one parent (the Cyclops of the world
///         "Test" lists 8 of its path children twice; that is another defect, the second listing holds the contents of the locker, so it is not a copy to drop).
///     </para>
/// </remarks>
internal static class IdCollisionRepair
{
    internal sealed record Fix(InventoryItemEntity Item, NitroxId OldId, NitroxId? ParentId, string TechType, string KeptBy);

    internal sealed record Skipped(NitroxId Id, string Reason);

    internal sealed class RepairPlan
    {
        public List<Fix> Fixes { get; } = [];
        public List<Skipped> Skips { get; } = [];
    }

    /// <param name="treeRoots">The entities of the global root data, with their nested children.</param>
    /// <param name="flat">The flat entity list of the entity data (one entity per id).</param>
    /// <param name="playerReferencedIds">The ids that the data of the players point at (equipment, quick slots).</param>
    internal static RepairPlan Plan(IEnumerable<Entity> treeRoots, IReadOnlyCollection<Entity> flat, HashSet<NitroxId> playerReferencedIds)
    {
        RepairPlan plan = new();

        // Every object of the trees once, by reference, in the order of a depth-first walk
        Dictionary<NitroxId, List<Entity>> occurrences = [];
        List<NitroxId> order = [];
        HashSet<Entity> visited = new(ReferenceEqualityComparer.Instance);
        Stack<Entity> pending = new();
        foreach (Entity root in Enumerable.Reverse(treeRoots))
        {
            pending.Push(root);
        }
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            if (!visited.Add(entity))
            {
                continue;
            }

            if (!occurrences.TryGetValue(entity.Id, out List<Entity>? list))
            {
                list = [];
                occurrences[entity.Id] = list;
                order.Add(entity.Id);
            }
            list.Add(entity);
            foreach (Entity child in Enumerable.Reverse(entity.ChildEntities))
            {
                pending.Push(child);
            }
        }

        Dictionary<NitroxId, Entity> flatById = [];
        HashSet<NitroxId> flatParents = [];
        foreach (Entity entity in flat)
        {
            flatById[entity.Id] = entity;
            if (entity.ParentId != null)
            {
                flatParents.Add(entity.ParentId);
            }
        }

        foreach (NitroxId id in order)
        {
            List<Entity> same = occurrences[id];
            if (same.Count < 2)
            {
                continue;
            }

            if (same.All(entity => Signature(entity) == Signature(same[0])))
            {
                plan.Skips.Add(new Skipped(id, $"the same {same[0].GetType().Name} is listed {same.Count} times under {same[0].ParentId?.ToString() ?? "no parent"}: not a collision of two entities, left as it is"));
                continue;
            }

            if (playerReferencedIds.Contains(id))
            {
                plan.Skips.Add(new Skipped(id, "the equipment or quick slots of a player point at this id and two different entities carry it: nothing is changed"));
                continue;
            }

            Entity? keeper = null;
            if (flatById.TryGetValue(id, out Entity? flatEntry))
            {
                List<Entity> matching = same.Where(entity => Signature(entity) == Signature(flatEntry)).ToList();
                if (matching.Count == 1)
                {
                    keeper = matching[0];
                }
            }
            if (keeper == null)
            {
                plan.Skips.Add(new Skipped(id, "two different entities carry this id and no single one matches the entry of the entity list: nothing is changed"));
                continue;
            }

            foreach (Entity other in same)
            {
                if (ReferenceEquals(other, keeper))
                {
                    continue;
                }

                if (other is InventoryItemEntity item && item.ChildEntities.Count == 0 && !flatParents.Contains(id))
                {
                    plan.Fixes.Add(new Fix(item, id, item.ParentId, item.TechType?.Name ?? "None", $"{keeper.GetType().Name} {keeper.TechType?.Name ?? "None"} in {keeper.ParentId?.ToString() ?? "no parent"}"));
                }
                else
                {
                    plan.Skips.Add(new Skipped(id, $"a {other.GetType().Name} {other.TechType?.Name ?? "None"} shares this id with a {keeper.GetType().Name} {keeper.TechType?.Name ?? "None"} and is not a childless inventory item: nothing is changed"));
                }
            }
        }
        return plan;
    }

    /// <summary>
    ///     Gives each item of the plan a new id.
    /// </summary>
    /// <returns>The new ids, in the order of the plan.</returns>
    internal static List<NitroxId> Apply(RepairPlan plan)
    {
        List<NitroxId> assigned = [];
        foreach (Fix fix in plan.Fixes)
        {
            NitroxId newId = new();
            fix.Item.Id = newId;
            assigned.Add(newId);
        }
        return assigned;
    }

    private static (string Type, string Tech, string Parent) Signature(Entity entity)
    {
        return (entity.GetType().Name, entity.TechType?.Name ?? "None", entity.ParentId?.ToString() ?? "");
    }
}
