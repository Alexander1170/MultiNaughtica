using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Nitrox.Model.DataStructures;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Helper;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning;

public class InstalledBatteryEntitySpawner : SyncEntitySpawner<InstalledBatteryEntity>
{
    protected override IEnumerator SpawnAsync(InstalledBatteryEntity entity, TaskResult<Optional<GameObject>> result)
    {
        if (!CanSpawn(entity, out EnergyMixin? energyMixin, out string errorLog))
        {
            Log.Error(errorLog);
            result.Set(Optional.Empty);
            yield break;
        }
        // Record this battery as known before waiting on its (potentially multi-frame) prefab spawn, so a pickup
        // happening in that window doesn't wrongly conclude the tool has no battery.
        BatteryChildEntityHelper.RegisterPendingInstalledBattery(entity);

        TaskResult<GameObject> prefabResult = new();
        yield return DefaultWorldEntitySpawner.RequestPrefab(entity.TechType.ToUnity(), prefabResult);
        GameObject gameObject = GameObjectExtensions.InstantiateWithId(prefabResult.Get(), entity.Id);

        SetupObject(gameObject, energyMixin);
        BatteryChildEntityHelper.ForgetPendingInstalledBattery(entity.ParentId, entity.ComponentIndex);

        result.Set(gameObject);
    }

    protected override bool SpawnSync(InstalledBatteryEntity entity, TaskResult<Optional<GameObject>> result)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, entity.TechType.ToUnity()))
        {
            return false;
        }
        if (!CanSpawn(entity, out EnergyMixin energyMixin, out string errorLog))
        {
            Log.Error(errorLog);
            return true;
        }
        BatteryChildEntityHelper.RegisterPendingInstalledBattery(entity);

        GameObject gameObject = GameObjectExtensions.SpawnFromPrefab(prefab, entity.Id);

        SetupObject(gameObject, energyMixin);
        BatteryChildEntityHelper.ForgetPendingInstalledBattery(entity.ParentId, entity.ComponentIndex);

        result.Set(gameObject);
        return true;
    }

    protected override bool SpawnsOwnChildren(InstalledBatteryEntity entity) => false;

    private bool CanSpawn(InstalledBatteryEntity entity, [NotNullWhen(true)] out EnergyMixin? energyMixin, [NotNullWhen(false)] out string? errorLog)
    {
        if (!NitroxEntity.TryGetObjectFrom(entity.ParentId, out GameObject parentObject))
        {
            energyMixin = null;
            errorLog = $"Unable to find parent to install battery {entity}";
            return false;
        }

        energyMixin = parentObject.GetAllComponentsInChildren<EnergyMixin>()
                                  .ElementAtOrDefault(entity.ComponentIndex);

        if (energyMixin == null)
        {
            errorLog = $"Unable to find EnergyMixin on parent to install battery {entity}";
            return false;
        }
        errorLog = null;
        return true;
    }

    private void SetupObject(GameObject gameObject, EnergyMixin energyMixin)
    {
        energyMixin.Initialize();
        energyMixin.RestoreBattery();

        using (PacketSuppressor<EntityReparented>.Suppress())
        using (PacketSuppressor<EntitySpawnedByClient>.Suppress())
        {
            // Swapping a power cell is one step in the game (EnergyMixin.Select): the old cell comes out, the new one goes in, then the old one is picked up. Here the new cell arrives first,
            // while the old one is still in the slot, and the slot refuses a second item: the new cell stayed loose at the world origin and the pick-up of the old one then emptied the slot
            // (the vehicle had no power on this client). The old cell is taken out now; the pick-up that follows destroys its object.
            StorageSlot slot = energyMixin.batterySlot;
            InventoryItem stored = slot.storedItem;
            if (stored != null && stored.item && stored.item.gameObject != gameObject)
            {
                slot.RemoveItem();
            }

            if (!slot.AddItem(new InventoryItem(gameObject.GetComponent<Pickupable>())))
            {
                Log.Error($"[{nameof(InstalledBatteryEntitySpawner)}] The battery {gameObject.name} could not be installed in {energyMixin.gameObject.GetFullHierarchyPath()}");
            }
        }
    }
}
