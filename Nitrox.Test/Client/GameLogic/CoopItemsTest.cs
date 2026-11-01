using System.Globalization;
using System.Text.RegularExpressions;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Extensions;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The table of the new items of the co-op build: every row is complete and cannot collide with the game's own items or with another row. The numbers and class ids are written into saved worlds,
///     so they are pinned here.
/// </summary>
[TestClass]
public class CoopItemsTest
{
    private static readonly string[] KnownPlaceholders = ["{depth}", "{speed}", "{seconds}", "{cooldown}", "{energy}", "{rate}", "{shareRange}", "{shareRate}", "{threshold}", "{reserve}", "{towPercent}", "{towDistance}", "{towAttach}", "{sonarRange}", "{clearance}", "{gunnerEnergy}", "{gunnerCooldown}", "{gunnerRange}", "{engineerSpeed}", "{overdriveSeconds}", "{overdriveCooldown}", "{shieldSaving}", "{ventCooldown}", "{silentBonus}", "{droneSpeed}", "{droneRange}", "{droneHealth}", "{zapEnergy}", "{zapCooldown}", "{zapRadius}"];

    private static int HighestVanillaTechType => Enum.GetValues(typeof(TechType)).Cast<int>().Max();

    [TestMethod]
    public void TheTableHasTheSeventeenNewItemsAndTheAdoptedSolarChargerOfTheFirstGroup()
    {
        CoopItems.All.Should().HaveCount(18);
        CoopItems.All.Select(item => item.Key).Should().Equal("CoopSeamothDepthModuleMK4", "CoopSeamothDepthModuleMK5", "CoopExosuitDepthModuleMK3", "CoopExosuitDepthModuleMK4", "CoopCyclopsDepthModuleMK4", "CoopCyclopsDepthModuleMK5", "CoopSeamothAfterburner", "SeamothSolarCharge", "CoopCyclopsSolarCharger",
                                                                "CoopCyclopsPowerShare", "CoopSeamothTowHook", "CoopCyclopsTowHook", "CoopSeamothRiderBar", "CoopCyclopsTerrainSonar", "CoopCyclopsGunnerStation", "CoopCyclopsEngineerConsole", "CoopCyclopsDroneBay", "CoopScoutDrone");
        CoopItems.All.Count(item => item.Kind == CoopItemKind.DepthModule).Should().Be(6);
        CoopItems.All.Count(item => item.Kind == CoopItemKind.TowHook).Should().Be(2);
        CoopItems.All.Count(item => item.Kind == CoopItemKind.TerrainSonar).Should().Be(1);
        CoopItems.All.Count(item => item.Kind == CoopItemKind.GunnerStation).Should().Be(1);
        CoopItems.All.Count(item => item.Kind == CoopItemKind.EngineerConsole).Should().Be(1);
        CoopItems.All.Count(item => item.Kind == CoopItemKind.DroneBay).Should().Be(1);
        CoopItems.All.Count(item => item.Kind == CoopItemKind.ScoutDrone).Should().Be(1);
        NewItems.Should().HaveCount(17);
        CoopItems.All.Where(item => item.Adopted).Select(item => item.Key).Should().Equal("SeamothSolarCharge");
    }

    [TestMethod]
    public void TheNumbersAreFixedForever()
    {
        // Saved worlds contain these numbers as text; a build that changes one turns the items of every existing world into something else
        NewItems.Select(item => item.TechTypeNumber).Should().Equal(20001, 20002, 20003, 20004, 20005, 20006, 20007, 20008, 20009, 20010, 20011, 20012, 20013, 20014, 20015, 20016, 20017);
        CoopItems.Get(CoopItems.SEAMOTH_SOLAR).TechType.Should().Be(TechType.SeamothSolarCharge, "the adopted item keeps the number the game gave it");
        CoopItems.SEAMOTH_SOLAR.Should().Be(2102);
    }

    [TestMethod]
    public void TheClassIdsAreFixedForever()
    {
        CoopItems.All.Select(item => item.ClassId).Should().Equal(
            "499d6269-555e-405f-9d64-0ae1d9166499", "4de8e5e8-60cb-44fe-9e52-9926323c3850", "b39ec19b-873f-4ba3-8867-186f3e7d36f6", "f2e5fc81-2f43-4aec-a087-afb2d9d42961",
            "08a80860-71ca-4edf-a3f2-7ddb4332fbec", "5aae431e-a2bc-4631-bbe8-8f1b58d04ed8", "c34467fe-9b28-488a-8a18-31483ad29434", "b008c2c5-4ad0-4321-a776-d78ec9d83c6f", "06c26e8c-51ff-4089-8121-5f971aa402ef",
            "80269355-00d1-4bf5-980a-e97ec430a154", "c5e8cda1-be2c-4ca6-aa3c-e2a693f0fc44", "1cfe7c35-ae11-4f8a-a91f-870e612de060", "c4f46458-317a-4a26-a60e-40accb9c2f26",
            "d73c3b12-9d53-41cf-9a83-ec77eadbc411", "247ea25f-9f3d-486b-8702-5d7f97c10d8c", "4c5fd870-c704-4ff9-a7f9-e8631d8bddbd", "f44bd6df-c8c3-431b-b392-900a6482d55c", "7e88148b-7508-444a-bc21-7527836543c1");
    }

    [TestMethod]
    public void TheNumbersAreUniqueAndAboveEveryValueOfTheGame()
    {
        int highest = HighestVanillaTechType;

        highest.Should().Be(10004, "the research of the game's enum: Databox is the highest value; if a game update raises it the block of the co-op build has to be checked");
        CoopItems.All.Select(item => item.TechTypeNumber).Should().OnlyHaveUniqueItems();
        foreach (CoopItemDefinition item in NewItems)
        {
            item.TechTypeNumber.Should().BeGreaterThan(highest, item.Key);
            Enum.IsDefined(typeof(TechType), item.TechTypeNumber).Should().BeFalse($"{item.Key} must not be a value of the game's enum");
        }
        foreach (CoopItemDefinition item in CoopItems.All.Where(item => item.Adopted))
        {
            Enum.IsDefined(typeof(TechType), item.TechTypeNumber).Should().BeTrue($"{item.Key} is an item of the game");
            ((TechType)item.TechTypeNumber).ToString().Should().Be(item.Key, "the key of an adopted item is the game's own name for it");
        }
        CoopItems.FIRST_TECH_TYPE.Should().Be(NewItems.Min(item => item.TechTypeNumber));
    }

    [TestMethod]
    public void TheNamesAreUniqueAndCannotMeetTheNameOfAGameItem()
    {
        string[] vanillaNames = Enum.GetNames(typeof(TechType));

        CoopItems.All.Select(item => item.Key).Should().OnlyHaveUniqueItems();
        foreach (CoopItemDefinition item in NewItems)
        {
            item.Key.Should().StartWith("Coop");
            item.Key.Should().MatchRegex("^[A-Za-z0-9]+$", "the name is a language key, a sprite name and a name in the game's parse tables");
            vanillaNames.Should().NotContain(name => string.Equals(name, item.Key, StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void TheClassIdsAreUniqueGuids()
    {
        CoopItems.All.Select(item => item.ClassId).Should().OnlyHaveUniqueItems();
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            Guid.TryParse(item.ClassId, out Guid guid).Should().BeTrue(item.Key);
            guid.ToString("D").Should().Be(item.ClassId, "the game's class ids are lower case GUIDs with dashes");
            CoopItems.IsCoopClassId(item.ClassId).Should().BeTrue();
        }
        CoopItems.IsCoopClassId("not an id").Should().BeFalse();
        CoopItems.IsCoopClassId(null).Should().BeFalse();
    }

    [TestMethod]
    public void TheTechTypeSurvivesTheWayThroughThePacketsInBothDirections()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            // What the game makes of a value that is not in its enum, which is what the packets and the saved world carry
            item.TechType.ToString().Should().Be(item.NitroxName);
            item.NitroxName.Should().Be(item.Adopted ? item.Key : item.TechTypeNumber.ToString(CultureInfo.InvariantCulture));

            // Nitrox: ToDto is a new NitroxTechType(value.ToString()), ToUnity is Enum.Parse
            item.TechType.ToDto().Name.Should().Be(item.NitroxName);
            item.TechType.ToDto().ToUnity().Should().Be(item.TechType);
            ((TechType)Enum.Parse(typeof(TechType), item.NitroxName)).Should().Be(item.TechType);
        }
    }

    [TestMethod]
    public void TheLookupFindsEveryItemAndNothingElse()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            CoopItems.IsCoopItem(item.TechType).Should().BeTrue();
            CoopItems.TryGet(item.TechType, out CoopItemDefinition found).Should().BeTrue();
            found.Should().BeSameAs(item);
            CoopItems.Get(item.TechTypeNumber).Should().BeSameAs(item);
        }
        CoopItems.IsCoopItem(TechType.Seamoth).Should().BeFalse();
        CoopItems.IsCoopItem((TechType)20100).Should().BeFalse();
        CoopItems.Get(1).Should().BeNull();
    }

    [TestMethod]
    public void EveryItemHasAnAsciiNameAndATooltipWithOnlyKnownPlaceholders()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            item.DisplayName.Should().NotBeNullOrWhiteSpace();
            item.Tooltip.Should().NotBeNullOrWhiteSpace();
            item.DisplayName.Should().MatchRegex("^[ -~]+$", "the game computes the glyphs of its font before the mod's strings are added");
            item.Tooltip.Should().MatchRegex("^[ -~]+$");
            foreach (Match placeholder in Regex.Matches(item.Tooltip, @"\{[A-Za-z]+\}"))
            {
                KnownPlaceholders.Should().Contain(placeholder.Value, item.Key);
            }
            item.TooltipKey.Should().Be($"Tooltip_{item.Key}");
        }
    }

    [TestMethod]
    public void EveryItemHasARecipeWithRealIngredients()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            item.Recipe.Should().NotBeEmpty(item.Key);
            item.Recipe.Select(line => line.Tech).Should().OnlyHaveUniqueItems(item.Key);
            foreach (CoopIngredient line in item.Recipe)
            {
                line.Amount.Should().BeGreaterThan(0);
                line.Tech.Should().NotBe(item.TechType, "an item cannot be an ingredient of itself");
                bool known = Enum.IsDefined(typeof(TechType), line.Tech) || CoopItems.IsCoopItem(line.Tech);
                known.Should().BeTrue($"{item.Key}: ingredient {(int)line.Tech} is neither an item of the game nor of the table");
            }
            item.CraftTimeSeconds.Should().BeInRange(1f, 30f);
        }
    }

    [TestMethod]
    public void TheNextTierIsBuiltFromThePreviousOne()
    {
        // MK5 = MK4 + materials, like every vanilla depth module is built from the module of the tier before
        RecipeOf(CoopItems.SEAMOTH_DEPTH_MK4).Select(line => line.Tech).Should().Contain(TechType.VehicleHullModule3);
        RecipeOf(CoopItems.SEAMOTH_DEPTH_MK5).Select(line => line.Tech).Should().Contain((TechType)CoopItems.SEAMOTH_DEPTH_MK4);
        RecipeOf(CoopItems.PRAWN_DEPTH_MK3).Select(line => line.Tech).Should().Contain(TechType.ExoHullModule2);
        RecipeOf(CoopItems.PRAWN_DEPTH_MK4).Select(line => line.Tech).Should().Contain((TechType)CoopItems.PRAWN_DEPTH_MK3);
        RecipeOf(CoopItems.CYCLOPS_DEPTH_MK4).Select(line => line.Tech).Should().Contain(TechType.CyclopsHullModule3);
        RecipeOf(CoopItems.CYCLOPS_DEPTH_MK5).Select(line => line.Tech).Should().Contain((TechType)CoopItems.CYCLOPS_DEPTH_MK4);
    }

    [TestMethod]
    public void TheRecipesAreTheOnesOfTheDocumentation()
    {
        Describe(CoopItems.SEAMOTH_DEPTH_MK4).Should().Be("VehicleHullModule3 x1, PlasteelIngot x1, Kyanite x3, Nickel x2");
        Describe(CoopItems.SEAMOTH_DEPTH_MK5).Should().Be("20001 x1, PlasteelIngot x2, Kyanite x4, Diamond x2");
        Describe(CoopItems.PRAWN_DEPTH_MK3).Should().Be("ExoHullModule2 x1, PlasteelIngot x1, Kyanite x3, Nickel x2");
        Describe(CoopItems.PRAWN_DEPTH_MK4).Should().Be("20003 x1, PlasteelIngot x2, Kyanite x4, Diamond x2");
        Describe(CoopItems.CYCLOPS_DEPTH_MK4).Should().Be("CyclopsHullModule3 x1, PlasteelIngot x2, Kyanite x4, Nickel x3");
        Describe(CoopItems.CYCLOPS_DEPTH_MK5).Should().Be("20005 x1, PlasteelIngot x3, Kyanite x5, Diamond x3");
        Describe(CoopItems.SEAMOTH_AFTERBURNER).Should().Be("AdvancedWiringKit x1, Lubricant x2, Sulphur x2");
        Describe(CoopItems.SEAMOTH_SOLAR).Should().Be("AdvancedWiringKit x1, EnameledGlass x1, Quartz x2");
        Describe(CoopItems.CYCLOPS_SOLAR).Should().Be("AdvancedWiringKit x1, EnameledGlass x2, Quartz x3");
        Describe(CoopItems.CYCLOPS_POWER_SHARE).Should().Be("AdvancedWiringKit x1, PowerCell x1, CopperWire x2");
        Describe(CoopItems.SEAMOTH_TOW_HOOK).Should().Be("ComputerChip x1, Lubricant x1, Titanium x3");
        Describe(CoopItems.CYCLOPS_TOW_HOOK).Should().Be("AdvancedWiringKit x1, PlasteelIngot x1, Lubricant x2");
        Describe(CoopItems.SEAMOTH_RIDER_BAR).Should().Be("Titanium x3, Lubricant x1");
        Describe(CoopItems.CYCLOPS_TERRAIN_SONAR).Should().Be("AdvancedWiringKit x1, ComputerChip x2, EnameledGlass x2");
        Describe(CoopItems.CYCLOPS_GUNNER_STATION).Should().Be("AdvancedWiringKit x1, ComputerChip x2, PlasteelIngot x1");
        Describe(CoopItems.CYCLOPS_ENGINEER_CONSOLE).Should().Be("AdvancedWiringKit x1, ComputerChip x1, Lubricant x2");
        Describe(CoopItems.CYCLOPS_DRONE_BAY).Should().Be("AdvancedWiringKit x1, Titanium x2, Lubricant x1");
        Describe(CoopItems.SCOUT_DRONE).Should().Be("AdvancedWiringKit x1, ComputerChip x1, Battery x1, Titanium x2");
    }

    [TestMethod]
    public void TheBorrowedItemsOfTheGameExist()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            Enum.IsDefined(typeof(TechType), item.Template).Should().BeTrue($"{item.Key}: template");
            Enum.IsDefined(typeof(TechType), item.IconSource).Should().BeTrue($"{item.Key}: icon");
            item.Template.Should().NotBe(TechType.None);
            item.IconSource.Should().NotBe(TechType.None);
        }
    }

    [TestMethod]
    public void EveryItemIsCraftedAtAFabricatorWithANodeThatFitsItsTree()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            item.Tree.Should().BeOneOf(CraftTree.Type.Workbench, CraftTree.Type.SeamothUpgrades, CraftTree.Type.CyclopsFabricator);
            if (item.Tree == CraftTree.Type.SeamothUpgrades)
            {
                item.TabPath.Should().Equal(["SeamothModules"], "the node goes into the Seamoth tab of the Vehicle Upgrade Console");
            }
            else
            {
                item.TabPath.Should().BeEmpty($"{item.Key}: the Modification Station and the Cyclops fabricator have no tabs");
            }
        }
    }

    [TestMethod]
    public void EachItemIsCraftedWhereTheVanillaTierBeforeItIsCrafted()
    {
        // Seamoth MK3, Prawn Suit MK2 and Cyclops MK3 are all made at the Modification Station
        foreach (int number in new[] { CoopItems.SEAMOTH_DEPTH_MK4, CoopItems.SEAMOTH_DEPTH_MK5, CoopItems.PRAWN_DEPTH_MK3, CoopItems.PRAWN_DEPTH_MK4, CoopItems.CYCLOPS_DEPTH_MK4, CoopItems.CYCLOPS_DEPTH_MK5 })
        {
            CoopItems.Get(number).Tree.Should().Be(CraftTree.Type.Workbench);
        }
        CoopItems.Get(CoopItems.SEAMOTH_AFTERBURNER).Tree.Should().Be(CraftTree.Type.SeamothUpgrades);
        CoopItems.Get(CoopItems.SEAMOTH_SOLAR).Tree.Should().Be(CraftTree.Type.SeamothUpgrades, "where the game has its node already");
        CoopItems.Get(CoopItems.CYCLOPS_SOLAR).Tree.Should().Be(CraftTree.Type.CyclopsFabricator, "next to the thermal reactor module");
        CoopItems.Get(CoopItems.CYCLOPS_POWER_SHARE).Tree.Should().Be(CraftTree.Type.CyclopsFabricator);
        CoopItems.Get(CoopItems.CYCLOPS_TOW_HOOK).Tree.Should().Be(CraftTree.Type.CyclopsFabricator);
        CoopItems.Get(CoopItems.SEAMOTH_TOW_HOOK).Tree.Should().Be(CraftTree.Type.SeamothUpgrades, "the Vehicle Upgrade Console");
        CoopItems.Get(CoopItems.SEAMOTH_RIDER_BAR).Tree.Should().Be(CraftTree.Type.SeamothUpgrades, "the Vehicle Upgrade Console");
        CoopItems.Get(CoopItems.CYCLOPS_TERRAIN_SONAR).Tree.Should().Be(CraftTree.Type.CyclopsFabricator);
        CoopItems.Get(CoopItems.CYCLOPS_GUNNER_STATION).Tree.Should().Be(CraftTree.Type.CyclopsFabricator);
        CoopItems.Get(CoopItems.CYCLOPS_ENGINEER_CONSOLE).Tree.Should().Be(CraftTree.Type.CyclopsFabricator);
    }

    [TestMethod]
    public void EachItemFitsTheSlotsOfItsVehicleAndOnlyTheAfterburnerIsInstantAndOnlyTheSeamothTowHookIsAToggle()
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            if (item.Kind == CoopItemKind.ScoutDrone)
            {
                // the one item that is not a module: it goes into the bay, not into a slot, and takes the room of the game's camera drone in the inventory
                item.Equipment.Should().Be(EquipmentType.None, item.Key);
                item.QuickSlot.Should().Be(QuickSlotType.None, item.Key);
                item.SizeX.Should().Be(2);
                item.SizeY.Should().Be(2);
                continue;
            }
            EquipmentType expected = item.Vehicle switch
            {
                CoopVehicle.Seamoth => EquipmentType.SeamothModule,
                CoopVehicle.Prawn => EquipmentType.ExosuitModule,
                _ => EquipmentType.CyclopsModule
            };
            item.Equipment.Should().Be(expected, item.Key);
            QuickSlotType expectedSlot = item.Kind == CoopItemKind.Afterburner ? QuickSlotType.Instant : item.Key == "CoopSeamothTowHook" ? QuickSlotType.Toggleable : QuickSlotType.Passive;
            item.QuickSlot.Should().Be(expectedSlot, item.Key);
            item.SizeX.Should().Be(1);
            item.SizeY.Should().Be(1);
        }
    }

    [TestMethod]
    public void TheUnlockPrerequisitesAreTheTierBeforeAndTheVehicle()
    {
        CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4).Prerequisite.Should().Be(TechType.VehicleHullModule3);
        CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK5).Prerequisite.Should().Be(TechType.VehicleHullModule3, "MK4 and MK5 are both known once MK3 is");
        CoopItems.Get(CoopItems.PRAWN_DEPTH_MK3).Prerequisite.Should().Be(TechType.ExoHullModule2);
        CoopItems.Get(CoopItems.PRAWN_DEPTH_MK4).Prerequisite.Should().Be(TechType.ExoHullModule2);
        CoopItems.Get(CoopItems.CYCLOPS_DEPTH_MK4).Prerequisite.Should().Be(TechType.CyclopsHullModule3);
        CoopItems.Get(CoopItems.CYCLOPS_DEPTH_MK5).Prerequisite.Should().Be(TechType.CyclopsHullModule3);
        CoopItems.Get(CoopItems.SEAMOTH_AFTERBURNER).Prerequisite.Should().Be(TechType.Seamoth);
        CoopItems.Get(CoopItems.SEAMOTH_SOLAR).Prerequisite.Should().Be(TechType.Seamoth);
        CoopItems.Get(CoopItems.CYCLOPS_SOLAR).Prerequisite.Should().Be(TechType.Cyclops);
        CoopItems.Get(CoopItems.CYCLOPS_POWER_SHARE).Prerequisite.Should().Be(TechType.Cyclops);
        CoopItems.Get(CoopItems.CYCLOPS_TOW_HOOK).Prerequisite.Should().Be(TechType.Cyclops);
        CoopItems.Get(CoopItems.SEAMOTH_TOW_HOOK).Prerequisite.Should().Be(TechType.Seamoth);
        CoopItems.Get(CoopItems.SEAMOTH_RIDER_BAR).Prerequisite.Should().Be(TechType.Seamoth);
        CoopItems.Get(CoopItems.CYCLOPS_TERRAIN_SONAR).Prerequisite.Should().Be(TechType.Cyclops);
        CoopItems.Get(CoopItems.CYCLOPS_GUNNER_STATION).Prerequisite.Should().Be(TechType.Cyclops);
        CoopItems.Get(CoopItems.CYCLOPS_ENGINEER_CONSOLE).Prerequisite.Should().Be(TechType.Cyclops);
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            Enum.IsDefined(typeof(TechType), item.Prerequisite).Should().BeTrue($"{item.Key}: the prerequisite is a blueprint of the game");
            item.Prerequisite.Should().NotBe(TechType.None);
        }
    }

    [TestMethod]
    public void TheTiersOfTheDepthModulesCountUpFromOnePerVehicle()
    {
        foreach (IGrouping<CoopVehicle, CoopItemDefinition> vehicle in CoopItems.All.Where(item => item.Kind == CoopItemKind.DepthModule).GroupBy(item => item.Vehicle))
        {
            vehicle.Select(item => item.Tier).Should().Equal(1, 2);
        }
        foreach (CoopItemDefinition item in CoopItems.All.Where(item => item.Kind != CoopItemKind.DepthModule))
        {
            item.Tier.Should().Be(0);
        }
    }

    [TestMethod]
    public void TheIconTierIsTheNumberInTheNameOfTheModule()
    {
        foreach (CoopItemDefinition item in CoopItems.All.Where(item => item.Kind == CoopItemKind.DepthModule))
        {
            item.DisplayName.Should().EndWith($"MK{item.IconTier}");
            item.Key.Should().EndWith($"MK{item.IconTier}");
        }
        foreach (CoopItemDefinition item in CoopItems.All.Where(item => item.Kind != CoopItemKind.DepthModule))
        {
            item.IconTier.Should().Be(0, "only the depth modules are told apart by a number on a copy of the same icon");
        }
    }

    private static IEnumerable<CoopItemDefinition> NewItems => CoopItems.All.Where(item => !item.Adopted);

    [TestMethod]
    public void TheAdoptedItemIsNamedLikeTheGameNamesItAndANewItemByItsNumber()
    {
        CoopItemDefinition solar = CoopItems.Get(CoopItems.SEAMOTH_SOLAR);

        solar.Adopted.Should().BeTrue();
        solar.NitroxName.Should().Be("SeamothSolarCharge", "TechType.ToString() of a value the enum defines is its name, and that is what Enum.Parse reads back");
        solar.Kind.Should().Be(CoopItemKind.SolarCharger);
        CoopItems.Get(CoopItems.SEAMOTH_DEPTH_MK4).Adopted.Should().BeFalse();
    }

    private static CoopIngredient[] RecipeOf(int number) => CoopItems.Get(number).Recipe;

    private static string Describe(int number)
    {
        return string.Join(", ", RecipeOf(number).Select(line => $"{(CoopItems.IsCoopItem(line.Tech) ? ((int)line.Tech).ToString(CultureInfo.InvariantCulture) : line.Tech.ToString())} x{line.Amount}"));
    }
}
