using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.Helper;
using NitroxPatcher.Patches.Dynamic;
using NitroxTest.Patcher;
using UnityEngine;

namespace Nitrox.Test.Patcher.Patches;

[TestClass]
public class AquariumTriggerTranspilerTest
{
    private static readonly MethodInfo checkRange = AccessTools.Method(typeof(PrecursorAquariumTeleporterTrigger), "CheckRange");
    private static readonly MethodInfo onTeleporterUncover = AccessTools.Method(typeof(PrecursorAquariumTeleporterTrigger), "OnTeleporterUncover");
    private static readonly MethodInfo distance = Reflect.Method(() => Vector3.Distance(default, default));
    private static readonly MethodInfo isInsideCollider = Reflect.Method(() => UWE.Utils.IsInsideCollider(default(Collider), default(Vector3)));
    private static readonly MethodInfo nearestPlayerDistance = Reflect.Method(() => PrecursorAquariumTeleporterTrigger_CheckRange_Patch.NearestPlayerDistance(default, default));
    private static readonly MethodInfo anyPlayerInside = Reflect.Method(() => PrecursorAquariumTeleporterTrigger_OnTeleporterUncover_Patch.AnyPlayerInside(default, default));

    [TestMethod]
    public void CheckRange_ReplacesTheDistanceToTheLocalPlayerByTheNearestPlayer()
    {
        List<CodeInstruction> original = PatchTestHelper.GetInstructionsFromMethod(checkRange).ToList();
        List<CodeInstruction> transformed = PrecursorAquariumTeleporterTrigger_CheckRange_Patch.Transpiler(PatchTestHelper.GetInstructionsFromMethod(checkRange)).ToList();

        transformed.Should().HaveCount(original.Count);
        original.Count(instruction => distance.Equals(instruction.operand)).Should().Be(1);
        transformed.Count(instruction => distance.Equals(instruction.operand)).Should().Be(0);
        transformed.Count(instruction => instruction.opcode == OpCodes.Call && nearestPlayerDistance.Equals(instruction.operand)).Should().Be(1);
    }

    [TestMethod]
    public void OnTeleporterUncover_ReplacesTheInsideCheckOfTheLocalPlayerByAnyPlayer()
    {
        List<CodeInstruction> original = PatchTestHelper.GetInstructionsFromMethod(onTeleporterUncover).ToList();
        List<CodeInstruction> transformed = PrecursorAquariumTeleporterTrigger_OnTeleporterUncover_Patch.Transpiler(PatchTestHelper.GetInstructionsFromMethod(onTeleporterUncover)).ToList();

        transformed.Should().HaveCount(original.Count);
        original.Count(instruction => isInsideCollider.Equals(instruction.operand)).Should().Be(1);
        transformed.Count(instruction => isInsideCollider.Equals(instruction.operand)).Should().Be(0);
        transformed.Count(instruction => instruction.opcode == OpCodes.Call && anyPlayerInside.Equals(instruction.operand)).Should().Be(1);
    }
}
