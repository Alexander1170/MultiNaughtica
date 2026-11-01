using System.Collections.Generic;
using System.Linq;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.EntityTransformUpdates;

namespace NitroxClient.MonoBehaviours;

public class EntityPositionBroadcaster : MonoBehaviour
{
    public static readonly float BROADCAST_INTERVAL = 0.25f;

    private static HashSet<NitroxId> watchingEntityIds = new();

    private static Dictionary<NitroxId, SplineTransformUpdate> splineUpdatesById = new();

    /// <summary>
    /// Last destination sent for each watched spline follower, so its position can still be sent every window when it did not call GoTo in between.
    /// </summary>
    private static Dictionary<NitroxId, SplineTransformUpdate> lastSplineUpdatesById = new();

    private IPacketSender packetSender;

    private float time;

    public void Awake()
    {
        packetSender = NitroxServiceLocator.LocateService<IPacketSender>();
    }

    public void Update()
    {
        time += Time.deltaTime;

        // Only do on a specific cadence to avoid hammering server
        if (time >= BROADCAST_INTERVAL)
        {
            time = 0;

            if (watchingEntityIds.Count > 0)
            {
                Dictionary<NitroxId, GameObject> watchedEntitiesById = NitroxEntity.GetObjectsFrom(watchingEntityIds);
                Dictionary<NitroxId, GameObject> nonSplineEntitiesById = watchedEntitiesById
                                                                                     .Where(item => !item.Value.GetComponent<SwimBehaviour>() && 
                                                                                                    !item.Value.GetComponent<WalkBehaviour>())
                                                                                     .ToDictionary(item => item.Key, item => item.Value);
                
                List<EntityTransformUpdate> updates = BuildUpdates(nonSplineEntitiesById, watchedEntitiesById);

                if (updates.Count > 0)
                {
                    packetSender.Send(new EntityTransformUpdates(updates));
                }
            }
        }
    }

    private List<EntityTransformUpdate> BuildUpdates(Dictionary<NitroxId, GameObject> nonSplineEntitiesById, Dictionary<NitroxId, GameObject> watchedEntitiesById)
    {
        List<EntityTransformUpdate> updates = new();

        foreach (KeyValuePair<NitroxId, GameObject> gameObjectWithId in nonSplineEntitiesById)
        {
            if (gameObjectWithId.Value)
            {
                updates.Add(new RawTransformUpdate(gameObjectWithId.Key, gameObjectWithId.Value.transform.position.ToDto(), gameObjectWithId.Value.transform.rotation.ToDto()));
            }
        }

        // Only send data for entities still simulated by the local player
        updates.AddRange(splineUpdatesById.Values.Where(
            splineUpdate => this.Resolve<SimulationOwnership>().HasAnyLockType(splineUpdate.Id)
        ));
        
        // A creature which did not call GoTo during this window still moved: send its position with its last destination so that other players can keep their copy close to it
        foreach (KeyValuePair<NitroxId, GameObject> watched in watchedEntitiesById)
        {
            if (!watched.Value || splineUpdatesById.ContainsKey(watched.Key) || !this.Resolve<SimulationOwnership>().HasAnyLockType(watched.Key))
            {
                continue;
            }
            Transform watchedTransform = watched.Value.transform;
            if (!lastSplineUpdatesById.TryGetValue(watched.Key, out SplineTransformUpdate last))
            {
                // A creature we just took over from another player has not called GoTo yet and would stay silent until it does, so its old copy would swim on alone:
                // continue from the course its copy was following (see RemotelyControlled.UpdateKnownSplineUser)
                // (a course of exactly zero means SwimTo was never called, sending it would pull the copy to the world origin)
                if (!watched.Value.TryGetComponent(out SwimBehaviour swimBehaviour) || swimBehaviour.originalTargetPosition == Vector3.zero)
                {
                    continue;
                }
                last = new SplineTransformUpdate(watched.Key, watchedTransform.position.ToDto(), watchedTransform.rotation.ToDto(), swimBehaviour.originalTargetPosition.ToDto(), swimBehaviour.originalTargetDirection.ToDto(), swimBehaviour.originalVelocity);
                lastSplineUpdatesById[watched.Key] = last;
            }
            updates.Add(new SplineTransformUpdate(watched.Key, watchedTransform.position.ToDto(), watchedTransform.rotation.ToDto(), last.DestinationPosition, last.DestinationDirection, last.Velocity));
        }

        splineUpdatesById.Clear();

        return updates;
    }

    public static void WatchEntity(NitroxId id)
    {
        watchingEntityIds.Add(id);

        // The game object may not exist at this very moment (due to being spawned in async). This is OK as we will
        // automatically start sending updates when we finally get it in the world. This behavior will also allow us
        // to resync or respawn entities while still have broadcasting enabled without doing anything extra.
        
        if (NitroxEntity.TryGetComponentFrom(id, out RemotelyControlled remotelyControlled))
        {
            Object.Destroy(remotelyControlled);
        }
    }

    public static void StopWatchingEntity(NitroxId id)
    {
        watchingEntityIds.Remove(id);
        lastSplineUpdatesById.Remove(id);
    }

    public static void RegisterSplineMovementChange(NitroxId id, GameObject gameObject, Vector3 targetPos, Vector3 targetDir, float velocity)
    {
        if (watchingEntityIds.Contains(id))
        {
            SplineTransformUpdate update = new(id, gameObject.transform.position.ToDto(), gameObject.transform.rotation.ToDto(), targetPos.ToDto(), targetDir.ToDto(), velocity);
            splineUpdatesById[id] = update;
            lastSplineUpdatesById[id] = update;
        }
    }

    public static void RemoveEntityMovementControl(GameObject gameObject, NitroxId entityId)
    {
        if (gameObject.TryGetComponent(out RemotelyControlled remotelyControlled))
        {
            Destroy(remotelyControlled);
        }
        StopWatchingEntity(entityId);
    }
}
