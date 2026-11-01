using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;
using NitroxPatcher.Patches.Dynamic;
using NitroxTest.Patcher;

namespace Nitrox.Test.Patcher.Patches;

/// <summary>
/// The patches that make the player's quick bar eight slots long (<see cref="PlayerQuickSlots" />), on the game's real code: the number 5 that <c>Inventory.Awake</c> makes the bar with, and the three methods that read the
/// game's static list of slot names with the number of the slot.
/// </summary>
[TestClass]
public class PlayerQuickSlotsTranspilerTest
{
    private const BindingFlags ANY_MEMBER = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool IsNameRead(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field.DeclaringType == typeof(QuickSlots) && field.Name == "slotNames";

    private static bool IsBarMadeWithFive(IList<CodeInstruction> code, int at) =>
        at + 1 < code.Count && code[at].opcode == OpCodes.Ldc_I4_5 && code[at + 1].opcode == OpCodes.Newobj && code[at + 1].operand is ConstructorInfo constructor && constructor.DeclaringType == typeof(QuickSlots);

    [TestMethod]
    public void TheGamesBarIsMadeWithTheNumberFiveAndThePatchMakesItWithEight()
    {
        MethodInfo target = typeof(Inventory).GetMethod("Awake", ANY_MEMBER);
        List<CodeInstruction> original = PatchTestHelper.GetInstructionsFromMethod(target).ToList();
        Enumerable.Range(0, original.Count).Count(at => IsBarMadeWithFive(original, at)).Should().Be(1, "Inventory.Awake makes the player's QuickSlots with the literal 5 right before the constructor call");

        List<CodeInstruction> patched = Inventory_Awake_QuickSlots_Patch.Transpiler(original.Select(instruction => new CodeInstruction(instruction)).ToList()).ToList();

        patched.Should().HaveCount(original.Count);
        Enumerable.Range(0, patched.Count).Count(at => IsBarMadeWithFive(patched, at)).Should().Be(0);
        int constructorAt = patched.FindIndex(instruction => instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructor && constructor.DeclaringType == typeof(QuickSlots));
        constructorAt.Should().BeGreaterThan(0);
        patched[constructorAt - 1].opcode.Should().Be(OpCodes.Ldc_I4);
        patched[constructorAt - 1].operand.Should().Be(PlayerQuickSlots.COUNT, "the bar is made with the eight slots of the player's bar");
        original.Where((instruction, at) => at != constructorAt - 1).Select(instruction => instruction.opcode)
                .Should().Equal(patched.Where((instruction, at) => at != constructorAt - 1).Select(instruction => instruction.opcode), "nothing else in Awake changed");
    }

    [TestMethod]
    public void EachMethodThatReadsTheGamesSlotNamesReadsThePlayersBarsInstead()
    {
        (string Name, Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> Transpile)[] methods =
        [
            ("Update", QuickSlots_Update_SlotNames_Patch.Transpiler),
            ("SelectInternal", QuickSlots_SelectInternal_Patch.Transpiler),
            ("DeselectInternal", QuickSlots_DeselectInternal_Patch.Transpiler)
        ];

        foreach ((string name, Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpile) in methods)
        {
            MethodInfo target = typeof(QuickSlots).GetMethod(name, ANY_MEMBER);
            List<CodeInstruction> original = PatchTestHelper.GetInstructionsFromMethod(target).ToList();
            original.Count(IsNameRead).Should().Be(1, $"QuickSlots.{name} reads the list of slot names once, with the number of the slot");

            List<CodeInstruction> patched = transpile(original.Select(instruction => new CodeInstruction(instruction)).ToList()).ToList();

            patched.Should().HaveCount(original.Count, name);
            patched.Count(IsNameRead).Should().Be(0, name);
            patched.Count(instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method && method.DeclaringType == typeof(PlayerQuickSlots) && method.Name == "get_SlotNames").Should().Be(1, name);
        }
    }

    [TestMethod]
    public void TheBarsSlotNamesStartWithTheGamesOwnSixAndThereIsOneForEverySlot()
    {
        string[] games = (string[])typeof(QuickSlots).GetField("slotNames", ANY_MEMBER).GetValue(null);

        games.Should().HaveCount(6, "the game has six names for its five slots");
        PlayerQuickSlots.SlotNames.Take(games.Length).Should().Equal(games);
        PlayerQuickSlots.SlotNames.Should().HaveCount(PlayerQuickSlots.COUNT);
    }

    [TestMethod]
    public void TheGamesKeysAreFiveAndTheCountThatTheHudUsesIsTheBarsOwn()
    {
        // uGUI_QuickSlots reads the keys 1 to 5 from a static list of five buttons: the extra keys are polled by PlayerQuickSlotKeys, not by the game's loop
        GameInput.Button[] keys = (GameInput.Button[])typeof(uGUI_QuickSlots).GetField("quickSlotButtons", ANY_MEMBER).GetValue(null);

        keys.Should().HaveCount(PlayerQuickSlots.GAME_COUNT);
    }
}
