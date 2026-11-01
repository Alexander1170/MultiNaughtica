using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class QuickSlots_SelectInternal_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((QuickSlots t) => t.SelectInternal(default(int)));

    public static void Prefix(QuickSlots __instance, int slotID, ref NitroxTechType __state)
    {
        InventoryItem item = __instance.binding[slotID];
        if (item == null)
        {
            return;
        }
        // Send the TechType if this is the first time using the tool (not yet in usedTools)
        __state = Player.main.IsToolUsed(item.item.GetTechType()) ? null : item.item.GetTechType().ToDto();
    }

    public static void Postfix(InventoryItem ____heldItem, NitroxTechType __state)
    {
        if (____heldItem == null)
        {
            return;
        }
        Pickupable pickupable = ____heldItem.item;
        if (pickupable.TryGetIdOrWarn(out NitroxId itemId))
        {
            PlayerTool component = pickupable.GetComponent<PlayerTool>();
            PlayerHeldItemChanged.ChangeType type = component ? PlayerHeldItemChanged.ChangeType.DRAW_AS_TOOL : PlayerHeldItemChanged.ChangeType.DRAW_AS_ITEM;
            Resolve<LocalPlayer>().BroadcastHeldItemChanged(itemId, type, __state);
        }
    }

    /// <summary>The slot's name for the equipment event comes from the list of the player's bar of eight slots, not from the game's list of six (<see cref="QuickSlots_Update_SlotNames_Patch" />).</summary>
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return QuickSlots_Update_SlotNames_Patch.UseTheBarsSlotNames(instructions, "SelectInternal");
    }
}
