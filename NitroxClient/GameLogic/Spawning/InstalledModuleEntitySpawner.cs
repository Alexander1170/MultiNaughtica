using System;
using System.Collections;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Helper;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning;

public class InstalledModuleEntitySpawner : SyncEntitySpawner<InstalledModuleEntity>
{
    // This spawner is created by Entities, which EquipmentSlots depends on: it cannot take it as a constructor parameter
    private readonly Lazy<EquipmentSlots> equipmentSlots = new(() => NitroxServiceLocator.LocateService<EquipmentSlots>());

    protected override IEnumerator SpawnAsync(InstalledModuleEntity entity, TaskResult<Optional<GameObject>> result)
    {
        if (!CanSpawn(entity, out GameObject parentObject, out Equipment equipment, out string errorLog))
        {
            Log.Info(errorLog);
            result.Set(Optional.Empty);
            yield break;
        }
        // A module in an extra slot that this game does not have (the option was lowered, or an older build loads the world) waits: it stays in the save and returns with the slot
        if (VehicleSlotLists.IsDormant(equipment, entity.Slot, entity.Id))
        {
            result.Set(Optional.Empty);
            yield break;
        }

        TaskResult<GameObject> gameObjectResult = new();
        yield return DefaultWorldEntitySpawner.CreateGameObject(entity.TechType.ToUnity(), entity.ClassId, entity.Id, gameObjectResult);
        GameObject gameObject = gameObjectResult.Get();

        SetupObject(entity, gameObject, parentObject, equipment);

        result.Set(Optional.Of(gameObject));
    }

    protected override bool SpawnSync(InstalledModuleEntity entity, TaskResult<Optional<GameObject>> result)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, entity.TechType.ToUnity(), entity.ClassId))
        {
            return false;
        }
        if (!CanSpawn(entity, out GameObject parentObject, out Equipment equipment, out string errorLog))
        {
            Log.Error(errorLog);
            return true;
        }
        if (VehicleSlotLists.IsDormant(equipment, entity.Slot, entity.Id))
        {
            return true;
        }

        GameObject gameObject = GameObjectExtensions.SpawnFromPrefab(prefab, entity.Id);

        SetupObject(entity, gameObject, parentObject, equipment);

        result.Set(gameObject);
        return true;
    }

    protected override bool SpawnsOwnChildren(InstalledModuleEntity entity) => false;

    private bool CanSpawn(InstalledModuleEntity entity, out GameObject parentObject, out Equipment equipment, out string errorLog)
    {
        if (!NitroxEntity.TryGetObjectFrom(entity.ParentId, out parentObject))
        {
            equipment = null;
            errorLog = $"Unable to find inventory container with id {entity.Id} for {entity}";
            return false;
        }
        // The game considers modules as vehicle equipment.  Get the container and install it into the required slot.
        Optional<Equipment> opEquipment = EquipmentHelper.FindEquipmentComponent(parentObject);
        if (!opEquipment.HasValue)
        {
            equipment = null;
            errorLog = $"Unable to find equipment container inside {parentObject}";
            return false;
        }

        equipment = opEquipment.Value;
        errorLog = null;
        return true;
    }

    private void SetupObject(InstalledModuleEntity entity, GameObject gameObject, GameObject parentObject, Equipment equipment)
    {
        Pickupable pickupable = gameObject.RequireComponent<Pickupable>();
        pickupable.Initialize();

        // Two players put a module into the same slot at the same moment and the server took this one (V0, U4): the module this client installed itself is still in the slot,
        // and the blind overwrite below would leave it behind as an orphan that is counted but in no slot. Take it out first.
        try
        {
            equipmentSlots.Value.ReleaseContestedOccupant(equipment, entity.Slot, entity.Id);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        InventoryItem inventoryItem = new(pickupable)
        {
            container = equipment
        };
        inventoryItem.item.Reparent(equipment.tr);

        equipment.equipment[entity.Slot] = inventoryItem;

        equipment.UpdateCount(pickupable.GetTechType(), true);
        Equipment.SendEquipmentEvent(pickupable, 0, parentObject, entity.Slot);
        equipment.NotifyEquip(entity.Slot, inventoryItem);

        Log.Debug($"Received: Added module {pickupable.GetTechType()} ({entity.Id}) to equipment {parentObject.GetFullHierarchyPath()} in slot {entity.Slot}");
    }
}
