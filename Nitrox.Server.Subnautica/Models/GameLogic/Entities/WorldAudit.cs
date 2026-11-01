using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

internal readonly record struct WorldAuditResult(int RegistryEntities, int DuplicateIds, int DoubledTopLevel, int TwinsLeftAlone, int SetsForReview)
{
    /// <summary>
    ///     The line the live checks read: <c>registry entities: N, duplicate ids: D, doubled top-level: T</c> and what the repair leaves alone.
    /// </summary>
    public override string ToString()
    {
        return $"registry entities: {RegistryEntities}, duplicate ids: {DuplicateIds}, doubled top-level: {DoubledTopLevel} (identical decor left alone: {TwinsLeftAlone}, sets that need review: {SetsForReview})";
    }
}

/// <summary>
///     Counts the signs of a batch that was generated twice in the registry: ids that two different objects claim, and top-level entities that exist twice at one spot.
/// </summary>
internal static class WorldAudit
{
    /// <param name="all">Every entity of the registry.</param>
    /// <param name="isMobileCreature">As for <see cref="DuplicateEntityRepair.Plan" />.</param>
    internal static WorldAuditResult Run(IReadOnlyCollection<Entity> all, Func<NitroxTechType?, bool> isMobileCreature)
    {
        Dictionary<NitroxId, Entity> byId = new(all.Count);
        int duplicateIds = 0;
        foreach (Entity entity in all)
        {
            if (byId.ContainsKey(entity.Id))
            {
                duplicateIds++;
            }
            else
            {
                byId[entity.Id] = entity;
            }
        }

        // A child that a parent lists but that is not the object registered under its id: a second object with the id (the registry holds one, the parent's list the other)
        foreach (Entity entity in all)
        {
            foreach (Entity child in entity.ChildEntities)
            {
                if (byId.TryGetValue(child.Id, out Entity? registered) && !ReferenceEquals(registered, child))
                {
                    duplicateIds++;
                }
            }
        }

        DuplicateEntityRepair.RepairPlan plan = DuplicateEntityRepair.Plan(all, [], isMobileCreature);
        return new WorldAuditResult(all.Count, duplicateIds, plan.CopiesToRemove + plan.CopiesForReview, plan.TwinsLeftAlone, plan.Review.Count);
    }

    /// <summary>
    ///     How many top-level entities each batch holds (an entity that came from the baked game data counts for the batch it was generated for, any other for the batch it stands in).
    ///     Compared before and after a restart, a number that moved says that a batch was generated again.
    /// </summary>
    internal static SortedDictionary<NitroxInt3, int> CountTopLevelByBatch(IEnumerable<Entity> entities)
    {
        SortedDictionary<NitroxInt3, int> counts = new(Comparer<NitroxInt3>.Create((left, right) =>
        {
            int byX = left.X.CompareTo(right.X);
            if (byX != 0)
            {
                return byX;
            }
            int byY = left.Y.CompareTo(right.Y);
            return byY != 0 ? byY : left.Z.CompareTo(right.Z);
        }));

        foreach (Entity entity in entities)
        {
            if (entity.ParentId != null || entity is not WorldEntity { Transform: not null } world || world is CellRootEntity or GlobalRootEntity)
            {
                continue;
            }

            NitroxInt3 batchId = world is SerializedWorldEntity serialized ? serialized.BatchId : new AbsoluteEntityCell(world.Transform.LocalPosition, 0).BatchId;
            counts.TryGetValue(batchId, out int count);
            counts[batchId] = count + 1;
        }
        return counts;
    }
}
