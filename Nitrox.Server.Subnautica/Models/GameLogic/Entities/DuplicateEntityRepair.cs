using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Finds the second copies of top-level world entities that a batch generated twice left in a save, and decides which copy stays. Pure: it reads a flat list of entities and returns a plan,
///     it changes nothing.
/// </summary>
/// <remarks>
///     <para>
///         What a save of coop.3 shows (the world "Test", read for this rule): a batch that was generated a second time (the first save after a server start forgot the batches that nobody
///         visited, see <see cref="Spawning.ParsedBatchBook" />) put a second copy of many top-level entities next to the first one: the same class, tech type, level, scale, position and rotation,
///         another id. 1428 such pairs among 180907 top-level entities, 2861 entities. Measured on the batches that were generated once (139 batches, 97632 top-level entities): 464 identical
///         pairs, 462 of them <see cref="SerializedWorldEntity" /> (decor of the baked game data, which holds a few objects twice) and 2 plain world entities. So an identical pair of decor says
///         nothing, but an identical pair of anything else the server spawned is a copy of a second generation: in the world "Test" every one of those pairs has both ids in the id sequence
///         of its batch, a few places apart (the number of databoxes or fragments that were left out in between).
///     </para>
///     <para>
///         The rule is therefore: top-level <see cref="PlaceholderGroupWorldEntity" /> and plain <see cref="WorldEntity" /> that the server spawned (creatures, resources, plants, eggs) form twins;
///         decor is only counted. Players, bases, vehicles, global root entities, everything a player dropped or built (<c>SpawnedByServer</c> is false) and everything they own or reference
///         (<c>protectedIds</c>) are never touched.
///     </para>
///     <para>
///         Which copy stays: a copy that carries state (metadata a client wrote, a child that is not a plain generated one, or a reference from a player or a base) beats a pristine one. A pristine
///         copy is what the generator makes: no metadata (or the metadata the bootstrapper writes), generated children only. Among equal copies the one with the most children stays (a copy
///         that lost its children when the second run took their ids is the one to drop), then the later crash-fish time, then the lowest id. Two copies that both carry state which cannot be
///         shown to be the same are left alone and reported for review. Positions are bucketed by whole metre before they are compared with the epsilon, so a pair that is a hair apart across a
///         whole-metre line is not compared (the copies of a second run are bit-identical, they share a bucket).
///     </para>
/// </remarks>
internal static class DuplicateEntityRepair
{
    internal const float POSITION_EPSILON = 0.01f;
    internal const float ROTATION_EPSILON = 0.001f;
    internal const float SCALE_EPSILON = 0.001f;

    /// <summary>
    ///     Two crash home entities whose last spawn time (game seconds, written by the clients) differs by less than this are the same state.
    /// </summary>
    internal const float CRASH_HOME_TIME_TOLERANCE = 5f;

    internal enum TwinKind
    {
        PlaceholderGroup,
        Creature,
        StaticResource,
        Decor
    }

    internal sealed record TwinSet(TwinKind Kind, NitroxId KeptId, IReadOnlyList<NitroxId> RemovedIds, int RemovedDescendants, string ClassId, string TechType, NitroxVector3 Position, string Reason);

    internal sealed record ReviewSet(TwinKind Kind, IReadOnlyList<NitroxId> Ids, string ClassId, string TechType, NitroxVector3 Position, string Reason);

    internal sealed class RepairPlan
    {
        /// <summary>
        ///     The twin sets from which copies are removed.
        /// </summary>
        public List<TwinSet> Sets { get; } = [];

        /// <summary>
        ///     Twin sets that were found but are left as they are because the copies carry different state.
        /// </summary>
        public List<ReviewSet> Review { get; } = [];

        /// <summary>
        ///     Every entity to take out: the removed copies and everything below them.
        /// </summary>
        public HashSet<NitroxId> RemovedIds { get; } = [];

        /// <summary>
        ///     Extra copies of decor: identical pairs of the baked game data as well, so a pair says nothing and none is touched.
        /// </summary>
        public int TwinsLeftAlone { get; set; }

        public int CopiesToRemove => Sets.Sum(set => set.RemovedIds.Count);

        public int DescendantsToRemove => Sets.Sum(set => set.RemovedDescendants);

        public int CopiesForReview => Review.Sum(set => set.Ids.Count - 1);
    }

    /// <summary>
    ///     A creature the simulation moves (a ghost when it exists twice at the same spot). Only names the kind of a twin set in the report: resources are repaired as well.
    /// </summary>
    internal static bool IsMobileCreature(NitroxTechType? techType)
    {
        return techType != null && SimulationWhitelist.MovementWhitelist.Contains(techType);
    }

    /// <param name="entities">The flat list of the entity data of a save (children are in it, linked by their parent id), after the loader has rebuilt the child lists.</param>
    /// <param name="protectedIds">Ids that a player, a base or a vehicle owns or references: a copy that holds one of them (or has one below it) is never removed.</param>
    /// <param name="isMobileCreature">Tells whether a tech type is a creature of the simulation (<see cref="IsMobileCreature" /> in the server).</param>
    internal static RepairPlan Plan(IReadOnlyCollection<Entity> entities, HashSet<NitroxId> protectedIds, Func<NitroxTechType?, bool> isMobileCreature)
    {
        RepairPlan plan = new();
        Dictionary<NitroxId, List<Entity>> childrenByParent = [];
        Dictionary<(TwinKind Kind, string? ClassId, string? TechType, int Level, long X, long Y, long Z), List<WorldEntity>> buckets = [];

        foreach (Entity entity in entities)
        {
            if (entity.ParentId != null)
            {
                if (!childrenByParent.TryGetValue(entity.ParentId, out List<Entity>? siblings))
                {
                    siblings = [];
                    childrenByParent[entity.ParentId] = siblings;
                }
                siblings.Add(entity);
                continue;
            }

            TwinKind? kind = Classify(entity, isMobileCreature);
            if (kind == null)
            {
                continue;
            }

            WorldEntity world = (WorldEntity)entity;
            NitroxVector3 position = world.Transform.LocalPosition;
            if (!IsFinite(position))
            {
                continue;
            }

            (TwinKind Kind, string? ClassId, string? TechType, int Level, long X, long Y, long Z) key = (kind.Value, world.ClassId, world.TechType?.Name, world.Level, Cell(position.X), Cell(position.Y), Cell(position.Z));
            if (!buckets.TryGetValue(key, out List<WorldEntity>? bucket))
            {
                bucket = [];
                buckets[key] = bucket;
            }
            bucket.Add(world);
        }

        foreach (KeyValuePair<(TwinKind Kind, string? ClassId, string? TechType, int Level, long X, long Y, long Z), List<WorldEntity>> pair in buckets)
        {
            List<WorldEntity> bucket = pair.Value;
            if (bucket.Count < 2)
            {
                continue;
            }

            bucket.Sort((left, right) => left.Id.CompareTo(right.Id));
            bool[] used = new bool[bucket.Count];
            for (int i = 0; i < bucket.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                used[i] = true;
                List<WorldEntity> copies = [bucket[i]];
                for (int j = i + 1; j < bucket.Count; j++)
                {
                    if (!used[j] && AreTwins(bucket[i], bucket[j]))
                    {
                        used[j] = true;
                        copies.Add(bucket[j]);
                    }
                }

                if (copies.Count > 1)
                {
                    ProcessSet(plan, pair.Key.Kind, copies, childrenByParent, protectedIds);
                }
            }
        }

        plan.Sets.Sort((left, right) => left.KeptId.CompareTo(right.KeptId));
        plan.Review.Sort((left, right) => left.Ids[0].CompareTo(right.Ids[0]));
        return plan;
    }

    private static TwinKind? Classify(Entity entity, Func<NitroxTechType?, bool> isMobileCreature)
    {
        if (entity.ParentId != null || entity is not WorldEntity { Transform: not null } world)
        {
            return null;
        }

        Type type = entity.GetType();
        if (type == typeof(PlaceholderGroupWorldEntity))
        {
            return world.SpawnedByServer ? TwinKind.PlaceholderGroup : null;
        }
        if (type == typeof(SerializedWorldEntity))
        {
            return TwinKind.Decor;
        }
        if (type == typeof(WorldEntity) && world.SpawnedByServer)
        {
            return isMobileCreature(world.TechType) ? TwinKind.Creature : TwinKind.StaticResource;
        }
        return null;
    }

    private static long Cell(float value) => (long)Math.Floor(value);

    private static bool IsFinite(NitroxVector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static bool AreTwins(WorldEntity first, WorldEntity second)
    {
        NitroxTransform left = first.Transform;
        NitroxTransform right = second.Transform;
        return first.GetType() == second.GetType() &&
               first.SpawnedByServer == second.SpawnedByServer &&
               first.Level == second.Level &&
               string.Equals(first.ClassId, second.ClassId, StringComparison.Ordinal) &&
               string.Equals(first.TechType?.Name, second.TechType?.Name, StringComparison.Ordinal) &&
               Math.Abs(left.LocalPosition.X - right.LocalPosition.X) <= POSITION_EPSILON &&
               Math.Abs(left.LocalPosition.Y - right.LocalPosition.Y) <= POSITION_EPSILON &&
               Math.Abs(left.LocalPosition.Z - right.LocalPosition.Z) <= POSITION_EPSILON &&
               Math.Abs(left.LocalRotation.X - right.LocalRotation.X) <= ROTATION_EPSILON &&
               Math.Abs(left.LocalRotation.Y - right.LocalRotation.Y) <= ROTATION_EPSILON &&
               Math.Abs(left.LocalRotation.Z - right.LocalRotation.Z) <= ROTATION_EPSILON &&
               Math.Abs(left.LocalRotation.W - right.LocalRotation.W) <= ROTATION_EPSILON &&
               Math.Abs(left.LocalScale.X - right.LocalScale.X) <= SCALE_EPSILON &&
               Math.Abs(left.LocalScale.Y - right.LocalScale.Y) <= SCALE_EPSILON &&
               Math.Abs(left.LocalScale.Z - right.LocalScale.Z) <= SCALE_EPSILON;
    }

    private sealed class Copy(WorldEntity entity, List<Entity> descendants, bool isProtected, bool touchedRoot, bool touchedBelow)
    {
        public WorldEntity Root { get; } = entity;
        public List<Entity> Descendants { get; } = descendants;
        public bool IsProtected { get; } = isProtected;
        public bool TouchedRoot { get; } = touchedRoot;
        public bool TouchedBelow { get; } = touchedBelow;
        public bool Touched => IsProtected || TouchedRoot || TouchedBelow;
    }

    private static void ProcessSet(RepairPlan plan, TwinKind kind, List<WorldEntity> copies, Dictionary<NitroxId, List<Entity>> childrenByParent, HashSet<NitroxId> protectedIds)
    {
        if (kind == TwinKind.Decor)
        {
            plan.TwinsLeftAlone += copies.Count - 1;
            return;
        }

        List<Copy> states = copies.Select(copy => Describe(copy, childrenByParent, protectedIds)).ToList();
        List<Copy> touched = states.Where(state => state.Touched).ToList();
        WorldEntity first = copies[0];
        string techType = first.TechType?.Name ?? "None";
        string classId = first.ClassId ?? "";

        Copy keep;
        string reason;
        if (states.Any(state => state.IsProtected))
        {
            // Something the players built or carry points into this set: nothing in it is taken out without a human looking at it
            plan.Review.Add(new ReviewSet(kind,
                                          copies.Select(copy => copy.Id).ToList(),
                                          classId,
                                          techType,
                                          first.Transform.LocalPosition,
                                          $"a player, base or vehicle references a copy or something below it ({string.Join(", ", states.Where(state => state.IsProtected).Select(state => state.Root.Id))}): nothing is removed"));
            return;
        }

        if (touched.Count == 0)
        {
            keep = Best(states);
            reason = $"no copy carries state: kept the most complete one ({keep.Descendants.Count} children below it), then the later crash time, then the lowest id";
        }
        else if (touched.Count == 1)
        {
            keep = touched[0];
            reason = $"only this copy carries state ({Why(keep)}), the others are what the generator makes";
        }
        else if (AreEquivalent(touched))
        {
            keep = Best(touched);
            reason = $"{touched.Count} copies carry the same state ({Why(keep)}): kept the most complete one, then the later crash time, then the lowest id";
        }
        else
        {
            plan.Review.Add(new ReviewSet(kind,
                                          copies.Select(copy => copy.Id).ToList(),
                                          classId,
                                          techType,
                                          first.Transform.LocalPosition,
                                          $"{touched.Count} copies carry different state ({string.Join("; ", touched.Select(state => $"{state.Root.Id}: {Why(state)}"))}): nothing is removed"));
            return;
        }

        List<NitroxId> removedIds = [];
        int removedDescendants = 0;
        foreach (Copy state in states)
        {
            if (state == keep)
            {
                continue;
            }

            removedIds.Add(state.Root.Id);
            plan.RemovedIds.Add(state.Root.Id);
            foreach (Entity descendant in state.Descendants)
            {
                plan.RemovedIds.Add(descendant.Id);
            }
            removedDescendants += state.Descendants.Count;
        }

        plan.Sets.Add(new TwinSet(kind, keep.Root.Id, removedIds, removedDescendants, classId, techType, first.Transform.LocalPosition, reason));
    }

    private static Copy Describe(WorldEntity copy, Dictionary<NitroxId, List<Entity>> childrenByParent, HashSet<NitroxId> protectedIds)
    {
        List<Entity> descendants = DescendantsOf(copy.Id, childrenByParent);
        bool isProtected = protectedIds.Contains(copy.Id) || descendants.Any(descendant => protectedIds.Contains(descendant.Id));
        bool touchedBelow = descendants.Any(descendant => !IsPristineDescendant(descendant, protectedIds));
        return new Copy(copy, descendants, isProtected, IsTouchedMetadata(copy.Metadata), touchedBelow);
    }

    private static List<Entity> DescendantsOf(NitroxId id, Dictionary<NitroxId, List<Entity>> childrenByParent)
    {
        List<Entity> descendants = [];
        HashSet<NitroxId> seen = [id];
        Stack<NitroxId> pending = new();
        pending.Push(id);
        while (pending.Count > 0)
        {
            if (!childrenByParent.TryGetValue(pending.Pop(), out List<Entity>? children))
            {
                continue;
            }

            foreach (Entity child in children)
            {
                if (seen.Add(child.Id))
                {
                    descendants.Add(child);
                    pending.Push(child.Id);
                }
            }
        }
        return descendants;
    }

    /// <summary>
    ///     Metadata a client wrote (a door that was cut, a crate that was opened, the time a crash fish came out). The metadata the server's bootstrappers give a new entity is not.
    /// </summary>
    private static bool IsTouchedMetadata(EntityMetadata? metadata)
    {
        if (metadata == null)
        {
            return false;
        }
        return !(metadata is CrashHomeMetadata { SpawnTime: 0f } || metadata is StayAtLeashPositionMetadata);
    }

    /// <summary>
    ///     A descendant that looks like the generator made it and that nobody uses: a plain generated world entity without metadata that no player, base or vehicle references.
    /// </summary>
    private static bool IsPristineDescendant(Entity descendant, HashSet<NitroxId> protectedIds)
    {
        if (protectedIds.Contains(descendant.Id) || IsTouchedMetadata(descendant.Metadata) || descendant is not WorldEntity { SpawnedByServer: true })
        {
            return false;
        }

        Type type = descendant.GetType();
        return type == typeof(WorldEntity) || type == typeof(PrefabPlaceholderEntity) || type == typeof(PlaceholderGroupWorldEntity) || type == typeof(ReefbackChildEntity);
    }

    private static bool AreEquivalent(List<Copy> touched)
    {
        if (touched.Any(copy => copy.IsProtected || copy.TouchedBelow))
        {
            return false;
        }

        // The only state shown to be the same is the crash fish time of a crash home: the two clients that saw the two copies wrote nearly the same game time
        EntityMetadata? reference = touched[0].Root.Metadata;
        return touched.All(copy => reference is CrashHomeMetadata first && copy.Root.Metadata is CrashHomeMetadata other && Math.Abs(first.SpawnTime - other.SpawnTime) <= CRASH_HOME_TIME_TOLERANCE);
    }

    private static float CrashHomeTime(Copy copy) => copy.Root.Metadata is CrashHomeMetadata crashHome ? crashHome.SpawnTime : 0f;

    private static Copy Best(List<Copy> candidates)
    {
        Copy best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            Copy candidate = candidates[i];
            int order = candidate.Descendants.Count.CompareTo(best.Descendants.Count);
            if (order == 0)
            {
                order = CrashHomeTime(candidate).CompareTo(CrashHomeTime(best));
            }
            if (order == 0)
            {
                order = best.Root.Id.CompareTo(candidate.Root.Id);
            }
            if (order > 0)
            {
                best = candidate;
            }
        }
        return best;
    }

    private static string Why(Copy copy)
    {
        List<string> reasons = [];
        if (copy.IsProtected)
        {
            reasons.Add("a player, base or vehicle references it or something below it");
        }
        if (copy.TouchedRoot)
        {
            reasons.Add($"metadata {copy.Root.Metadata}");
        }
        if (copy.TouchedBelow)
        {
            reasons.Add("a child that is not a plain generated one");
        }
        return string.Join(", ", reasons);
    }
}
