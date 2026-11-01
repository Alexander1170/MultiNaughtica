#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public partial class HarnessConsoleCommands
{
    /// <summary>
    ///     <c>lockercraft &lt;state|craft|take|open|expand|menu|close|stand|scope|claim&gt; [tech]</c> (live check of crafting from lockers, a harness script that is not part of this repository). Works on the crafting station (a
    ///     <c>GhostCrafter</c>) of the sub the player stands in, else the nearest one. Lines, key=value pairs:<br/>
    ///     <c>state</c>: <c>[SyncHarness] lockercraft: state crafter=&lt;name&gt; sub=&lt;name&gt; kind=&lt;base|cyclops|other|none&gt; scope=&lt;id8&gt; enabled=&lt;bool&gt; ingredients=&lt;bool&gt; tech=&lt;T&gt; inv=&lt;n&gt;
    ///     lockers=&lt;n&gt; combined=&lt;n&gt; raw=&lt;n&gt; rawAll=&lt;n&gt; fulfilled=&lt;bool&gt; vanillaFulfilled=&lt;bool&gt; unlocked=&lt;bool&gt; tooltip=&lt;text&gt; containers=&lt;n&gt;</c> plus one <c>lockercraft: locker ...</c> line per container.
    ///     <c>inv</c> is the player's inventory, <c>lockers</c> what <see cref="LockerCrafting" /> counts in the lockers, <c>combined</c> what the menu counts (<see cref="LockerCrafting.GetCount" />),
    ///     <c>raw</c> every StorageContainer under the sub root (no filter), <c>fulfilled</c> what <c>CrafterLogic.IsCraftRecipeFulfilled</c> says while the menu of that crafter is "open" (the
    ///     harness tells <see cref="LockerCrafting" /> which crafter), <c>vanillaFulfilled</c> the same with the option off, <c>tooltip</c> the ingredient texts of the recipe tooltip.<br/>
    ///     <c>craft</c>: the menu's click (<c>ITreeActionReceiver.PerformAction</c>), then <c>lockercraft: craft ...</c> at once and <c>lockercraft: after ...</c> 1 s and 7 s later (inventory,
    ///     lockers, whether the crafter is crafting and what). The claim itself is logged by the product ([CraftFromLockers] lines).<br/>
    ///     <c>take</c>: picks up the finished craft like a click on the crafter, then logs the inventory 3 s later. <c>open</c>/<c>close</c>: the real menu of the crafter; <c>expand</c>: opens the categories down to the recipe and says whether the menu lights it up.
    /// </summary>
    private void LiveCommand_lockercraft(NotificationCenter.Notification n)
    {
        string action = LiveArgString(n, 0, "state").ToLowerInvariant();
        string techText = LiveArgString(n, 1, "Glass");
        if (!Player.main)
        {
            Log.Warn("[SyncHarness] lockercraft: there is no local player");
            return;
        }

        if (action == "stand")
        {
            LockerCraftStand();
            return;
        }

        if (action == "scope" || action == "claim")
        {
            // no crafter needed: the lockers of the base the player is in (or the nearest one) and a claim for the first items of a tech type
            if (!Enum.TryParse(techText, true, out TechType scopeTech))
            {
                Log.Warn($"[SyncHarness] lockercraft: '{techText}' is not a TechType");
                return;
            }

            LockerCraftScopeOrClaim(action == "claim", scopeTech);
            return;
        }

        GhostCrafter crafter = LockerCraftFindCrafter();
        if (!crafter)
        {
            Log.Warn("[SyncHarness] lockercraft: no crafting station found");
            return;
        }

        if (!Enum.TryParse(techText, true, out TechType tech))
        {
            Log.Warn($"[SyncHarness] lockercraft: '{techText}' is not a TechType");
            return;
        }

        switch (action)
        {
            case "state":
                LockerCraftState(crafter, tech);
                break;
            case "craft":
                StartCoroutine(LockerCraftAsync(crafter, tech));
                break;
            case "take":
                StartCoroutine(LockerCraftTakeAsync(crafter, tech));
                break;
            case "open":
                // the menu closes by itself when the player is farther than closeDistance from the crafter: the picture needs it open for a while
                crafter.closeDistance = 1000f;
                crafter.OnHandClick(Player.main.guiHand);
                Log.Info($"[SyncHarness] lockercraft: open crafter={crafter.name} opened={crafter.opened} menuClient={(uGUI.main && uGUI.main.craftingMenu && uGUI.main.craftingMenu.client != null)}");
                break;
            case "expand":
                LockerCraftExpand(tech);
                break;
            case "menu":
                StartCoroutine(LockerCraftMenuAsync(crafter, tech));
                break;
            case "close":
                crafter.Close();
                Log.Info($"[SyncHarness] lockercraft: close crafter={crafter.name}");
                break;
            default:
                Log.Warn($"[SyncHarness] lockercraft: unknown action '{action}' (state, craft, take, open, expand, menu, close, stand, scope, claim)");
                break;
        }
    }

    /// <summary>
    ///     <c>lockercraft stand</c>: puts the player in the middle of the colliders of the nearest base (<c>enterbase</c> puts the player on the origin of the base, which in a fresh room is a corner of
    ///     its geometry: a floor module placed 1 m away is outside the room, and no wall is within reach of a wall module), keeps the base as the current sub.
    ///     <c>[SyncHarness] lockercraft: stand at=&lt;x,y,z&gt; origin=&lt;x,y,z&gt; sub=&lt;name&gt;</c>
    /// </summary>
    private static void LockerCraftStand()
    {
        Base nearest = null;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (candidate && !candidate.isGhost && (!nearest || Vector3.Distance(candidate.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = candidate;
            }
        }

        SubRoot subRoot = nearest ? nearest.GetComponent<SubRoot>() : null;
        if (!subRoot)
        {
            Log.Warn("[SyncHarness] lockercraft: stand: no base nearby");
            return;
        }

        Bounds bounds = new(nearest.transform.position, Vector3.zero);
        foreach (Collider collider in nearest.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled && !collider.isTrigger)
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        Vector3 inside = new(bounds.center.x, nearest.transform.position.y + 0.3f, bounds.center.z);
        Player.main.OnPlayerPositionCheat();
        Player.main.SetPosition(inside);
        Player.main.SetCurrentSub(subRoot, true);
        Log.Info($"[SyncHarness] lockercraft: stand at={LockerCraftPosition(Player.main.transform.position)} origin={LockerCraftPosition(nearest.transform.position)} sub={(Player.main.currentSub ? Player.main.currentSub.name : "none")}");
    }

    private static SubRoot LockerCraftBaseSubRoot()
    {
        if (Player.main.currentSub && Player.main.currentSub.isBase)
        {
            return Player.main.currentSub;
        }

        Base nearest = null;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (candidate && !candidate.isGhost && (!nearest || Vector3.Distance(candidate.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = candidate;
            }
        }

        return nearest ? nearest.GetComponent<SubRoot>() : null;
    }

    private static int lockerCraftClaimNumber = 9000;

    /// <summary>
    ///     <c>lockercraft scope &lt;tech&gt;</c>: what the lockers of the base look like to the product, no crafter needed:
    ///     <c>[SyncHarness] lockercraft: scope sub=&lt;name&gt; kind=&lt;base|none&gt; scope=&lt;id8&gt; containers=&lt;n&gt; tech=&lt;T&gt; lockers=&lt;n&gt; raw=&lt;n&gt;</c> (<c>lockers</c> what the product counts, <c>raw</c> every StorageContainer under the sub root).<br/>
    ///     <c>lockercraft claim &lt;tech&gt;</c>: sends the claim a craft would send, for the first two items of that tech type in the lockers of the base (the order every client picks in), and reports the answer:
    ///     <c>[SyncHarness] lockercraft: claim tech=&lt;T&gt; sent=&lt;n&gt; scope=&lt;id8&gt; claim=&lt;n&gt;</c> then <c>[SyncHarness] lockercraft: claim result=&lt;Outcome&gt; unavailable=&lt;n&gt; claim=&lt;n&gt;</c>. It is the server and
    ///     the scope id of a base that this proves; the craft itself is the same <c>GhostCrafter</c> code as in the Cyclops.
    /// </summary>
    private static void LockerCraftScopeOrClaim(bool claim, TechType tech)
    {
        SubRoot subRoot = LockerCraftBaseSubRoot();
        LockerCrafting.LockerScope scope = LockerCrafting.ScopeOfSubRoot(subRoot);
        string scopeText = scope != null ? scope.ScopeId.ToString().Substring(0, 8) : "none";
        if (!claim)
        {
            Log.Info($"[SyncHarness] lockercraft: scope sub={(subRoot ? subRoot.name : "none")} kind={(subRoot ? (subRoot.isBase ? "base" : subRoot.isCyclops ? "cyclops" : "other") : "none")} scope={scopeText} containers={(scope != null ? scope.Lockers.Count : 0)} tech={tech} lockers={(scope != null ? scope.CountOf(tech) : -1)} raw={LockerCraftRaw(subRoot, tech, false)} enabled={LockerCrafting.Enabled}");
            return;
        }

        if (scope == null)
        {
            Log.Info($"[SyncHarness] lockercraft: claim tech={tech} sent=0 scope=none claim=0 reason=no-scope");
            return;
        }

        // the same pick a craft makes (inventory ignored): the first two items in the order of container and item id
        List<LockerCraftingRules.IngredientLine<TechType>> lines = new() { new LockerCraftingRules.IngredientLine<TechType>(tech, 2) };
        LockerCraftingRules.CraftPlan<TechType> plan = LockerCraftingRules.CreatePlan(lines, _ => 0, scope.ItemsOf);
        List<Nitrox.Model.DataStructures.NitroxId> ids = new();
        foreach (LockerCraftingRules.PickedItem<TechType> picked in plan.FromLockers)
        {
            ids.Add(scope.IdOf(picked.Item.ItemId));
        }

        int claimNumber = ++lockerCraftClaimNumber;
        LockerCrafting.ResponseObserver = response =>
        {
            if (response.ClaimId == claimNumber)
            {
                Log.Info($"[SyncHarness] lockercraft: claim result={response.Outcome} unavailable={response.UnavailableIds.Count} claim={claimNumber}");
            }
        };
        Log.Info($"[SyncHarness] lockercraft: claim tech={tech} sent={ids.Count} scope={scopeText} claim={claimNumber}");
        if (ids.Count > 0)
        {
            Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.Communication.Abstract.IPacketSender>().Send(new Nitrox.Model.Subnautica.Packets.CraftingClaimRequest(claimNumber, scope.ScopeId, ids));
        }
    }

    /// <summary>
    ///     <c>lockercraft menu &lt;tech&gt;</c>: everything for the picture in one command (the chat cannot be typed while the menu is open): opens the real menu of the crafter, expands it down to the recipe, waits for
    ///     the icons, takes the picture the way <c>liveshot now</c> does (tag <c>menu-&lt;kind&gt;-&lt;on|off&gt;</c>, the file is <c>shot-&lt;player&gt;-&lt;tag&gt;.png</c>) and closes the menu again.
    ///     <c>[SyncHarness] lockercraft: menu done tag=&lt;tag&gt; open=&lt;bool&gt;</c> after the <c>expand</c> and <c>liveshot</c> lines.
    /// </summary>
    private IEnumerator LockerCraftMenuAsync(GhostCrafter crafter, TechType tech)
    {
        SubRoot subRoot = crafter.GetComponentInParent<SubRoot>();
        string tag = $"menu-{(subRoot && subRoot.isBase ? "base" : "cyclops")}-{(LockerCrafting.Enabled ? "on" : "off")}";
        crafter.closeDistance = 1000f;
        crafter.OnHandClick(Player.main.guiHand);
        yield return new WaitForSeconds(1f);
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        bool open = menu && menu.client != null;
        Log.Info($"[SyncHarness] lockercraft: open crafter={crafter.name} opened={crafter.opened} menuClient={open}");
        if (open)
        {
            LockerCraftExpand(tech);
            yield return new WaitForSeconds(3f);
            yield return LiveShotRoutine(false, 0f, tag);
        }

        crafter.Close();
        Log.Info($"[SyncHarness] lockercraft: menu done tag={tag} open={open}");
    }

    private static string LockerCraftPosition(Vector3 position)
    {
        return string.Join(",", position.x.ToString("F1", CultureInfo.InvariantCulture), position.y.ToString("F1", CultureInfo.InvariantCulture), position.z.ToString("F1", CultureInfo.InvariantCulture));
    }

    private static GhostCrafter LockerCraftFindCrafter()
    {
        SubRoot currentSub = Player.main.currentSub;
        GhostCrafter best = null;
        float bestScore = float.MaxValue;
        foreach (GhostCrafter candidate in FindObjectsOfType<GhostCrafter>())
        {
            if (!candidate)
            {
                continue;
            }

            SubRoot candidateSub = candidate.GetComponentInParent<SubRoot>();
            float score = (candidateSub && candidateSub == currentSub ? 0f : 1000000f) + Vector3.Distance(candidate.transform.position, Player.main.transform.position);
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static int LockerCraftRaw(SubRoot subRoot, TechType tech, bool all)
    {
        int total = 0;
        if (!subRoot)
        {
            return total;
        }

        foreach (StorageContainer storage in subRoot.GetComponentsInChildren<StorageContainer>(true))
        {
            if (storage && storage.container != null)
            {
                total += all ? storage.container.count : storage.container.GetCount(tech);
            }
        }

        return total;
    }

    private static string LockerCraftTooltip(TechType tech)
    {
        System.Collections.ObjectModel.ReadOnlyCollection<Ingredient> ingredients = TechData.GetIngredients(tech);
        if (ingredients == null)
        {
            return "none";
        }

        List<TooltipIcon> icons = new();
        TooltipFactory.WriteIngredients(ingredients, icons);
        StringBuilder text = new();
        foreach (TooltipIcon icon in icons)
        {
            if (text.Length > 0)
            {
                text.Append('|');
            }

            // the tags of the colour are part of the text; the counts are what matters
            text.Append(System.Text.RegularExpressions.Regex.Replace(icon.text ?? string.Empty, "<[^>]*>", string.Empty).Replace(' ', '_'));
        }

        return text.Length == 0 ? "none" : text.ToString();
    }

    private void LockerCraftState(GhostCrafter crafter, TechType tech)
    {
        SubRoot subRoot = crafter.GetComponentInParent<SubRoot>();
        string kind = !subRoot ? "none" : subRoot.isBase ? "base" : subRoot.isCyclops ? "cyclops" : "other";
        LockerCrafting.LockerScope scope = LockerCrafting.GetScope(crafter);
        bool enabled = LockerCrafting.Enabled;

        // The recipe is <tech>; the thing that is counted in the inventory and in the lockers is its first ingredient (Glass: Quartz)
        TechType counted = TechData.GetIngredients(tech) is { Count: > 0 } recipe ? recipe[0].techType : tech;
        float power = crafter.powerRelay ? crafter.powerRelay.GetPower() : -1f;

        // The menu is not open in the harness: tell the product which crafter the menu would belong to while the numbers are asked for
        int inventoryCount = Inventory.main ? Inventory.main.GetPickupCount(counted) : -1;
        int lockerCount = scope != null ? scope.CountOf(counted) : -1;
        LockerCrafting.MenuCrafterOverride = crafter;
        int combined;
        bool fulfilled;
        string tooltip;
        try
        {
            combined = Inventory.main ? LockerCrafting.GetCount(Inventory.main, counted) : -1;
            fulfilled = CrafterLogic.IsCraftRecipeFulfilled(tech);
            tooltip = LockerCraftTooltip(tech);
        }
        finally
        {
            LockerCrafting.MenuCrafterOverride = null;
        }

        bool optionBefore = LockerCrafting.Enabled;
        LockerCrafting.Enabled = false;
        bool vanillaFulfilled;
        try
        {
            LockerCrafting.MenuCrafterOverride = crafter;
            vanillaFulfilled = CrafterLogic.IsCraftRecipeFulfilled(tech);
        }
        finally
        {
            LockerCrafting.MenuCrafterOverride = null;
            LockerCrafting.Enabled = optionBefore;
        }

        Log.Info($"[SyncHarness] lockercraft: state crafter={crafter.name} sub={(subRoot ? subRoot.name : "none")} kind={kind} scope={(scope != null ? scope.ScopeId.ToString().Substring(0, 8) : "none")} enabled={enabled} ingredients={GameModeUtils.RequiresIngredients()} tech={tech} ingredient={counted} requiresPower={GameModeUtils.RequiresPower()} power={power.ToString("F1", CultureInfo.InvariantCulture)} inv={inventoryCount} lockers={lockerCount} combined={combined} raw={LockerCraftRaw(subRoot, counted, false)} rawAll={LockerCraftRaw(subRoot, counted, true)} fulfilled={fulfilled} vanillaFulfilled={vanillaFulfilled} unlocked={CrafterLogic.IsCraftRecipeUnlocked(tech)} tooltip={tooltip} containers={(scope != null ? scope.Lockers.Count : 0)} inProgress={(crafter.logic ? crafter.logic.inProgress : false)}");

        if (scope == null)
        {
            return;
        }

        foreach (LockerCrafting.LockerSlot locker in scope.Lockers)
        {
            Log.Info($"[SyncHarness] lockercraft: locker id={(locker.Id.Length >= 8 ? locker.Id.Substring(0, 8) : locker.Id)} items={(locker.Container != null ? locker.Container.count : -1)} {counted}={(locker.Container != null ? locker.Container.GetCount(counted) : -1)}");
        }
    }

    /// <summary>
    ///     Expands the open menu down to the recipe of <paramref name="tech" /> (what a player does with the mouse) and says what the menu itself decides about it: <c>available</c> is
    ///     <c>uGUI_CraftingMenu.ActionAvailable</c> of that node, the thing that makes the icon of a recipe light up.
    ///     <c>[SyncHarness] lockercraft: expand tech=&lt;T&gt; found=&lt;bool&gt; depth=&lt;n&gt; available=&lt;bool&gt;</c>
    /// </summary>
    private static void LockerCraftExpand(TechType tech)
    {
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        if (!menu || menu.tree == null || menu.client == null)
        {
            Log.Info($"[SyncHarness] lockercraft: expand tech={tech} found=False depth=0 available=False reason=menu-not-open");
            return;
        }

        List<uGUI_CraftingMenu.Node> path = new();
        uGUI_CraftingMenu.Node target = LockerCraftFindNode(menu.tree, tech, path);
        if (target == null)
        {
            // what the menu does have: the top level and the recipes it lists (the menu only offers the recipes whose blueprint is known)
            List<string> top = new();
            foreach (uGUI_CraftingMenu.Node child in menu.tree)
            {
                top.Add($"{child.id}:{child.childCount}");
            }

            List<string> recipes = new();
            LockerCraftCollectRecipes(menu.tree, recipes);
            // and what the tree of that crafter has before the menu filters it by the known blueprints
            List<string> source = new();
            CraftTree sourceTree = CraftTree.GetTree(menu.treeType);
            if (sourceTree != null)
            {
                LockerCraftCollectSource(sourceTree.nodes, source);
            }

            Log.Info($"[SyncHarness] lockercraft: expand tech={tech} found=False depth=0 available=False reason=no-such-recipe top=[{string.Join(",", top)}] recipes={recipes.Count} listed=[{string.Join(",", recipes.GetRange(0, Mathf.Min(recipes.Count, 50)))}] treeType={menu.treeType} blueprints={GameModeUtils.RequiresBlueprints()} state={KnownTech.GetTechUnlockState(tech)} known={KnownTech.Contains(tech)} sourceRecipes={source.Count} sourceHasTech={source.Contains(tech.ToString())} source=[{string.Join(",", source.GetRange(0, Mathf.Min(source.Count, 60)))}]");
            return;
        }

        foreach (uGUI_CraftingMenu.Node ancestor in path)
        {
            menu.Expand(ancestor);
        }

        Log.Info($"[SyncHarness] lockercraft: expand tech={tech} found=True depth={path.Count} available={menu.ActionAvailable(target)}");
    }

    private static void LockerCraftCollectSource(CraftNode parent, List<string> recipes)
    {
        foreach (CraftNode child in parent)
        {
            if (child.action == TreeAction.Craft)
            {
                recipes.Add(child.techType0.ToString());
            }

            LockerCraftCollectSource(child, recipes);
        }
    }

    private static void LockerCraftCollectRecipes(uGUI_CraftingMenu.Node parent, List<string> recipes)
    {
        foreach (uGUI_CraftingMenu.Node child in parent)
        {
            if (child.action == TreeAction.Craft)
            {
                recipes.Add(child.techType.ToString());
            }

            LockerCraftCollectRecipes(child, recipes);
        }
    }

    private static uGUI_CraftingMenu.Node LockerCraftFindNode(uGUI_CraftingMenu.Node parent, TechType tech, List<uGUI_CraftingMenu.Node> path)
    {
        foreach (uGUI_CraftingMenu.Node child in parent)
        {
            if (child.action == TreeAction.Craft && child.techType == tech)
            {
                return child;
            }

            path.Add(child);
            uGUI_CraftingMenu.Node found = LockerCraftFindNode(child, tech, path);
            if (found != null)
            {
                return found;
            }

            path.RemoveAt(path.Count - 1);
        }

        return null;
    }

    private static IEnumerator LockerCraftAsync(GhostCrafter crafter, TechType tech)
    {
        SubRoot subRoot = crafter.GetComponentInParent<SubRoot>();
        TechType firstIngredient = TechData.GetIngredients(tech) is { Count: > 0 } ingredients ? ingredients[0].techType : TechType.None;
        string Snapshot() => $"result={(Inventory.main ? Inventory.main.GetPickupCount(tech) : -1)} inv={(Inventory.main ? Inventory.main.GetPickupCount(firstIngredient) : -1)} raw={LockerCraftRaw(subRoot, firstIngredient, false)} crafting={(crafter.logic ? crafter.logic.inProgress : false)} craftingTech={(crafter.logic ? crafter.logic.craftingTechType : TechType.None)} ingredient={firstIngredient}";

        string before = Snapshot();
        bool performed = ((ITreeActionReceiver)crafter).PerformAction(tech);
        Log.Info($"[SyncHarness] lockercraft: craft tech={tech} performed={performed} before: {before}");
        yield return new WaitForSeconds(1f);
        Log.Info($"[SyncHarness] lockercraft: after seconds=1 tech={tech} {Snapshot()}");
        yield return new WaitForSeconds(6f);
        Log.Info($"[SyncHarness] lockercraft: after seconds=7 tech={tech} {Snapshot()}");
    }

    private static IEnumerator LockerCraftTakeAsync(GhostCrafter crafter, TechType tech)
    {
        int before = Inventory.main ? Inventory.main.GetPickupCount(tech) : -1;
        bool hasItem = crafter.HasCraftedItem();
        if (crafter.logic)
        {
            crafter.logic.TryPickup();
        }

        yield return new WaitForSeconds(3f);
        int after = Inventory.main ? Inventory.main.GetPickupCount(tech) : -1;
        Log.Info($"[SyncHarness] lockercraft: take tech={tech} hadCraftedItem={hasItem} inv={before}->{after}");
    }
}
#endif
