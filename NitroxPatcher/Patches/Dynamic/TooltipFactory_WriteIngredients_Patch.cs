using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops: the counts in the tooltip of a recipe. <c>TooltipFactory.WriteIngredients</c> shows how many of every ingredient the player has
/// (green when enough, red when not, the count in brackets when some); it counts with <c>Inventory.GetPickupCount</c>, replaced here by <see cref="LockerCrafting.GetCount" /> like in
/// <see cref="CrafterLogic_IsCraftRecipeFulfilled_Patch" />.
/// </summary>
public sealed partial class TooltipFactory_WriteIngredients_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => TooltipFactory.WriteIngredients(default, default));

    public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
    {
        return CrafterLogic_IsCraftRecipeFulfilled_Patch.ReplaceGetPickupCount(instructions);
    }
}
