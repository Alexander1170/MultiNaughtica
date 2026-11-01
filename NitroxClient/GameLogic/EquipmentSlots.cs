using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using System.Linq;
using UnityEngine;

namespace NitroxClient.GameLogic;

public class EquipmentSlots
{
    private const string LOG_TAG = "[ModuleSlot]";
    private const string TAKEN_BACK_MESSAGE = "Another player was faster: the module went back to your inventory.";

    private readonly IPacketSender packetSender;
    private readonly Entities entities;
    private readonly EntityMetadataManager entityMetadataManager;

    /// <summary>
    /// The modules this client installed a moment ago, see <see cref="ReleaseContestedOccupant" />.
    /// </summary>
    private readonly RecentModuleInstalls recentInstalls = new(() => Time.unscaledTime);

    public EquipmentSlots(IPacketSender packetSender, Entities entities, EntityMetadataManager entityMetadataManager)
    {
        this.packetSender = packetSender;
        this.entities = entities;
        this.entityMetadataManager = entityMetadataManager;
    }

    public void BroadcastEquip(Pickupable pickupable, GameObject owner, string slot)
    {
        if (!owner.TryGetIdOrWarn(out NitroxId ownerId))
        {
            return;
        }
        if (!pickupable.TryGetIdOrWarn(out NitroxId itemId))
        {
            return;
        }

        if (owner.TryGetComponent(out Player player))
        {
            entities.EntityMetadataChanged(player, ownerId);
        }
        else
        {
            string classId = pickupable.RequireComponent<PrefabIdentifier>().ClassId;
            NitroxTechType techType = pickupable.GetTechType().ToDto();
            EntityMetadata metadata = entityMetadataManager.Extract(pickupable.gameObject).OrNull();

            InstalledModuleEntity moduleEntity = new(slot, classId, itemId, techType, metadata, ownerId, Items.GetStorageContents(pickupable.gameObject, itemId, entityMetadataManager).ToList());
            entities.MarkAsSpawned(moduleEntity);
            // The server may refuse this install if another player was first (V0, U4): until it answered, a module that arrives for the slot has to take this one out
            recentInstalls.Add(itemId);

            if (packetSender.Send(new EntitySpawnedByClient(moduleEntity, true)))
            {
                Log.Debug($"Sent: Added module {pickupable.GetTechType()} ({itemId}) to equipment {owner.GetFullHierarchyPath()} in slot {slot}");
            }
        }
    }

    public void BroadcastUnequip(Pickupable pickupable, GameObject owner)
    {
        if (!pickupable.TryGetIdOrWarn(out NitroxId itemId))
        {
            return;
        }
        if (!owner.TryGetIdOrWarn(out NitroxId ownerId))
        {
            return;
        }

        // Sync oxygen tank metadata when unequipped
        if (pickupable.TryGetComponent(out Oxygen oxygen))
        {
            entities.EntityMetadataChanged(oxygen, itemId);
        }

        if (owner.TryGetComponent(out Player player))
        {
            entities.EntityMetadataChanged(player, ownerId);
        }
        else
        {
            recentInstalls.Forget(itemId);
            entities.RemoveEntity(itemId);
            packetSender.Send(new EntityDestroyed(itemId));
        }
    }

    /// <summary>
    /// The server refused the install of a module (another player put a different module into the same slot first, V0 U4): takes the module out of the slot with the vanilla
    /// unequip, so that the vehicle, the console and the PDA react as if the player had taken it out himself, and puts it into the player's inventory. Nothing is sent to the
    /// server, which never registered the install. Never throws.
    /// </summary>
    /// <returns>True if a module was taken back, false if there was nothing to do (the module is gone, in another slot or somewhere else) or if it failed.</returns>
    public bool TakeBackModule(NitroxId itemId, NitroxId parentId, string slot)
    {
        try
        {
            if (!NitroxEntity.TryGetObjectFrom(itemId, out GameObject itemObject) || !itemObject.TryGetComponent(out Pickupable pickupable))
            {
                Log.Debug($"{LOG_TAG} Install of {itemId} into slot {slot} of {parentId} was refused, but the module does not exist here (any more)");
                return false;
            }

            InventoryItem item = pickupable.inventoryItem;
            Equipment equipment = item?.container as Equipment;
            bool inEquipmentOfParent = equipment != null && equipment.owner && equipment.owner.TryGetNitroxId(out NitroxId ownerId) && ownerId.Equals(parentId);
            string slotInDictionary = null;
            string foundSlot = null;
            if (inEquipmentOfParent && equipment.GetItemSlot(pickupable, ref foundSlot))
            {
                slotInDictionary = foundSlot;
            }

            switch (ModuleSlotRejectionRules.Classify(inEquipmentOfParent, slotInDictionary, slot))
            {
                case ModuleTakeBack.TakeOutOfSlot:
                    Log.Info($"{LOG_TAG} Install of {pickupable.GetTechType()} ({itemId}) into slot {slot} of {parentId} was refused (another player was first): taking it out of the slot");
                    return TakeOutOfSlot(equipment, slot);
                case ModuleTakeBack.ClearOrphan:
                    Log.Info($"{LOG_TAG} Install of {pickupable.GetTechType()} ({itemId}) into slot {slot} of {parentId} was refused and the slot holds another module now: clearing the orphan");
                    return ClearOrphan(equipment, slot, itemId, pickupable, item);
                default:
                    Log.Debug($"{LOG_TAG} Install of {itemId} into slot {slot} of {parentId} was refused, but the module is not in that slot any more (in slot {slotInDictionary ?? "none"}, equipment of the owner: {inEquipmentOfParent})");
                    return false;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"{LOG_TAG} Could not take {itemId} back out of slot {slot} of {parentId}");
            return false;
        }
    }

    /// <summary>
    /// A module arrives for a slot (<see cref="Spawning.InstalledModuleEntitySpawner" />). If the slot still holds a module that this client installed a moment ago, the server
    /// accepted the arriving one instead (this client's install lost the race and its refusal is still on the way): the old module is taken out first, else the arriving one would
    /// overwrite its entry in the equipment and leave it behind as an orphan that is counted but in no slot. A module that is in the slot for any other reason (the prefab, the join
    /// snapshot, a forced respawn) is left to the caller. Never throws.
    /// </summary>
    public void ReleaseContestedOccupant(Equipment equipment, string slot, NitroxId incomingId)
    {
        try
        {
            if (equipment == null || slot == null)
            {
                return;
            }

            InventoryItem occupant = equipment.GetItemInSlot(slot);
            Pickupable occupantItem = occupant != null ? occupant.item : null;
            if (!occupantItem || !occupantItem.TryGetNitroxId(out NitroxId occupantId))
            {
                return;
            }
            if (occupantId.Equals(incomingId) || !recentInstalls.IsRecent(occupantId))
            {
                return;
            }

            Log.Info($"{LOG_TAG} {occupantItem.GetTechType()} ({occupantId}) was installed here a moment ago and the slot {slot} is now taken by {incomingId}: taking it out of the slot");
            TakeOutOfSlot(equipment, slot);
        }
        catch (Exception e)
        {
            Log.Error(e, $"{LOG_TAG} Could not make room in slot {slot} for {incomingId}");
        }
    }

    /// <summary>
    /// The module is in the dictionary at <paramref name="slot" />: the vanilla unequip (counts, module and owner events, PDA), then into the inventory.
    /// </summary>
    private static bool TakeOutOfSlot(Equipment equipment, string slot)
    {
        using (PacketSuppressor<EntityDestroyed, EntityMetadataUpdate, EntityReparented, EntitySpawnedByClient, PlayerQuickSlotsBindingChanged>.Suppress())
        {
            // forced: a storage module that holds items may be taken out. The packets that this raises (the unequip's EntityDestroyed, the oxygen metadata, the item entering
            // the inventory) describe a change that the server does not know about: it never registered the install
            InventoryItem removed = equipment.RemoveItem(slot, true, false);
            Pickupable moved = removed != null ? removed.item : null;
            if (!moved)
            {
                Log.Warn($"{LOG_TAG} Nothing left in slot {slot} to take out");
                return false;
            }

            ReturnToInventory(moved);
        }

        Log.InGame(TAKEN_BACK_MESSAGE);
        return true;
    }

    /// <summary>
    /// The module belongs to the equipment but the winner's module overwrote its dictionary entry, so vanilla cannot remove it. Undoes what its install did, without firing the
    /// equipment delegates (the winner's module sits in that slot and its UI entry exists), then puts it into the inventory. A safety net: <see cref="ReleaseContestedOccupant" />
    /// keeps this from happening. This bypasses <c>Equipment.RemoveItem</c>, so the bookkeeping that <see cref="BroadcastUnequip" /> does for a vanilla unequip is done here by hand
    /// (the install is forgotten and the module is no longer a known <see cref="InstalledModuleEntity" />): the module then enters the inventory like after a normal take-out
    /// (<c>ItemContainers.BroadcastItemAdd</c> sees an unknown id and re-types it as an inventory item) instead of staying typed as an installed module.
    /// </summary>
    private bool ClearOrphan(Equipment equipment, string slot, NitroxId itemId, Pickupable pickupable, InventoryItem item)
    {
        using (PacketSuppressor<EntityDestroyed, EntityMetadataUpdate, EntityReparented, EntitySpawnedByClient, PlayerQuickSlotsBindingChanged>.Suppress())
        {
            TechType techType = pickupable.GetTechType();
            equipment.UpdateCount(techType, false);
            Equipment.SendEquipmentEvent(pickupable, 1, equipment.owner, slot);
            item.container = null;
            recentInstalls.Forget(itemId);
            entities.RemoveEntity(itemId);
            RefreshOwner(equipment, slot, techType);

            ReturnToInventory(pickupable);
        }

        Log.InGame(TAKEN_BACK_MESSAGE);
        return true;
    }

    /// <summary>
    /// Makes the owner recompute what depends on the slot after the orphan left: the global parts from the corrected counts, then the per-slot parts of the module that holds the slot.
    /// </summary>
    private static void RefreshOwner(Equipment equipment, string slot, TechType removedTechType)
    {
        GameObject owner = equipment.owner;
        if (!owner)
        {
            return;
        }

        if (owner.TryGetComponent(out Vehicle vehicle))
        {
            vehicle.UpgradeModuleChanged(slot, removedTechType, false);
            InventoryItem holder = equipment.GetItemInSlot(slot);
            Pickupable holderItem = holder != null ? holder.item : null;
            if (holderItem)
            {
                vehicle.UpgradeModuleChanged(slot, holderItem.GetTechType(), true);
            }
        }

        if (owner.TryGetComponent(out UpgradeConsole console))
        {
            // The Cyclops recomputes its upgrades, depth and power rating from the corrected dictionary and counts in its next Update
            SubRoot subRoot = console.GetComponentInParent<SubRoot>();
            if (subRoot)
            {
                subRoot.OnSubModulesChanged(slot, null);
            }
            console.UpdateVisuals();
        }
    }

    /// <summary>
    /// Same call as the one that undoes a move into a full container (<c>EntityReparentedProcessor</c>): <c>UnsafeAdd</c> can leave the inventory over capacity by one item if the
    /// player filled the freed cell meanwhile. No drop at the feet: the game refuses to drop inside a base, a pod or a vehicle, which is where upgrade consoles are.
    /// </summary>
    private static void ReturnToInventory(Pickupable pickupable)
    {
        if (!Inventory.main)
        {
            Log.Warn($"{LOG_TAG} No inventory to put {pickupable.GetTechType()} back into");
            return;
        }

        ItemsContainer inventory = Inventory.main.container;
        bool hadRoom = inventory.HasRoomFor(pickupable);
        inventory.UnsafeAdd(new InventoryItem(pickupable));
        if (!hadRoom)
        {
            Log.Warn($"{LOG_TAG} The inventory had no room for {pickupable.GetTechType()}, it is over capacity by one item now");
        }
    }
}
