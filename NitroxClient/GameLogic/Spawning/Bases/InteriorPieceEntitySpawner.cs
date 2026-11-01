using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Bases;

public class InteriorPieceEntitySpawner : EntitySpawner<InteriorPieceEntity>
{
    private readonly Entities entities;
    private readonly EntityMetadataManager entityMetadataManager;

    public InteriorPieceEntitySpawner(Entities entities, EntityMetadataManager entityMetadataManager)
    {
        this.entities = entities;
        this.entityMetadataManager = entityMetadataManager;
    }

    protected override IEnumerator SpawnAsync(InteriorPieceEntity entity, TaskResult<Optional<GameObject>> result)
    {
        if (entity.ParentId == null || !NitroxEntity.TryGetComponentFrom(entity.ParentId, out Base @base))
        {
            Log.Error($"Couldn't find a Base component on the parent object of InteriorPieceEntity {entity.Id}");
            yield break;
        }
        yield return RestoreInteriorPiece(entity, @base, result);
        if (!result.Get().HasValue)
        {
            Log.Error($"Restoring interior piece failed: {entity}");
            yield break;
        }
        bool isWaterPark = entity.IsWaterPark;

        List<Entity> batch = new();
        foreach (Entity childEntity in entity.ChildEntities)
        {
            switch(childEntity)
            {
                case InventoryItemEntity:
                case InstalledModuleEntity:
                    batch.Add(childEntity);
                    break;

                case PlanterEntity:
                    foreach (InventoryItemEntity childItemEntity in childEntity.ChildEntities.OfType<InventoryItemEntity>())
                    {
                        batch.Add(childItemEntity);
                    }
                    break;

                case WorldEntity:
                    if (isWaterPark)
                    {
                        batch.Add(childEntity);
                    }
                    break;
            }
        }

        if (isWaterPark)
        {
            // Must happen before child plant spawning
            foreach (Planter planter in result.Get().Value.GetComponentsInChildren<Planter>(true))
            {
                yield return planter.DeserializeAsync();
            }
        }

        yield return entities.SpawnBatchAsync(batch, true);

        // A generator's charge comes with the entity's metadata (PowerSourceMetadata, applied right after this spawner), it does not start full
    }

    protected override bool SpawnsOwnChildren(InteriorPieceEntity entity) => true;

    public IEnumerator RestoreInteriorPiece(InteriorPieceEntity interiorPiece, Base @base, TaskResult<Optional<GameObject>> result = null)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, classId: interiorPiece.ClassId))
        {
            TaskResult<GameObject> prefabResult = new();
            yield return DefaultWorldEntitySpawner.RequestPrefab(interiorPiece.ClassId, prefabResult);
            if (!prefabResult.Get())
            {
                Log.Error($"Couldn't find a prefab for interior piece of ClassId {interiorPiece.ClassId}");
                yield break;
            }
            prefab = prefabResult.Get();
        }

        Base.Face face = interiorPiece.BaseFace.ToUnity();
        face.cell += @base.GetAnchor();
        GameObject moduleObject = @base.SpawnModule(prefab, face);
        if (moduleObject)
        {
            NitroxEntity.SetNewId(moduleObject, interiorPiece.Id);
            yield return BuildingPostSpawner.ApplyPostSpawner(moduleObject, interiorPiece.Id);
            entityMetadataManager.ApplyMetadata(moduleObject, interiorPiece.Metadata);
            result.Set(moduleObject);
        }
    }

    public static InteriorPieceEntity From(IBaseModule module, EntityMetadataManager entityMetadataManager)
    {
        InteriorPieceEntity interiorPiece = InteriorPieceEntity.MakeEmpty();
        interiorPiece.Level = (int)LargeWorldEntity.CellLevel.Global;

        GameObject gameObject = (module as Component).gameObject;
        if (gameObject && gameObject.TryGetComponent(out PrefabIdentifier identifier))
        {
            interiorPiece.ClassId = identifier.ClassId;
        }
        else
        {
            Log.Warn($"Couldn't find an identifier for the interior piece {module.GetType()}");
        }

        if (gameObject.TryGetIdOrWarn(out NitroxId entityId))
        {
            interiorPiece.Id = entityId;
        }

        if (gameObject.TryGetComponentInParent(out Base parentBase, true) &&
            parentBase.TryGetNitroxId(out NitroxId parentId))
        {
            interiorPiece.ParentId = parentId;
        }

        switch (module)
        {
            case LargeRoomWaterPark:
                PlanterEntity leftPlanter = new(interiorPiece.Id.Increment(), interiorPiece.Id);
                PlanterEntity rightPlanter = new(leftPlanter.Id.Increment(), interiorPiece.Id);
                interiorPiece.ChildEntities.Add(leftPlanter);
                interiorPiece.ChildEntities.Add(rightPlanter);
                break;
            // When you deconstruct (not entirely) then construct back those pieces, they keep their inventories
            case BaseNuclearReactor baseNuclearReactor:
                interiorPiece.ChildEntities.AddRange(Items.GetEquipmentModuleEntities(baseNuclearReactor.equipment, entityId, entityMetadataManager));
                break;
            case BaseBioReactor baseBioReactor:
                foreach (ItemsContainer.ItemGroup itemGroup in baseBioReactor.container._items.Values)
                {
                    foreach (InventoryItem item in itemGroup.items)
                    {
                        interiorPiece.ChildEntities.Add(Items.ConvertToInventoryItemEntity(item.item.gameObject, interiorPiece.Id, entityMetadataManager));
                    }
                }
                break;
            case WaterPark:
                PlanterEntity planter = new(interiorPiece.Id.Increment(), interiorPiece.Id);
                interiorPiece.ChildEntities.Add(planter);
                break;
        }

        interiorPiece.BaseFace = module.moduleFace.ToDto();

        return interiorPiece;
    }

    public static IEnumerator RestoreMapRoom(Base @base, MapRoomEntity mapRoomEntity, Entities entities, EntityMetadataManager entityMetadataManager)
    {
        MapRoomFunctionality mapRoomFunctionality = @base.GetMapRoomFunctionalityForCell(mapRoomEntity.Cell.ToUnity());
        if (!mapRoomFunctionality)
        {
            Log.Error($"Couldn't find MapRoomFunctionality in base for cell {mapRoomEntity.Cell}");
            yield break;
        }
        NitroxEntity.SetNewId(mapRoomFunctionality.gameObject, mapRoomEntity.Id);

        // What the room scans for
        entityMetadataManager.ApplyMetadata(mapRoomFunctionality.gameObject, mapRoomEntity.Metadata);

        // The upgrade chips are children of the room on the server but the room itself is created by the base: the chips are put into its container here (it was empty after every
        // join and every resync, the room lost its range and speed upgrades). Their parent is the room, which has its id now.
        List<Entity> upgrades = mapRoomEntity.ChildEntities.OfType<InventoryItemEntity>().ToList<Entity>();
        if (upgrades.Count > 0)
        {
            yield return entities.SpawnBatchAsync(upgrades, false, false);
            // The container tells the room that it changed and the room updates itself in its next frame; this is the same without the wait
            mapRoomFunctionality.UpdateScanRangeAndInterval();
        }
    }
}
