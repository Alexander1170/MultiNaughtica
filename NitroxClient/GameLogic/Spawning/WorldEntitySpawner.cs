using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning;

internal sealed class WorldEntitySpawner(EntityMetadataManager entityMetadataManager, Entities entities, SimulationOwnership simulationOwnership) : SyncEntitySpawner<WorldEntity>
{
    private readonly WorldEntitySpawnerResolver worldEntitySpawnResolver = new(entityMetadataManager, entities, simulationOwnership);
    private readonly Lazy<Dictionary<Int3, BatchCells>> batchCellsById = new(() =>
    {
        if (NitroxEnvironment.IsNormal)
        {
            return (Dictionary<Int3, BatchCells>)LargeWorldStreamer.main.cellManager.batch2cells;
        }
        return [];
    });

    protected override IEnumerator? SpawnAsync(WorldEntity entity, TaskResult<Optional<GameObject>> result)
    {
        bool foundParentCell = TryFindAwakeParentCell(entity, out EntityCell parentCell);
        if (foundParentCell)
        {
            parentCell.EnsureRoot();
        }
        Optional<GameObject> parent = entity.ParentId != null ? NitroxEntity.GetObjectFrom(entity.ParentId) : Optional.Empty;

        // No place to spawn the entity
        if (!foundParentCell && !parent.HasValue)
        {
            RecordSkippedSpawn(entity, parentCell);
            return null;
        }

        IWorldEntitySpawner entitySpawner = worldEntitySpawnResolver.ResolveEntitySpawner(entity);

        if (entitySpawner is IWorldEntitySyncSpawner syncSpawner &&
            syncSpawner.SpawnSync(entity, parent, parentCell, result))
        {
            return null;
        }

        return entitySpawner.SpawnAsync(entity, parent, parentCell, result);
    }

    protected override bool SpawnsOwnChildren(WorldEntity entity)
    {
        IWorldEntitySpawner entitySpawner = worldEntitySpawnResolver.ResolveEntitySpawner(entity);
        return entitySpawner.SpawnsOwnChildren();
    }

    protected override bool SpawnSync(WorldEntity entity, TaskResult<Optional<GameObject>> result)
    {
        bool foundParentCell = TryFindAwakeParentCell(entity, out EntityCell parentCell);
        if (foundParentCell)
        {
            parentCell.EnsureRoot();
        }
        Optional<GameObject> parent = (entity.ParentId != null) ? NitroxEntity.GetObjectFrom(entity.ParentId) : Optional.Empty;

        // No place to spawn the entity
        if (!foundParentCell && !parent.HasValue)
        {
            RecordSkippedSpawn(entity, parentCell);
            return true;
        }

        IWorldEntitySpawner entitySpawner = worldEntitySpawnResolver.ResolveEntitySpawner(entity);

        return entitySpawner is IWorldEntitySyncSpawner syncSpawner && syncSpawner.SpawnSync(entity, parent, parentCell, result);
    }

    /// <summary>
    /// The entity is not spawned because its cell is not awake. It is not held back (see <see cref="SkippedSpawnTracker"/>): the next load of the cell brings it again with the whole
    /// cell. The skip is counted by the state of the cell, and the first one of a cell is logged.
    /// </summary>
    private void RecordSkippedSpawn(WorldEntity entity, EntityCell? cell)
    {
        AbsoluteEntityCell entityCell = entity.AbsoluteEntityCell;
        string cellState = cell != null ? cell.state.ToString() : SkippedSpawnTracker.STATE_NO_CELL;
        if (entities.SkippedSpawns.Record(entityCell, cellState) == 1)
        {
            Log.Info($"[SkippedSpawn] {entity.TechType} {entity.Id} not spawned, its cell is not awake (state {cellState}): {entityCell}. Other skips in that cell are only counted.");
        }
    }

    public bool TryFindAwakeParentCell(WorldEntity entity, out EntityCell parentCell)
    {
        Int3 batchId = entity.AbsoluteEntityCell.BatchId.ToUnity();
        Int3 cellId = entity.AbsoluteEntityCell.CellId.ToUnity();

        if (batchCellsById.Value.TryGetValue(batchId, out BatchCells batchCells))
        {
            parentCell = batchCells.Get(cellId, entity.Level);
            // in both states, the cell is awake
            return parentCell != null &&
                   (parentCell.state == EntityCell.State.IsAwake || parentCell.state == EntityCell.State.QueuedForSleep);
        }

        parentCell = null;
        return false;
    }

    public EntityCell EnsureCell(WorldEntity entity)
    {
        EntityCell entityCell;

        Int3 batchId = entity.AbsoluteEntityCell.BatchId.ToUnity();
        Int3 cellId = entity.AbsoluteEntityCell.CellId.ToUnity();

        if (!batchCellsById.Value.TryGetValue(batchId, out BatchCells batchCells))
        {
            batchCells = LargeWorldStreamer.main.cellManager.InitializeBatchCells(batchId);
        }

        try
        {
            entityCell = batchCells.EnsureCell(cellId, entity.Level);
        }
        catch (Exception)
        {
            // Error logging is done in BatchCells.EnsureCell
            return null;
        }

        entityCell.EnsureRoot();

        return entityCell;
    }
}
