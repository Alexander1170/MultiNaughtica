using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Three methods of <c>QuickSlots</c> (<c>Update</c>, <c>SelectInternal</c> and <c>DeselectInternal</c>) end by sending an equipment event named after the slot, with <c>QuickSlots.slotNames[slotID]</c>: a static list of
/// six names (QuickSlot0 to QuickSlot5) for a bar of five. On the player's bar of eight (<see cref="PlayerQuickSlots" />) the slots 6 and 7 would read past its end and throw, every frame for a slot with an item in it.
/// The read of the game's static list is replaced with <see cref="PlayerQuickSlots.SlotNames" /> (the game's six names and two more); the game's list itself is left alone (it is read-only). This class does it for
/// <c>Update</c>, and <see cref="UseTheBarsSlotNames" /> is what <see cref="QuickSlots_SelectInternal_Patch" /> and <see cref="QuickSlots_DeselectInternal_Patch" /> use for theirs.
/// </summary>
public sealed partial class QuickSlots_Update_SlotNames_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((QuickSlots t) => t.Update());

    private static readonly FieldInfo SLOT_NAMES = typeof(QuickSlots).GetField("slotNames", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo SLOT_NAMES_GETTER = Reflect.Property(() => PlayerQuickSlots.SlotNames).GetGetMethod();

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return UseTheBarsSlotNames(instructions, "Update");
    }

    /// <summary>The read of the game's list of slot names, in the body of the method called <paramref name="method" />, becomes a call to <see cref="PlayerQuickSlots.SlotNames" />.</summary>
    internal static IEnumerable<CodeInstruction> UseTheBarsSlotNames(IEnumerable<CodeInstruction> instructions, string method)
    {
        int reads = 0;
        List<CodeInstruction> result = [];
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field == SLOT_NAMES)
            {
                result.Add(new CodeInstruction(OpCodes.Call, SLOT_NAMES_GETTER) { labels = instruction.labels, blocks = instruction.blocks });
                reads++;
            }
            else
            {
                result.Add(instruction);
            }
        }
        if (reads != 1)
        {
            Log.Warn($"[QuickSlots] QuickSlots.{method} has {reads} reads of the list of slot names (1 expected): the game has changed, the player's bar of eight slots may throw for its sixth to eighth slot");
        }
        return result;
    }
}
