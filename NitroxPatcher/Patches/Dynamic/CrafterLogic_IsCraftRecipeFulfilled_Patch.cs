using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops: which recipes light up in the menu. <c>CrafterLogic.IsCraftRecipeFulfilled</c> counts the ingredients with
/// <c>Inventory.GetPickupCount</c>; the call is replaced by <see cref="LockerCrafting.GetCount" />, which adds the items of the lockers while the menu of a crafting station of a
/// base or a Cyclops is open and is the vanilla count at any other time.
/// </summary>
public sealed partial class CrafterLogic_IsCraftRecipeFulfilled_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => CrafterLogic.IsCraftRecipeFulfilled(default));

    private static readonly MethodInfo getPickupCount = Reflect.Method((Inventory t) => t.GetPickupCount(default));
    private static readonly MethodInfo getCount = Reflect.Method(() => LockerCrafting.GetCount(default, default));

    public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceGetPickupCount(instructions);
    }

    /// <summary>
    /// Replaces every call of <c>Inventory.GetPickupCount(TechType)</c> (instance, receiver and argument on the stack) by the call of the static <see cref="LockerCrafting.GetCount" />
    /// with the same two values on the stack. Shared with <see cref="TooltipFactory_WriteIngredients_Patch" />.
    /// </summary>
    internal static IEnumerable<CodeInstruction> ReplaceGetPickupCount(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(getPickupCount))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = getCount;
            }
            yield return instruction;
        }
    }
}
