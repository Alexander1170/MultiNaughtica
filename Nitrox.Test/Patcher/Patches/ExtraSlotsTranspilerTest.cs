using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxPatcher.Patches.Dynamic;
using NitroxTest.Patcher;

namespace Nitrox.Test.Patcher.Patches;

/// <summary>
/// The transpiler of <c>SubRoot.SetCyclopsUpgrades</c> for the extra Cyclops upgrade slots, on the game's real code: the sanity test of all transpilers only knows that the code changed and by how many
/// instructions; this one knows that exactly the two numbers 6 and the one read of the game's static list are gone, and three calls of <see cref="VehicleSlotLists" /> took their place.
/// </summary>
[TestClass]
public class ExtraSlotsTranspilerTest
{
    private static bool IsRead(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field.DeclaringType == typeof(SubRoot) && field.Name == "slotNames";

    [TestMethod]
    public void TheCyclopsUpgradeLoopTakesItsListAndItsLengthFromTheSlotLists()
    {
        MethodInfo target = typeof(SubRoot).GetMethod("SetCyclopsUpgrades", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        List<CodeInstruction> original = PatchTestHelper.GetInstructionsFromMethod(target).ToList();
        original.Count(instruction => instruction.opcode == OpCodes.Ldc_I4_6).Should().Be(2, "the game has the number 6 written into the method twice");
        original.Count(IsRead).Should().Be(1, "the game reads the list of slot names once");

        List<CodeInstruction> patched = SubRoot_SetCyclopsUpgrades_ExtraSlots_Patch.Transpiler(target, original.Select(instruction => new CodeInstruction(instruction)).ToList()).ToList();

        patched.Should().HaveCount(original.Count);
        patched.Count(instruction => instruction.opcode == OpCodes.Ldc_I4_6).Should().Be(0);
        patched.Count(IsRead).Should().Be(0);
        patched.Count(instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method && method.DeclaringType == typeof(VehicleSlotLists)).Should().Be(3);
    }

    [TestMethod]
    public void TheSlotListGetterPatchesTargetTheOverridesOfTheVehicles()
    {
        // Vehicle.slotIDs is virtual and returns null; SeaMoth and Exosuit override it with their static list. A patch on the base getter would never run for them (the first live run showed exactly that).
        foreach ((Type patch, Type vehicle) in new[] { (typeof(SeaMoth_slotIDs_ExtraSlots_Patch), typeof(SeaMoth)), (typeof(Exosuit_slotIDs_ExtraSlots_Patch), typeof(Exosuit)) })
        {
            MethodInfo target = (MethodInfo)patch.GetField("TARGET_METHOD", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            target.Should().NotBeNull();
            target.DeclaringType.Should().Be(vehicle);
            target.Name.Should().Be("get_slotIDs");
            PatchTestHelper.GetInstructionsFromMethod(target).Any(instruction => instruction.opcode == OpCodes.Ldsfld).Should().BeTrue("it is the getter that reads the game's static list");
        }
    }
}
