using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>The player's quick bar of eight slots (docs/coop/VEHICLE_UPGRADES.md, "Eight quick slots for the player"): the numbers, the names the game's code indexes, and what a join does with a saved bar of another length.</summary>
[TestClass]
public class PlayerQuickSlotsTest
{
    private static string ReadFile(params string[] path)
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine([directory, .. path]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException(Path.Combine(path));
    }

    [TestMethod]
    public void TheBarHasEightSlotsAndTheExtraKeysAreTheThreeAfterTheGamesFive()
    {
        PlayerQuickSlots.COUNT.Should().Be(8);
        PlayerQuickSlots.GAME_COUNT.Should().Be(5, "Inventory.Awake makes the game's bar with the literal 5");
        PlayerQuickSlots.EXTRA_KEYS.Should().Be(3, "the keys 6, 7 and 8");
        PlayerQuickSlots.FIRST_EXTRA_KEY_NUMBER.Should().Be(QuickSlotKeyRules.SIXTH_KEY_NUMBER, "the game's keys 1 to 5 hand 0 to 4 to IQuickSlots.SlotKeyDown, so key 6 is 5, as for a vehicle's sixth key");
    }

    [TestMethod]
    public void TheSlotNamesFollowTheGamesNamingAndThereIsOneForEverySlot()
    {
        PlayerQuickSlots.SlotNames.Should().HaveCount(PlayerQuickSlots.COUNT, "the game's code reads the name of a slot with the slot's number");
        PlayerQuickSlots.SlotNames.Should().Equal("QuickSlot0", "QuickSlot1", "QuickSlot2", "QuickSlot3", "QuickSlot4", "QuickSlot5", "QuickSlot6", "QuickSlot7");
        PlayerQuickSlots.MakeNames(3).Should().Equal("QuickSlot0", "QuickSlot1", "QuickSlot2");
        PlayerQuickSlots.MakeNames(0).Should().BeEmpty();
        PlayerQuickSlots.MakeNames(-4).Should().BeEmpty();
    }

    [TestMethod]
    public void ASavedBarOfFiveLeavesTheLastThreeSlotsToBeEmptiedAndNothingIsWrittenPastTheBar()
    {
        PlayerQuickSlots.SlotsWithoutASavedBinding(5, 8).Should().Equal(5, 6, 7);
        PlayerQuickSlots.SlotsWithoutASavedBinding(8, 8).Should().BeEmpty("a save of this build says something about every slot");
        PlayerQuickSlots.SlotsWithoutASavedBinding(0, 8).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
        PlayerQuickSlots.SlotsWithoutASavedBinding(-3, 8).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
        PlayerQuickSlots.SlotsWithoutASavedBinding(11, 8).Should().BeEmpty();
        PlayerQuickSlots.SlotsWithoutASavedBinding(5, 5).Should().BeEmpty("a bar that kept the game's five");

        PlayerQuickSlots.SlotsToRestore(5, 8).Should().Be(5);
        PlayerQuickSlots.SlotsToRestore(8, 8).Should().Be(8);
        PlayerQuickSlots.SlotsToRestore(8, 5).Should().Be(5, "a bar of five (the patch did not take hold) must not be written past its end with a save of eight");
        PlayerQuickSlots.SlotsToRestore(0, 8).Should().Be(0);
        PlayerQuickSlots.SlotsToRestore(-1, 8).Should().Be(0);
    }

    [TestMethod]
    public void TheDocsTellAboutTheBarAndTheOptionsHaveTheKeys()
    {
        string docs = ReadFile("docs", "coop", "VEHICLE_UPGRADES.md");

        docs.Should().Contain("### Eight quick slots for the player");
        docs.Should().Contain("`PlayerQuickSlots`");
        docs.Should().Contain("`PlayerQuickSlotKeys`");
        docs.Should().Contain("`Inventory_Awake_QuickSlots_Patch`");
        docs.Should().Contain("`QuickSlots_Update_SlotNames_Patch`");
        docs.Should().Contain("`QuickSlotInitialSyncProcessor`");
        docs.Should().Contain("Quick slot 7").And.Contain("Quick slot 8");
    }
}
