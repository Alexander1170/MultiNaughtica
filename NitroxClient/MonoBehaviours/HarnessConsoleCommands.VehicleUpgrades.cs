#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;
using UWE;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the live check of the vehicle upgrades (a harness script that is not part of this repository, docs/coop/VEHICLE_UPGRADES.md). All start with <c>vu</c>, take at most two arguments (items are
///     named by their TechType number: 20001..20008, or 2102 for the game's Seamoth Solar Charger) and log one <c>[SyncHarness] vu...: key=value</c> line per fact.
///     <c>vuitems</c> what the registry put into the game; <c>vuspawn</c> the prefab by class id and by TechType and a spawn through the Nitrox spawner; <c>vumenu</c> the real crafting menu of a fabricator
///     tree with the node lit up (and a picture); <c>vucraft give|make</c> the ingredients into the inventory, the craft itself (spend the recipe, add the result); <c>vufind</c> where the items are
///     (inventory, lockers, vehicle slots, Cyclops console, world) and whether any became a loot cube; <c>vudepth</c> the crush depth of every vehicle; <c>vuenter</c>/<c>vuexit</c>/<c>vuburn</c>/<c>vuspeed</c>
///     the afterburner; <c>vucharge</c> the solar chargers.
/// </summary>
public partial class HarnessConsoleCommands
{
    private const string VU = "[SyncHarness] ";

    private static string VuNum(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static CoopItemDefinition VuItem(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (item.TechTypeNumber.ToString(CultureInfo.InvariantCulture) == text || string.Equals(item.Key, text, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// A Cyclops of the game, not Nitrox's VirtualCyclops (the motionless copy at y=500 that the pawns of the players walk in: no NitroxId, no power, no modules). Every command that walks over the
    /// Cyclops must skip it or its lines (id=noid, energy 0/0) mix with the real Cyclops'.
    /// </summary>
    private static bool VuIsRealCyclops(SubRoot subRoot)
    {
        return subRoot && subRoot.isCyclops && subRoot.name != VirtualCyclops.NAME && !subRoot.GetComponent<VirtualCyclops>();
    }

    private static string VuId(GameObject gameObject)
    {
        if (!gameObject)
        {
            return "noid";
        }
        string id = U4IdOfObject(gameObject);
        return string.IsNullOrEmpty(id) ? "noid" : U4Short(id);
    }

    private static string VuGroup(TechType tech)
    {
        return CraftData.GetBuilderIndex(tech, out TechGroup group, out TechCategory category, out int _) ? $"{group}/{category}" : "none";
    }

    private static string VuLock(Component component)
    {
        if (!component || !component.TryGetNitroxId(out NitroxId id))
        {
            return "noid";
        }
        SimulationOwnership ownership = NitroxServiceLocator.LocateService<SimulationOwnership>();
        return ownership.HasExclusiveLock(id) ? "exclusive" : ownership.HasAnyLockType(id) ? "transient" : "none";
    }

    private static bool VuNodeExists(CoopItemDefinition item)
    {
        CraftTree tree = CraftTree.GetTree(item.Tree);
        if (tree == null)
        {
            return false;
        }
        TreeNode parent = tree.nodes;
        foreach (string tab in item.TabPath)
        {
            parent = parent?[tab];
        }
        return parent?[item.Key] != null;
    }

    // ---- vuitems

    private void LiveCommand_vuitems(NotificationCenter.Notification n)
    {
        try
        {
            foreach (CoopItemDefinition item in CoopItems.All)
            {
                TechType tech = item.TechType;
                bool scene = ScenePrefabDatabase.TryGetScenePrefab(item.ClassId, out GameObject scenePrefab) && scenePrefab;
                CraftData.techMapping.TryGetValue(tech, out string mappedClass);
                TechType mappedTech = TechType.None;
                if (mappedClass != null)
                {
                    CraftData.entClassTechTable.TryGetValue(mappedClass, out mappedTech);
                }
                bool parsed = TechTypeExtensions.FromString(item.Key, out TechType fromString, true) && fromString == tech;
                string tooltip = Language.main.Get(item.TooltipKey);
                ReadOnlyCollectionCount(tech, out int recipe);
                Log.Info(string.Concat($"{VU}vuitems: item={item.Key} tech={item.TechTypeNumber} adopted={item.Adopted} asString={TechTypeExtensions.AsString(tech, false)} fromString={parsed} encodeKey={TechTypeExtensions.EncodeKey(tech) ?? "null"} ", $"classId={item.ClassId} scenePrefab={scene} mappedClass={mappedClass ?? "none"} mappedTech={(int)mappedTech} name=\"{Language.main.Get(item.Key)}\" tooltipOk={tooltip.Length > 0 && !tooltip.StartsWith("Tooltip_", StringComparison.Ordinal)} ", $"techData={TechData.Contains(tech)} equipment={TechData.GetEquipmentType(tech)} slot={TechData.GetSlotType(tech)} size={TechData.GetItemSize(tech).x}x{TechData.GetItemSize(tech).y} recipeLines={recipe} node={VuNodeExists(item)} known={KnownTech.Contains(tech)} prereqKnown={KnownTech.Contains(item.Prerequisite)} group={VuGroup(tech)} ", $"state={KnownTech.GetTechUnlockState(tech)} icon={SpriteManager.Get(tech, null) != null} banned={PropulsionCannon.bannedTech.Contains(tech)}"));
            }
            Log.Info(string.Concat($"{VU}vuitems: done count={CoopItems.All.Count} registered={CoopItemRegistry.Registered} enabled={VehicleUpgradeSettings.Enabled} fromStart={VehicleUpgradeSettings.UnlockedFromStart} ", $"step={VuNum(VehicleUpgradeSettings.ExtraDepthPerModule)} blueprints={GameModeUtils.RequiresBlueprints()} burnSpeed={VuNum(VehicleUpgradeSettings.AfterburnerSpeed)} burnSeconds={VuNum(VehicleUpgradeSettings.AfterburnerSeconds)} ", $"cooldown={VuNum(VehicleUpgradeSettings.AfterburnerCooldownSeconds)} energy={VuNum(VehicleUpgradeSettings.AfterburnerEnergy)} solarSeamoth={VuNum(VehicleUpgradeSettings.SolarChargeSeamoth)} solarCyclops={VuNum(VehicleUpgradeSettings.SolarChargeCyclops)}"));
        }
        catch (Exception e)
        {
            Log.Warn($"{VU}vuitems: failed {e.GetType().Name}: {e.Message}");
        }
    }

    private static void ReadOnlyCollectionCount(TechType tech, out int count)
    {
        count = TechData.GetIngredients(tech)?.Count ?? -1;
    }

    // ---- vuspawn <item>

    private void LiveCommand_vuspawn(NotificationCenter.Notification n)
    {
        CoopItemDefinition item = VuItem(LiveArgString(n, 0, ""));
        if (item == null)
        {
            Log.Warn($"{VU}vuspawn: needs an item number (20001..20008, 2102)");
            return;
        }
        StartCoroutine(VuSpawnAsync(item));
    }

    private IEnumerator VuSpawnAsync(CoopItemDefinition item)
    {
        // The two lookups every place that spawns an item again uses, and one spawn through the Nitrox spawner itself
        IPrefabRequest byClass = PrefabDatabase.GetPrefabAsync(item.ClassId);
        yield return byClass;
        bool classOk = byClass.TryGetPrefab(out GameObject classPrefab);
        CoroutineTask<GameObject> byTech = CraftData.GetPrefabForTechTypeAsync(item.TechType, false);
        yield return byTech;
        GameObject techPrefab = byTech.GetResult();
        TaskResult<GameObject> spawned = new();
        yield return DefaultWorldEntitySpawner.CreateGameObject(item.TechType, item.ClassId, new NitroxId(), spawned);
        GameObject spawn = spawned.Get();
        Pickupable pickupable = spawn ? spawn.GetComponent<Pickupable>() : null;
        PrefabIdentifier identifier = spawn ? spawn.GetComponent<PrefabIdentifier>() : null;
        TechTag tag = spawn ? spawn.GetComponent<TechTag>() : null;
        Log.Info(string.Concat($"{VU}vuspawn: item={item.Key} tech={item.TechTypeNumber} byClassId={classOk} byTechType={(techPrefab ? "True" : "False")} spawned={(spawn ? "True" : "False")} spawnTech={(pickupable ? ((int)pickupable.GetTechType()).ToString() : "none")} spawnClassId={(identifier ? identifier.ClassId : "none")} ", $"expectedClassId={item.ClassId} tagType={(tag ? ((int)tag.type).ToString() : "none")} overrideUsed={(pickupable ? pickupable.overrideTechUsed.ToString() : "none")} name={(spawn ? spawn.name : "none")}"));
        if (spawn)
        {
            Destroy(spawn);
        }
    }

    // ---- vumenu <workbench|upgrades|cyclops> <item>

    private sealed class VuTreeReceiver : ITreeActionReceiver
    {
        public bool inProgress => false;
        public float progress => 0f;
        public bool PerformAction(TechType techType) => false;
    }

    private void LiveCommand_vumenu(NotificationCenter.Notification n)
    {
        string treeText = LiveArgString(n, 0, "workbench").ToLowerInvariant();
        CoopItemDefinition item = VuItem(LiveArgString(n, 1, ""));
        CraftTree.Type? type = treeText switch
        {
            "workbench" => CraftTree.Type.Workbench,
            "upgrades" => CraftTree.Type.SeamothUpgrades,
            "cyclops" => CraftTree.Type.CyclopsFabricator,
            _ => null
        };
        if (type == null || item == null)
        {
            Log.Warn($"{VU}vumenu: needs <workbench|upgrades|cyclops> and an item number");
            return;
        }
        StartCoroutine(VuMenuAsync(type.Value, treeText, item));
    }

    private IEnumerator VuMenuAsync(CraftTree.Type type, string treeText, CoopItemDefinition item)
    {
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        if (!menu)
        {
            Log.Warn($"{VU}vumenu: tree={treeText} item={item.TechTypeNumber} found=False reason=no-crafting-menu");
            yield break;
        }

        VuTreeReceiver receiver = new();
        menu.Open(type, receiver);
        yield return new WaitForSeconds(1f);
        bool open = menu.client != null && menu.tree != null;
        bool found = false;
        bool available = false;
        int depth = 0;
        if (open)
        {
            List<uGUI_CraftingMenu.Node> path = new();
            uGUI_CraftingMenu.Node target = LockerCraftFindNode(menu.tree, item.TechType, path);
            if (target != null)
            {
                found = true;
                depth = path.Count;
                foreach (uGUI_CraftingMenu.Node ancestor in path)
                {
                    menu.Expand(ancestor);
                }
                available = menu.ActionAvailable(target);
            }
        }
        Log.Info($"{VU}vumenu: tree={treeText} item={item.TechTypeNumber} open={open} found={found} depth={depth} available={available} nodeInTree={VuNodeExists(item)} blueprints={GameModeUtils.RequiresBlueprints()} known={KnownTech.Contains(item.TechType)} state={KnownTech.GetTechUnlockState(item.TechType)}");
        if (open)
        {
            yield return new WaitForSeconds(3f);
            yield return LiveShotRoutine(false, 0f, $"vumenu-{treeText}-{item.TechTypeNumber}");
        }
        menu.Close(receiver);
        Log.Info($"{VU}vumenu: done tree={treeText} item={item.TechTypeNumber}");
    }

    // ---- vucraft <give|make> <item>

    private void LiveCommand_vucraft(NotificationCenter.Notification n)
    {
        string action = LiveArgString(n, 0, "make").ToLowerInvariant();
        CoopItemDefinition item = VuItem(LiveArgString(n, 1, ""));
        if (item == null || (action != "give" && action != "make"))
        {
            Log.Warn($"{VU}vucraft: needs give|make and an item number");
            return;
        }
        StartCoroutine(action == "give" ? VuCraftGiveAsync(item) : VuCraftMakeAsync(item));
    }

    private IEnumerator VuCraftGiveAsync(CoopItemDefinition item)
    {
        // The ingredients go into the inventory the way lockeradd does it (a new object with an id, which Nitrox tells the server about)
        int given = 0;
        System.Collections.ObjectModel.ReadOnlyCollection<Ingredient> recipe = TechData.GetIngredients(item.TechType);
        if (recipe == null)
        {
            Log.Warn($"{VU}vucraft: given item={item.TechTypeNumber} count=0 recipe=none");
            yield break;
        }
        foreach (Ingredient line in recipe)
        {
            for (int i = 0; i < Mathf.Min(line.amount, 12); i++)
            {
                TaskResult<GameObject> prefabResult = new();
                yield return CraftData.InstantiateFromPrefabAsync(line.techType, prefabResult, false);
                string failure = U4GiveOne(line.techType, prefabResult.Get());
                if (failure != null)
                {
                    Log.Warn($"{VU}vucraft: give item={item.TechTypeNumber} ingredient={(int)line.techType} failed {failure}");
                }
                else
                {
                    given++;
                }
            }
        }
        Log.Info($"{VU}vucraft: given item={item.TechTypeNumber} count={given} recipe={VuRecipe(item)}");
    }

    private static string VuRecipe(CoopItemDefinition item)
    {
        System.Collections.ObjectModel.ReadOnlyCollection<Ingredient> recipe = TechData.GetIngredients(item.TechType);
        return recipe == null ? "none" : string.Join(",", recipe.Select(line => $"{(int)line.techType}x{line.amount}"));
    }

    private static string VuInventoryOf(CoopItemDefinition item)
    {
        ItemsContainer container = Inventory.main.container;
        System.Collections.ObjectModel.ReadOnlyCollection<Ingredient> recipe = TechData.GetIngredients(item.TechType);
        string lines = recipe == null ? "" : string.Join(",", recipe.Select(line => $"{(int)line.techType}={container.GetCount(line.techType)}"));
        return $"{lines},result={container.GetCount(item.TechType)}";
    }

    private IEnumerator VuCraftMakeAsync(CoopItemDefinition item)
    {
        // What a fabricator does when the recipe is started and finished, without the animation: check the recipe, spend it, add the result (CraftData.AddToInventoryAsync spawns it by its TechType)
        string before = VuInventoryOf(item);
        bool fulfilled = CrafterLogic.IsCraftRecipeFulfilled(item.TechType);
        bool consumed = false;
        bool added = false;
        if (fulfilled)
        {
            consumed = CrafterLogic.ConsumeResources(item.TechType);
            TaskResult<GameObject> result = new();
            yield return CraftData.AddToInventoryAsync(item.TechType, result, 1, true, false);
            added = result.Get();
        }
        yield return new WaitForSeconds(1f);
        Log.Info($"{VU}vucraft: made item={item.TechTypeNumber} fulfilled={fulfilled} consumed={consumed} added={added} before=[{before}] after=[{VuInventoryOf(item)}] recipe={VuRecipe(item)} survival={GameModeUtils.RequiresSurvival()}");
    }

    // ---- vufind <item>

    private void LiveCommand_vufind(NotificationCenter.Notification n)
    {
        CoopItemDefinition item = VuItem(LiveArgString(n, 0, ""));
        if (item == null)
        {
            Log.Warn($"{VU}vufind: needs an item number");
            return;
        }

        try
        {
            TechType tech = item.TechType;
            int inventory = Inventory.main.container.GetCount(tech);
            int lockers = 0;
            foreach (StorageContainer storage in FindObjectsOfType<StorageContainer>())
            {
                if (storage.container != null)
                {
                    lockers += storage.container.GetCount(tech);
                }
            }

            List<string> slots = new();
            foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
            {
                VuCollectSlots(vehicle.modules, vehicle is SeaMoth ? "seamoth" : vehicle is Exosuit ? "prawn" : "vehicle", tech, slots);
            }
            foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
            {
                if (VuIsRealCyclops(subRoot) && subRoot.upgradeConsole)
                {
                    VuCollectSlots(subRoot.upgradeConsole.modules, "cyclops", tech, slots);
                }
            }

            int world = 0;
            int loot = 0;
            foreach (Pickupable pickupable in FindObjectsOfType<Pickupable>())
            {
                if (pickupable.inventoryItem != null)
                {
                    continue;
                }
                if (pickupable.GetTechType() == tech)
                {
                    world++;
                    PrefabIdentifier identifier = pickupable.GetComponent<PrefabIdentifier>();
                    if (pickupable.overrideTechUsed && identifier && !CoopItems.IsCoopClassId(identifier.ClassId))
                    {
                        loot++;
                    }
                }
            }
            Log.Info($"{VU}vufind: item={item.TechTypeNumber} inventory={inventory} lockers={lockers} slots={slots.Count} slotList=[{string.Join(",", slots)}] world={world} lootCubes={loot}");
        }
        catch (Exception e)
        {
            Log.Warn($"{VU}vufind: failed {e.GetType().Name}: {e.Message}");
        }
    }

    private static void VuCollectSlots(Equipment equipment, string kind, TechType tech, List<string> slots)
    {
        if (equipment == null)
        {
            return;
        }
        foreach (string slot in U4SlotNames(equipment))
        {
            InventoryItem held = equipment.GetItemInSlot(slot);
            if (held?.item && held.item.GetTechType() == tech)
            {
                slots.Add($"{kind}:{slot}");
            }
        }
    }

    // ---- vuslots

    /// <summary>
    ///     vuslots: for every slot of the nearest Seamoth the module in it, whether its storage input is enabled and its torpedo silo active, and what each must be for that module (a storage input is
    ///     enabled only for a storage module, a silo active only for a torpedo module). The game's <c>SeaMoth.OnUpgradeModuleChange</c> does that update first for every module type.
    /// </summary>
    private void LiveCommand_vuslots(NotificationCenter.Notification n)
    {
        SeaMoth seaMoth = VuNearestSeamoth(100000f);
        if (!seaMoth)
        {
            Log.Warn($"{VU}vuslots: no Seamoth");
            return;
        }
        string[] slots = seaMoth.slotIDs;
        for (int i = 0; i < slots.Length; i++)
        {
            TechType tech = seaMoth.modules.GetTechTypeInSlot(slots[i]);
            SeamothStorageInput input = i < seaMoth.storageInputs.Length ? seaMoth.storageInputs[i] : null;
            GameObject silo = i < seaMoth.torpedoSilos.Length ? seaMoth.torpedoSilos[i] : null;
            Log.Info(string.Concat($"{VU}vuslots: slot={i} name={slots[i]} module={(int)tech} storageEnabled={(input ? input.state.ToString() : "none")} expectedStorage={tech == TechType.VehicleStorageModule} ", $"siloActive={(silo ? silo.activeSelf.ToString() : "none")} expectedSilo={tech == TechType.SeamothTorpedoModule}"));
        }
        Log.Info($"{VU}vuslots: done slots={slots.Length}");
    }

    // ---- vudepth

    private void LiveCommand_vudepth(NotificationCenter.Notification n)
    {
        try
        {
            int count = 0;
            foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
            {
                string kind = vehicle is SeaMoth ? "seamoth" : vehicle is Exosuit ? "prawn" : "vehicle";
                CrushDamage crush = vehicle.crushDamage;
                if (!crush)
                {
                    continue;
                }
                count++;
                Log.Info($"{VU}vudepth: kind={kind} id={VuId(vehicle.gameObject)} crush={VuNum(crush.crushDepth)} extra={VuNum(crush.extraCrushDepth)} baseDepth={VuNum(crush.kBaseCrushDepth)} modules=[{VuModules(vehicle.modules)}] docked={vehicle.docked}");
            }
            foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
            {
                CrushDamage crush = subRoot.GetComponent<CrushDamage>();
                if (!VuIsRealCyclops(subRoot) || !crush)
                {
                    continue;
                }
                count++;
                Log.Info($"{VU}vudepth: kind=cyclops id={VuId(subRoot.gameObject)} crush={VuNum(crush.crushDepth)} extra={VuNum(crush.extraCrushDepth)} baseDepth={VuNum(crush.kBaseCrushDepth)} modules=[{VuModules(subRoot.upgradeConsole ? subRoot.upgradeConsole.modules : null)}] docked=False");
            }
            Log.Info($"{VU}vudepth: done vehicles={count} enabled={VehicleUpgradeSettings.Enabled} step={VuNum(VehicleUpgradeSettings.ExtraDepthPerModule)}");
        }
        catch (Exception e)
        {
            Log.Warn($"{VU}vudepth: failed {e.GetType().Name}: {e.Message}");
        }
    }

    private static string VuModules(Equipment equipment)
    {
        if (equipment == null)
        {
            return "";
        }
        List<string> parts = new();
        foreach (string slot in U4SlotNames(equipment))
        {
            InventoryItem held = equipment.GetItemInSlot(slot);
            if (held?.item)
            {
                parts.Add($"{slot}:{(int)held.item.GetTechType()}");
            }
        }
        return string.Join(",", parts);
    }

    // ---- vuenter, vuexit, vuburn, vuspeed

    private static SeaMoth VuNearestSeamoth(float maxDistance)
    {
        SeaMoth nearest = null;
        foreach (SeaMoth seaMoth in FindObjectsOfType<SeaMoth>())
        {
            float distance = Vector3.Distance(seaMoth.transform.position, Player.main.transform.position);
            if (distance <= maxDistance && (!nearest || distance < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = seaMoth;
            }
        }
        return nearest;
    }

    private static SeaMoth VuPilotedSeamoth()
    {
        foreach (SeaMoth seaMoth in FindObjectsOfType<SeaMoth>())
        {
            if (seaMoth.GetPilotingMode())
            {
                return seaMoth;
            }
        }
        return null;
    }

    private static SeaMoth VuPilotedOrNearestSeamoth(float maxDistance)
    {
        SeaMoth piloted = VuPilotedSeamoth();
        return piloted ? piloted : VuNearestSeamoth(maxDistance);
    }

    private void LiveCommand_vuenter(NotificationCenter.Notification n)
    {
        SeaMoth seaMoth = VuNearestSeamoth(30f);
        if (!seaMoth)
        {
            Log.Warn($"{VU}vuenter: no Seamoth within 30 m");
            return;
        }
        StartCoroutine(VuEnterAsync(seaMoth));
    }

    private IEnumerator VuEnterAsync(SeaMoth seaMoth)
    {
        // The click of the player on the Seamoth (Nitrox asks for the lock first; the answer takes a moment)
        seaMoth.OnHandClick(Player.main.guiHand);
        yield return new WaitForSeconds(3f);
        Log.Info($"{VU}vuenter: seamoth={VuId(seaMoth.gameObject)} piloting={seaMoth.GetPilotingMode()} mode={Player.main.GetMode()}");
    }

    private void LiveCommand_vuexit(NotificationCenter.Notification n)
    {
        Player.main.TryEject();
        StartCoroutine(VuExitLogAsync());
    }

    private IEnumerator VuExitLogAsync()
    {
        yield return new WaitForSeconds(2f);
        Log.Info($"{VU}vuexit: mode={Player.main.GetMode()} piloted={VuPilotedSeamoth() != null}");
    }

    /// <summary>vuburn press: the key press of the quick slot that holds the afterburner (Vehicle.SlotKeyDown) and what came of it. vuburn state: the same without the press.</summary>
    private void LiveCommand_vuburn(NotificationCenter.Notification n)
    {
        string action = LiveArgString(n, 0, "state").ToLowerInvariant();
        SeaMoth seaMoth = VuPilotedOrNearestSeamoth(30f);
        if (!seaMoth)
        {
            Log.Warn($"{VU}vuburn: no Seamoth");
            return;
        }
        if (action == "seq" || action == "seqx")
        {
            // vuburn seq 6,9,13,28: presses the afterburner that many seconds from now (the keys of the scenario are busy holding the forward key meanwhile); seqx: and gets out of the Seamoth
            // 2 seconds after the last press (inside the burn, whatever the delay of the chat commands)
            List<float> times = new();
            foreach (string part in LiveArgString(n, 1, "").Split(','))
            {
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                {
                    times.Add(seconds);
                }
            }
            times.Sort();
            StartCoroutine(VuBurnSequenceAsync(seaMoth, times, action == "seqx"));
            return;
        }
        if (action == "drain")
        {
            VuDrain(seaMoth.gameObject, LiveArgFloat(n, 1, 100f), "seamoth");
            return;
        }
        VuBurnLine(seaMoth, action);
    }

    private IEnumerator VuBurnSequenceAsync(SeaMoth seaMoth, List<float> times, bool exit)
    {
        float start = Time.time;
        for (int i = 0; i < times.Count; i++)
        {
            float wait = start + times[i] - Time.time;
            if (wait > 0f)
            {
                yield return new WaitForSeconds(wait);
            }
            if (!seaMoth)
            {
                yield break;
            }
            VuBurnLine(seaMoth, "press");
        }
        if (exit && seaMoth)
        {
            yield return new WaitForSeconds(2f);
            Player.main.TryEject();
            yield return new WaitForSeconds(1f);
            Log.Info($"{VU}vuburn: seq exit mode={Player.main.GetMode()}");
            VuBurnLine(seaMoth, "state");
        }
        Log.Info($"{VU}vuburn: seq done presses={times.Count}");
    }

    private static void VuBurnLine(SeaMoth seaMoth, string action)
    {

        string[] slots = seaMoth.slotIDs;
        int slotIndex = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (seaMoth.modules.GetTechTypeInSlot(slots[i]) == (TechType)CoopItems.SEAMOTH_AFTERBURNER)
            {
                slotIndex = i;
                break;
            }
        }
        AfterburnerState state = VehicleAfterburners.StateOf(seaMoth);
        seaMoth.GetEnergyValues(out float chargeBefore, out float capacity);
        float cooldownBefore = slotIndex >= 0 ? seaMoth.GetQuickSlotCooldown(slotIndex) : -1f;
        int usesBefore = state.Uses;
        if (action == "press" && slotIndex >= 0)
        {
            seaMoth.SlotKeyDown(slotIndex);
        }
        seaMoth.GetEnergyValues(out float chargeAfter, out _);
        float now = Time.time;
        Log.Info(string.Concat($"{VU}vuburn: action={action} seamoth={VuId(seaMoth.gameObject)} slot={slotIndex} piloting={seaMoth.GetPilotingMode()} phase={state.PhaseAt(now)} uses={usesBefore}->{state.Uses} cooldownBefore={VuNum(cooldownBefore)} cooldownAfter={VuNum(slotIndex >= 0 ? seaMoth.GetQuickSlotCooldown(slotIndex) : -1f)} ", $"energy={VuNum(chargeBefore)}->{VuNum(chargeAfter)}/{VuNum(capacity)} spent={VuNum(chargeBefore - chargeAfter)} speedFactor={VuNum(state.SpeedFactor(now))} lastEnd={state.LastEndReason} settingsSeconds={VuNum(VehicleUpgradeSettings.AfterburnerSeconds)} settingsEnergy={VuNum(VehicleUpgradeSettings.AfterburnerEnergy)}"));
    }

    /// <summary>vuspeed &lt;seconds&gt;: every 0.5 s for that long the speed of the nearest Seamoth, from its rigidbody and from how far it moved, with the phase of the afterburner and its energy.</summary>
    private void LiveCommand_vuspeed(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 20f), 1f, 120f);
        StartCoroutine(VuSpeedAsync(seconds));
    }

    private IEnumerator VuSpeedAsync(float seconds)
    {
        SeaMoth seaMoth = VuPilotedOrNearestSeamoth(200f);
        if (!seaMoth)
        {
            Log.Warn($"{VU}vuspeed: no Seamoth");
            yield break;
        }
        Rigidbody body = seaMoth.GetComponent<Rigidbody>();
        AfterburnerState state = VehicleAfterburners.StateOf(seaMoth);
        Vector3 last = seaMoth.transform.position;
        float lastTime = Time.time;
        float end = Time.time + seconds;
        while (Time.time < end && seaMoth)
        {
            yield return new WaitForSeconds(0.5f);
            float dt = Time.time - lastTime;
            Vector3 position = seaMoth.transform.position;
            float moved = dt > 0f ? Vector3.Distance(position, last) / dt : 0f;
            seaMoth.GetEnergyValues(out float charge, out _);
            Log.Info($"{VU}vuspeed: t={VuNum(Time.time)} body={(body ? VuNum(body.velocity.magnitude) : "none")} moved={VuNum(moved)} phase={state.PhaseAt(Time.time)} factor={VuNum(state.SpeedFactor(Time.time))} energy={VuNum(charge)} piloting={seaMoth.GetPilotingMode()}");
            last = position;
            lastTime = Time.time;
        }
        Log.Info($"{VU}vuspeed: done");
    }

    // ---- vutooltip <craft|blueprint|inventory> <item>, vupda <tab>

    private sealed class VuTooltip : ITooltip
    {
        private readonly Action<TooltipData> fill;

        public VuTooltip(Action<TooltipData> fill)
        {
            this.fill = fill;
        }

        public bool showTooltipOnDrag => false;

        public void GetTooltip(TooltipData tooltip)
        {
            fill(tooltip);
        }
    }

    private static InventoryItem VuFindInstalledOrCarried(TechType tech)
    {
        foreach (InventoryItem candidate in Inventory.main.container)
        {
            if (candidate?.item && candidate.item.GetTechType() == tech)
            {
                return candidate;
            }
        }
        List<Equipment> places = new();
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            places.Add(vehicle.modules);
        }
        foreach (UpgradeConsole console in FindObjectsOfType<UpgradeConsole>())
        {
            places.Add(console.modules);
        }
        foreach (Equipment equipment in places)
        {
            if (equipment == null)
            {
                continue;
            }
            foreach (string slot in U4SlotNames(equipment))
            {
                InventoryItem held = equipment.GetItemInSlot(slot);
                if (held?.item && held.item.GetTechType() == tech)
                {
                    return held;
                }
            }
        }
        return null;
    }

    private static string VuPlain(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace((text ?? "").Replace('"', '\''), "<[^>]*>", "").Replace("\r", "").Replace("\n", " | ");
    }

    /// <summary>
    ///     vutooltip: the tooltip the game builds for the item, as text (what <c>TooltipFactory</c> writes: title and description in the prefix, the actions in the postfix, the ingredients as icons with a
    ///     name) and as a picture (<c>uGUI_Tooltip.Set</c>, then the picture the way <c>liveshot now</c> takes it). craft: the recipe in the crafting menu (the ingredient names come from the
    ///     ingredient name cache), blueprint: the PDA's blueprint list, inventory: the item as it is carried or installed.
    /// </summary>
    private void LiveCommand_vutooltip(NotificationCenter.Notification n)
    {
        string kind = LiveArgString(n, 0, "craft").ToLowerInvariant();
        CoopItemDefinition item = VuItem(LiveArgString(n, 1, ""));
        if (item == null || (kind != "craft" && kind != "blueprint" && kind != "inventory"))
        {
            Log.Warn($"{VU}vutooltip: needs craft|blueprint|inventory and an item number");
            return;
        }
        StartCoroutine(VuTooltipAsync(kind, item));
    }

    private IEnumerator VuTooltipAsync(string kind, CoopItemDefinition item)
    {
        InventoryItem held = kind == "inventory" ? VuFindInstalledOrCarried(item.TechType) : null;
        if (kind == "inventory" && held == null)
        {
            Log.Warn($"{VU}vutooltip: kind={kind} item={item.TechTypeNumber} failed reason=not-carried-or-installed");
            yield break;
        }
        Action<TooltipData> fill = kind switch
        {
            "craft" => data => TooltipFactory.CraftRecipe(item.TechType, false, data),
            "blueprint" => data => TooltipFactory.Blueprint(item.TechType, false, data),
            _ => data => TooltipFactory.InventoryItem(held, data)
        };
        TooltipData text = new();
        fill(text);
        string icons = string.Join("|", text.icons.Select(icon => VuPlain(icon.text)));
        string blueprintName = TechTypeExtensions.GetOrFallback(Language.main, uGUI_BlueprintsTab.blueprintEntryStrings.Get(item.TechType), item.TechType);
        Log.Info(string.Concat($"{VU}vutooltip: kind={kind} item={item.TechTypeNumber} prefix=\"{VuPlain(text.prefix.ToString())}\" postfix=\"{VuPlain(text.postfix.ToString())}\" ", $"icons=\"{icons}\" blueprintListName=\"{VuPlain(blueprintName)}\" group={VuGroup(item.TechType)}"));
        uGUI_Tooltip.Set(new VuTooltip(fill));
        yield return new WaitForSeconds(2f);
        yield return LiveShotRoutine(false, 0f, $"vutooltip-{kind}-{item.TechTypeNumber}");
        uGUI_Tooltip.Clear();
        Log.Info($"{VU}vutooltip: done kind={kind} item={item.TechTypeNumber}");
    }

    /// <summary>vupda &lt;tab&gt;: opens the PDA on that tab (the number of PDATab: 1 Inventory, 2 Journal, 6 Encyclopedia ...), takes the picture and closes it again; the blueprint list is on one of them.</summary>
    private void LiveCommand_vupda(NotificationCenter.Notification n)
    {
        int tab = (int)LiveArgFloat(n, 0, 1f);
        StartCoroutine(VuPdaAsync((PDATab)tab));
    }

    private IEnumerator VuPdaAsync(PDATab tab)
    {
        PDA pda = Player.main.GetPDA();
        bool opened = pda.Open(tab, null, null);
        yield return new WaitForSeconds(3f);
        Log.Info($"{VU}vupda: tab={tab} opened={opened} isOpen={pda.isOpen}");
        yield return LiveShotRoutine(false, 0f, $"vupda-{tab}");
        pda.Close();
        yield return new WaitForSeconds(1f);
        Log.Info($"{VU}vupda: done tab={tab}");
    }

    // ---- vudrain <seamoth|cyclops> <energy>, vumove <depth>

    /// <summary>vudrain: leaves that much energy in the power cells of the nearest Seamoth or Cyclops (the charge setter of the cells, which Nitrox syncs), so that a charger has room to charge.</summary>
    private void LiveCommand_vudrain(NotificationCenter.Notification n)
    {
        string kind = LiveArgString(n, 0, "seamoth").ToLowerInvariant();
        float energy = LiveArgFloat(n, 1, 100f);
        GameObject target = null;
        if (kind == "cyclops")
        {
            foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
            {
                if (VuIsRealCyclops(subRoot))
                {
                    target = subRoot.gameObject;
                    break;
                }
            }
        }
        else
        {
            SeaMoth seaMoth = VuNearestSeamoth(100000f);
            target = seaMoth ? seaMoth.gameObject : null;
        }
        if (!target)
        {
            Log.Warn($"{VU}vudrain: no {kind}");
            return;
        }
        VuDrain(target, energy, kind);
    }

    private static void VuDrain(GameObject target, float energy, string kind)
    {
        List<EnergyMixin> cells = [];
        foreach (EnergyMixin cell in target.GetComponentsInChildren<EnergyMixin>(true))
        {
            // The cell of a vehicle docked in the Cyclops is a child of it: its energy is not the Cyclops' (and it must not be drained with it)
            if (cell.battery != null && !(kind == "cyclops" && cell.GetComponentInParent<Vehicle>()))
            {
                cells.Add(cell);
            }
        }
        int count = cells.Count;
        float before = 0f;
        foreach (EnergyMixin cell in cells)
        {
            before += cell.battery.charge;
        }
        // Scaled, not capped: the cells of a Cyclops that earlier rounds drained cell by cell hold different charges, and a cap per cell would leave less than asked for
        if (before > energy && before > 0f)
        {
            float factor = energy / before;
            foreach (EnergyMixin cell in cells)
            {
                cell.battery.charge *= factor;
            }
        }
        float total = 0f;
        foreach (EnergyMixin cell in cells)
        {
            total += cell.battery.charge;
        }
        Log.Info($"{VU}vudrain: kind={kind} cells={count} energy={VuNum(total)}");
    }

    /// <summary>vumove &lt;depth&gt;: puts the nearest Seamoth straight down (or up) to that depth in metres below the surface, at rest.</summary>
    private void LiveCommand_vumove(NotificationCenter.Notification n)
    {
        SeaMoth seaMoth = VuNearestSeamoth(100000f);
        if (!seaMoth)
        {
            Log.Warn($"{VU}vumove: no Seamoth");
            return;
        }
        float depth = LiveArgFloat(n, 0, 0f);
        // the optional second number: metres to the east (a negative one to the west), so that a range test does not depend on driving by keys
        float east = LiveArgFloat(n, 1, 0f);
        Vector3 position = seaMoth.transform.position;
        Teleport(seaMoth, seaMoth.GetComponent<Rigidbody>(), new Vector3(position.x + east, -depth, position.z));
        Log.Info($"{VU}vumove: seamoth={VuId(seaMoth.gameObject)} depth={VuNum(Ocean.GetDepthOf(seaMoth.gameObject))} east={VuNum(east)} position={VuVector(seaMoth.transform.position)}");
    }

    // ---- vudrop <item>

    /// <summary>vudrop: drops one item of that type from the inventory into the world (Inventory.InternalDropItem, the path of a player who drops something), where Nitrox tells the server about it.</summary>
    private void LiveCommand_vudrop(NotificationCenter.Notification n)
    {
        CoopItemDefinition item = VuItem(LiveArgString(n, 0, ""));
        if (item == null)
        {
            Log.Warn($"{VU}vudrop: needs an item number");
            return;
        }
        Pickupable found = null;
        foreach (InventoryItem candidate in Inventory.main.container)
        {
            if (candidate?.item && candidate.item.GetTechType() == item.TechType)
            {
                found = candidate.item;
                break;
            }
        }
        if (!found)
        {
            Log.Warn($"{VU}vudrop: item={item.TechTypeNumber} failed reason=not-in-inventory");
            return;
        }
        StartCoroutine(VuDropAsync(item, found));
    }

    private IEnumerator VuDropAsync(CoopItemDefinition item, Pickupable pickupable)
    {
        // The game refuses to drop an item inside a submarine, in an escape pod and while piloting (Inventory.CanDropItemHere): say which of them it is when the drop is refused
        Player player = Player.main;
        string why = $"currentSub={(player.currentSub ? player.currentSub.name : "none")} piloting={player.isPiloting} escapePod={player.escapePod.value} waterPark={(player.currentWaterPark ? "yes" : "no")}";
        bool dropped = Inventory.main.InternalDropItem(pickupable, false);
        yield return new WaitForSeconds(3f);
        // wait until the item has come to rest, so that the check knows it is a stable world item
        Rigidbody body = dropped && pickupable ? pickupable.GetComponent<Rigidbody>() : null;
        for (int i = 0; body && i < 20 && body.velocity.sqrMagnitude > 0.01f; i++)
        {
            yield return new WaitForSeconds(1f);
        }
        bool rest = !body || body.velocity.sqrMagnitude <= 0.01f;
        Log.Info($"{VU}vudrop: item={item.TechTypeNumber} dropped={dropped} rest={rest} position={LockerCraftPosition(pickupable.transform.position)} {why}");
    }

    // ---- vucharge [seconds]

    /// <summary>vucharge &lt;seconds&gt;: every 2 s for that long the energy of the nearest Seamoth and of the Cyclops, with what the formula expects (modules, depth, light).</summary>
    private void LiveCommand_vucharge(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 0f), 0f, 300f);
        StartCoroutine(VuChargeAsync(seconds));
    }

    private IEnumerator VuChargeAsync(float seconds)
    {
        float end = Time.time + seconds;
        do
        {
            VuChargeLines();
            if (seconds <= 0f)
            {
                break;
            }
            yield return new WaitForSeconds(2f);
        }
        while (Time.time < end);
        Log.Info($"{VU}vucharge: done");
    }

    private static void VuChargeLines()
    {
        float light = DayNightCycle.main ? DayNightCycle.main.GetLocalLightScalar() : -1f;
        foreach (SeaMoth seaMoth in FindObjectsOfType<SeaMoth>())
        {
            seaMoth.GetEnergyValues(out float charge, out float capacity);
            float depth = Ocean.GetDepthOf(seaMoth.gameObject);
            int modules = seaMoth.modules != null ? seaMoth.modules.GetCount(TechType.SeamothSolarCharge) : 0;
            float expected = modules * SolarChargeRules.EnergyFor(VehicleUpgradeSettings.SolarChargeSeamoth, depth, light, 1f);
            Log.Info($"{VU}vucharge: kind=seamoth id={VuId(seaMoth.gameObject)} energy={VuNum(charge)}/{VuNum(capacity)} modules={modules} depth={VuNum(depth)} light={VuNum(light)} expectedPerSecond={VuNum(expected)} t={VuNum(Time.time)} day={VuNum(DayNightCycle.main ? DayNightCycle.main.GetDayScalar() : -1f)} piloting={seaMoth.GetPilotingMode()} lock={VuLock(seaMoth)}");
        }
        foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
        {
            if (!VuIsRealCyclops(subRoot) || !subRoot.powerRelay)
            {
                continue;
            }
            float depth = Ocean.GetDepthOf(subRoot.gameObject);
            int modules = subRoot.upgradeConsole && subRoot.upgradeConsole.modules != null ? subRoot.upgradeConsole.modules.GetCount((TechType)CoopItems.CYCLOPS_SOLAR) : 0;
            int thermal = subRoot.upgradeConsole && subRoot.upgradeConsole.modules != null ? subRoot.upgradeConsole.modules.GetCount(TechType.CyclopsThermalReactorModule) : 0;
            float expected = SolarChargeRules.CountingModules(modules) * SolarChargeRules.EnergyFor(VehicleUpgradeSettings.SolarChargeCyclops, depth, light, 1f);
            Log.Info($"{VU}vucharge: kind=cyclops id={VuId(subRoot.gameObject)} energy={VuNum(subRoot.powerRelay.GetPower())}/{VuNum(subRoot.powerRelay.GetMaxPower())} modules={modules} thermalModules={thermal} depth={VuNum(depth)} light={VuNum(light)} expectedPerSecond={VuNum(expected)} t={VuNum(Time.time)} lock={VuLock(subRoot)}");
        }
    }
}
#endif
