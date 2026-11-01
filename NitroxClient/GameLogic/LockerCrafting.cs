using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;
using static NitroxClient.GameLogic.LockerCraftingRules;

namespace NitroxClient.GameLogic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops (item 3). The crafting stations of a base (fabricator, modification station, the vehicle upgrade console of a moonpool; all
/// of them are a <c>GhostCrafter</c>) and the fabricator of a Cyclops also use the items of the lockers (every <c>StorageContainer</c> that is a locker) of the sub root they belong to:
/// <list type="bullet">
/// <item>The numbers the crafting menu works with count the inventory and the lockers together: <c>CrafterLogic.IsCraftRecipeFulfilled</c> (which recipes light up) and
/// <c>TooltipFactory.WriteIngredients</c> (the counts in the tooltip) ask <see cref="GetCount" /> instead of <c>Inventory.GetPickupCount</c> (transpilers).</item>
/// <item>A craft spends the inventory first and the lockers for the rest (<see cref="LockerCraftingRules.CreatePlan{TKey}" />). When the lockers are needed, the craft does not start
/// at once: the exact items are claimed at the server (<see cref="CraftingClaimRequest" />), which removes them for everybody, and only a granted answer starts the craft
/// (<see cref="OnResponse" />). So two players cannot both get the last items of a locker. A refused claim cancels the craft with a short message and takes nothing.</item>
/// <item>After a granted claim the items are gone for good: if the craft cannot start any more (the inventory lost its share meanwhile, the station lost its power) the claimed
/// items are given back to the player (fresh copies, <c>CraftData.AddToInventory</c>) with a message.</item>
/// <item>Whenever the vanilla method of a craft runs (the inventory pays for it, or the station has no power) the lockers are not counted (<see cref="inventoryOnly" />): vanilla
/// would spend nothing for the part that is in the lockers.</item>
/// </list>
/// Everything is off when the server has the option off (<see cref="Enabled" />, set from the join), in the creative modes (no ingredients), and for crafters that are not in a sub root.
/// </summary>
public static class LockerCrafting
{
    /// <summary>The server option <c>CraftFromLockers</c>, as the server told us when we joined.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>The craft of a claim whose answer is awaited. One at a time: the local player crafts one thing at a time.</summary>
    private static Claim pending;

    /// <summary>The claim whose granted craft is running right now: its inventory share is spent by <see cref="OnConsumeResources" />.</summary>
    private static Claim granting;

    /// <summary>
    /// Set while the vanilla <c>GhostCrafter.Craft</c> of a crafter that takes part in crafting from lockers runs without a claim: <see cref="GetCount" /> counts the inventory only,
    /// so that <c>CrafterLogic.ConsumeResources</c> cannot decide on counts it cannot pay for. Reset by <see cref="OnCraftFinished" />.
    /// </summary>
    private static bool inventoryOnly;

    /// <summary>Plans of claims that timed out, so that a late granted answer can still give the items back.</summary>
    private static readonly Dictionary<int, CraftPlan<TechType>> abandoned = [];

    private static int nextClaimId = 1;

    private static int scopeFrame = -1;
    private static Crafter scopeCrafter;
    private static LockerScope scopeCache;

    private static uGUI_CraftingMenu subscribedMenu;
    private static readonly List<ItemsContainer> subscribedContainers = [];

    /// <summary>The recipe node the menu was clicked on, and the frame of the click: <c>uGUI_CraftingMenu.Action</c> calls <c>PerformAction</c> in the same frame.</summary>
    private static uGUI_CraftingMenu.Node actionNode;
    private static int actionFrame = -1;

#if DEBUG
    /// <summary>Debug only: the harness has no open crafting menu, this lets its probes ask what the menu would show for a crafter.</summary>
    internal static GhostCrafter MenuCrafterOverride;

    /// <summary>Debug only: sees every answer to a claim before the product does (the harness claims items without a crafter and reports the answer).</summary>
    internal static Action<CraftingClaimResponse> ResponseObserver;

    /// <summary>Debug only: the lockers of a sub root, whether or not a crafter asks (a base without a fabricator).</summary>
    internal static LockerScope ScopeOfSubRoot(SubRoot subRoot) => subRoot ? BuildScope(subRoot) : null;
#endif

    public sealed class LockerScope
    {
        private readonly List<LockerSlot> lockers = [];
        private readonly Dictionary<string, NitroxId> itemIdsByKey = [];

        public LockerScope(SubRoot subRoot, NitroxId scopeId)
        {
            SubRoot = subRoot;
            ScopeId = scopeId;
        }

        public SubRoot SubRoot { get; }

        /// <summary>The base (the id of its <c>Base</c>) or the Cyclops (the id of its vehicle), the id the server looks the items up under.</summary>
        public NitroxId ScopeId { get; }

        public IReadOnlyList<LockerSlot> Lockers => lockers;

        public void Add(string containerId, ItemsContainer container)
        {
            lockers.Add(new LockerSlot(containerId, container));
        }

        /// <summary>The items of a tech type in the lockers that can be claimed (an item the server does not know by an id cannot).</summary>
        public IReadOnlyList<LockerItemRef> ItemsOf(TechType techType)
        {
            List<LockerItemRef> items = [];
            foreach (LockerSlot locker in lockers)
            {
                if (locker.Container == null || locker.Container.GetCount(techType) <= 0)
                {
                    continue;
                }

                IList<InventoryItem> inContainer = locker.Container.GetItems(techType);
                if (inContainer == null)
                {
                    continue;
                }
                foreach (InventoryItem item in inContainer)
                {
                    Pickupable pickupable = item?.item;
                    if (pickupable && !pickupable.isDestroyed && pickupable.TryGetNitroxId(out NitroxId itemId))
                    {
                        string key = itemId.ToString();
                        itemIdsByKey[key] = itemId;
                        items.Add(new LockerItemRef(locker.Id, key));
                    }
                }
            }
            return items;
        }

        public int CountOf(TechType techType) => ItemsOf(techType).Count;

        public NitroxId IdOf(string itemKey) => itemIdsByKey.TryGetValue(itemKey, out NitroxId id) ? id : null;
    }

    public readonly struct LockerSlot
    {
        public LockerSlot(string id, ItemsContainer container)
        {
            Id = id ?? string.Empty;
            Container = container;
        }

        public string Id { get; }
        public ItemsContainer Container { get; }
    }

    private sealed class Claim
    {
        public int Id;
        public int Attempt;
        public GhostCrafter Crafter;
        public TechType TechType;
        public float Duration;
        public CraftPlan<TechType> Plan;
        public float RequestTime;

        /// <summary>The recipe node of the open menu that was clicked, null when the craft did not come from the menu.</summary>
        public uGUI_CraftingMenu.Node Node;

        public readonly HashSet<string> Unavailable = [];
    }

    /// <summary>
    /// The lockers of the sub root a crafting station belongs to. Null when crafting from lockers does not apply to it: it is off, the station is not in a base or a Cyclops, or it is
    /// the model of the virtual Cyclops. Containers that are not lockers (see <see cref="BuildScope" />) are not part of it.
    /// </summary>
    public static LockerScope GetScope(Crafter crafter)
    {
        if (!Enabled || !crafter || crafter is not GhostCrafter || !GameModeUtils.RequiresIngredients())
        {
            return null;
        }

        // The crafting menu asks for every recipe of a frame: the lockers are looked up once per frame
        if (scopeFrame == Time.frameCount && scopeCrafter == crafter)
        {
            return scopeCache;
        }

        LockerScope scope = null;
        SubRoot subRoot = crafter.GetComponentInParent<SubRoot>();
        if (subRoot && !subRoot.GetComponentInParent<VirtualCyclops>())
        {
            scope = BuildScope(subRoot);
        }

        scopeFrame = Time.frameCount;
        scopeCrafter = crafter;
        scopeCache = scope;
        return scope;
    }

    private static LockerScope BuildScope(SubRoot subRoot)
    {
        if (!subRoot.TryGetNitroxId(out NitroxId scopeId) && !(subRoot is BaseRoot baseRoot && baseRoot.baseComp && baseRoot.baseComp.TryGetNitroxId(out scopeId)))
        {
            return null;
        }

        // The containers of machines that hold living or processed things are no lockers: the growbeds of planters (and of water parks), aquariums (fish), trash cans, filtration machines and
        // scanner rooms name their StorageContainer in a field, and that container may sit next to them rather than below them
        HashSet<StorageContainer> notLockers = [];
        foreach (Planter planter in subRoot.GetComponentsInChildren<Planter>(true))
        {
            notLockers.Add(planter.storageContainer);
        }
        foreach (Aquarium aquarium in subRoot.GetComponentsInChildren<Aquarium>(true))
        {
            notLockers.Add(aquarium.storageContainer);
        }
        foreach (Trashcan trashcan in subRoot.GetComponentsInChildren<Trashcan>(true))
        {
            notLockers.Add(trashcan.storageContainer);
        }
        foreach (FiltrationMachine machine in subRoot.GetComponentsInChildren<FiltrationMachine>(true))
        {
            notLockers.Add(machine.storageContainer);
        }
        foreach (MapRoomFunctionality mapRoom in subRoot.GetComponentsInChildren<MapRoomFunctionality>(true))
        {
            notLockers.Add(mapRoom.storageContainer);
        }

        LockerScope scope = new(subRoot, scopeId);
        foreach (StorageContainer storage in subRoot.GetComponentsInChildren<StorageContainer>(true))
        {
            // The storage of a docked vehicle belongs to the vehicle
            if (storage && storage.container != null && !notLockers.Contains(storage) && !storage.GetComponentInParent<Planter>(true) && !storage.GetComponentInParent<Vehicle>(true))
            {
                scope.Add(storage.TryGetNitroxId(out NitroxId storageId) ? storageId.ToString() : string.Empty, storage.container);
            }
        }

        return scope;
    }

    /// <summary>
    /// The crafting station whose menu is open, if it takes part in crafting from lockers.
    /// </summary>
    private static LockerScope GetOpenMenuScope()
    {
        GhostCrafter crafter = null;
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        if (menu)
        {
            crafter = menu.client as GhostCrafter;
        }
#if DEBUG
        if (!crafter)
        {
            crafter = MenuCrafterOverride;
        }
#endif
        return crafter ? GetScope(crafter) : null;
    }

    /// <summary>
    /// What <c>Inventory.GetPickupCount</c> is replaced by in <c>CrafterLogic.IsCraftRecipeFulfilled</c> and <c>TooltipFactory.WriteIngredients</c> (transpilers): the count of the inventory,
    /// and, while the menu of a crafting station of a base or a Cyclops is open, the items of its lockers on top.
    /// </summary>
    public static int GetCount(Inventory inventory, TechType techType)
    {
        int inventoryCount = inventory.GetPickupCount(techType);
        if (inventoryOnly)
        {
            return inventoryCount;
        }

        LockerScope scope = GetOpenMenuScope();
        return scope == null ? inventoryCount : CombinedCount(inventoryCount, scope.CountOf(techType));
    }

    /// <summary>
    /// <c>uGUI_CraftingMenu.Action</c> (prefix): a recipe was clicked. <c>Crafter.PerformAction</c> follows in the same frame.
    /// </summary>
    public static void OnMenuAction(uGUI_CraftingMenu.Node sender)
    {
        actionNode = sender;
        actionFrame = Time.frameCount;
    }

    /// <summary>
    /// <c>GhostCrafter.Craft</c> was called (the menu's click, <c>Crafter.PerformAction</c>). Returns true when the vanilla method is to run, false when it is not: either the craft waits
    /// for the answer of the server to a claim, or it cannot be done. The vanilla method also runs for the craft that the answer of the server starts (<see cref="granting" />).
    /// </summary>
    public static bool OnCraft(GhostCrafter crafter, TechType techType, float duration)
    {
        if (granting != null)
        {
            return true;
        }

        LockerScope scope = GetScope(crafter);
        if (scope == null)
        {
            return true;
        }

        // Vanilla runs from here on or not at all, and its ConsumeResources must not count on the lockers
        inventoryOnly = true;

        if (pending != null)
        {
            if (pending.Crafter && !HasTimedOut(Time.time - pending.RequestTime))
            {
                // The click of a player who did not wait for the answer
                return false;
            }
            Abandon(pending);
        }

        ReadOnlyCollection<Ingredient> ingredients = TechData.GetIngredients(techType);
        if (ingredients == null || ingredients.Count == 0)
        {
            return true;
        }

        // The menu counted the lockers in, so vanilla must not run unless the inventory is enough: it would spend nothing for the part that is in the lockers
        CraftPlan<TechType> plan = PlanFor(scope, ingredients, null);
        if (!plan.IsComplete)
        {
            ErrorMessage.AddWarning(Language.main.Get("DontHaveNeededIngredients"));
            return false;
        }
        if (!plan.NeedsLockers)
        {
            return true;
        }

        // What the vanilla click checks before it gets here (Crafter.PerformAction) and after the ingredients, so that a craft that cannot start claims nothing
        if (!crafter.logic || crafter.logic.inProgress || crafter.HasCraftedItem())
        {
            return false;
        }
        if (!crafter.HasEnoughPower())
        {
            // Vanilla takes the power first and stops at it
            return true;
        }

        Claim claim = new() { Attempt = 1, Crafter = crafter, TechType = techType, Duration = duration, Node = actionFrame == Time.frameCount ? actionNode : null };
        SendClaim(claim, scope, plan);
        return false;
    }

    /// <summary>
    /// <c>GhostCrafter.Craft</c> ended (finalizer): the lockers count again for the menu.
    /// </summary>
    public static void OnCraftFinished()
    {
        inventoryOnly = false;
    }

    private static CraftPlan<TechType> PlanFor(LockerScope scope, IEnumerable<Ingredient> ingredients, ICollection<string> excluded)
    {
        List<IngredientLine<TechType>> lines = [];
        foreach (Ingredient ingredient in ingredients)
        {
            lines.Add(new IngredientLine<TechType>(ingredient.techType, ingredient.amount));
        }
        Inventory inventory = Inventory.main;
        return CreatePlan(lines, techType => inventory.GetPickupCount(techType), scope.ItemsOf, excluded);
    }

    private static void SendClaim(Claim claim, LockerScope scope, CraftPlan<TechType> plan)
    {
        List<NitroxId> itemIds = [];
        foreach (PickedItem<TechType> picked in plan.FromLockers)
        {
            itemIds.Add(scope.IdOf(picked.Item.ItemId));
        }

        claim.Id = nextClaimId++;
        claim.Plan = plan;
        claim.RequestTime = Time.time;
        pending = claim;
        Log.Info($"[CraftFromLockers] claiming {itemIds.Count} items for {claim.TechType} (try {claim.Attempt}, claim {claim.Id}), {plan.FromInventory.Count} ingredients come from the inventory");
        if (!NitroxServiceLocator.LocateService<IPacketSender>().Send(new CraftingClaimRequest(claim.Id, scope.ScopeId, itemIds)))
        {
            pending = null;
            Log.InGame(Language.main.Get("Nitrox_CraftFromLockersFailed"));
        }
    }

    /// <summary>
    /// The answer of the server to a claim.
    /// </summary>
    public static void OnResponse(CraftingClaimResponse response)
    {
#if DEBUG
        ResponseObserver?.Invoke(response);
#endif
        Claim claim = pending;
        if (claim == null || claim.Id != response.ClaimId)
        {
            // The claim was given up when its answer was too late: its items are gone, so a granted answer gives them back
            if (abandoned.TryGetValue(response.ClaimId, out CraftPlan<TechType> plan))
            {
                abandoned.Remove(response.ClaimId);
                if (response.Outcome == CraftingClaimOutcome.Granted)
                {
                    ReturnClaimedItems(plan);
                }
            }
            return;
        }
        pending = null;
        if (response.Outcome == CraftingClaimOutcome.Disabled)
        {
            // The server's answer is the truth: the menu counts the inventory only from now on
            Enabled = false;
        }

        bool replanComplete = false;
        LockerScope scope = null;
        ReadOnlyCollection<Ingredient> ingredients = null;
        if (response.Outcome == CraftingClaimOutcome.Taken)
        {
            foreach (NitroxId unavailable in response.UnavailableIds)
            {
                claim.Unavailable.Add(unavailable.ToString());
            }
            scope = claim.Crafter ? GetScope(claim.Crafter) : null;
            ingredients = TechData.GetIngredients(claim.TechType);
            replanComplete = scope != null && ingredients != null && PlanFor(scope, ingredients, claim.Unavailable).IsComplete;
        }

        ClaimStep step = NextStep(response.Outcome, claim.Attempt, replanComplete);
        Log.Info($"[CraftFromLockers] claim {claim.Id} for {claim.TechType}: {response.Outcome}, {step}");
        switch (step)
        {
            case ClaimStep.Craft:
                Craft(claim);
                break;
            case ClaimStep.Retry:
                claim.Attempt++;
                CraftPlan<TechType> retryPlan = PlanFor(scope, ingredients, claim.Unavailable);
                if (retryPlan.NeedsLockers)
                {
                    SendClaim(claim, scope, retryPlan);
                }
                else
                {
                    // The inventory pays for all of it by now (the player picked up what was missing while the answer was on its way): nothing to claim
                    claim.Plan = retryPlan;
                    Craft(claim);
                }
                break;
            default:
                Log.InGame(Language.main.Get(response.Outcome switch
                {
                    CraftingClaimOutcome.Taken => "Nitrox_CraftFromLockersTaken",
                    CraftingClaimOutcome.Disabled => "Nitrox_CraftFromLockersDisabled",
                    _ => "Nitrox_CraftFromLockersFailed"
                }));
                break;
        }
    }

    /// <summary>
    /// The items are ours: start the craft the way the menu's click would have. The vanilla <c>GhostCrafter.Craft</c> takes the power, <see cref="OnConsumeResources" /> spends the
    /// inventory's share (the lockers' share is gone already), and the crafting station starts.
    /// </summary>
    private static void Craft(Claim claim)
    {
        bool inventoryHoldsItsShare = true;
        foreach (KeyValuePair<TechType, int> share in claim.Plan.FromInventory)
        {
            inventoryHoldsItsShare &= Inventory.main && Inventory.main.GetPickupCount(share.Key) >= share.Value;
        }

        // The answer can come after another player finished a craft at the same station and before anybody picked it up: starting a craft would overwrite it
        bool started = false;
        if (inventoryHoldsItsShare && claim.Crafter && claim.Crafter.logic && !claim.Crafter.logic.inProgress && !claim.Crafter.HasCraftedItem())
        {
            granting = claim;
            try
            {
                claim.Crafter.Craft(claim.TechType, claim.Duration);
            }
            catch (Exception exception)
            {
                // The items are gone from the world: whatever went wrong in the vanilla method or another patch, the check below decides about the refund
                Log.Error(exception, $"[CraftFromLockers] the craft of {claim.TechType} threw after the claim was granted");
            }
            finally
            {
                granting = null;
            }
            started = claim.Crafter.logic && claim.Crafter.logic.inProgress && claim.Crafter.logic.craftingTechType == claim.TechType;
        }

        if (ShouldReturnClaimedItems(inventoryHoldsItsShare, started))
        {
            Log.Warn($"[CraftFromLockers] the craft of {claim.TechType} did not start after the claim (inventory share held: {inventoryHoldsItsShare}), giving the items back");
            ReturnClaimedItems(claim.Plan);
            if (claim.Plan.NeedsLockers)
            {
                // A retry that needed no claim at all (the inventory caught up) has nothing to give back
                Log.InGame(Language.main.Get("Nitrox_CraftFromLockersReturned"));
            }
            return;
        }

        RestoreMenuLock(claim);
    }

    /// <summary>
    /// The click of the menu would have set the menu up for a craft that started (<c>uGUI_CraftingMenu.Action</c>: it locks the menu and remembers the node, which shows the progress and
    /// opens the menu again when the craft is done). Our craft started after the click, so that step is done here. <c>Crafter.OnCraftingBegin</c> has already locked or closed the menu.
    /// </summary>
    private static void RestoreMenuLock(Claim claim)
    {
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        if (!menu || claim.Node == null || !ReferenceEquals(menu.client, claim.Crafter))
        {
            return;
        }

        if (menu.tree != null)
        {
            menu.SetLocked(true);
        }
        menu.craftedNode = claim.Node;
    }

    /// <summary>
    /// <c>CrafterLogic.ConsumeResources</c> (prefix). During the craft that a granted claim starts, only the inventory's share is spent; the lockers' items were removed by the server.
    /// Returns false (vanilla must not run) for that craft.
    /// </summary>
    public static bool OnConsumeResources(TechType techType, ref bool result)
    {
        Claim claim = granting;
        if (claim == null || claim.TechType != techType)
        {
            return true;
        }

        ReadOnlyCollection<Ingredient> ingredients = TechData.GetIngredients(techType);
        List<IngredientLine<TechType>> lines = [];
        foreach (Ingredient ingredient in ingredients)
        {
            lines.Add(new IngredientLine<TechType>(ingredient.techType, ingredient.amount));
        }

        foreach (IngredientLine<TechType> line in MergeLines(lines))
        {
            int fromInventory = claim.Plan.InventoryShare(line.Key);
            for (int i = 0; i < line.Amount; i++)
            {
                if (i < fromInventory && !Inventory.main.DestroyItem(line.Key, true))
                {
                    Log.Error($"[CraftFromLockers] unable to remove one {line.Key} from the inventory to craft {techType}");
                }
                // Like vanilla: the icon of every ingredient that is used flies away
                uGUI_IconNotifier.main.Play(line.Key, uGUI_IconNotifier.AnimationType.To, null);
            }
        }

        result = true;
        return false;
    }

    private static void Abandon(Claim claim)
    {
        Log.Warn($"[CraftFromLockers] no answer to claim {claim.Id} for {claim.TechType} after {Time.time - claim.RequestTime:F0} s, giving it up");
        if (abandoned.Count > 8)
        {
            abandoned.Clear();
        }
        abandoned[claim.Id] = claim.Plan;
        pending = null;
    }

    /// <summary>
    /// Gives the claimed items back as fresh items in the inventory (or at the player's feet when it is full): the claimed ones are gone from the world.
    /// </summary>
    private static void ReturnClaimedItems(CraftPlan<TechType> plan)
    {
        Dictionary<TechType, int> counts = [];
        foreach (PickedItem<TechType> picked in plan.FromLockers)
        {
            counts[picked.Key] = counts.TryGetValue(picked.Key, out int count) ? count + 1 : 1;
        }
        foreach (KeyValuePair<TechType, int> pair in counts)
        {
            CraftData.AddToInventory(pair.Key, pair.Value, false, true);
        }
    }

    /// <summary>
    /// Lets the open crafting menu notice when the items of a locker change (the other player takes or puts items): the menu only listens to the inventory.
    /// </summary>
    public static void OnMenuOpened(uGUI_CraftingMenu menu)
    {
        OnMenuClosed();
        LockerScope scope = GetOpenMenuScope();
        if (scope == null)
        {
            return;
        }

        subscribedMenu = menu;
        foreach (LockerSlot locker in scope.Lockers)
        {
            if (locker.Container != null)
            {
                locker.Container.onAddItem += menu.InventoryChanged;
                locker.Container.onRemoveItem += menu.InventoryChanged;
                subscribedContainers.Add(locker.Container);
            }
        }
    }

    /// <summary>
    /// The menu was deselected (the end of every use of it; <c>uGUI_CraftingMenu.Close</c> is called for any crafter and does nothing unless it is the menu's own).
    /// </summary>
    public static void OnMenuClosed()
    {
        if (subscribedMenu)
        {
            foreach (ItemsContainer container in subscribedContainers)
            {
                container.onAddItem -= subscribedMenu.InventoryChanged;
                container.onRemoveItem -= subscribedMenu.InventoryChanged;
            }
        }
        subscribedContainers.Clear();
        subscribedMenu = null;
    }
}
