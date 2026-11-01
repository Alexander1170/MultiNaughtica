using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Helper;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <remarks>
///     Regular <see cref="WorldEntity" /> are held in cells and should be registered in <see cref="worldEntitiesByCell" />
///     .
///     But <see cref="GlobalRootEntity" /> are held in their own root object (GlobalRoot) so they should never be
///     registered in cells (they're seeable at all times).
/// </remarks>
// TODO: Refactor to not use internal dictionaries but instead depend on EntityRegistry for data. Maybe add API to EntityRegistry to set custom filter+indexer on entity type (i.e. WorldEntity) for improved world entity lookups?
internal sealed class WorldEntityManager
{
    private readonly BatchEntitySpawner batchEntitySpawner;
    private readonly ConcurrentDictionary<NitroxInt3, Lazy<Task<int>>> batchRegistrationTasks = new();
    private readonly EntityRegistry entityRegistry;

    private readonly Lock globalRootEntitiesLock = new();
    private readonly ILogger<WorldEntityManager> logger;
    private readonly IPacketSender packetSender;
    private readonly PlayerManager playerManager;
    private readonly IProgressReporter progressReporter;

    private readonly Lock worldEntitiesLock = new();

    /// <summary>
    ///     Global root entities that are always visible.
    /// </summary>
    internal Dictionary<NitroxId, GlobalRootEntity> globalRootEntitiesById = [];

    /// <summary>
    ///     World entities can disappear if you go out of range.
    /// </summary>
    internal Dictionary<AbsoluteEntityCell, Dictionary<NitroxId, WorldEntity>> worldEntitiesByCell = [];

    public WorldEntityManager(IPacketSender packetSender, EntityRegistry entityRegistry, BatchEntitySpawner batchEntitySpawner, PlayerManager playerManager, IProgressReporter progressReporter, ILogger<WorldEntityManager> logger)
    {
        this.packetSender = packetSender;
        this.entityRegistry = entityRegistry;
        this.batchEntitySpawner = batchEntitySpawner;
        this.playerManager = playerManager;
        this.logger = logger;
        this.progressReporter = progressReporter;
    }

    public List<GlobalRootEntity> GetGlobalRootEntities(bool rootOnly = false)
    {
        if (rootOnly)
        {
            return GetGlobalRootEntities<GlobalRootEntity>().Where(entity => entity.ParentId == null).ToList();
        }
        return GetGlobalRootEntities<GlobalRootEntity>();
    }

    public List<T> GetGlobalRootEntities<T>() where T : GlobalRootEntity
    {
        lock (globalRootEntitiesLock)
        {
            return new(globalRootEntitiesById.Values.OfType<T>());
        }
    }

    public List<GlobalRootEntity> GetPersistentGlobalRootEntities()
    {
        // TODO: refactor if there are more entities that should not be persisted
        return GetGlobalRootEntities(true).Where(entity =>
        {
            if (entity.Metadata is CyclopsMetadata cyclopsMetadata)
            {
                // Do not save cyclops wrecks
                return !cyclopsMetadata.IsDestroyed;
            }

            return true;
        }).ToList();
    }

    public List<WorldEntity> GetEntities(AbsoluteEntityCell cell)
    {
        List<WorldEntity> current = [];
        List<WorldEntity>? stale = null;
        lock (worldEntitiesLock)
        {
            if (!worldEntitiesByCell.TryGetValue(cell, out Dictionary<NitroxId, WorldEntity> batchEntities))
            {
                return current;
            }

            foreach (WorldEntity entity in batchEntities.Values)
            {
                Entity? registered = entityRegistry.TryGetEntityById(entity.Id, out Entity registeredEntity) ? registeredEntity : null;
                switch (CellIndexRules.Classify(entity, registered))
                {
                    case CellIndexRules.EntryState.Current:
                        current.Add(entity);
                        break;
                    case CellIndexRules.EntryState.Stale:
                        // The registry holds another object under this id (the entity was replaced): it is never handed out and leaves the index for good
                        (stale ??= []).Add(entity);
                        break;
                    case CellIndexRules.EntryState.Hidden:
                        // The id left the registry (destroyed, picked up): never handed out again
                        break;
                }
            }

            if (stale != null)
            {
                foreach (WorldEntity staleEntity in stale)
                {
                    batchEntities.Remove(staleEntity.Id);
                }
            }
        }

        if (stale != null)
        {
            string described = CellIndexRules.Describe(stale);
            logger.ZLogWarning($"Cell {cell}: {stale.Count} entries of the cell index were not the object that the registry holds under their id (copies of entities that were replaced), removed so that no player is sent them: {described}");
        }

        return current;
    }

    /// <summary>
    ///     The cell an entity left last and when (<see cref="Environment.TickCount64" />), see <see cref="GetRecentlyLeftCell" />. Guarded by <see cref="worldEntitiesLock" />.
    /// </summary>
    private readonly Dictionary<NitroxId, (AbsoluteEntityCell Cell, long Tick)> recentlyLeftCellsById = [];

    /// <summary>
    ///     Players who still have a copy of an entity that moved out of every cell they have loaded, and when to check whether they still do (K4e).
    ///     Guarded by <see cref="worldEntitiesLock" />.
    /// </summary>
    private readonly Dictionary<NitroxId, GhostCheck> ghostChecksById = [];

    private sealed class GhostCheck(long dueTick)
    {
        public readonly HashSet<SessionId> Holders = [];
        public long DueTick = dueTick;
    }

    /// <summary>
    ///     How long (ms) after an entity left the cells a player has loaded that player's copy may stay before it is removed from their world.
    /// </summary>
    private const long GHOST_COPY_DELAY_MS = 3000;

    private const int MAX_GHOST_CHECKS = 512;
    private const int MAX_REMEMBERED_LEFT_CELLS = 512;
    private const long FORGET_LEFT_CELL_AFTER_MS = 10000;

    /// <summary>
    ///     A player who has the old cell loaded and not the new one still has a copy of the entity (a creature, a dropped item) which nothing updates once the server
    ///     stopped sending it positions: it stood in the loaded cell, a ghost of something that is elsewhere now (K4e). Remembers those players so that
    ///     <see cref="TakeDueGhostHolders" /> can tell them to drop the copy if it has not come back into their view after <see cref="GHOST_COPY_DELAY_MS" />.
    /// </summary>
    private void RememberGhostHolders(WorldEntity entity, AbsoluteEntityCell oldCell, AbsoluteEntityCell newCell)
    {
        foreach (Player player in playerManager.ConnectedPlayers())
        {
            if (!player.HasCellLoaded(oldCell) || player.HasCellLoaded(newCell) || player.OutOfCellVisibleEntities.Contains(entity.Id))
            {
                continue;
            }

            if (!ghostChecksById.TryGetValue(entity.Id, out GhostCheck? check))
            {
                if (ghostChecksById.Count >= MAX_GHOST_CHECKS)
                {
                    long now = Environment.TickCount64;
                    List<NitroxId> stale = ghostChecksById.Where(pair => now - pair.Value.DueTick > 60000).Select(pair => pair.Key).ToList();
                    foreach (NitroxId staleId in stale)
                    {
                        ghostChecksById.Remove(staleId);
                    }
                }
                check = new GhostCheck(Environment.TickCount64 + GHOST_COPY_DELAY_MS);
                ghostChecksById[entity.Id] = check;
            }
            check.Holders.Add(player.SessionId);
        }
    }

    /// <returns>The players whose check for this entity is due now: they still had a copy when the entity left their loaded cells, the caller drops it if they still cannot see the entity</returns>
    public List<SessionId> TakeDueGhostHolders(NitroxId id)
    {
        lock (worldEntitiesLock)
        {
            if (!ghostChecksById.TryGetValue(id, out GhostCheck? check) || Environment.TickCount64 < check.DueTick)
            {
                return [];
            }
            ghostChecksById.Remove(id);
            return [..check.Holders];
        }
    }

    private void RememberLeftCell(NitroxId id, AbsoluteEntityCell cell)
    {
        long now = Environment.TickCount64;
        if (recentlyLeftCellsById.Count >= MAX_REMEMBERED_LEFT_CELLS)
        {
            List<NitroxId> expired = [];
            foreach (KeyValuePair<NitroxId, (AbsoluteEntityCell Cell, long Tick)> entry in recentlyLeftCellsById)
            {
                if (now - entry.Value.Tick > FORGET_LEFT_CELL_AFTER_MS)
                {
                    expired.Add(entry.Key);
                }
            }
            foreach (NitroxId expiredId in expired)
            {
                recentlyLeftCellsById.Remove(expiredId);
            }
        }
        recentlyLeftCellsById[id] = (cell, now);
    }

    /// <summary>
    ///     The cell the entity left last, if that was at most <paramref name="withinMs" /> ms ago. A player who had that cell loaded still has the entity and
    ///     must keep receiving its position until its own copy reports leaving the loaded area (see OutOfCellEntity).
    /// </summary>
    public AbsoluteEntityCell? GetRecentlyLeftCell(NitroxId id, long withinMs)
    {
        lock (worldEntitiesLock)
        {
            return recentlyLeftCellsById.TryGetValue(id, out (AbsoluteEntityCell Cell, long Tick) left) && Environment.TickCount64 - left.Tick <= withinMs ? left.Cell : null;
        }
    }

    public bool TryUpdateEntityPosition(NitroxId id, NitroxVector3 position, NitroxQuaternion rotation, out AbsoluteEntityCell? newCell, out WorldEntity worldEntity)
    {
        lock (worldEntitiesLock)
        {
            if (!entityRegistry.TryGetEntityById(id, out worldEntity))
            {
                logger.ZLogWarningOnce($"can't update entity position of {id} because it isn't registered");
                newCell = null;
                return false;
            }

            // Return early because a GlobalRootEntity doesn't have an AbsoluteEntityCell, thus it would throw an exception
            if (worldEntity is GlobalRootEntity)
            {
                worldEntity.Transform.Position = position;
                worldEntity.Transform.Rotation = rotation;
                newCell = null;
                return true;
            }

            AbsoluteEntityCell oldCell = worldEntity.AbsoluteEntityCell;

            worldEntity.Transform.Position = position;
            worldEntity.Transform.Rotation = rotation;

            newCell = worldEntity.AbsoluteEntityCell;

            if (oldCell != newCell)
            {
                EntitySwitchedCells(worldEntity, oldCell, newCell);
                RememberLeftCell(id, oldCell);
                RememberGhostHolders(worldEntity, oldCell, newCell);
            }

            return true;
        }
    }

    public Optional<Entity> RemoveGlobalRootEntity(NitroxId entityId, bool removeFromRegistry = true)
    {
        Optional<Entity> removedEntity = Optional.Empty;
        lock (globalRootEntitiesLock)
        {
            if (removeFromRegistry)
            {
                // In case there were player entities under the removed entity, we need to reparent them to the GlobalRoot
                // to make sure that they won't be removed
                if (entityRegistry.TryGetEntityById(entityId, out GlobalRootEntity globalRootEntity))
                {
                    MovePlayerChildrenToRoot(globalRootEntity);
                }
                removedEntity = entityRegistry.RemoveEntity(entityId);
            }
            globalRootEntitiesById.Remove(entityId);
        }
        return removedEntity;
    }

    public void MovePlayerChildrenToRoot(GlobalRootEntity globalRootEntity)
    {
        List<PlayerEntity> playerEntities = FindPlayerEntitiesInChildren(globalRootEntity);
        foreach (PlayerEntity childPlayerEntity in playerEntities)
        {
            // Reparent the entity on top of GlobalRoot
            globalRootEntity.ChildEntities.Remove(childPlayerEntity);
            // The pose of a player entity is relative to its parent while it has one and world space while it has none (read back by clients as such): keep the world pose through the change
            if (childPlayerEntity.Transform != null)
            {
                childPlayerEntity.Transform.SetParent(null, true);
            }
            childPlayerEntity.ParentId = null;

            // Make sure the PlayerEntity is correctly registered
            AddOrUpdateGlobalRootEntity(childPlayerEntity);
        }
    }

    public void TrackEntityInTheWorld(WorldEntity entity)
    {
        if (entity is GlobalRootEntity globalRootEntity)
        {
            AddOrUpdateGlobalRootEntity(globalRootEntity, false);
            return;
        }

        RegisterWorldEntity(entity);
    }

    /// <summary>
    ///     Automatically registers a WorldEntity in its AbsoluteEntityCell
    /// </summary>
    /// <remarks>
    ///     The provided should NOT be a GlobalRootEntity (they don't stand in cells)
    /// </remarks>
    public void RegisterWorldEntity(WorldEntity entity)
    {
        RegisterWorldEntityInCell(entity, entity.AbsoluteEntityCell);
    }

    public void RegisterWorldEntityInCell(WorldEntity entity, AbsoluteEntityCell cell)
    {
        lock (worldEntitiesLock)
        {
            if (!worldEntitiesByCell.TryGetValue(cell, out Dictionary<NitroxId, WorldEntity> worldEntitiesInCell))
            {
                worldEntitiesInCell = worldEntitiesByCell[cell] = [];
            }
            worldEntitiesInCell[entity.Id] = entity;
        }
    }

    /// <summary>
    ///     Automatically unregisters a WorldEntity in its AbsoluteEntityCell
    /// </summary>
    public void UnregisterWorldEntity(WorldEntity entity)
    {
        UnregisterWorldEntityFromCell(entity.Id, entity.AbsoluteEntityCell);
    }

    public void UnregisterWorldEntityFromCell(NitroxId entityId, AbsoluteEntityCell cell)
    {
        lock (worldEntitiesLock)
        {
            if (worldEntitiesByCell.TryGetValue(cell, out Dictionary<NitroxId, WorldEntity> worldEntitiesInCell))
            {
                worldEntitiesInCell.Remove(entityId);
            }
        }
    }

    public async Task LoadAllUnspawnedEntitiesAsync(CancellationToken token)
    {
        int totalBatches = SubnauticaMap.DimensionsInBatches.X * SubnauticaMap.DimensionsInBatches.Y * SubnauticaMap.DimensionsInBatches.Z;
        int batchesLoaded = 0;

        for (int x = 0; x < SubnauticaMap.DimensionsInBatches.X; x++)
        {
            token.ThrowIfCancellationRequested();
            for (int y = 0; y < SubnauticaMap.DimensionsInBatches.Y; y++)
            {
                for (int z = 0; z < SubnauticaMap.DimensionsInBatches.Z; z++)
                {
                    int spawned = await LoadUnspawnedEntitiesAsync(new(x, y, z), true);

                    logger.LogEntitiesLoadedFromBatch(spawned, x, y, z);

                    batchesLoaded++;
                }
            }

            if (batchesLoaded > 0)
            {
                float progress = (float)batchesLoaded / totalBatches;
                int percentage = (int)(100f * progress);
                logger.ZLogInformation($"Loading : {percentage}%");
                await progressReporter.ReportProgressAsync($"Loading entities: {percentage}%", progress);
            }
        }
    }

    public Task<int> LoadUnspawnedEntitiesAsync(NitroxInt3 batchId, bool suppressLogs)
    {
        return batchRegistrationTasks.GetOrAdd(batchId, id => new Lazy<Task<int>>(() => LoadAndRegisterBatchInternalAsync(id, suppressLogs))).Value;
    }

    private async Task<int> LoadAndRegisterBatchInternalAsync(NitroxInt3 batchId, bool suppressLogs)
    {
        List<Entity> spawnedEntities = await batchEntitySpawner.LoadUnspawnedEntitiesAsync(batchId, suppressLogs);

        List<WorldEntity> entitiesInCells = spawnedEntities.Where(entity => typeof(WorldEntity).IsAssignableFrom(entity.GetType()) &&
                                                                            entity.GetType() != typeof(CellRootEntity) &&
                                                                            entity.GetType() != typeof(GlobalRootEntity))
                                                           .Cast<WorldEntity>()
                                                           .ToList();

        // UWE stores entities serialized with a handful of parent cell roots.  These only represent a small fraction of all possible cell
        // roots that could exist.  There is no reason for the server to know about these and much easier to consider top-level world entities
        // as positioned globally and not locally.  Thus, we promote cell root children to top level and throw the cell roots away. 
        foreach (CellRootEntity cellRoot in spawnedEntities.OfType<CellRootEntity>())
        {
            foreach (WorldEntity worldEntity in cellRoot.ChildEntities.Cast<WorldEntity>())
            {
                worldEntity.ParentId = null;
                worldEntity.Transform.SetParent(null);
                entitiesInCells.Add(worldEntity);
            }

            cellRoot.ChildEntities = new List<Entity>();
        }
        // Specific type of entities which is not parented to a CellRootEntity
        entitiesInCells.AddRange(spawnedEntities.OfType<SerializedWorldEntity>());

        // A generated entity never replaces one the registry already holds (the registered one has the state the players gave it): a batch is only generated twice when the record of
        // generated batches missed it, and then the ids of the second run can be one off (see EntityRegistry.AddEntitiesKeepingExisting).
        List<Entity> ignored = [];
        entityRegistry.AddEntitiesKeepingExisting(entitiesInCells.OfType<Entity>().ToList(), ignored);
        if (ignored.Count > 0)
        {
            logger.ZLogWarning($"Batch {batchId} was generated again: kept the {ignored.Count} entities of the world that have the ids of generated ones, the generated ones were dropped (the record of generated batches did not list this batch)");
        }

        // Only the objects that are in the registry go into the cells: the entities that were kept are in them since the world was loaded
        int registered = 0;
        foreach (WorldEntity entity in entitiesInCells)
        {
            if (entityRegistry.TryGetEntityById(entity.Id, out Entity registeredEntity) && ReferenceEquals(registeredEntity, entity))
            {
                RegisterWorldEntity(entity);
                registered++;
            }
        }

        return registered;
    }

    public void StopTrackingEntity(WorldEntity entity)
    {
        if (entity is GlobalRootEntity)
        {
            RemoveGlobalRootEntity(entity.Id, false);
        }
        else
        {
            UnregisterWorldEntity(entity);
        }
    }

    public bool TryDestroyEntity(NitroxId entityId, [NotNullWhen(true)] out Entity? entity)
    {
        Optional<Entity> optEntity = entityRegistry.RemoveEntity(entityId);

        if (!optEntity.HasValue)
        {
            entity = null;
            return false;
        }
        entity = optEntity.Value;

        if (entity is WorldEntity worldEntity)
        {
            StopTrackingEntity(worldEntity);
        }
        // The registry removed the children with their parent, they must also leave the cell index
        StopTrackingDescendants(entity);

        return true;
    }

    private void StopTrackingDescendants(Entity entity)
    {
        foreach (Entity child in entity.ChildEntities)
        {
            if (child is WorldEntity childWorldEntity)
            {
                StopTrackingEntity(childWorldEntity);
            }
            StopTrackingDescendants(child);
        }
    }

    /// <summary>
    ///     To avoid risking not having the same entity in <see cref="globalRootEntitiesById" /> and in EntityRegistry, we
    ///     update both at the same time.
    /// </summary>
    public void AddOrUpdateGlobalRootEntity(GlobalRootEntity entity, bool addOrUpdateRegistry = true)
    {
        lock (globalRootEntitiesLock)
        {
            if (addOrUpdateRegistry)
            {
                entityRegistry.AddOrUpdate(entity);
            }
            globalRootEntitiesById[entity.Id] = entity;
        }
    }

    private void EntitySwitchedCells(WorldEntity entity, AbsoluteEntityCell oldCell, AbsoluteEntityCell newCell)
    {
        if (entity is GlobalRootEntity)
        {
            return; // We don't care what cell a global root entity resides in.  Only phasing entities.
        }

        if (oldCell != newCell)
        {
            lock (worldEntitiesLock)
            {
                // Specifically remove entity from oldCell
                UnregisterWorldEntityFromCell(entity.Id, oldCell);

                // Automatically add entity to its new cell
                RegisterWorldEntityInCell(entity, newCell);

                // It can happen for some players that the entity moves to a loaded cell of theirs, but that they hadn't spawned it in the first place
                foreach (Player player in playerManager.ConnectedPlayers())
                {
                    if (player.HasCellLoaded(newCell) && !player.HasCellLoaded(oldCell))
                    {
                        packetSender.SendPacketAsync(new SpawnEntities(entity), player.SessionId);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Iterative breadth-first search which gets all children player entities in <paramref name="parentEntity" />'s
    ///     hierarchy.
    /// </summary>
    private List<PlayerEntity> FindPlayerEntitiesInChildren(Entity parentEntity)
    {
        List<PlayerEntity> playerEntities = [];
        List<Entity> entitiesToSearch = [parentEntity];

        while (entitiesToSearch.Count > 0)
        {
            Entity currentEntity = entitiesToSearch[^1];
            entitiesToSearch.RemoveAt(entitiesToSearch.Count - 1);

            if (currentEntity is PlayerEntity playerEntity)
            {
                playerEntities.Add(playerEntity);
            }
            else
            {
                entitiesToSearch.InsertRange(0, currentEntity.ChildEntities);
            }
        }
        return playerEntities;
    }
}
