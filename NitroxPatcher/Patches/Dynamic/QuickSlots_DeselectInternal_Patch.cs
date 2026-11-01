using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class QuickSlots_DeselectInternal_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((QuickSlots t) => t.DeselectInternal());

    public static void Prefix(InventoryItem ____heldItem)
    {
        if (____heldItem != null && ____heldItem.item) //____heldItem.item can be null on game quitting
        {
            Pickupable pickupable = ____heldItem.item;
            if (pickupable.TryGetIdOrWarn(out NitroxId itemId))
            {
                PlayerTool component = pickupable.GetComponent<PlayerTool>();
                PlayerHeldItemChanged.ChangeType type = component ? PlayerHeldItemChanged.ChangeType.HOLSTER_AS_TOOL : PlayerHeldItemChanged.ChangeType.HOLSTER_AS_ITEM;
                Resolve<LocalPlayer>().BroadcastHeldItemChanged(itemId, type, null);
            }
        }
    }

    /// <summary>The slot's name for the equipment event comes from the list of the player's bar of eight slots, not from the game's list of six (<see cref="QuickSlots_Update_SlotNames_Patch" />).</summary>
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return QuickSlots_Update_SlotNames_Patch.UseTheBarsSlotNames(instructions, "DeselectInternal");
    }
}
