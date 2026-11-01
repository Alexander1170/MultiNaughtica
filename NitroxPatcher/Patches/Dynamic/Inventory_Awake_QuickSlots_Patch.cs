using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// <c>Inventory.Awake</c> makes the player's quick bar (<c>new QuickSlots(..., 5)</c>) with the number of slots written into it as the literal 5, and everything else about the bar (the HUD, the scroll, the
/// binding, the packets) goes by the count it was made with. The number is replaced with <see cref="PlayerQuickSlots.COUNT" />: the player's bar has eight slots (docs/coop/VEHICLE_UPGRADES.md, "Eight quick slots
/// for the player"), with the keys 6, 7 and 8 read by <see cref="PlayerQuickSlotKeys" />. The game's list of slot names is made long enough by <see cref="QuickSlots_Update_SlotNames_Patch" />.
/// </summary>
public sealed partial class Inventory_Awake_QuickSlots_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Inventory t) => t.Awake());

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = [..instructions];
        int replaced = 0;
        for (int i = 0; i + 1 < result.Count; i++)
        {
            if (result[i].opcode == OpCodes.Ldc_I4_5 && result[i + 1].opcode == OpCodes.Newobj && result[i + 1].operand is ConstructorInfo constructor && constructor.DeclaringType == typeof(QuickSlots))
            {
                result[i] = new CodeInstruction(OpCodes.Ldc_I4, PlayerQuickSlots.COUNT) { labels = result[i].labels, blocks = result[i].blocks };
                replaced++;
            }
        }
        if (replaced != 1)
        {
            Log.Warn($"[QuickSlots] Inventory.Awake has {replaced} places that make the quick bar with the number 5 (1 expected): the game has changed, the player's bar keeps the game's five slots");
        }
        return result;
    }
}
