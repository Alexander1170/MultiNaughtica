using System;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops (item 3): the decisions, free of Unity types (like <see cref="PdaBadgeRules" />) so that they can be tested. A crafting
/// station inside a base or a Cyclops counts the items of the lockers of that sub root as well as the player's inventory (<see cref="CombinedCount" />); a craft spends the inventory first
/// and takes what is still missing out of the lockers, item by item in a fixed order (<see cref="CreatePlan{TKey}" />). The items of the lockers are claimed at the server before the craft
/// starts (<c>CraftingClaimRequest</c>), and what the answer means for the craft is decided by <see cref="NextStep" /> and <see cref="ShouldReturnClaimedItems" />.
/// <para>
/// The ingredient key is generic: the game passes its <c>TechType</c>, the tests a string. Only net472-safe APIs are used (the client is built for net472 as well).
/// </para>
/// </summary>
public static class LockerCraftingRules
{
    /// <summary>
    /// Times a claim is tried again with other items after the server said that some of its items were taken (the other player's removal reaches us before the answer, so the second
    /// pick already sees the lockers without them). A third try would only be a third race.
    /// </summary>
    public const int MAX_ATTEMPTS = 2;

    /// <summary>
    /// Seconds after which a claim whose answer never came is given up (the craft is cancelled and the player can try again). The server would have removed the items already if the
    /// answer was lost on its way; the answer of a granted claim is not lost with the connection, a lost connection ends the session.
    /// </summary>
    public const float ANSWER_TIMEOUT = 15f;

    public readonly struct IngredientLine<TKey>
    {
        public IngredientLine(TKey key, int amount)
        {
            Key = key;
            Amount = amount;
        }

        public TKey Key { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// An item that lies in a locker. The ids are the item's and the container's entity ids (strings here, <c>NitroxId</c> in the game); they only give the picks their order.
    /// </summary>
    public readonly struct LockerItemRef
    {
        public LockerItemRef(string containerId, string itemId)
        {
            ContainerId = containerId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
        }

        public string ContainerId { get; }
        public string ItemId { get; }
    }

    /// <summary>An item picked out of a locker for one ingredient.</summary>
    public readonly struct PickedItem<TKey>
    {
        public PickedItem(TKey key, LockerItemRef item)
        {
            Key = key;
            Item = item;
        }

        public TKey Key { get; }
        public LockerItemRef Item { get; }
    }

    public sealed class CraftPlan<TKey>
    {
        public CraftPlan(bool isComplete, List<KeyValuePair<TKey, int>> fromInventory, List<PickedItem<TKey>> fromLockers, List<KeyValuePair<TKey, int>> missing)
        {
            IsComplete = isComplete;
            FromInventory = fromInventory;
            FromLockers = fromLockers;
            Missing = missing;
        }

        /// <summary>Inventory and lockers together have everything the recipe needs.</summary>
        public bool IsComplete { get; }

        /// <summary>How many units of each ingredient come out of the inventory (only those with at least one).</summary>
        public List<KeyValuePair<TKey, int>> FromInventory { get; }

        /// <summary>The items to claim, in the order they are spent.</summary>
        public List<PickedItem<TKey>> FromLockers { get; }

        /// <summary>What is missing even with the lockers, per ingredient.</summary>
        public List<KeyValuePair<TKey, int>> Missing { get; }

        /// <summary>The inventory alone is not enough: the craft has to claim items first.</summary>
        public bool NeedsLockers => FromLockers.Count > 0;

        /// <summary>How many units of <paramref name="key" /> the inventory has to give.</summary>
        public int InventoryShare(TKey key)
        {
            EqualityComparer<TKey> comparer = EqualityComparer<TKey>.Default;
            foreach (KeyValuePair<TKey, int> pair in FromInventory)
            {
                if (comparer.Equals(pair.Key, key))
                {
                    return pair.Value;
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// What the user sees and what a craft may use: the inventory's count plus the lockers' count of one ingredient. Negative counts are nothing, and the sum does not overflow.
    /// </summary>
    public static int CombinedCount(int inventoryCount, int lockerCount)
    {
        long sum = (long)Math.Max(inventoryCount, 0) + Math.Max(lockerCount, 0);
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>
    /// The recipe with an ingredient that is listed twice merged into one line, in the order of the first mention. Lines without an amount are dropped.
    /// </summary>
    public static List<IngredientLine<TKey>> MergeLines<TKey>(IEnumerable<IngredientLine<TKey>> lines)
    {
        List<IngredientLine<TKey>> merged = [];
        EqualityComparer<TKey> comparer = EqualityComparer<TKey>.Default;
        foreach (IngredientLine<TKey> line in lines)
        {
            if (line.Amount <= 0)
            {
                continue;
            }

            int index = merged.FindIndex(existing => comparer.Equals(existing.Key, line.Key));
            if (index < 0)
            {
                merged.Add(line);
            }
            else
            {
                merged[index] = new IngredientLine<TKey>(line.Key, (int)Math.Min((long)merged[index].Amount + line.Amount, int.MaxValue));
            }
        }
        return merged;
    }

    /// <summary>
    /// Spends the inventory first and the lockers for the rest. From the lockers the items are taken in a fixed order, by container id and then by item id (ordinal), so that every client
    /// that looks at the same lockers picks the same items; items in <paramref name="excludedItemIds" /> (the ones the server said are gone) are skipped.
    /// </summary>
    /// <param name="lines">The recipe.</param>
    /// <param name="inventoryCount">How many of an ingredient the player's inventory holds.</param>
    /// <param name="lockerItems">The items of an ingredient that lie in the lockers of the sub root, in any order.</param>
    /// <param name="excludedItemIds">Items that must not be picked; may be null.</param>
    public static CraftPlan<TKey> CreatePlan<TKey>(IEnumerable<IngredientLine<TKey>> lines, Func<TKey, int> inventoryCount, Func<TKey, IReadOnlyList<LockerItemRef>> lockerItems, ICollection<string> excludedItemIds = null)
    {
        List<KeyValuePair<TKey, int>> fromInventory = [];
        List<PickedItem<TKey>> fromLockers = [];
        List<KeyValuePair<TKey, int>> missing = [];
        HashSet<string> picked = [];

        foreach (IngredientLine<TKey> line in MergeLines(lines))
        {
            int fromInventoryCount = Math.Min(Math.Max(inventoryCount(line.Key), 0), line.Amount);
            if (fromInventoryCount > 0)
            {
                fromInventory.Add(new KeyValuePair<TKey, int>(line.Key, fromInventoryCount));
            }

            int stillNeeded = line.Amount - fromInventoryCount;
            if (stillNeeded == 0)
            {
                continue;
            }

            List<LockerItemRef> candidates = [];
            HashSet<string> listed = [];
            IReadOnlyList<LockerItemRef> inLockers = lockerItems(line.Key);
            if (inLockers != null)
            {
                foreach (LockerItemRef candidate in inLockers)
                {
                    // An item that a lookup lists twice is still one item (a claim with the same item twice is invalid)
                    if (candidate.ItemId.Length > 0 && (excludedItemIds == null || !excludedItemIds.Contains(candidate.ItemId)) && !picked.Contains(candidate.ItemId) && listed.Add(candidate.ItemId))
                    {
                        candidates.Add(candidate);
                    }
                }
            }
            candidates.Sort(CompareByContainerThenItem);

            int taken = 0;
            foreach (LockerItemRef candidate in candidates)
            {
                if (taken == stillNeeded)
                {
                    break;
                }
                fromLockers.Add(new PickedItem<TKey>(line.Key, candidate));
                picked.Add(candidate.ItemId);
                taken++;
            }

            if (taken < stillNeeded)
            {
                missing.Add(new KeyValuePair<TKey, int>(line.Key, stillNeeded - taken));
            }
        }

        // A claim has a size limit at the server; a recipe that needs more than that from the lockers cannot be claimed
        bool complete = missing.Count == 0 && fromLockers.Count <= CraftingClaimRequest.MAX_ITEMS;
        return new CraftPlan<TKey>(complete, fromInventory, fromLockers, missing);
    }

    private static int CompareByContainerThenItem(LockerItemRef a, LockerItemRef b)
    {
        int byContainer = string.CompareOrdinal(a.ContainerId, b.ContainerId);
        return byContainer != 0 ? byContainer : string.CompareOrdinal(a.ItemId, b.ItemId);
    }

    public enum ClaimStep
    {
        /// <summary>The items are ours: start the craft.</summary>
        Craft,
        /// <summary>Some items were taken but the lockers still have enough: claim other items.</summary>
        Retry,
        /// <summary>Nothing was removed and nothing can be done: tell the player and stop.</summary>
        Cancel,
    }

    /// <summary>
    /// What a server answer means for the craft.
    /// </summary>
    /// <param name="outcome">The answer.</param>
    /// <param name="attempt">The number of this claim's try, starting at 1.</param>
    /// <param name="replanComplete">Inventory and the lockers as they look now (without the items the server said are gone) still have everything the recipe needs.</param>
    public static ClaimStep NextStep(CraftingClaimOutcome outcome, int attempt, bool replanComplete)
    {
        switch (outcome)
        {
            case CraftingClaimOutcome.Granted:
                return ClaimStep.Craft;
            case CraftingClaimOutcome.Taken:
                return attempt < MAX_ATTEMPTS && replanComplete ? ClaimStep.Retry : ClaimStep.Cancel;
            default:
                return ClaimStep.Cancel;
        }
    }

    /// <summary>
    /// After a granted claim the items are gone from the lockers for good, so the craft has to happen or the items go back to the player: it does not happen when the inventory lost the
    /// part of the ingredients it had been counted for in the meantime (the player dropped or stored it while the answer was on its way), or when the crafting station cannot take the
    /// craft any more (it is busy, it lost power).
    /// </summary>
    public static bool ShouldReturnClaimedItems(bool inventoryStillHasItsShare, bool craftStarted)
    {
        return !inventoryStillHasItsShare || !craftStarted;
    }

    /// <summary>
    /// Whether a claim that waits since <paramref name="secondsSinceRequest" /> seconds is given up.
    /// </summary>
    public static bool HasTimedOut(float secondsSinceRequest)
    {
        return secondsSinceRequest >= ANSWER_TIMEOUT;
    }
}
