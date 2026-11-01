using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NitroxClient.Communication;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Spawning;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.Bases;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic
{
    public class Entities
    {
        private readonly IPacketSender packetSender;
        private readonly ThrottledPacketSender throttledPacketSender;
        private readonly EntityMetadataManager entityMetadataManager;
        private readonly SimulationOwnership simulationOwnership;
        private readonly Terrain terrain;

        private readonly Dictionary<NitroxId, Type> spawnedAsType = [];
        private readonly PendingParentEntities pendingParentEntities = new();
        private float lastPendingSweep;
        private const float PENDING_SWEEP_INTERVAL_SECONDS = 30f;

        private readonly Dictionary<Type, IEntitySpawner> entitySpawnersByType = [];

        public List<Entity> EntitiesToSpawn { get; private init; }
        public List<AbsoluteEntityCell> CellsToSpawn { get; private init; }
        public bool SpawningEntities { get; private set; }

        /// <summary>
        /// World entities that were not spawned because their cell was not awake, counted instead of being dropped silently
        /// </summary>
        internal SkippedSpawnTracker SkippedSpawns { get; } = new();

        /// <summary>
        /// Entities held back until the entity they are children of is spawned
        /// </summary>
        internal int PendingParentEntityCount => pendingParentEntities.Count;
        internal PendingParentEntities PendingParents => pendingParentEntities;

        private readonly HashSet<NitroxId> deletedEntitiesIds = [];

        public Entities(IPacketSender packetSender, ThrottledPacketSender throttledPacketSender, EntityMetadataManager entityMetadataManager, PlayerManager playerManager, LocalPlayer localPlayer, LiveMixinManager liveMixinManager, TimeManager timeManager, SimulationOwnership simulationOwnership, Terrain terrain)
        {
            this.packetSender = packetSender;
            this.throttledPacketSender = throttledPacketSender;
            this.entityMetadataManager = entityMetadataManager;
            this.simulationOwnership = simulationOwnership;
            this.terrain = terrain;

            EntitiesToSpawn = [];
            CellsToSpawn = [];

            entitySpawnersByType[typeof(PrefabChildEntity)] = new PrefabChildEntitySpawner();
            entitySpawnersByType[typeof(PathBasedChildEntity)] = new PathBasedChildEntitySpawner();
            entitySpawnersByType[typeof(InstalledModuleEntity)] = new InstalledModuleEntitySpawner();
            entitySpawnersByType[typeof(InstalledBatteryEntity)] = new InstalledBatteryEntitySpawner();
            entitySpawnersByType[typeof(InventoryEntity)] = new InventoryEntitySpawner();
            entitySpawnersByType[typeof(InventoryItemEntity)] = new InventoryItemEntitySpawner(entityMetadataManager);
            entitySpawnersByType[typeof(WorldEntity)] = new WorldEntitySpawner(entityMetadataManager, this, simulationOwnership);
            entitySpawnersByType[typeof(PlaceholderGroupWorldEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(PrefabPlaceholderEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(EscapePodEntity)] = new EscapePodEntitySpawner(localPlayer);
            entitySpawnersByType[typeof(PlayerEntity)] = new PlayerEntitySpawner(playerManager, localPlayer);
            entitySpawnersByType[typeof(VehicleEntity)] = new VehicleEntitySpawner();
            entitySpawnersByType[typeof(SerializedWorldEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(GlobalRootEntity)] = new GlobalRootEntitySpawner();
            entitySpawnersByType[typeof(BaseLeakEntity)] = new BaseLeakEntitySpawner(liveMixinManager);
            entitySpawnersByType[typeof(BuildEntity)] = new BuildEntitySpawner(this, (BaseLeakEntitySpawner)entitySpawnersByType[typeof(BaseLeakEntity)], entityMetadataManager);
            entitySpawnersByType[typeof(RadiationLeakEntity)] = new RadiationLeakEntitySpawner(timeManager, entityMetadataManager);
            entitySpawnersByType[typeof(ModuleEntity)] = new ModuleEntitySpawner(this);
            entitySpawnersByType[typeof(GhostEntity)] = new GhostEntitySpawner();
            entitySpawnersByType[typeof(OxygenPipeEntity)] = new OxygenPipeEntitySpawner((WorldEntitySpawner)entitySpawnersByType[typeof(WorldEntity)]);
            entitySpawnersByType[typeof(PlacedWorldEntity)] = new PlacedWorldEntitySpawner((WorldEntitySpawner)entitySpawnersByType[typeof(WorldEntity)]);
            entitySpawnersByType[typeof(InteriorPieceEntity)] = new InteriorPieceEntitySpawner(this, entityMetadataManager);
            entitySpawnersByType[typeof(GeyserWorldEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(ReefbackEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(ReefbackChildEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(CreatureRespawnEntity)] = entitySpawnersByType[typeof(WorldEntity)];
            entitySpawnersByType[typeof(CyclopsFireEntity)] = new CyclopsFireEntitySpawner();
            entitySpawnersByType[typeof(CyclopsDamagePointEntity)] = new CyclopsDamagePointEntitySpawner();
        }

        public void EntityMetadataChanged(object o, NitroxId id)
        {
            Optional<EntityMetadata> metadata = entityMetadataManager.Extract(o);

            if (metadata.HasValue)
            {
                BroadcastMetadataUpdate(id, metadata.Value);
            }
        }

        public void EntityMetadataChangedThrottled(object o, NitroxId id, float throttleTime = 0.2f)
        {
            // As throttled broadcasting is done after some time by a different function, this is where the packet sending should be interrupted
            if (PacketSuppressor<EntityMetadataUpdate>.IsSuppressed)
            {
                return;
            }
            Optional<EntityMetadata> metadata = entityMetadataManager.Extract(o);

            if (metadata.HasValue)
            {
                BroadcastMetadataUpdateThrottled(id, metadata.Value, throttleTime);
            }
        }

        public void BroadcastMetadataUpdate(NitroxId id, EntityMetadata metadata)
        {
            packetSender.Send(new EntityMetadataUpdate(id, metadata));
        }

        public void BroadcastMetadataUpdateThrottled(NitroxId id, EntityMetadata metadata, float throttleTime = 0.2f)
        {
            throttledPacketSender.SendThrottled(new EntityMetadataUpdate(id, metadata), packet => packet.Id, throttleTime);
        }

        public void BroadcastEntitySpawnedByClient(Entity entity, bool requireRespawn = false, bool onlyIfNew = false)
        {
            packetSender.Send(new EntitySpawnedByClient(entity, requireRespawn, onlyIfNew: onlyIfNew));
        }

        private IEnumerator SpawnNewEntities(bool coldStart = false)
        {
            if (coldStart)
            {
                yield return null; // Skips a frame
            }

            // The coroutine waits a frame after SpawnBatchAsync finishes, and another entity may be enqueued then, so a loop is needed
            while (EntitiesToSpawn.Count > 0)
            {
                yield return SpawnBatchAsync(EntitiesToSpawn).OnYieldError(Log.Error);
            }

            SpawningEntities = false;
            SweepPendingParentEntities(Time.realtimeSinceStartup);

            entityMetadataManager.ClearNewerMetadata();
            deletedEntitiesIds.Clear();
            simulationOwnership.ClearNewerSimulations();

            foreach (AbsoluteEntityCell absoluteEntityCell in CellsToSpawn)
            {
                int skipped = SkippedSpawns.Close(absoluteEntityCell);
                if (skipped > 1)
                {
                    // The first skip of the cell was logged when it happened
                    Log.Info($"[SkippedSpawn] {skipped} entities were not spawned in {absoluteEntityCell}, the cell was not awake");
                }
                terrain.AddFullySpawnedCell(absoluteEntityCell);
            }
            CellsToSpawn.Clear();
        }

        public void EnqueueEntitiesToSpawn(List<Entity> entitiesToEnqueue, List<AbsoluteEntityCell> cellsToSpawn, bool coldStart = false)
        {
            EntitiesToSpawn.InsertRange(0, entitiesToEnqueue);
            CellsToSpawn.AddRange(cellsToSpawn);
            if (!SpawningEntities)
            {
                SpawningEntities = true;
                CoroutineHost.StartCoroutine(SpawnNewEntities(coldStart));
            }
        }

        /// <remarks>
        /// Yield returning takes too much time (at least once per IEnumerator branch) and it quickly gets out of hand with long function call hierarchies so
        /// we want to reduce the amount of yield operations and only skip to the next frame when required (to maintain the FPS).
        /// Also saves resources by using the IOut instances
        /// </remarks>
        /// <param name="batch"></param>
        /// <param name="forceRespawn">Should children be spawned even if already marked as spawned</param>
        /// <param name="skipFrames"></param>
        public IEnumerator SpawnBatchAsync(List<Entity> batch, bool forceRespawn = false, bool skipFrames = true)
        {
            // we divide the FPS by 2.5 because we consider (time for 1 frame + spawning time without a frame + extra computing time)
            float allottedTimePerFrame = 0.4f / Application.targetFrameRate;
            float timeLimit = Time.realtimeSinceStartup + allottedTimePerFrame;

            TaskResult<Optional<GameObject>> entityResult = new();
            TaskResult<Exception> exception = new();

            while (batch.Count > 0)
            {
                entityResult.Set(Optional.Empty);
                exception.Set(null);

                Entity entity = batch[^1];
                batch.RemoveAt(batch.Count - 1);

                // Preconditions which may get the spawn process cancelled or postponed
                if (deletedEntitiesIds.Remove(entity.Id))
                {
                    continue;
                }
                if (WasAlreadySpawned(entity) && !forceRespawn)
                {
                    UpdateEntity(entity);
                    continue;
                }
                else if (entity.ParentId != null && !IsParentReady(entity.ParentId))
                {
                    AddPendingParentEntity(entity);
                    continue;
                }

                // Executing the spawn instructions whether they're sync or async
                IEntitySpawner entitySpawner = entitySpawnersByType[entity.GetType()];
                if (entitySpawner is not ISyncEntitySpawner syncEntitySpawner ||
                    (!syncEntitySpawner.SpawnSyncSafe(entity, entityResult, exception) && exception.Get() == null))
                {
                    IEnumerator coroutine = entitySpawner.SpawnAsync(entity, entityResult);
                    if (coroutine != null)
                    {
                        yield return coroutine.OnYieldError(Log.Error);
                    }
                }

                // Any error in there would make spawning children useless
                if (exception.Get() != null)
                {
                    Log.Error(exception.Get());
                    continue;
                }
                else if (!entityResult.Get().Value)
                {
                    continue;
                }

                entityMetadataManager.ApplyMetadata(entityResult.Get().Value, entity.Metadata);
                simulationOwnership.ApplyNewerSimulation(entity.Id);

                MarkAsSpawned(entity);

                // Finding out about all children (can be hidden in the object's hierarchy or in a pending list)

                if (!entitySpawner.SpawnsOwnChildren(entity))
                {
                    batch.AddRange(entity.ChildEntities);
                }
                // Also the parents that spawn their own children can have entities waiting for them that are not among those children
                if (pendingParentEntities.HasWaitersFor(entity.Id))
                {
                    List<NitroxId> childrenIds = entity.ChildEntities.Select(child => child.Id).ToList();
                    foreach (Entity waiting in pendingParentEntities.Take(entity.Id, childrenIds))
                    {
                        // It may be in the world already (spawned by a route that does not go through here): then it is not spawned a second time, even in a forced batch
                        if (WasAlreadySpawned(waiting))
                        {
                            try
                            {
                                UpdateEntity(waiting);
                            }
                            catch (Exception ex)
                            {
                                // The waiters were taken out of the table already: one that throws must not cost the others their spawn
                                Log.Error(ex, $"Could not apply the metadata of the waiting entity {waiting.Id}");
                            }
                        }
                        else
                        {
                            batch.Add(waiting);
                        }
                    }
                }

                // Skip a frame to maintain FPS
                if (Time.realtimeSinceStartup >= timeLimit && skipFrames)
                {
                    yield return new WaitForEndOfFrame();
                    timeLimit = Time.realtimeSinceStartup + allottedTimePerFrame;
                }
            }
        }

        public IEnumerator SpawnEntityAsync(Entity entity, bool forceRespawn = false, bool skipFrames = false)
        {
            return SpawnBatchAsync(new() { entity }, forceRespawn, skipFrames);
        }

        public void CleanupExistingEntities(List<Entity> dirtyEntities)
        {
            foreach (Entity entity in dirtyEntities)
            {
                RemoveEntityHierarchy(entity);

                Optional<GameObject> gameObject = NitroxEntity.GetObjectFrom(entity.Id);

                if (gameObject.HasValue)
                {
                    DestroyObject(gameObject.Value);
                }
            }
        }

        /// <summary>
        /// Either perform a special operation (e.g. for plants) or a simple <see cref="UnityEngine.Object.Destroy"/>
        /// </summary>
        public static void DestroyObject(GameObject gameObject)
        {
            UnregisterResourceTrackers(gameObject);

            if (gameObject.TryGetComponent(out Plantable plantable))
            {
                plantable.FreeSpot();
                return;
            }
            if (gameObject.TryGetComponent(out GrownPlant grownPlant))
            {
                grownPlant.seed.AliveOrNull()?.FreeSpot();
                return;
            }
            if (gameObject.TryGetComponent(out Pickupable pickupable))
            {
                // Running this now means it won't get ran in OnDestroy() at the end of the frame, so no packets get sent
                pickupable.SetInventoryItem(null);
            }

            UnityEngine.Object.Destroy(gameObject);
        }

        /// <summary>
        /// ResourceTracker only unregisters itself when the resource is killed, picked up, broken or scanned locally: an object destroyed because another
        /// player took it would stay in the scanner room's and the HUD's resource lists. The tracker's id is the NitroxId (see NitroxEntity.SetNewId).
        /// </summary>
        public static void UnregisterResourceTrackers(GameObject gameObject)
        {
            foreach (ResourceTracker resourceTracker in gameObject.GetComponentsInChildren<ResourceTracker>(true))
            {
                resourceTracker.Unregister();
            }
        }

        private void UpdateEntity(Entity entity)
        {
            if (!NitroxEntity.TryGetObjectFrom(entity.Id, out GameObject gameObject))
            {
#if DEBUG && ENTITY_LOG
                Log.Error($"Entity was already spawned but not found(is it in another chunk?) NitroxId: {entity.Id} TechType: {entity.TechType} ClassId: {entity.ClassId} Transform: {entity.Transform}");
#endif
                return;
            }
            entityMetadataManager.ApplyMetadata(gameObject, entity.Metadata);
        }

        private void AddPendingParentEntity(Entity entity)
        {
            float now = Time.realtimeSinceStartup;
            pendingParentEntities.Add(entity, now);
            SweepPendingParentEntities(now);
        }

        /// <summary>
        /// An entity whose parent never comes (its cell was left) must not stay in the table for good. Runs when something is added and when a spawn run ends,
        /// at most every <see cref="PENDING_SWEEP_INTERVAL_SECONDS" />.
        /// </summary>
        private void SweepPendingParentEntities(float now)
        {
            if (now - lastPendingSweep <= PENDING_SWEEP_INTERVAL_SECONDS)
            {
                return;
            }

            lastPendingSweep = now;
            float oldestAllowed = now - PendingParentEntities.MAX_AGE_SECONDS;
            string description = pendingParentEntities.DescribeOlderThan(oldestAllowed);
            int expired = pendingParentEntities.RemoveOlderThan(oldestAllowed);
            if (expired > 0)
            {
                // Expected: late answers for cells that went to sleep already end here. Info, not a warning that reads like a fault in every log.
                Log.Info($"Dropped {expired} entities that waited more than {PendingParentEntities.MAX_AGE_SECONDS:F0} s for a parent that never spawned ({description})");
            }
        }

        /// <summary>
        /// The server moved an entity that has not spawned yet (it waits for its parent) to another parent: it must not come up under the old one.
        /// </summary>
        public void ReparentPendingEntity(NitroxId entityId, NitroxId newParentId)
        {
            if (!pendingParentEntities.TryGet(entityId, out Entity entity))
            {
                return;
            }

            pendingParentEntities.Remove(entityId);
            entity.ParentId = newParentId;
            if (newParentId != null && IsParentReady(newParentId))
            {
                // Nothing would ever flush it: the new parent is in the world already
                EnqueueEntitiesToSpawn([entity], []);
            }
            else
            {
                AddPendingParentEntity(entity);
            }
        }

        /// <summary>
        /// The cell went to sleep. The server sends everything in it again the next time it loads, so the entities of it that waited for a parent are outdated.
        /// </summary>
        public void ForgetPendingEntitiesOfCell(AbsoluteEntityCell cell)
        {
            int dropped = pendingParentEntities.RemoveCell(cell);
            if (dropped > 0)
            {
                Log.Debug($"Dropped {dropped} entities of {cell} that waited for a parent, the cell went to sleep");
            }
        }

        // Entites can sometimes be spawned as one thing but need to be respawned later as another.  For example, a flare
        // spawned inside an Inventory as an InventoryItemEntity can later be dropped in the world as a WorldEntity. Another
        // example would be a base ghost that needs to be respawned a completed piece.
        public bool WasAlreadySpawned(Entity entity)
        {
            if (spawnedAsType.TryGetValue(entity.Id, out Type type))
            {
                return type == entity.GetType() && NitroxEntity.TryGetObjectFrom(entity.Id, out _);
            }

            return false;
        }

        public bool IsKnownEntity(NitroxId id)
        {
            return spawnedAsType.ContainsKey(id);
        }

        public Type RequireEntityType(NitroxId id)
        {
            if (spawnedAsType.TryGetValue(id, out Type type))
            {
                return type;
            }

            throw new InvalidOperationException($"Did not have a type for {id}");
        }

        public bool IsParentReady(NitroxId id)
        {
            return NitroxEntity.TryGetObjectFrom(id, out _);
        }

        public void MarkAsSpawned(Entity entity)
        {
            spawnedAsType[entity.Id] = entity.GetType();
            // It may have waited for its parent while the parent spawned it with its own children
            pendingParentEntities.Remove(entity.Id);
        }

        public void RemoveEntity(NitroxId id)
        {
            spawnedAsType.Remove(id);
            pendingParentEntities.Remove(id);
            pendingParentEntities.RemoveParent(id);
        }

        /// <summary>
        /// A spawn that arrives after a destroy is newer than the destroy (the server's packets arrive in order): a module that was destroyed and made again under the same id (a slot
        /// move, swapped arms) must not be skipped as "deleted" (V0, U3).
        /// </summary>
        public void ForgetDeletion(Entity entity)
        {
            deletedEntitiesIds.Remove(entity.Id);
            foreach (Entity child in entity.ChildEntities)
            {
                ForgetDeletion(child);
            }
        }

        public void MarkForDeletion(NitroxId id)
        {
            deletedEntitiesIds.Add(id);
            pendingParentEntities.Remove(id);
            pendingParentEntities.RemoveParent(id);
        }

        /// <summary>
        /// Allows the ability to respawn an entity and its entire hierarchy. Callers are responsible for ensuring the
        /// entity is no longer in the world.
        /// </summary>
        public void RemoveEntityHierarchy(Entity entity)
        {
            RemoveEntity(entity.Id);

            foreach (Entity child in entity.ChildEntities)
            {
                RemoveEntityHierarchy(child);
            }
        }
    }
}
