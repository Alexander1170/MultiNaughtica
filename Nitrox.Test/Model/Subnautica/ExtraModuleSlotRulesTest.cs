using Nitrox.Model.Configuration;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace Nitrox.Model.Subnautica;

/// <summary>
/// The names and the order of the extra module slots (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots"): the game's own lists stay as they are, the extra names come after them, the option decides how
/// many exist, and a module in a slot that does not exist is dormant.
/// </summary>
[TestClass]
public class ExtraModuleSlotRulesTest
{
    [TestMethod]
    public void TheVanillaListsAreTheGamesOwnNamesInTheGamesOwnOrder()
    {
        ExtraModuleSlotRules.VanillaSeamoth.Should().Equal("SeamothModule1", "SeamothModule2", "SeamothModule3", "SeamothModule4");
        // the arms come first: the game addresses them (and the first modules) by position
        ExtraModuleSlotRules.VanillaExosuit.Should().Equal("ExosuitArmLeft", "ExosuitArmRight", "ExosuitModule1", "ExosuitModule2", "ExosuitModule3", "ExosuitModule4");
        ExtraModuleSlotRules.VanillaCyclops.Should().Equal("Module1", "Module2", "Module3", "Module4", "Module5", "Module6");
    }

    [TestMethod]
    public void WithNoExtraSlotsTheListsAreVanilla()
    {
        ExtraModuleSlotRules.Seamoth(0).Should().Equal(ExtraModuleSlotRules.VanillaSeamoth);
        ExtraModuleSlotRules.Exosuit(0).Should().Equal(ExtraModuleSlotRules.VanillaExosuit);
        ExtraModuleSlotRules.Cyclops(0).Should().Equal(ExtraModuleSlotRules.VanillaCyclops);
    }

    [TestMethod]
    public void TwoExtraSlotsMakeSixSixAndEight()
    {
        ExtraModuleSlotRules.Seamoth(2).Should().HaveCount(6).And.EndWith(new[] { "SeamothModule5", "SeamothModule6" });
        // the Prawn Suit has six positions in the game (two arms and four modules): the new ones are appended, so the arms and the four modules keep their positions
        ExtraModuleSlotRules.Exosuit(2).Should().HaveCount(8).And.StartWith(ExtraModuleSlotRules.VanillaExosuit).And.EndWith(new[] { "ExosuitModule5", "ExosuitModule6" });
        ExtraModuleSlotRules.Cyclops(2).Should().HaveCount(8).And.StartWith(ExtraModuleSlotRules.VanillaCyclops).And.EndWith(new[] { "Module7", "Module8" });
    }

    [TestMethod]
    public void OneExtraSlotAddsTheFirstNameOnly()
    {
        ExtraModuleSlotRules.Seamoth(1).Should().HaveCount(5).And.EndWith("SeamothModule5");
        ExtraModuleSlotRules.Exosuit(1).Should().HaveCount(7).And.EndWith("ExosuitModule5");
        ExtraModuleSlotRules.Cyclops(1).Should().HaveCount(7).And.EndWith("Module7");
    }

    [TestMethod]
    public void TheOptionIsClampedToZeroToTwo()
    {
        ExtraModuleSlotRules.Clamp(-3).Should().Be(0);
        ExtraModuleSlotRules.Clamp(7).Should().Be(2);
        ExtraModuleSlotRules.Seamoth(-1).Should().Equal(ExtraModuleSlotRules.VanillaSeamoth);
        ExtraModuleSlotRules.Cyclops(99).Should().HaveCount(8);
        ExtraModuleSlotRules.DEFAULT_EXTRA.Should().Be(2);
    }

    [TestMethod]
    public void EveryNameIsUniqueAcrossTheVehicles()
    {
        List<string> all = [.. ExtraModuleSlotRules.Seamoth(2), .. ExtraModuleSlotRules.Exosuit(2), .. ExtraModuleSlotRules.Cyclops(2)];
        all.Should().OnlyHaveUniqueItems();
        ExtraModuleSlotRules.AllExtraSlots.Select(pair => pair.Slot).Should().OnlyHaveUniqueItems().And.HaveCount(6);
    }

    [TestMethod]
    public void ExtraSlotsAreRecognisedByName()
    {
        ExtraModuleSlotRules.IsExtraSlot("SeamothModule5").Should().BeTrue();
        ExtraModuleSlotRules.IsExtraSlot("Module8").Should().BeTrue();
        ExtraModuleSlotRules.IsExtraSlot("Module6").Should().BeFalse();
        ExtraModuleSlotRules.IsExtraSlot("ExosuitArmLeft").Should().BeFalse();
        ExtraModuleSlotRules.AllExtraSlots.Where(pair => pair.Kind == ExtraSlotKind.Exosuit).Select(pair => pair.Slot).Should().Equal("ExosuitModule5", "ExosuitModule6");
    }

    [TestMethod]
    public void ExtraOfIsWhatAVehicleMadeBeforeTheOptionArrivedHasToBeGiven()
    {
        ExtraModuleSlotRules.ExtraOf(ExtraSlotKind.Seamoth, 2).Should().Equal("SeamothModule5", "SeamothModule6");
        ExtraModuleSlotRules.ExtraOf(ExtraSlotKind.Cyclops, 1).Should().Equal("Module7");
        ExtraModuleSlotRules.ExtraOf(ExtraSlotKind.Exosuit, 0).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("SeamothModule4", 0, true)]
    [DataRow("ExosuitArmRight", 0, true)]
    [DataRow("Module6", 0, true)]
    [DataRow("SeamothModule5", 0, false)]
    [DataRow("SeamothModule5", 1, true)]
    [DataRow("SeamothModule6", 1, false)]
    [DataRow("SeamothModule6", 2, true)]
    [DataRow("ExosuitModule6", 1, false)]
    [DataRow("Module7", 1, true)]
    [DataRow("Module8", 1, false)]
    [DataRow("Module8", 2, true)]
    [DataRow("SomethingElse", 0, true)]
    public void AModuleInASlotThatDoesNotExistIsDormant(string slot, int extra, bool exists)
    {
        // a module in a slot that does not exist (the option was lowered, or an older build loads the world) is not installed and not touched: it stays in the save and returns with the slot
        ExtraModuleSlotRules.SlotExists(slot, extra).Should().Be(exists);
    }

    [TestMethod]
    public void OnlyAnExtraSlotThatTheEquipmentLacksIsDormant()
    {
        ExtraModuleSlotRules.IsDormant("SeamothModule6", equipmentHasSlot: false).Should().BeTrue();
        ExtraModuleSlotRules.IsDormant("Module7", equipmentHasSlot: false).Should().BeTrue();
        ExtraModuleSlotRules.IsDormant("SeamothModule6", equipmentHasSlot: true).Should().BeFalse();
        // a slot of the game itself that is missing is a fault, not dormancy
        ExtraModuleSlotRules.IsDormant("SeamothModule3", equipmentHasSlot: false).Should().BeFalse();
        ExtraModuleSlotRules.IsDormant("Module6", equipmentHasSlot: false).Should().BeFalse();
    }

    [TestMethod]
    public void EveryVanillaSlotHasAPlace()
    {
        List<string> all = [.. ExtraModuleSlotRules.VanillaSeamoth, .. ExtraModuleSlotRules.VanillaExosuit, .. ExtraModuleSlotRules.VanillaCyclops];
        ExtraModuleSlotRules.VanillaLayout.Keys.Should().BeEquivalentTo(all);
    }

    [TestMethod]
    public void NoExtraSlotCoversAnotherSlotOfItsVehicle()
    {
        Dictionary<ExtraSlotKind, IReadOnlyList<string>> vanilla = new()
        {
            [ExtraSlotKind.Seamoth] = ExtraModuleSlotRules.VanillaSeamoth,
            [ExtraSlotKind.Exosuit] = ExtraModuleSlotRules.VanillaExosuit,
            [ExtraSlotKind.Cyclops] = ExtraModuleSlotRules.VanillaCyclops
        };
        foreach ((string slot, ExtraSlotKind kind) in ExtraModuleSlotRules.AllExtraSlots)
        {
            (float x, float y) = ExtraModuleSlotRules.LayoutOf(slot);
            List<(string Other, float X, float Y, float Size)> others = vanilla[kind].Select(id => (id, ExtraModuleSlotRules.VanillaLayout[id].X, ExtraModuleSlotRules.VanillaLayout[id].Y, ExtraModuleSlotRules.VanillaLayout[id].Size)).ToList();
            others.AddRange(ExtraModuleSlotRules.AllExtraSlots.Where(pair => pair.Kind == kind && pair.Slot != slot).Select(pair => (pair.Slot, ExtraModuleSlotRules.LayoutOf(pair.Slot).X, ExtraModuleSlotRules.LayoutOf(pair.Slot).Y, ExtraModuleSlotRules.SLOT_SIZE)));
            foreach ((string other, float otherX, float otherY, float otherSize) in others)
            {
                float reach = (ExtraModuleSlotRules.SLOT_SIZE + otherSize) / 2f;
                bool overlaps = Math.Abs(x - otherX) < reach && Math.Abs(y - otherY) < reach;
                overlaps.Should().BeFalse($"{slot} at ({x}, {y}) must not cover {other} at ({otherX}, {otherY})");
            }
        }
    }

    [TestMethod]
    public void EveryExtraSlotCopiesAPlainSlotOfItsOwnVehicle()
    {
        foreach ((string slot, ExtraSlotKind kind) in ExtraModuleSlotRules.AllExtraSlots)
        {
            string template = ExtraModuleSlotRules.TemplateOf(slot);
            IReadOnlyList<string> own = kind switch
            {
                ExtraSlotKind.Seamoth => ExtraModuleSlotRules.VanillaSeamoth,
                ExtraSlotKind.Exosuit => ExtraModuleSlotRules.VanillaExosuit,
                _ => ExtraModuleSlotRules.VanillaCyclops
            };
            own.Should().Contain(template);
            // the first module slot of each group carries the picture of the vehicle, so a copy of it would carry the picture too
            template.Should().NotBe(own.First(id => id.EndsWith("1", StringComparison.Ordinal)));
        }
        Action unknown = () => ExtraModuleSlotRules.LayoutOf("Module6");
        unknown.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void TheOptionIsClampedWhenAPlayerJoinsAndSaysSo()
    {
        SessionSettings told = new();
        List<string> messages = [];

        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { ExtraModuleSlots = 7 }, messages);
        told.ExtraModuleSlots.Should().Be(ExtraModuleSlotRules.MAX_EXTRA);
        messages.Should().ContainSingle().Which.Should().Contain("ExtraModuleSlots").And.Contain("above 2");

        messages.Clear();
        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { ExtraModuleSlots = -3 }, messages);
        told.ExtraModuleSlots.Should().Be(ExtraModuleSlotRules.MIN_EXTRA);
        messages.Should().ContainSingle().Which.Should().Contain("below 0");

        messages.Clear();
        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { ExtraModuleSlots = 1 }, messages);
        told.ExtraModuleSlots.Should().Be(1);
        messages.Should().BeEmpty();
    }

    [TestMethod]
    public void TheClientTakesTheOptionAndClampsItAgain()
    {
        try
        {
            VehicleUpgradeSettings.Apply(new SessionSettings { ExtraModuleSlots = 1 });
            VehicleUpgradeSettings.ExtraModuleSlots.Should().Be(1);

            VehicleUpgradeSettings.Apply(new SessionSettings { ExtraModuleSlots = 99 });
            VehicleUpgradeSettings.ExtraModuleSlots.Should().Be(ExtraModuleSlotRules.MAX_EXTRA);
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
        VehicleUpgradeSettings.ExtraModuleSlots.Should().Be(ExtraModuleSlotRules.DEFAULT_EXTRA);
    }

    [TestMethod]
    public void TheListsOfTheGameFollowTheOptionInBothDirectionsInOneProcess()
    {
        // the lists are read through getters that are patched in, not through the game's static readonly arrays: a session with 2, one with 0 and one with 1 in the same process must each see their own
        try
        {
            VehicleSlotLists.UseOption(2).Should().BeTrue();
            VehicleSlotLists.SeamothSlots.Should().HaveCount(6);
            VehicleSlotLists.ExosuitSlots.Should().HaveCount(8);
            VehicleSlotLists.CyclopsSlotNames.Should().HaveCount(8);
            VehicleSlotLists.CyclopsSlotCount.Should().Be(8);
            VehicleSlotLists.ExtraCyclopsSlots.Should().Equal("Module7", "Module8");

            VehicleSlotLists.UseOption(0).Should().BeTrue();
            VehicleSlotLists.SeamothSlots.Should().Equal(ExtraModuleSlotRules.VanillaSeamoth);
            VehicleSlotLists.ExosuitSlots.Should().Equal(ExtraModuleSlotRules.VanillaExosuit);
            VehicleSlotLists.CyclopsSlotNames.Should().Equal(ExtraModuleSlotRules.VanillaCyclops);
            VehicleSlotLists.CyclopsSlotCount.Should().Be(6);
            VehicleSlotLists.ExtraCyclopsSlots.Should().BeEmpty();

            VehicleSlotLists.UseOption(1).Should().BeTrue();
            VehicleSlotLists.SeamothSlots.Should().HaveCount(5);
            VehicleSlotLists.CyclopsSlotNames.Should().HaveCount(7);
            VehicleSlotLists.ExtraCyclopsSlots.Should().Equal("Module7");

            VehicleSlotLists.UseOption(1).Should().BeFalse("the same number again changes nothing");
            VehicleSlotLists.UseOption(77).Should().BeTrue();
            VehicleSlotLists.Extra.Should().Be(ExtraModuleSlotRules.MAX_EXTRA);
        }
        finally
        {
            VehicleSlotLists.UseOption(0);
        }
        VehicleSlotLists.Extra.Should().Be(0);
    }
}
