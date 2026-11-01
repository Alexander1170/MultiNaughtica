namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>The vehicle a module belongs to (the vanilla names are Seamoth, Exosuit = Prawn Suit and Cyclops).</summary>
public enum CoopVehicle
{
    Seamoth,
    Prawn,
    Cyclops
}

/// <summary>What the module does. The behaviour itself lives in Harmony patches that look at the TechType of the installed modules, never in a component on the item.</summary>
public enum CoopItemKind
{
    /// <summary>Raises the crush depth of the vehicle (<see cref="DepthModuleRules" />).</summary>
    DepthModule,

    /// <summary>Seamoth instant-use quick slot module that boosts the speed for a short time (<see cref="AfterburnerState" />).</summary>
    Afterburner,

    /// <summary>Passive module that charges the vehicle's power cells in daylight (<see cref="SolarChargeRules" />).</summary>
    SolarCharger,

    /// <summary>Passive Cyclops module: charges the Seamoths and Prawn Suits near the Cyclops and, in an emergency, draws on the vehicle docked in it (feature group 2).</summary>
    PowerShare,

    /// <summary>Tow hook: an instant use quick slot module of the Seamoth, a passive module of the Cyclops that adds a control to its helm (feature group 2).</summary>
    TowHook,

    /// <summary>Passive Seamoth module: a bar on the hull that a swimming player can hold on to for a ride along (feature group 2).</summary>
    RiderBar,

    /// <summary>Passive Cyclops module: the hologram at the helm shows the terrain around the Cyclops and a side desk of the cockpit becomes the navigator console (feature group 3, station 1).</summary>
    TerrainSonar,

    /// <summary>Passive Cyclops module: the right side desk of the cockpit becomes the gunner console that fires stasis spheres through a turret camera (feature group 3, station 2).</summary>
    GunnerStation,

    /// <summary>Passive Cyclops module: a console in the engine room routes the power, vents the engine heat and shows the damage (feature group 3, station 3).</summary>
    EngineerConsole,

    /// <summary>Passive Cyclops module: a dock on the hull holds a scout drone and a console in the engine room flies it (feature group 3, station 4).</summary>
    DroneBay,

    /// <summary>The scout drone itself: a craftable item (not a module) cloned from the game's camera drone, put into the bay of a Cyclops (feature group 3, station 4).</summary>
    ScoutDrone
}

public readonly struct CoopIngredient
{
    public CoopIngredient(TechType tech, int amount)
    {
        Tech = tech;
        Amount = amount;
    }

    public TechType Tech { get; }
    public int Amount { get; }
}

/// <summary>
/// One new item of the co-op build (docs/coop/VEHICLE_UPGRADES.md), as a row of the table <see cref="CoopItems.All" />. Everything the game needs to know about the item is here, so that
/// adding an item is one more row: its fixed <see cref="TechTypeNumber" /> and <see cref="ClassId" />, its name and tooltip, the vanilla item whose prefab and icon it borrows, how it is
/// equipped, its recipe, where it is crafted and what unlocks it. <see cref="CoopItemRegistry" /> turns a row into the game's tables.
/// </summary>
/// <remarks>
/// The numbers and class ids are written into saved worlds (the server stores them as text), so they must never change once a build with them has been handed out.
/// </remarks>
public sealed class CoopItemDefinition
{
    public CoopItemDefinition(string key, int techTypeNumber, string classId, string displayName, string tooltip, CoopVehicle vehicle, CoopItemKind kind, int tier, TechType template,
                              TechType iconSource, int iconTier, EquipmentType equipment, QuickSlotType quickSlot, int sizeX, int sizeY, float craftTimeSeconds, CoopIngredient[] recipe,
                              CraftTree.Type tree, string[] tabPath, TechType prerequisite, bool adopted = false)
    {
        Adopted = adopted;
        Key = key;
        TechTypeNumber = techTypeNumber;
        ClassId = classId;
        DisplayName = displayName;
        Tooltip = tooltip;
        Vehicle = vehicle;
        Kind = kind;
        Tier = tier;
        Template = template;
        IconSource = iconSource;
        IconTier = iconTier;
        Equipment = equipment;
        QuickSlot = quickSlot;
        SizeX = sizeX;
        SizeY = sizeY;
        CraftTimeSeconds = craftTimeSeconds;
        Recipe = recipe;
        Tree = tree;
        TabPath = tabPath;
        Prerequisite = prerequisite;
    }

    /// <summary>
    /// The name of the item, which is also its language key, its sprite name and what the game's <c>TechTypeExtensions.AsString</c> returns. Starts with "Coop" so that it can never meet a
    /// name of the game or of another mod. Never put this name into a <c>NitroxTechType</c>: that carries <see cref="TechTypeNumber" /> as text, because the server does not know the names and
    /// the client turns a name back into a TechType with <c>Enum.Parse</c>, which only accepts numbers for values that are not in the game's enum.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The item exists in the game already (its TechType is a member of the game's enum), but the game only half finished it: this build completes whatever the game lacks (recipe, strings, icon,
    /// prefab, unlock) and changes its behaviour to the server's settings. The registry never overwrites what the game has, never adds a second craft node and only removes what it added itself.
    /// The <see cref="Key" /> is then the game's own name of the TechType, and <see cref="ClassId" /> is only used when the game has no prefab for it.
    /// </summary>
    public bool Adopted { get; }

    /// <summary>The value of the item's TechType: far above the highest value of the game (10004, Databox) for a new item, the game's own value for an adopted one.</summary>
    public int TechTypeNumber { get; }

    /// <summary>The ClassId of the item's prefab (a fixed GUID): spawning an item again after a restart looks the prefab up by this id.</summary>
    public string ClassId { get; }

    public string DisplayName { get; }

    /// <summary>The tooltip with placeholders such as {depth} that <see cref="CoopItemText" /> fills from the server's settings.</summary>
    public string Tooltip { get; }

    public CoopVehicle Vehicle { get; }
    public CoopItemKind Kind { get; }

    /// <summary>For a depth module the number of steps (<see cref="VehicleUpgradeSettings.ExtraDepthPerModule" />) it adds to the best vanilla module; 0 for the others.</summary>
    public int Tier { get; }

    /// <summary>The vanilla item whose prefab is cloned and whose TechData entry (sounds, background) is copied.</summary>
    public TechType Template { get; }

    /// <summary>The vanilla item whose icon is used; <see cref="IconTier" /> &gt; 0 draws that number into a corner and tints the icon so that tiers can be told apart.</summary>
    public TechType IconSource { get; }

    public int IconTier { get; }

    /// <summary>The slot type that accepts the item (SeamothModule, ExosuitModule or CyclopsModule).</summary>
    public EquipmentType Equipment { get; }

    /// <summary>Passive for the modules that work by being installed, Instant for the quick slot module.</summary>
    public QuickSlotType QuickSlot { get; }

    public int SizeX { get; }
    public int SizeY { get; }
    public float CraftTimeSeconds { get; }
    public CoopIngredient[] Recipe { get; }

    /// <summary>The fabricator the item is crafted at and the tabs to its node: empty for the root of the tree (the Modification Station and the Cyclops fabricator are flat).</summary>
    public CraftTree.Type Tree { get; }

    public string[] TabPath { get; }

    /// <summary>The blueprint that unlocks this one (a vanilla one or another row of the table).</summary>
    public TechType Prerequisite { get; }

    public TechType TechType => (TechType)TechTypeNumber;

    public string TooltipKey => $"Tooltip_{Key}";

    /// <summary>The language key under which the game looks up the name of this item in the ingredient list of a recipe tooltip.</summary>
    public string IngredientKey => $"{Key}.TooltipIngredient";

    /// <summary>The language key under which the game looks up the name of this item in the PDA's blueprint list.</summary>
    public string BlueprintKey => $"{Key}.BlueprintsTab";

    /// <summary>
    /// The text a <c>NitroxTechType</c> carries for this item: whatever <c>TechType.ToString()</c> gives, which is the number for a value the game's enum does not define and the game's name for an
    /// adopted item.
    /// </summary>
    public string NitroxName => Adopted ? Key : TechTypeNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
