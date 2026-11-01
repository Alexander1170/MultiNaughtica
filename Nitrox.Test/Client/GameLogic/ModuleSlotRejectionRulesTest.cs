namespace NitroxClient.GameLogic;

[TestClass]
public class ModuleSlotRejectionRulesTest
{
    [TestMethod]
    [DataRow(false, null, "SeamothModule1", ModuleTakeBack.NothingToDo, DisplayName = "not in any equipment of the owner (destroyed, in the inventory, in a locker)")]
    [DataRow(false, "SeamothModule1", "SeamothModule1", ModuleTakeBack.NothingToDo, DisplayName = "another owner's equipment holds a module of that slot name")]
    [DataRow(true, "SeamothModule1", "SeamothModule1", ModuleTakeBack.TakeOutOfSlot, DisplayName = "the dictionary holds it at the rejected slot")]
    [DataRow(true, "SeamothModule2", "SeamothModule1", ModuleTakeBack.NothingToDo, DisplayName = "moved to another slot since, that install is arbitrated on its own")]
    [DataRow(true, null, "SeamothModule1", ModuleTakeBack.ClearOrphan, DisplayName = "in the equipment but in no slot: the winner overwrote its entry")]
    [DataRow(true, "seamothmodule1", "SeamothModule1", ModuleTakeBack.NothingToDo, DisplayName = "slot names are compared exactly")]
    public void Classify_DecidesWhatToDoWithTheRefusedModule(bool inEquipmentOfParent, string slotInDictionary, string rejectedSlot, ModuleTakeBack expected)
    {
        ModuleSlotRejectionRules.Classify(inEquipmentOfParent, slotInDictionary, rejectedSlot).Should().Be(expected);
    }

    [TestMethod]
    public void Classify_ALateRejectionOfAModuleThatWasTakenOutAlready_DoesNothing()
    {
        // The reject follows the install by a round trip: by then the player may have taken the module out himself (G1 or his own unequip), and a second undo must not touch it
        ModuleSlotRejectionRules.Classify(itemIsInAnEquipmentOfTheParent: false, slotOfItemInDictionary: null, rejectedSlot: "SeamothModule1").Should().Be(ModuleTakeBack.NothingToDo);
    }
}
