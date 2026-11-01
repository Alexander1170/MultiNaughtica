using System.Collections;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Spawning.Metadata.Processor;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

public class CrashEntitySpawner : IWorldEntitySpawner, IWorldEntitySyncSpawner
{
    public IEnumerator SpawnAsync(WorldEntity entity, Optional<GameObject> parent, EntityCell cellRoot, TaskResult<Optional<GameObject>> result)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, classId: entity.ClassId))
        {
            TaskResult<GameObject> prefabResult = new();
            yield return DefaultWorldEntitySpawner.RequestPrefab(entity.ClassId, prefabResult);
            if (!prefabResult.Get())
            {
                Log.Error($"Couldn't find a prefab for {nameof(WorldEntity)} of ClassId {entity.ClassId}");
                yield break;
            }
            prefab = prefabResult.Get();
        }

        GameObject gameObject = GameObjectExtensions.InstantiateWithId(prefab, entity.Id);
        if (!VerifyCanSpawnOrError(entity, gameObject, parent.Value, out Crash crash, out CrashHome crashHome))
        {
            yield break;
        }
        SetupObject(entity, crash, crashHome);

        result.Set(gameObject);
    }

    public bool SpawnsOwnChildren() => false;

    public bool SpawnSync(WorldEntity entity, Optional<GameObject> parent, EntityCell cellRoot, TaskResult<Optional<GameObject>> result)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, classId: entity.ClassId))
        {
            return false;
        }

        GameObject gameObject = GameObjectExtensions.InstantiateWithId(prefab, entity.Id);
        if (!VerifyCanSpawnOrError(entity, gameObject, parent.Value, out Crash crash, out CrashHome crashHome))
        {
            return true;
        }
        SetupObject(entity, crash, crashHome);

        result.Set(gameObject);
        return true;
    }

    private static bool VerifyCanSpawnOrError(WorldEntity entity, GameObject prefabObject, GameObject parentObject, out Crash crash, out CrashHome crashHome)
    {
        if (!prefabObject.TryGetComponent(out crash))
        {
            Log.Error($"Couldn't find component {nameof(Crash)} on prefab with ClassId: {entity.ClassId}");
            crashHome = null;
            // The object is made already and nothing else knows it: left alone it would swim at the world origin, active, with the id of the entity
            UnityEngine.Object.Destroy(prefabObject);
            return false;
        }
        if (parentObject && parentObject.TryGetComponent(out crashHome))
        {
            return true;
        }

        crashHome = null;
        Log.Error($"Couldn't find a valid parent for {entity}");
        // Same: a Crashfish whose parent is not a home (the id of the home belongs to another entity) was seen at (0, 0, 0) in the log of 2026-10-25
        UnityEngine.Object.Destroy(prefabObject);
        return false;
    }

    private static void SetupObject(WorldEntity worldEntity, Crash crash, CrashHome crashHome)
    {
        crash.transform.SetPositionAndRotation(worldEntity.Transform.Position.ToUnity(), worldEntity.Transform.Rotation.ToUnity());
        crash.transform.localScale = worldEntity.Transform.LocalScale.ToUnity();

        // A home points to one fish. The server's fish is not a child of the home's transform (the vanilla one is), so nothing but CrashHome.OnDestroy removes it, and that only knows the object that
        // crash points to: a second Crash entity of the same home would leave its first object in the world for good. The incoming fish stays (see CrashHomeRules.DecideIncomingFish).
        Crash previousFish = crashHome.crash;
        if (CrashHomeRules.DecideIncomingFish(previousFish, previousFish == crash) == CrashHomeRules.IncomingFishCall.ReplaceTheOldFish)
        {
            string previousId = previousFish.TryGetNitroxId(out NitroxId foundPreviousId) ? foundPreviousId.ToString() : "none";
            string homeId = crashHome.TryGetNitroxId(out NitroxId foundHomeId) ? foundHomeId.ToString() : "none";
            Log.Warn($"[{nameof(CrashEntitySpawner)}] The CrashHome {homeId} has the living Crash {previousId} already: the Crash {worldEntity.Id} of the server takes its place, the old one is destroyed (two Crash entities under one home)");
            UnityEngine.Object.Destroy(previousFish.gameObject);
        }

        crashHome.crash = crash;
        CrashHomeMetadataProcessor.UpdateCrashHomeOpen(crashHome);
        LargeWorldStreamer.main.MakeEntityTransient(crash.gameObject);
    }
}
