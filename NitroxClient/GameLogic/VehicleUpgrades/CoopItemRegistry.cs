using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// Puts the rows of <see cref="CoopItems" /> into the game, with no Harmony patch: the game keeps everything about an item in plain static dictionaries and lists, which this class fills.
/// <list type="bullet">
/// <item>The TechType: the five caches of <c>TechTypeExtensions</c> (name, lowercase name, parse both ways, notification key), the tooltip and ingredient name caches, the repulsion cannon's ban list.</item>
/// <item>The prefab: a clone of a vanilla module prefab with the new ClassId and TechTag, kept under a hidden root that survives scene changes, registered in <c>ScenePrefabDatabase</c> (that is what
/// <c>PrefabDatabase.GetPrefabAsync(classId)</c> asks first) and in <c>CraftData.techMapping</c> / <c>entClassTechTable</c> (what <c>GetPrefabForTechTypeAsync</c> asks). Every place that spawns
/// the item again after a restart or a rejoin (inventory, locker, vehicle slot, Cyclops console, dropped in the world) goes through one of the two.</item>
/// <item>The icon: the sprite of a vanilla item, with its tier drawn into a corner.</item>
/// <item>The recipe, sizes and slot types: entries of <c>TechData</c>, which the game wipes and reloads at every game load.</item>
/// <item>The craft nodes, the unlock rules, the depth tables and the language strings: they depend on the server's settings and follow <see cref="VehicleUpgradeSettings.Changed" />.</item>
/// </list>
/// Everything is idempotent per table (nothing runs a second time that is already in place), because the game rebuilds some tables at every game load and keeps others for the whole process.
/// The server and the packets never see any of it: they carry the TechType as the number and the ClassId as text.
/// </summary>
public static class CoopItemRegistry
{
    private const string ITEMS_ATLAS = "Items";
    private const float SPRITE_WAIT_SECONDS = 20f;

    private static GameObject templatesRoot;
    private static readonly Dictionary<int, Sprite> icons = new();
    private static readonly List<Texture2D> iconTextures = [];
    private const int MAX_ATTEMPTS = 3;

    private static bool subscribed;
    private static bool registered;
    private static bool complete;
    private static int attempts;
    private static int failures;

    // What this class added to the game's static tables: only that is ever removed again. An adopted item has a craft node and perhaps an unlock rule of the game's own.
    private static readonly HashSet<TechType> addedNodes = [];
    private static readonly HashSet<TechType> addedRules = [];
    private static readonly HashSet<TechType> addedGroupEntries = [];

    /// <summary>
    /// True once <see cref="EnsureRegisteredAsync" /> ran through without a failed step in this session, or after the last of its attempts: what asks for it (the entity spawner, the initial sync) runs
    /// the registration again while it is false, so that a step that failed once is tried again before the first entity of the server is spawned as a loot cube.
    /// </summary>
    public static bool Registered => registered && (complete || attempts >= MAX_ATTEMPTS);

    /// <summary>
    /// Registers everything that does not depend on the server's settings and applies the settings known so far. Runs before the client joins the server (so before any entity can be spawned),
    /// once more as the first step of the initial sync, and is a no-op for every table that is already in place. A failing step is logged and does not stop the others, so that a problem with
    /// one icon never keeps a player out of the game.
    /// </summary>
    public static IEnumerator EnsureRegisteredAsync()
    {
        Subscribe();
        attempts++;
        failures = 0;

        RegisterTechTypes();
        yield return RegisterPrefabsAsync();
        yield return RegisterIconsAsync();
        Guarded("TechData", RegisterTechData);
        complete = failures == 0;
        if (!complete)
        {
            Log.Error($"[VehicleUpgrades] {failures} steps of the registration of the new items failed (attempt {attempts} of {MAX_ATTEMPTS}){(attempts >= MAX_ATTEMPTS ? ": giving up, those items will be loot cubes" : ", trying again before they are spawned")}");
            if (attempts >= MAX_ATTEMPTS)
            {
                Log.InGame("Some of the new vehicle upgrade items could not be set up. See the log of the game.");
            }
        }
        // Only what does not depend on the server's settings: they are not here yet. The craft nodes, the unlock rules and the depth tables follow when they arrive (VehicleUpgradeSettings.Changed),
        // so that a server that has switched the vehicle upgrades off never sees the new blueprints unlocked by a join that ran with the defaults.
        registered = true;
        Guarded("language", ApplyLanguage, false);
    }

    /// <summary>Takes everything that depends on the settings of the server to the state those settings ask for. Does nothing until this session's settings have arrived.</summary>
    public static void ApplySettings()
    {
        if (!registered || !VehicleUpgradeSettings.Received)
        {
            return;
        }

        VehicleUpgradeUnlockRules.Plan plan = VehicleUpgradeUnlockRules.CreatePlan(VehicleUpgradeSettings.Enabled, VehicleUpgradeSettings.UnlockedFromStart, CoopItems.All);
        Guarded("craft nodes", () => SyncCraftNodes(plan.ShowNodes.Count > 0));
        Guarded("blueprint groups", () => SyncBlueprintGroups(plan.ShowNodes.Count > 0));
        Guarded("depth tables", () => SyncDepthTables(VehicleUpgradeSettings.Enabled));
        Guarded("unlock rules", () => SyncUnlocks(plan));
        // The slot lists of the vehicles, before any vehicle exists (the processor that spawns them depends on this one); the master switch takes the extra slots with it
        Guarded("extra module slots", () => VehicleSlotLists.Apply(VehicleUpgradeSettings.Enabled ? VehicleUpgradeSettings.ExtraModuleSlots : 0));
        Guarded("language", ApplyLanguage);
    }

    /// <summary>Writes names and tooltips (with the numbers of the server's settings) into the language dictionary. Also called after every language load, which clears the dictionary.</summary>
    public static void ApplyLanguage()
    {
        if (Language.main)
        {
            CoopItemText.Apply(Language.main.strings);
        }
    }

    /// <summary>
    /// The session ended: nothing of the new items may stay reachable in the next game of the same process (a vanilla single player game): the craft nodes and the unlock rules go, the depth tables
    /// lose the new entries. The TechType, prefab and icon registrations stay; without TechData entries and blueprints nobody can obtain the items.
    /// </summary>
    public static void OnSessionEnded()
    {
        registered = false;
        complete = false;
        attempts = 0;
        PatchProbe.Reset();
        Guarded("craft nodes", () => SyncCraftNodes(false));
        Guarded("blueprint groups", () => SyncBlueprintGroups(false));
        Guarded("depth tables", () => SyncDepthTables(false));
        Guarded("unlock rules", () => SyncUnlocks(VehicleUpgradeUnlockRules.CreatePlan(false, false, CoopItems.All)));
        Guarded("extra module slots", VehicleSlotLists.Reset);
        VehicleUpgradeSettings.Reset();
    }

    private static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        VehicleUpgradeSettings.Changed += ApplySettings;
        Multiplayer.OnAfterMultiplayerEnd += OnSessionEnded;
    }

    // ---- TechTypes

    private static void RegisterTechTypes()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (!item.Adopted)
            {
                CoopItemDefinition current = item;
                Guarded($"TechType of {item.Key}", () => RegisterTechType(current));
            }
        }
    }

    private static void RegisterTechType(CoopItemDefinition item)
    {
        // (An adopted item is a value of the game's enum: its name is in every cache already)
        TechType techType = item.TechType;
        string key = item.NitroxName;

        // Indexers, not Add: the game's own constructor fills the key tables with Add, and a second registration must not throw
        TechTypeExtensions.stringsNormal[techType] = item.Key;
        TechTypeExtensions.stringsLowercase[techType] = item.Key.ToLowerInvariant();
        TechTypeExtensions.techTypesNormal[item.Key] = techType;
        TechTypeExtensions.techTypesIgnoreCase[item.Key] = techType;
        TechTypeExtensions.techTypeKeys[techType] = key;
        TechTypeExtensions.keyTechTypes[key] = techType;
        if (!TechTypeExtensions.sTechTypes.Contains(techType))
        {
            TechTypeExtensions.sTechTypes.Add(techType);
        }

        // The game builds these name caches once from the values of its enum, which does not contain our values: an item without an entry has no tooltip text at all
        TooltipFactory.techTypeTooltipStrings.valueToString[techType] = item.TooltipKey;
        TooltipFactory.techTypeIngredientStrings.valueToString[techType] = item.IngredientKey;
        uGUI_BlueprintsTab.blueprintEntryStrings.valueToString[techType] = item.BlueprintKey;

        // The repulsion cannon must not be able to pull a module out of a vehicle, like the vanilla ones
        PropulsionCannon.bannedTech.Add(techType);
    }

    // ---- Prefabs

    private static GameObject GetTemplatesRoot()
    {
        if (!templatesRoot)
        {
            templatesRoot = new GameObject("CoopItemTemplates");
            // Inactive, so that the clones below it never run Awake (which would give the template an id of its own)
            templatesRoot.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(templatesRoot);
            templatesRoot.AddComponent<SceneCleanerPreserve>();
        }
        return templatesRoot;
    }

    private static IEnumerator RegisterPrefabsAsync()
    {
        // The prefab id caches of the game are created at the first use and then replace the dictionaries: they have to exist before anything is written into them
        if (!Guarded("prefab id cache", CraftData.PreparePrefabIDCache))
        {
            yield break;
        }

        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (ScenePrefabDatabase.TryGetScenePrefab(item.ClassId, out GameObject existing) && existing)
            {
                RegisterPrefabMappings(item);
                continue;
            }

            if (item.Adopted)
            {
                string gameClassId = null;
                Guarded($"class id of {item.Key}", () => gameClassId = CraftData.GetClassIdForTechType(item.TechType));
                if (!string.IsNullOrEmpty(gameClassId))
                {
                    IPrefabRequest gameRequest = PrefabDatabase.GetPrefabAsync(gameClassId);
                    yield return gameRequest;
                    if (gameRequest.TryGetPrefab(out GameObject _))
                    {
                        Log.Info($"[VehicleUpgrades] the game has a prefab for {item.Key} ({gameClassId}), it is kept");
                        continue;
                    }
                }
                Log.Info($"[VehicleUpgrades] the game has no prefab for {item.Key}, one is made from {item.Template}");
            }

            string templateClassId = null;
            Guarded($"template class id of {item.Key}", () => templateClassId = CraftData.GetClassIdForTechType(item.Template));
            if (string.IsNullOrEmpty(templateClassId))
            {
                Log.Error($"[VehicleUpgrades] no prefab known for {item.Template}, the template of {item.Key}. The item will not spawn.");
                failures++;
                continue;
            }

            IPrefabRequest request = PrefabDatabase.GetPrefabAsync(templateClassId);
            yield return request;
            if (!request.TryGetPrefab(out GameObject template))
            {
                Log.Error($"[VehicleUpgrades] the prefab {templateClassId} ({item.Template}) did not load, the template of {item.Key}. The item will not spawn.");
                failures++;
                continue;
            }

            Guarded($"prefab of {item.Key}", () => CreatePrefab(item, template));
        }
    }

    private static void CreatePrefab(CoopItemDefinition item, GameObject template)
    {
        // From the loaded asset and under the inactive root: the copy keeps the asset's state and none of its Awake methods ran
        GameObject clone = UnityEngine.Object.Instantiate(template, GetTemplatesRoot().transform, false);
        clone.name = item.Key;

        PrefabIdentifier identifier = clone.GetComponent<PrefabIdentifier>();
        if (!identifier)
        {
            identifier = clone.AddComponent<PrefabIdentifier>();
        }
        identifier.ClassId = item.ClassId;

        TechTag tag = clone.GetComponent<TechTag>();
        if (!tag)
        {
            tag = clone.AddComponent<TechTag>();
        }
        tag.type = item.TechType;

        Pickupable pickupable = clone.GetComponent<Pickupable>();
        if (pickupable && pickupable.overrideTechUsed)
        {
            pickupable.overrideTechType = item.TechType;
        }

        if (item.Kind == CoopItemKind.ScoutDrone)
        {
            // The marker that tells the patches of the drone bay a scout drone from a scanner room's camera: on the prefab, so that every client that spawns the item has it
            clone.AddComponent<NitroxClient.MonoBehaviours.Cyclops.ScoutDrone>();
        }

        ScenePrefabDatabase.AddScenePrefab(clone);
        RegisterPrefabMappings(item);
        Log.Info($"[VehicleUpgrades] registered {item.Key} (TechType {item.TechTypeNumber}, class id {item.ClassId}) from {item.Template}");
    }

    private static void RegisterPrefabMappings(CoopItemDefinition item)
    {
        CraftData.techMapping[item.TechType] = item.ClassId;
        CraftData.entClassTechTable[item.ClassId] = item.TechType;
        // A spawn that happened before the registration is remembered by the spawner for the whole process: forget it
        DefaultWorldEntitySpawner.Forget(item.ClassId, item.TechType);
    }

    // ---- Icons

    private static IEnumerator RegisterIconsAsync()
    {
        float waited = 0f;
        while (!(SpriteManager.hasInitialized && SpriteManager.atlases != null))
        {
            if (waited > SPRITE_WAIT_SECONDS)
            {
                Log.Error("[VehicleUpgrades] the sprite atlases did not load in time, the new items get the default icon.");
                yield break;
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        Guarded("icons", RegisterIcons, false);
    }

    private static void RegisterIcons()
    {
        if (!SpriteManager.atlases.TryGetValue(ITEMS_ATLAS, out Dictionary<string, Sprite> atlas))
        {
            Log.Error($"[VehicleUpgrades] the sprite atlas {ITEMS_ATLAS} does not exist, the new items get the default icon.");
            return;
        }

        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (icons.TryGetValue(item.TechTypeNumber, out Sprite known) && known)
            {
                atlas[item.Key] = known;
                continue;
            }
            if (item.Adopted && SpriteManager.Get(item.TechType, null))
            {
                // The game has an icon for it
                continue;
            }

            Sprite source = SpriteManager.Get(item.IconSource, null);
            if (!source)
            {
                Log.Warn($"[VehicleUpgrades] no icon found for {item.IconSource}, the icon of {item.Key}");
                continue;
            }

            Sprite sprite = source;
            if (item.IconTier > 0)
            {
                Sprite derived = null;
                Guarded($"icon of {item.Key}", () => derived = CreateTierSprite(source, item.IconTier), false);
                if (derived)
                {
                    sprite = derived;
                }
            }

            icons[item.TechTypeNumber] = sprite;
            atlas[item.Key] = sprite;
        }
    }

    /// <summary>
    /// A copy of the sprite with the tier tinted and drawn into its corner. Cut out of the atlas texture on the GPU, so that it works for textures the game cannot read directly. Only sprites that
    /// sit in the atlas as an unrotated rectangle can be cut out like that; for any other the caller keeps the plain icon.
    /// </summary>
    private static Sprite CreateTierSprite(Sprite source, int tier)
    {
        if (source.packed && (source.packingRotation != SpritePackingRotation.None || source.packingMode != SpritePackingMode.Rectangle))
        {
            Log.Warn($"[VehicleUpgrades] the icon {source.name} is packed {source.packingMode} / rotation {source.packingRotation}, the tier is not drawn into it");
            return null;
        }

        Texture2D atlasTexture = source.texture;
        Rect rect = source.textureRect;
        int width = Mathf.RoundToInt(rect.width);
        int height = Mathf.RoundToInt(rect.height);
        if (!atlasTexture || width < 16 || height < 16)
        {
            return null;
        }

        RenderTexture target = RenderTexture.GetTemporary(atlasTexture.width, atlasTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        try
        {
            Graphics.Blit(atlasTexture, target);
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
            copy.Apply(false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }

        Color32[] pixels = copy.GetPixels32();
        byte[] rgba = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = pixels[i].r;
            rgba[i * 4 + 1] = pixels[i].g;
            rgba[i * 4 + 2] = pixels[i].b;
            rgba[i * 4 + 3] = pixels[i].a;
        }
        IconPixels.Tint(rgba, IconPixels.TintFor(tier));
        IconPixels.DrawDigit(rgba, width, height, tier, IconPixels.ScaleFor(height));
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
        }
        copy.SetPixels32(pixels);
        copy.Apply(false);
        // Kept for the whole process: the sprite only wraps the texture
        iconTextures.Add(copy);

        Vector2 pivot = new(source.pivot.x / width, source.pivot.y / height);
        return Sprite.Create(copy, new Rect(0, 0, width, height), pivot, source.pixelsPerUnit);
    }

    // ---- TechData

    private static void RegisterTechData()
    {
        bool anyAdded = false;
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (TechData.entries.TryGetValue(item.TechType, out JsonValue existing))
            {
                if (item.Adopted && FillInTechData(item, existing))
                {
                    anyAdded = true;
                }
                continue;
            }
            TechData.entries[item.TechType] = CreateTechDataEntry(item);
            anyAdded = true;
        }
        if (anyAdded)
        {
            // The ingredients are cached when the table is loaded; a changed table needs the cache built again
            TechData.Cache();
        }
    }

    private static JsonValue CreateTechDataEntry(CoopItemDefinition item)
    {
        // The entry of the vanilla item the prefab is cloned from carries the sounds and the background; everything that decides what the item is, is set below
        JsonValue entry = TechData.TryGetValue(item.Template, out JsonValue templateEntry) ? templateEntry.Copy() : new JsonValue(JsonValue.Type.Object);
        foreach (int property in new[] { TechData.propertyLinkedItems, TechData.propertyProcessed, TechData.propertyBuildable, TechData.propertyHarvestType, TechData.propertyHarvestOutput, TechData.propertyHarvestFinalCutBonus, TechData.propertyMaxCharge, TechData.propertyEnergyCost, TechData.propertyPoweredPrefab })
        {
            entry.Remove(property);
        }

        entry[TechData.propertyTechType] = new JsonValue((int)item.TechType);
        entry[TechData.propertyCraftAmount] = new JsonValue(1);
        SetEquipment(entry, item);
        SetSlotType(entry, item);
        SetCraftTime(entry, item);
        SetItemSize(entry, item);
        SetIngredients(entry, item);
        return entry;
    }

    /// <summary>
    /// Completes the entry the game has for an adopted item: sets what is missing and leaves what is there. Returns true when the recipe was added, which the ingredient cache has to learn about.
    /// </summary>
    private static bool FillInTechData(CoopItemDefinition item, JsonValue entry)
    {
        if (entry.GetInt(TechData.propertyEquipmentType, 0) == (int)EquipmentType.None)
        {
            SetEquipment(entry, item);
        }
        if (!entry.Contains(TechData.propertySlotType))
        {
            SetSlotType(entry, item);
        }
        if (!entry.Contains(TechData.propertyCraftTime))
        {
            SetCraftTime(entry, item);
        }
        if (!entry.Contains(TechData.propertyItemSize))
        {
            SetItemSize(entry, item);
        }
        if (!entry.GetArray(TechData.propertyIngredients, out JsonValue ingredients, null) || ingredients == null || ingredients.Count == 0)
        {
            SetIngredients(entry, item);
            Log.Info($"[VehicleUpgrades] the game has no recipe for {item.Key}, the recipe of the table is used");
            return true;
        }
        return false;
    }

    private static void SetEquipment(JsonValue entry, CoopItemDefinition item) => entry[TechData.propertyEquipmentType] = new JsonValue((int)item.Equipment);

    private static void SetSlotType(JsonValue entry, CoopItemDefinition item) => entry[TechData.propertySlotType] = new JsonValue((int)item.QuickSlot);

    private static void SetCraftTime(JsonValue entry, CoopItemDefinition item) => entry[TechData.propertyCraftTime] = new JsonValue((double)item.CraftTimeSeconds);

    private static void SetItemSize(JsonValue entry, CoopItemDefinition item)
    {
        JsonValue size = new(JsonValue.Type.Object);
        size[TechData.propertyX] = new JsonValue(item.SizeX);
        size[TechData.propertyY] = new JsonValue(item.SizeY);
        entry[TechData.propertyItemSize] = size;
    }

    private static void SetIngredients(JsonValue entry, CoopItemDefinition item)
    {
        JsonValue ingredients = new(JsonValue.Type.Array);
        foreach (CoopIngredient ingredient in item.Recipe)
        {
            JsonValue line = new(JsonValue.Type.Object);
            line[TechData.propertyTechType] = new JsonValue((int)ingredient.Tech);
            line[TechData.propertyAmount] = new JsonValue(ingredient.Amount);
            ingredients.Add(line);
        }
        entry[TechData.propertyIngredients] = ingredients;
    }

    // ---- Settings dependent state

    private static void SyncCraftNodes(bool show)
    {
        CraftTree.Initialize();
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            CraftTree tree = CraftTree.GetTree(item.Tree);
            if (tree == null)
            {
                Log.Error($"[VehicleUpgrades] the craft tree {item.Tree} of {item.Key} does not exist");
                continue;
            }

            TreeNode parent = tree.nodes;
            foreach (string tab in item.TabPath)
            {
                parent = parent?[tab];
            }
            if (parent == null)
            {
                Log.Error($"[VehicleUpgrades] the tab {string.Join("/", item.TabPath)} of the craft tree {item.Tree} does not exist, the node of {item.Key} is missing");
                continue;
            }

            TreeNode node = parent[item.Key];
            if (show && node == null)
            {
                parent.AddNode(new CraftNode(item.Key, TreeAction.Craft, item.TechType));
                CraftTree.craftableTech.Add(item.TechType);
                addedNodes.Add(item.TechType);
            }
            else if (!show && node != null && addedNodes.Remove(item.TechType))
            {
                // Only a node this class added: the game's own node of an adopted item stays
                parent.RemoveNode(node);
                CraftTree.craftableTech.Remove(item.TechType);
            }
        }
    }

    /// <summary>
    /// The PDA's blueprints tab lists the technologies by group and category (CraftData.groups), and an item that is in none of them is not listed. Each item is added to the group of the fabricator
    /// it is crafted at (Modification Station, Vehicle Upgrade Console, Cyclops fabricator), where the game lists the modules next to it. Only entries this class added are removed again.
    /// </summary>
    private static void SyncBlueprintGroups(bool show)
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            (TechGroup group, TechCategory category) = item.Tree switch
            {
                CraftTree.Type.Workbench => (TechGroup.Workbench, TechCategory.Workbench),
                CraftTree.Type.SeamothUpgrades => (TechGroup.VehicleUpgrades, TechCategory.VehicleUpgrades),
                _ => (TechGroup.Cyclops, TechCategory.CyclopsUpgrades)
            };
            if (!CraftData.groups.TryGetValue(group, out Dictionary<TechCategory, List<TechType>> categories) || !categories.TryGetValue(category, out List<TechType> list))
            {
                Log.Warn($"[VehicleUpgrades] the blueprint group {group}/{category} of {item.Key} does not exist, the PDA will not list it");
                continue;
            }

            if (show && !list.Contains(item.TechType))
            {
                list.Add(item.TechType);
                addedGroupEntries.Add(item.TechType);
            }
            else if (!show && addedGroupEntries.Remove(item.TechType))
            {
                list.Remove(item.TechType);
            }
        }
    }

    private static void SyncDepthTables(bool enabled)
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (item.Kind != CoopItemKind.DepthModule)
            {
                continue;
            }

            // The Seamoth has no table to fill: its patch asks DepthModuleRules for every change of its modules
            Dictionary<TechType, float> table = item.Vehicle switch
            {
                CoopVehicle.Prawn => Exosuit.crushDepths,
                CoopVehicle.Cyclops => SubRoot.hullReinforcement,
                _ => null
            };
            if (table == null)
            {
                continue;
            }

            if (enabled)
            {
                table[item.TechType] = DepthModuleRules.ExtraFor(item.Vehicle, item.Tier, VehicleUpgradeSettings.ExtraDepthPerModule);
            }
            else
            {
                table.Remove(item.TechType);
            }
        }
    }

    private static void SyncUnlocks(VehicleUpgradeUnlockRules.Plan plan)
    {
        if (KnownTech.compoundTech == null)
        {
            // The technologies of the game are not loaded (yet); the next call after they are does it
            return;
        }

        foreach (TechType added in new List<TechType>(addedRules))
        {
            if (!Contains(plan.UnlockWithPrerequisite, added))
            {
                KnownTech.compoundTech.RemoveAll(rule => rule.techType == added);
                addedRules.Remove(added);
            }
        }
        foreach (CoopItemDefinition item in plan.UnlockWithPrerequisite)
        {
            // A rule of the game's own for an adopted item, or a blueprint it knows from the start, is enough
            if (KnownTech.compoundTech.Exists(rule => rule.techType == item.TechType) || (item.Adopted && KnownTech.defaultTech.Contains(item.TechType)))
            {
                continue;
            }
            KnownTech.compoundTech.Add(new KnownTech.CompoundTech { techType = item.TechType, dependencies = [item.Prerequisite] });
            addedRules.Add(item.TechType);
        }
        if (plan.UnlockWithPrerequisite.Count > 0)
        {
            // A prerequisite that is already known unlocks its items now
            KnownTech.UnlockCompoundTech(false);
        }

        if (plan.UnlockNow.Count > 0)
        {
            // Every client unlocks them itself when it gets the settings, so nothing is sent
            using (PacketSuppressor<KnownTechEntryAdd>.Suppress())
            {
                foreach (CoopItemDefinition item in plan.UnlockNow)
                {
                    KnownTech.Add(item.TechType, false);
                }
            }
        }
    }

    private static bool Contains(IReadOnlyList<CoopItemDefinition> items, TechType techType)
    {
        foreach (CoopItemDefinition item in items)
        {
            if (item.TechType == techType)
            {
                return true;
            }
        }
        return false;
    }

    private static bool Guarded(string what, Action action, bool critical = true)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[VehicleUpgrades] registering {what} failed");
            if (critical)
            {
                failures++;
            }
            return false;
        }
    }
}
