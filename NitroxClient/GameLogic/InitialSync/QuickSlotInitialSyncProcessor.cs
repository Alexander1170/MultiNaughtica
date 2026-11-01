using System.Collections;
using System.Collections.Generic;
using NitroxClient.GameLogic.InitialSync.Abstract;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic.InitialSync;

public sealed class QuickSlotInitialSyncProcessor : InitialSyncProcessor
{
    public QuickSlotInitialSyncProcessor()
    {
        AddDependency<PlayerInitialSyncProcessor>();  // the player needs to be configured before we can set quick slots.
        AddDependency<EquippedItemInitialSyncProcessor>(); // we need to have the items spawned into our inventory before we can quick slot them.
    }

    public override IEnumerator Process(InitialPlayerSync packet, WaitScreen.ManualWaitItem waitScreenItem)
    {
        int nonEmptySlots = 0;

        Dictionary<NitroxId, InventoryItem> inventoryItemsById = GetItemsById();

        // (as many as the bar has: a save with eight bindings on a bar of five, a build whose patch did not take hold, must not write past its end)
        int slotCount = Inventory.main.quickSlots.slotCount;
        int restored = PlayerQuickSlots.SlotsToRestore(packet.QuickSlotsBindingIds.Length, slotCount);
        for (int i = 0; i < restored; i++)
        {
            waitScreenItem.SetProgress(i, packet.QuickSlotsBindingIds.Length);

            Optional<NitroxId> opId = packet.QuickSlotsBindingIds[i];

            if (opId.HasValue && inventoryItemsById.TryGetValue(opId.Value, out InventoryItem inventoryItem) )
            {
                Inventory.main.quickSlots.binding[i] = inventoryItem;
                Inventory.main.quickSlots.NotifyBind(i, state: true);
                nonEmptySlots++;
            }
            else
            {
                // Unbind any default stuff from equipment addition.
                Inventory.main.quickSlots.Unbind(i);
            }

            yield return null;
        }

        // A save from before the bar had eight slots lists five: the game bound tools to the free slots as the items came into the inventory, and the others are not in the saved bar.
        foreach (int slot in PlayerQuickSlots.SlotsWithoutASavedBinding(packet.QuickSlotsBindingIds.Length, slotCount))
        {
            Inventory.main.quickSlots.Unbind(slot);
        }

        Log.Info($"Received initial sync with {nonEmptySlots} quick slots populated with items ({restored} saved bindings read, bar of {slotCount})");
    }

    private Dictionary<NitroxId, InventoryItem> GetItemsById()
    {
        Dictionary<NitroxId, InventoryItem> itemsById = new();

        foreach (InventoryItem inventoryItem in Inventory.main.container)
        {
            if (inventoryItem.item.TryGetIdOrWarn(out NitroxId itemId))
            {
                itemsById.Add(itemId, inventoryItem);
            }
        }

        return itemsById;
    }
}
