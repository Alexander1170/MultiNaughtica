using System.Collections.Generic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The table of the new items of the co-op build: one <see cref="CoopItemDefinition" /> per item. To add an item (the tow hook, the power share and the Cyclops stations will) add a number, a
/// class id and a row; <see cref="CoopItemRegistry" /> does the rest and the tests of <c>CoopItemsTest</c> check that the row is complete and does not collide with anything.
/// </summary>
public static class CoopItems
{
    /// <summary>The first number of the block this build uses for its own items. The game's own highest value is 10004 (Databox); mods that add TechTypes usually start at about 11000.</summary>
    public const int FIRST_TECH_TYPE = 20001;

    public const int SEAMOTH_DEPTH_MK4 = 20001;
    public const int SEAMOTH_DEPTH_MK5 = 20002;
    public const int PRAWN_DEPTH_MK3 = 20003;
    public const int PRAWN_DEPTH_MK4 = 20004;
    public const int CYCLOPS_DEPTH_MK4 = 20005;
    public const int CYCLOPS_DEPTH_MK5 = 20006;
    public const int SEAMOTH_AFTERBURNER = 20007;

    public const int CYCLOPS_SOLAR = 20008;

    // feature group 2
    public const int CYCLOPS_POWER_SHARE = 20009;
    public const int SEAMOTH_TOW_HOOK = 20010;
    public const int CYCLOPS_TOW_HOOK = 20011;
    public const int SEAMOTH_RIDER_BAR = 20012;

    // feature group 3: the Cyclops stations (station 1: the terrain sonar and the navigator console; station 2: the gunner station)
    public const int CYCLOPS_TERRAIN_SONAR = 20013;
    public const int CYCLOPS_GUNNER_STATION = 20014;
    public const int CYCLOPS_ENGINEER_CONSOLE = 20015;
    public const int CYCLOPS_DRONE_BAY = 20016;
    public const int SCOUT_DRONE = 20017;

    /// <summary>The game's own Seamoth Solar Charger (TechType.SeamothSolarCharge, 2102): adopted, not a new item.</summary>
    public const int SEAMOTH_SOLAR = (int)TechType.SeamothSolarCharge;

    private static readonly CoopIngredient[] SeamothMk4Recipe =
    [
        new(TechType.VehicleHullModule3, 1), new(TechType.PlasteelIngot, 1), new(TechType.Kyanite, 3), new(TechType.Nickel, 2)
    ];

    private static readonly CoopIngredient[] SeamothMk5Recipe =
    [
        new((TechType)SEAMOTH_DEPTH_MK4, 1), new(TechType.PlasteelIngot, 2), new(TechType.Kyanite, 4), new(TechType.Diamond, 2)
    ];

    private static readonly CoopIngredient[] PrawnMk3Recipe =
    [
        new(TechType.ExoHullModule2, 1), new(TechType.PlasteelIngot, 1), new(TechType.Kyanite, 3), new(TechType.Nickel, 2)
    ];

    private static readonly CoopIngredient[] PrawnMk4Recipe =
    [
        new((TechType)PRAWN_DEPTH_MK3, 1), new(TechType.PlasteelIngot, 2), new(TechType.Kyanite, 4), new(TechType.Diamond, 2)
    ];

    private static readonly CoopIngredient[] CyclopsMk4Recipe =
    [
        new(TechType.CyclopsHullModule3, 1), new(TechType.PlasteelIngot, 2), new(TechType.Kyanite, 4), new(TechType.Nickel, 3)
    ];

    private static readonly CoopIngredient[] CyclopsMk5Recipe =
    [
        new((TechType)CYCLOPS_DEPTH_MK4, 1), new(TechType.PlasteelIngot, 3), new(TechType.Kyanite, 5), new(TechType.Diamond, 3)
    ];

    private static readonly CoopIngredient[] AfterburnerRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.Lubricant, 2), new(TechType.Sulphur, 2)
    ];

    private static readonly CoopIngredient[] SeamothSolarRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.EnameledGlass, 1), new(TechType.Quartz, 2)
    ];

    private static readonly CoopIngredient[] CyclopsSolarRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.EnameledGlass, 2), new(TechType.Quartz, 3)
    ];

    private static readonly CoopIngredient[] PowerShareRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.PowerCell, 1), new(TechType.CopperWire, 2)
    ];

    private static readonly CoopIngredient[] SeamothTowHookRecipe =
    [
        new(TechType.ComputerChip, 1), new(TechType.Lubricant, 1), new(TechType.Titanium, 3)
    ];

    private static readonly CoopIngredient[] CyclopsTowHookRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.PlasteelIngot, 1), new(TechType.Lubricant, 2)
    ];

    private static readonly CoopIngredient[] RiderBarRecipe =
    [
        new(TechType.Titanium, 3), new(TechType.Lubricant, 1)
    ];

    private static readonly CoopIngredient[] TerrainSonarRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.ComputerChip, 2), new(TechType.EnameledGlass, 2)
    ];

    private static readonly CoopIngredient[] GunnerStationRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.ComputerChip, 2), new(TechType.PlasteelIngot, 1)
    ];

    private static readonly CoopIngredient[] EngineerConsoleRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.ComputerChip, 1), new(TechType.Lubricant, 2)
    ];

    private static readonly CoopIngredient[] DroneBayRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.Titanium, 2), new(TechType.Lubricant, 1)
    ];

    private static readonly CoopIngredient[] ScoutDroneRecipe =
    [
        new(TechType.AdvancedWiringKit, 1), new(TechType.ComputerChip, 1), new(TechType.Battery, 1), new(TechType.Titanium, 2)
    ];

    private static readonly string[] NoTab = [];
    private static readonly string[] SeamothModulesTab = ["SeamothModules"];

    /// <summary>
    /// Every item, new and adopted. Placeholders in the tooltips: {depth} (the crush depth in metres the module gives), {speed}, {seconds}, {cooldown} and {energy} (the afterburner),
    /// {rate} (solar energy per second at the surface at noon), {shareRange} {shareRate} {threshold} {reserve} {towPercent} {towDistance} {towAttach} (group 2), {sonarRange} and {clearance} (the terrain sonar,
    /// group 3), {gunnerEnergy} {gunnerCooldown} {gunnerRange} (the gunner station, group 3), {engineerSpeed} {overdriveSeconds} {overdriveCooldown} {shieldSaving} {ventCooldown} {silentBonus} (the engineer console, group 3), {droneSpeed} {droneRange} {droneHealth} {zapEnergy} {zapCooldown} {zapRadius} (the drone bay and the scout drone, group 3). The texts are plain ASCII on purpose: the game computes the glyphs of its font before the mod's strings are added.
    /// </summary>
    public static readonly IReadOnlyList<CoopItemDefinition> All =
    [
        new("CoopSeamothDepthModuleMK4", SEAMOTH_DEPTH_MK4, "499d6269-555e-405f-9d64-0ae1d9166499", "Seamoth Depth Module MK4",
            "Allows safe diving to a depth of {depth} meters. Only the best depth module counts, they do not add up.",
            CoopVehicle.Seamoth, CoopItemKind.DepthModule, 1, TechType.VehicleHullModule3, TechType.VehicleHullModule3, 4, EquipmentType.SeamothModule, QuickSlotType.Passive,
            1, 1, 5f, SeamothMk4Recipe, CraftTree.Type.Workbench, NoTab, TechType.VehicleHullModule3),
        new("CoopSeamothDepthModuleMK5", SEAMOTH_DEPTH_MK5, "4de8e5e8-60cb-44fe-9e52-9926323c3850", "Seamoth Depth Module MK5",
            "Allows safe diving to a depth of {depth} meters. Only the best depth module counts, they do not add up.",
            CoopVehicle.Seamoth, CoopItemKind.DepthModule, 2, TechType.VehicleHullModule3, TechType.VehicleHullModule3, 5, EquipmentType.SeamothModule, QuickSlotType.Passive,
            1, 1, 6f, SeamothMk5Recipe, CraftTree.Type.Workbench, NoTab, TechType.VehicleHullModule3),
        new("CoopExosuitDepthModuleMK3", PRAWN_DEPTH_MK3, "b39ec19b-873f-4ba3-8867-186f3e7d36f6", "Prawn Suit Depth Module MK3",
            "Allows safe diving to a depth of {depth} meters. Only the best depth module counts, they do not add up.",
            CoopVehicle.Prawn, CoopItemKind.DepthModule, 1, TechType.ExoHullModule2, TechType.ExoHullModule2, 3, EquipmentType.ExosuitModule, QuickSlotType.Passive,
            1, 1, 5f, PrawnMk3Recipe, CraftTree.Type.Workbench, NoTab, TechType.ExoHullModule2),
        new("CoopExosuitDepthModuleMK4", PRAWN_DEPTH_MK4, "f2e5fc81-2f43-4aec-a087-afb2d9d42961", "Prawn Suit Depth Module MK4",
            "Allows safe diving to a depth of {depth} meters. Only the best depth module counts, they do not add up.",
            CoopVehicle.Prawn, CoopItemKind.DepthModule, 2, TechType.ExoHullModule2, TechType.ExoHullModule2, 4, EquipmentType.ExosuitModule, QuickSlotType.Passive,
            1, 1, 6f, PrawnMk4Recipe, CraftTree.Type.Workbench, NoTab, TechType.ExoHullModule2),
        new("CoopCyclopsDepthModuleMK4", CYCLOPS_DEPTH_MK4, "08a80860-71ca-4edf-a3f2-7ddb4332fbec", "Cyclops Depth Module MK4",
            "Allows safe diving to a depth of {depth} meters. Only the best depth module counts, they do not add up.",
            CoopVehicle.Cyclops, CoopItemKind.DepthModule, 1, TechType.CyclopsHullModule3, TechType.CyclopsHullModule3, 4, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 6f, CyclopsMk4Recipe, CraftTree.Type.Workbench, NoTab, TechType.CyclopsHullModule3),
        new("CoopCyclopsDepthModuleMK5", CYCLOPS_DEPTH_MK5, "5aae431e-a2bc-4631-bbe8-8f1b58d04ed8", "Cyclops Depth Module MK5",
            "Allows safe diving to a depth of {depth} meters. Only the best depth module counts, they do not add up.",
            CoopVehicle.Cyclops, CoopItemKind.DepthModule, 2, TechType.CyclopsHullModule3, TechType.CyclopsHullModule3, 5, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 7f, CyclopsMk5Recipe, CraftTree.Type.Workbench, NoTab, TechType.CyclopsHullModule3),
        new("CoopSeamothAfterburner", SEAMOTH_AFTERBURNER, "c34467fe-9b28-488a-8a18-31483ad29434", "Seamoth Afterburner",
            "Quick slot module. Press the key of its slot to boost the Seamoth to {speed} times its speed for {seconds} seconds. Costs {energy} energy per use and needs {cooldown} seconds to recover.",
            CoopVehicle.Seamoth, CoopItemKind.Afterburner, 0, TechType.SeamothSonarModule, TechType.VehiclePowerUpgradeModule, 0, EquipmentType.SeamothModule, QuickSlotType.Instant,
            1, 1, 4f, AfterburnerRecipe, CraftTree.Type.SeamothUpgrades, SeamothModulesTab, TechType.Seamoth),
        // The game has this module in its code and craft tree but may lack parts of it; the registry completes what is missing and leaves what is there ({rate} per module, they add up)
        new("SeamothSolarCharge", SEAMOTH_SOLAR, "b008c2c5-4ad0-4321-a776-d78ec9d83c6f", "Seamoth Solar Charger",
            "Charges the Seamoth's power cell with sunlight: up to {rate} energy per second per module at the surface at noon, less when deeper, nothing at night or below 200 meters. Several modules add up.",
            CoopVehicle.Seamoth, CoopItemKind.SolarCharger, 0, TechType.VehiclePowerUpgradeModule, TechType.SolarPanel, 0, EquipmentType.SeamothModule, QuickSlotType.Passive,
            1, 1, 4f, SeamothSolarRecipe, CraftTree.Type.SeamothUpgrades, SeamothModulesTab, TechType.Seamoth, adopted: true),
        new("CoopCyclopsSolarCharger", CYCLOPS_SOLAR, "06c26e8c-51ff-4089-8121-5f971aa402ef", "Cyclops Solar Charger",
            "Charges the power cells of the Cyclops with sunlight: up to {rate} energy per second at the surface at noon, less when deeper, nothing at night or below 200 meters. Only one counts.",
            CoopVehicle.Cyclops, CoopItemKind.SolarCharger, 0, TechType.CyclopsThermalReactorModule, TechType.SolarPanel, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 4f, CyclopsSolarRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopCyclopsPowerShare", CYCLOPS_POWER_SHARE, "80269355-00d1-4bf5-980a-e97ec430a154", "Cyclops Power Share",
            "Passive module. Charges every Seamoth and Prawn Suit within {shareRange} meters of the Cyclops that is not full, {shareRate} energy per second each, with no docking. When the power of the Cyclops falls below {threshold} percent it draws on the vehicle docked in it, down to {reserve} percent of that vehicle's power. Only one counts.",
            CoopVehicle.Cyclops, CoopItemKind.PowerShare, 0, TechType.CyclopsThermalReactorModule, TechType.PowerTransmitter, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 4f, PowerShareRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopSeamothTowHook", SEAMOTH_TOW_HOOK, "c5e8cda1-be2c-4ca6-aa3c-e2a693f0fc44", "Seamoth Tow Hook",
            "Quick slot module. Press the key of its slot to switch it on: it hooks the nearest Seamoth or Prawn Suit that nobody sits in, within {towAttach} meters, and tows it; press again to let go. A tow is {towDistance} meters of cable at most and slows the Seamoth to {towPercent} percent of its speed.",
            CoopVehicle.Seamoth, CoopItemKind.TowHook, 0, TechType.SeamothSonarModule, TechType.PropulsionCannon, 0, EquipmentType.SeamothModule, QuickSlotType.Toggleable,
            1, 1, 4f, SeamothTowHookRecipe, CraftTree.Type.SeamothUpgrades, SeamothModulesTab, TechType.Seamoth),
        new("CoopCyclopsTowHook", CYCLOPS_TOW_HOOK, "1cfe7c35-ae11-4f8a-a91f-870e612de060", "Cyclops Tow Hook",
            "Passive module. Adds a tow control to the helm that hooks the nearest Seamoth or Prawn Suit that nobody sits in, within {towAttach} meters, on top of the vehicle in the docking bay. A tow is {towDistance} meters of cable at most and slows the Cyclops to {towPercent} percent of its speed. Only one counts.",
            CoopVehicle.Cyclops, CoopItemKind.TowHook, 0, TechType.CyclopsThermalReactorModule, TechType.RepulsionCannon, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 4f, CyclopsTowHookRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopSeamothRiderBar", SEAMOTH_RIDER_BAR, "c4f46458-317a-4a26-a60e-40accb9c2f26", "Seamoth Rider Bar",
            "Passive module. Fits a bar to the hull of the Seamoth that a swimming player can hold on to for a ride along: no oxygen is used while holding on. One rider per Seamoth.",
            CoopVehicle.Seamoth, CoopItemKind.RiderBar, 0, TechType.VehicleHullModule1, TechType.Seaglide, 0, EquipmentType.SeamothModule, QuickSlotType.Passive,
            1, 1, 4f, RiderBarRecipe, CraftTree.Type.SeamothUpgrades, SeamothModulesTab, TechType.Seamoth),
        new("CoopCyclopsTerrainSonar", CYCLOPS_TERRAIN_SONAR, "d73c3b12-9d53-41cf-9a83-ec77eadbc411", "Cyclops Terrain Sonar",
            "Passive module. The round hologram at the helm shows the terrain around the Cyclops (as far as its display holds), and the left side desk of the cockpit becomes the navigator console: a map of the terrain within {sonarRange} meters to turn and zoom, marks that everybody sees, a route for the pilot to follow and a warning when the space around the hull (within {clearance} meters) gets tight. Only one counts.",
            CoopVehicle.Cyclops, CoopItemKind.TerrainSonar, 0, TechType.CyclopsThermalReactorModule, TechType.MapRoomHUDChip, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 5f, TerrainSonarRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopCyclopsGunnerStation", CYCLOPS_GUNNER_STATION, "247ea25f-9f3d-486b-8702-5d7f97c10d8c", "Cyclops Gunner Station",
            "Passive module. The right side desk of the cockpit becomes the gunner console: a player looks through a turret camera on the hull, aims with the mouse, holds the button to charge and releases it to fire a stasis sphere that freezes what it hits. A full charge costs {gunnerEnergy} energy of the Cyclops, the next shot waits {gunnerCooldown} seconds and the sphere flies {gunnerRange} meters. One gunner at a time. Only one counts.",
            CoopVehicle.Cyclops, CoopItemKind.GunnerStation, 0, TechType.CyclopsThermalReactorModule, TechType.StasisRifle, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 5f, GunnerStationRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopCyclopsEngineerConsole", CYCLOPS_ENGINEER_CONSOLE, "4c5fd870-c704-4ff9-a7f9-e8631d8bddbd", "Cyclops Engineer Console",
            "Passive module. The console in the engine room becomes the engineer's station: send the Cyclops' power to the engines ({engineerSpeed} percent faster, and a {overdriveSeconds} second overdrive past flank speed that heats the engine), to the shields (the shield uses {shieldSaving} percent less) or to the systems (the sonar sees farther and silent running is {silentBonus} percent quieter, for more power); vent the engine heat; see the fires and the breaches of the hull and start the fire suppression. One engineer at a time.",
            CoopVehicle.Cyclops, CoopItemKind.EngineerConsole, 0, TechType.CyclopsThermalReactorModule, TechType.CyclopsFireSuppressionModule, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 5f, EngineerConsoleRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopCyclopsDroneBay", CYCLOPS_DRONE_BAY, "f44bd6df-c8c3-431b-b392-900a6482d55c", "Cyclops Drone Bay",
            "Passive module. A dock on the hull holds a Scout Drone and a console in the engine room flies it: a camera drone that goes {droneSpeed} times as fast as a Seamoth, sees as far as {droneRange} metres from the Cyclops (static on the picture near the edge), marks resources and threats for everybody and carries a zapper. Load the drone at the console; it recharges in the bay.",
            CoopVehicle.Cyclops, CoopItemKind.DroneBay, 0, TechType.CyclopsThermalReactorModule, TechType.MapRoomCamera, 0, EquipmentType.CyclopsModule, QuickSlotType.Passive,
            1, 1, 5f, DroneBayRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops),
        new("CoopScoutDrone", SCOUT_DRONE, "7e88148b-7508-444a-bc21-7527836543c1", "Scout Drone",
            "A camera drone for the Cyclops Drone Bay. Fast ({droneSpeed} times a Seamoth) and fragile ({droneHealth} health): creatures attack it, and a destroyed drone is gone. Its zapper ({zapEnergy} of its battery, every {zapCooldown} seconds, {zapRadius} metres around it) knocks Lava Larvae off the hull and scares small creatures. Put it into the bay at the drone console.",
            CoopVehicle.Cyclops, CoopItemKind.ScoutDrone, 0, TechType.MapRoomCamera, TechType.MapRoomCamera, 0, EquipmentType.None, QuickSlotType.None,
            2, 2, 8f, ScoutDroneRecipe, CraftTree.Type.CyclopsFabricator, NoTab, TechType.Cyclops)
    ];

    private static Dictionary<int, CoopItemDefinition> byNumber;

    private static Dictionary<int, CoopItemDefinition> ByNumber
    {
        get
        {
            if (byNumber == null)
            {
                Dictionary<int, CoopItemDefinition> map = new();
                foreach (CoopItemDefinition item in All)
                {
                    map[item.TechTypeNumber] = item;
                }
                byNumber = map;
            }
            return byNumber;
        }
    }

    private static HashSet<string> classIds;

    public static bool IsCoopClassId(string classId)
    {
        if (string.IsNullOrEmpty(classId))
        {
            return false;
        }
        if (classIds == null)
        {
            HashSet<string> set = new();
            foreach (CoopItemDefinition item in All)
            {
                set.Add(item.ClassId);
            }
            classIds = set;
        }
        return classIds.Contains(classId);
    }

    public static bool IsCoopItem(TechType techType) => ByNumber.ContainsKey((int)techType);

    public static bool TryGet(TechType techType, out CoopItemDefinition item) => ByNumber.TryGetValue((int)techType, out item);

    public static CoopItemDefinition Get(int techTypeNumber) => ByNumber.TryGetValue(techTypeNumber, out CoopItemDefinition item) ? item : null;
}
