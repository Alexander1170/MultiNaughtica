using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// <c>SubRoot.SetCyclopsUpgrades</c> goes through the Cyclops' upgrade slots with the list <c>SubRoot.slotNames</c> and with the number 6 written into it twice: as the size of the list of upgrades that it
/// broadcasts to the helm's displays and as the end of its loop. With extra upgrade slots the modules in the seventh and the eighth would be installed but never counted (shield, sonar, vehicle repair, decoy tubes
/// and thermal reactor would not see them). The read of the list and both numbers come from <see cref="VehicleSlotLists" /> instead (the game's static readonly list is left alone, see there). The displays that
/// receive the list of upgrades go through its length too.
/// </summary>
public sealed partial class SubRoot_SetCyclopsUpgrades_ExtraSlots_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.SetCyclopsUpgrades());

    private static readonly FieldInfo SLOT_NAMES = typeof(SubRoot).GetField(nameof(SubRoot.slotNames), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo SLOT_NAMES_GETTER = Reflect.Property(() => VehicleSlotLists.CyclopsSlotNames).GetGetMethod();
    private static readonly MethodInfo SLOT_COUNT_GETTER = Reflect.Property(() => VehicleSlotLists.CyclopsSlotCount).GetGetMethod();

    public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
    {
        int numbers = 0;
        int reads = 0;
        List<CodeInstruction> result = [];
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_6)
            {
                result.Add(new CodeInstruction(OpCodes.Call, SLOT_COUNT_GETTER) { labels = instruction.labels, blocks = instruction.blocks });
                numbers++;
            }
            else if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field == SLOT_NAMES)
            {
                result.Add(new CodeInstruction(OpCodes.Call, SLOT_NAMES_GETTER) { labels = instruction.labels, blocks = instruction.blocks });
                reads++;
            }
            else
            {
                result.Add(instruction);
            }
        }
        if (numbers != 2 || reads != 1)
        {
            Log.Warn($"[VehicleUpgrades] extra module slots: SubRoot.SetCyclopsUpgrades has {numbers} places with the number 6 and {reads} reads of the slot names (2 and 1 expected): the game has changed, the Cyclops' 7th and 8th upgrade slots may not count");
        }
        return result;
    }
}
