using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Gui.Input;

namespace NitroxClient.GameLogic;

/// <summary>The keys of the quick slots of a vehicle with extra module slots: the sixth key, which the game does not have, and what it reaches on a Seamoth and on a Prawn Suit.</summary>
[TestClass]
public class QuickSlotKeyRulesTest
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
    public void TheSixthKeyHandsTheNumberAfterTheGamesFiveKeys()
    {
        QuickSlotKeyRules.SIXTH_KEY_NUMBER.Should().Be(5, "the game's keys 1 to 5 hand 0 to 4 to IQuickSlots.SlotKeyDown");
        QuickSlotKeyRules.SIXTH_MODULE.Should().Be(6);
    }

    [TestMethod]
    public void ASeamothHasASixthModuleOnlyWithBothExtraSlots()
    {
        QuickSlotKeyRules.HasSixthModule(isExosuit: false, ExtraModuleSlotRules.Seamoth(0).Length).Should().BeFalse("four slots");
        QuickSlotKeyRules.HasSixthModule(isExosuit: false, ExtraModuleSlotRules.Seamoth(1).Length).Should().BeFalse("five slots: the sixth key has nothing to press");
        QuickSlotKeyRules.HasSixthModule(isExosuit: false, ExtraModuleSlotRules.Seamoth(2).Length).Should().BeTrue("six slots");
    }

    [TestMethod]
    public void APrawnSuitHasASixthModuleOnlyWithBothExtraSlotsAndItsArmsComeFirst()
    {
        QuickSlotKeyRules.HasSixthModule(isExosuit: true, ExtraModuleSlotRules.Exosuit(0).Length).Should().BeFalse("two arms and four modules");
        QuickSlotKeyRules.HasSixthModule(isExosuit: true, ExtraModuleSlotRules.Exosuit(1).Length).Should().BeFalse("two arms and five modules");
        QuickSlotKeyRules.HasSixthModule(isExosuit: true, ExtraModuleSlotRules.Exosuit(2).Length).Should().BeTrue("two arms and six modules");
        QuickSlotKeyRules.EXOSUIT_ARMS.Should().Be(2);
        ExtraModuleSlotRules.Exosuit(2).Take(QuickSlotKeyRules.EXOSUIT_ARMS).Should().AllSatisfy(name => name.Should().Contain("Arm"), "the list of a Prawn Suit starts with its two arms");
        ExtraModuleSlotRules.Exosuit(2).Skip(QuickSlotKeyRules.EXOSUIT_ARMS).Should().AllSatisfy(name => name.Should().NotContain("Arm"));
    }

    [TestMethod]
    public void TheKeysReachTheSlotsTheGameCountsThemTo()
    {
        for (int key = 0; key <= 5; key++)
        {
            QuickSlotKeyRules.SlotIndex(isExosuit: false, key).Should().Be(key, "a Seamoth's key n is its slot n");
            QuickSlotKeyRules.SlotIndex(isExosuit: true, key).Should().Be(key + 2, "a Prawn Suit adds its two arms (Exosuit.SlotKeyDown asks for the number + 2)");
        }
        QuickSlotKeyRules.SlotIndex(isExosuit: false, -1).Should().Be(-1);
        QuickSlotKeyRules.SlotIndex(isExosuit: true, 6).Should().Be(-1);

        string[] seamoth = ExtraModuleSlotRules.Seamoth(2);
        string[] exosuit = ExtraModuleSlotRules.Exosuit(2);
        seamoth[QuickSlotKeyRules.SlotIndex(isExosuit: false, 4)].Should().Be("SeamothModule5", "key 5 on a Seamoth");
        seamoth[QuickSlotKeyRules.SlotIndex(isExosuit: false, 5)].Should().Be("SeamothModule6", "the sixth key on a Seamoth");
        exosuit[QuickSlotKeyRules.SlotIndex(isExosuit: true, 4)].Should().Be("ExosuitModule5", "key 5 on a Prawn Suit");
        exosuit[QuickSlotKeyRules.SlotIndex(isExosuit: true, 5)].Should().Be("ExosuitModule6", "the sixth key on a Prawn Suit");
        exosuit[QuickSlotKeyRules.SlotIndex(isExosuit: true, 0)].Should().Be("ExosuitModule1", "key 1 on a Prawn Suit is its first module, not an arm");
    }

    [TestMethod]
    public void AKeyThatIsPolledAsHeldGivesThePressTheHoldAndTheRelease()
    {
        QuickSlotKeyRules.Transition(wasHeld: false, isHeld: false).Should().Be((false, false, false));
        QuickSlotKeyRules.Transition(wasHeld: false, isHeld: true).Should().Be((true, false, false), "the press is the frame in which it starts to be held");
        QuickSlotKeyRules.Transition(wasHeld: true, isHeld: true).Should().Be((false, true, false));
        QuickSlotKeyRules.Transition(wasHeld: true, isHeld: false).Should().Be((false, false, true));
    }

    [TestMethod]
    public void TheKeysSixToEightAreNitroxBindingsAtTheEndOfTheListWithTheirNumbersAsDefaultsAndTheOthersKeepTheirPlaces()
    {
        KeyBindingManager.KeyBindings.Should().HaveCount(8);
        for (int key = 6; key <= 8; key++)
        {
            KeyBindingManager.KeyBindings[key - 1].ButtonLabel.Should().Be($"Nitrox_Settings_Keybind_QuickSlot{key}", $"key {key} keeps its place: a binding's button is its place in the list");
            KeyBindingManager.KeyBindings[key - 1].DefaultKeyboardKey.Should().Be(key.ToString(), "rebindable in the options like the others");
        }
        KeyBindingManager.KeyBindings[^1].ButtonLabel.Should().Be("Nitrox_Settings_Keybind_QuickSlot8");
        KeyBindingManager.KeyBindings[4].ButtonLabel.Should().Be("Nitrox_Settings_Keybind_Emote", "a binding's button is its place in the list: the existing ones must not move");
        KeyBindingManager.KeyBindings.Select(binding => binding.ButtonLabel).Should().OnlyHaveUniqueItems();
        KeyBindingManager.KeyBindings.Select(binding => binding.DefaultKeyboardKey).Should().OnlyHaveUniqueItems("no two bindings share a default key");
    }

    [TestMethod]
    public void TheLanguageFileHasTheLabelOfEveryKeyBindingAndTheDocsTellAboutTheSixthKey()
    {
        string english = ReadFile("Nitrox.Assets.Subnautica", "LanguageFiles", "en.json");
        string docs = ReadFile("docs", "coop", "VEHICLE_UPGRADES.md");

        foreach (string label in KeyBindingManager.KeyBindings.Select(binding => binding.ButtonLabel))
        {
            english.Should().Contain($"\"{label}\"", "the options list shows the label of every key binding");
        }
        english.Should().Contain("\"Nitrox_Settings_Keybind_QuickSlot6\": \"Quick slot 6 (and a vehicle's sixth module)\"");
        english.Should().Contain("\"Nitrox_Settings_Keybind_QuickSlot7\": \"Quick slot 7\"");
        english.Should().Contain("\"Nitrox_Settings_Keybind_QuickSlot8\": \"Quick slot 8\"");
        docs.Should().Contain("Quick slot 6 (and a vehicle's sixth module)");
        docs.Should().Contain("QuickSlotKeyRules");
    }
}
