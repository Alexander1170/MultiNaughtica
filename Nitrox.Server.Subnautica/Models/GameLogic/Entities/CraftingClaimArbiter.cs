using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Decides a claim for crafting from lockers (<see cref="CraftingClaimRequest" />): the client of a crafting player picked the exact items it needs out of the lockers of a base or
///     a Cyclops, and nobody may get an item twice. Each client only knows its own copy of the lockers, so two players who craft at the same moment can pick the same last items.
/// </summary>
/// <remarks>
///     <para>
///         All or nothing. The scope has to be a base (<see cref="BuildEntity" />) or a Cyclops: a claim names the sub root the items are taken from, and a player, a planter or any
///         other vehicle as scope would turn the exclusions below around (the walk up from an item stops at the scope before it looks at what it passes). Every item has to exist and has
///         to hang below the scope without passing through a player (somebody's inventory), a planter, another vehicle, or a machine that is no locker (<see cref="NotLockers" />); if
///         any one does not, nothing is removed and the answer lists the ones that failed, so the client can pick others. The claims run one at a time (<see cref="gate" />), the second
///         of two claims for the same item finds it gone.
///     </para>
///     <para>
///         The removal itself is a callback (the processor passes the world entity manager's destroy, which is what an <c>EntityDestroyed</c> does). Every item is checked once more
///         right before it is removed; an item that vanishes (or moves) in between, or a removal that throws, makes the claim fail and the items taken so far are put back with their
///         parent.
///     </para>
///     <para>
///         <see cref="ClaimAsync" /> also holds the claims of different players apart while the answer is delivered: the removals and the answer of one claim reach everybody before
///         the next claim is decided, so the loser of two claims always hears about the winner's removal before it hears its own refusal.
///     </para>
/// </remarks>
public class CraftingClaimArbiter
{
    /// <summary>How far up the parents of an item are followed; a locker is two or three levels below its base.</summary>
    private const int MAX_DEPTH = 16;

    /// <summary>
    ///     Standalone modules of a base that hold items but are no lockers (aquariums, trash cans), by tech type. The machines that are part of the base itself (bioreactors, reactors, water parks,
    ///     filtration machines, the upgrade console, the moonpool) are <see cref="InteriorPieceEntity" />s and the scanner room is a <see cref="MapRoomEntity" />: no tech type is needed for
    ///     those, the walk up refuses the types. Lockers, wall lockers and fabricators are <see cref="ModuleEntity" />s (see <c>ModuleEntitySpawner.FillObject</c>).
    /// </summary>
    private static readonly HashSet<string> NotLockers = new(StringComparer.Ordinal)
    {
        "Aquarium",
        "Trashcans",
        "LabTrashcan",
    };

    private readonly Lock gate = new();
    private readonly SemaphoreSlim exclusive = new(1, 1);

    /// <summary>
    ///     Decides a claim and delivers its outcome (the removals and the answer) before the next claim is decided.
    /// </summary>
    /// <param name="entityRegistry">See <see cref="Claim" />.</param>
    /// <param name="scopeId">See <see cref="Claim" />.</param>
    /// <param name="itemIds">See <see cref="Claim" />.</param>
    /// <param name="remove">See <see cref="Claim" />.</param>
    /// <param name="deliver">Sends what the decision needs sent; called for every outcome, while no other claim is decided.</param>
    public async Task<CraftingClaimDecision> ClaimAsync(EntityRegistry entityRegistry, NitroxId? scopeId, IReadOnlyList<NitroxId>? itemIds, Func<NitroxId, Entity?> remove, Func<CraftingClaimDecision, Task> deliver)
    {
        await exclusive.WaitAsync();
        try
        {
            CraftingClaimDecision decision = Claim(entityRegistry, scopeId, itemIds, remove);
            await deliver(decision);
            return decision;
        }
        finally
        {
            exclusive.Release();
        }
    }

    /// <param name="entityRegistry">The registry the items are looked up in, and put back into when a removal failed half way.</param>
    /// <param name="scopeId">The base or the Cyclops the items have to be in.</param>
    /// <param name="itemIds">The items, in the order they are spent.</param>
    /// <param name="remove">Removes an entity from the world, or returns null when it is not there any more.</param>
    public CraftingClaimDecision Claim(EntityRegistry entityRegistry, NitroxId? scopeId, IReadOnlyList<NitroxId>? itemIds, Func<NitroxId, Entity?> remove)
    {
        if (scopeId == null || itemIds == null || itemIds.Count == 0 || itemIds.Count > CraftingClaimRequest.MAX_ITEMS || HasDuplicatesOrNulls(itemIds))
        {
            return CraftingClaimDecision.Invalid;
        }

        lock (gate)
        {
            if (!entityRegistry.TryGetEntityById(scopeId, out Entity? scope) || !IsScope(scope))
            {
                return CraftingClaimDecision.Invalid;
            }

            List<NitroxId> unavailable = [];
            foreach (NitroxId itemId in itemIds)
            {
                if (!IsClaimable(entityRegistry, scopeId, itemId))
                {
                    unavailable.Add(itemId);
                }
            }
            if (unavailable.Count > 0)
            {
                return new CraftingClaimDecision(CraftingClaimOutcome.Taken, unavailable, []);
            }

            List<Entity> removed = new(itemIds.Count);
            List<NitroxId?> parentsOfRemoved = new(itemIds.Count);
            foreach (NitroxId itemId in itemIds)
            {
                // The removal takes an entity out of its parent and clears the link, so the parent is remembered for the case that the claim has to be undone
                NitroxId? parentId = entityRegistry.TryGetEntityById(itemId, out Entity? current) ? current.ParentId : null;
                Entity? entity;
                try
                {
                    entity = IsClaimable(entityRegistry, scopeId, itemId) ? remove(itemId) : null;
                }
                catch (Exception)
                {
                    PutBack(entityRegistry, removed, parentsOfRemoved);
                    return CraftingClaimDecision.Invalid;
                }

                if (entity == null)
                {
                    // Gone (or moved) since the check: the claim is all or nothing
                    PutBack(entityRegistry, removed, parentsOfRemoved);
                    return new CraftingClaimDecision(CraftingClaimOutcome.Taken, [itemId], []);
                }
                removed.Add(entity);
                parentsOfRemoved.Add(parentId);
            }

            return new CraftingClaimDecision(CraftingClaimOutcome.Granted, [], removed);
        }
    }

    private static void PutBack(EntityRegistry entityRegistry, List<Entity> removed, List<NitroxId?> parentsOfRemoved)
    {
        for (int i = 0; i < removed.Count; i++)
        {
            removed[i].ParentId = parentsOfRemoved[i];
            entityRegistry.AddOrUpdate(removed[i]);
        }
    }

    /// <summary>
    ///     The sub roots the items of lockers can be claimed from: a base, or a Cyclops (the same test the player parenting uses).
    /// </summary>
    internal static bool IsScope(Entity scope) => scope is BuildEntity || PlayerParentRules.IsCyclops(scope);

    private static bool HasDuplicatesOrNulls(IReadOnlyList<NitroxId> itemIds)
    {
        HashSet<NitroxId> seen = [];
        foreach (NitroxId? itemId in itemIds)
        {
            if (itemId == null || !seen.Add(itemId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    ///     An item can be claimed when it is an item of a container and its parents lead to <paramref name="scopeId" /> without a player (that is somebody's inventory), a planter (that
    ///     holds living plants), a vehicle that is not the scope itself (a vehicle docked in a moonpool or in a Cyclops holds its own storage), a machine that is part of the base
    ///     (bioreactor, reactor, water park, filtration machine: an interior piece; the scanner room) or a module that is no locker (<see cref="NotLockers" />).
    /// </summary>
    internal static bool IsClaimable(EntityRegistry entityRegistry, NitroxId scopeId, NitroxId itemId)
    {
        if (!entityRegistry.TryGetEntityById(itemId, out InventoryItemEntity? item))
        {
            return false;
        }

        NitroxId? parentId = item.ParentId;
        for (int depth = 0; depth < MAX_DEPTH && parentId != null; depth++)
        {
            if (parentId.Equals(scopeId))
            {
                return true;
            }
            if (!entityRegistry.TryGetEntityById(parentId, out Entity? parent) || parent is PlayerEntity or PlanterEntity or VehicleEntity or InteriorPieceEntity or MapRoomEntity || (parent.TechType != null && NotLockers.Contains(parent.TechType.Name)))
            {
                return false;
            }
            parentId = parent.ParentId;
        }
        return false;
    }
}

public readonly record struct CraftingClaimDecision(CraftingClaimOutcome Outcome, IReadOnlyList<NitroxId> Unavailable, IReadOnlyList<Entity> Removed)
{
    public static CraftingClaimDecision Invalid { get; } = new(CraftingClaimOutcome.Invalid, [], []);
}
