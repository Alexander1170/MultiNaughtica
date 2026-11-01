using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.Helper;
using NitroxPatcher.Patches.Dynamic;
using NitroxTest.Patcher;

namespace Nitrox.Test.Patcher.Patches;

[TestClass]
public class StoryGoalCustomEventHandlerRangeTranspilerTest
{
    private static readonly MethodInfo targetMethod = Reflect.Method((StoryGoalCustomEventHandler t) => t.NotifyGoalComplete(default));
    private static readonly MethodInfo triggerMethod = Reflect.Method((Story.StoryGoal t) => t.Trigger());
    private static readonly MethodInfo replacementMethod = Reflect.Method(() => StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.RequestSunbeamRangeOutcome(default));
    private static readonly FieldInfo inRangeGoalField = Reflect.Field((StoryGoalCustomEventHandler t) => t.sunbeamDestroyEventInRange);

    [TestMethod]
    public void Transpiler_ReplacesOnlyTheTriggerOfTheRangeGoal()
    {
        // Each read gives new instruction objects: the transpiler changes them in place
        List<CodeInstruction> original = PatchTestHelper.GetInstructionsFromMethod(targetMethod).ToList();
        List<CodeInstruction> transformed = StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.Transpiler(PatchTestHelper.GetInstructionsFromMethod(targetMethod)).ToList();

        transformed.Should().HaveCount(original.Count);
        // Labels and locals are created anew by every read of the method, so only the opcodes and the called methods or loaded fields can be compared
        int[] changed = Enumerable.Range(0, original.Count).Where(i => original[i].opcode != transformed[i].opcode || (original[i].operand is MemberInfo && !Equals(original[i].operand, transformed[i].operand))).ToArray();
        changed.Should().HaveCount(1);

        int index = changed[0];
        original[index].opcode.Should().Be(OpCodes.Callvirt);
        original[index].operand.Should().Be(triggerMethod);
        original[index - 1].opcode.Should().Be(OpCodes.Ldfld);
        original[index - 1].operand.Should().Be(inRangeGoalField);
        transformed[index].opcode.Should().Be(OpCodes.Call);
        transformed[index].operand.Should().Be(replacementMethod);
    }

    [TestMethod]
    public void Transpiler_LeavesTheOtherTriggersOfTheMethod()
    {
        // The sunbeamGoals loop and the Goal_Disable_Gun branch trigger goals too and must keep doing it
        int triggersBefore = PatchTestHelper.GetInstructionsFromMethod(targetMethod).Count(instruction => triggerMethod.Equals(instruction.operand));
        int triggersAfter = StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.Transpiler(PatchTestHelper.GetInstructionsFromMethod(targetMethod)).Count(instruction => triggerMethod.Equals(instruction.operand));

        triggersBefore.Should().Be(4);
        triggersAfter.Should().Be(triggersBefore - 1);
    }

    [TestMethod]
    public void Transpiler_KeepsTheLabelsOfTheReplacedInstruction()
    {
        // In the vanilla IL the branch that picks the out of range goal jumps to the Trigger call, which is where the replacement stands
        Label outOfRangeBranchTarget = new();
        CodeInstruction trigger = new(OpCodes.Callvirt, triggerMethod);
        trigger.labels.Add(outOfRangeBranchTarget);
        List<CodeInstruction> input =
        [
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, inRangeGoalField),
            trigger,
            new(OpCodes.Ret)
        ];

        List<CodeInstruction> transformed = StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.Transpiler(input).ToList();

        transformed.Should().HaveCount(4);
        transformed[2].opcode.Should().Be(OpCodes.Call);
        transformed[2].operand.Should().Be(replacementMethod);
        transformed[2].labels.Should().Contain(outOfRangeBranchTarget);
    }

    [TestMethod]
    public void Transpiler_WithoutTheRangeGoalTrigger_Throws()
    {
        // A game update that moves the code must fail loudly at patch time instead of silently bringing the contradicting logs back
        List<CodeInstruction> input = [new(OpCodes.Ldarg_0), new(OpCodes.Callvirt, triggerMethod), new(OpCodes.Ret)];

        Invoking(() => StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.Transpiler(input).ToList()).Should().Throw<Exception>();
    }
}
