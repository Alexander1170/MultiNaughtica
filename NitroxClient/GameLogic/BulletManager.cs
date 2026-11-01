using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Core;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// Registers one stasis sphere per connected remote player, and syncs their behaviour.<br/>
/// Also syncs remote torpedo (of all types) shots and hits.
/// </summary>
public class BulletManager
{
    private readonly PlayerManager playerManager;

    // This only allows for one stasis sphere per player
    // (which is the normal capacity, but could be adapted for a mod letting multiple stasis spheres)
    private readonly Dictionary<SessionId, StasisSphere> stasisSpherePerSessionId = [];

    /// <summary>
    /// TechTypes of objects which should have a Vehicle MB
    /// </summary>
    private static readonly HashSet<TechType> preloadedVehicleTypes = [
        TechType.Seamoth, TechType.Exosuit
    ];

    private readonly Dictionary<TechType, GameObject> torpedoPrefabByTechType = [];

    private GameObject stasisSpherePrefab;

    /// <summary>How many stasis shots and hits of other players this client has replayed (the live check of the gunner station reads them: a replay leaves nothing else to see).</summary>
    public static int RemoteStasisShots { get; private set; }

    public static int RemoteStasisHits { get; private set; }

    /// <summary>The time (unscaled) of the last shot of another player that was replayed here; negative when there was none.</summary>
    public static float LastRemoteStasisShotAt { get; private set; } = -1f;

    public BulletManager(PlayerManager playerManager)
    {
        this.playerManager = playerManager;
    }

    public void ShootSeamothTorpedo(NitroxId bulletId, TechType techType, Vector3 position, Quaternion rotation, float speed, float lifeTime)
    {
        if (!torpedoPrefabByTechType.TryGetValue(techType, out GameObject prefab))
        {
            Log.ErrorOnce($"[{nameof(BulletManager)}] Received ShootSeamothTorpedo request with TechType: {techType} but no prefab was loaded for it");
            return;
        }

        GameObject torpedoClone = GameObjectExtensions.SpawnFromPrefab(prefab, bulletId);
        // We mark it to be able to ignore events from remote bullets
        torpedoClone.AddComponent<RemotePlayerBullet>();

        // We cast it to Bullet to ensure we're calling the same method as in Vehicle.TorpedoShot
        Bullet seamothTorpedo = torpedoClone.GetComponent<SeamothTorpedo>();
        seamothTorpedo.Shoot(position, rotation, speed, lifeTime);
    }

    public void TorpedoHit(NitroxId bulletId, Vector3 position, Quaternion rotation)
    {
        // On the local player, the torpedo might have already exploded while the packet is received with latency.
        // Therefore we don't need to log a failed query of bulletId
        if (NitroxEntity.TryGetComponentFrom(bulletId, out SeamothTorpedo torpedo))
        {
            torpedo.tr.position = position;
            torpedo.tr.rotation = rotation;
            torpedo.OnHit(default);
            torpedo.Deactivate();
        }
    }

    public void TorpedoTargetAcquired(NitroxId bulletId, NitroxId targetId, Vector3 position, Quaternion rotation)
    {
        // The target object might not be findable in which case we'll just ignore it
        // because the explosion will still be moved to the right spot
        if (NitroxEntity.TryGetComponentFrom(bulletId, out SeamothTorpedo torpedo) &&
            NitroxEntity.TryGetObjectFrom(targetId, out GameObject targetObject))
        {
            torpedo.tr.position = position;
            torpedo.tr.rotation = rotation;
            // Stuff from SeamothTorpedo.RepeatingTargeting
            torpedo.homingTarget = targetObject;
            torpedo.CancelInvoke();
        }
    }

    public void ShootStasisSphere(SessionId sessionId, Vector3 position, Quaternion rotation, float speed, float lifeTime, float chargeNormalized)
    {
        StasisSphere cloneSphere = EnsurePlayerHasSphere(sessionId);

        RemoteStasisShots++;
        LastRemoteStasisShotAt = Time.unscaledTime;
#if DEBUG
        Log.Info($"[VehicleUpgrades] stasis: replaying the shot of session {sessionId}: speed {speed:F1}, life {lifeTime:F1} s, charge {chargeNormalized:F2}, from {position:F1}");
#endif
        cloneSphere.Shoot(position, rotation, speed, lifeTime, chargeNormalized);
    }

    public void StasisSphereHit(SessionId sessionId, Vector3 position, Quaternion rotation, float chargeNormalized, float consumption)
    {
        StasisSphere cloneSphere = EnsurePlayerHasSphere(sessionId);

        RemoteStasisHits++;
#if DEBUG
        Log.Info($"[VehicleUpgrades] stasis: replaying the hit of session {sessionId}: charge {chargeNormalized:F2}, at {position:F1}");
#endif
        // Setup the sphere in case the shot was sent earlier
        cloneSphere.Shoot(position, rotation, 0, 0, chargeNormalized);
        // We override this field (set by .Shoot) with the right data
        cloneSphere._consumption = consumption;

        // Code from Bullet.Update when finding an object to hit
        cloneSphere._visible = true;
        cloneSphere.OnMadeVisible();
        cloneSphere.EnableField();
        cloneSphere.Deactivate();
    }

    private StasisSphere EnsurePlayerHasSphere(SessionId sessionId)
    {
        if (stasisSpherePerSessionId.TryGetValue(sessionId, out StasisSphere remoteSphere) && remoteSphere)
        {
            return remoteSphere;
        }
        
        // It should be set to inactive automatically in Bullet.Awake
        GameObject playerSphereClone = Object.Instantiate(stasisSpherePrefab);
        playerSphereClone.name = $"remote-{sessionId}-{playerSphereClone.name}";
        // We mark it to be able to ignore events from remote bullets
        playerSphereClone.AddComponent<RemotePlayerBullet>();
        StasisSphere stasisSphere = playerSphereClone.GetComponent<StasisSphere>();

        stasisSpherePerSessionId[sessionId] = stasisSphere;
        return stasisSphere;
    }

    private void DestroyPlayerSphere(SessionId sessionId)
    {
        if (stasisSpherePerSessionId.TryGetValue(sessionId, out StasisSphere stasisSphere) && stasisSphere)
        {
            DestroySphere(stasisSphere);
        }
        stasisSpherePerSessionId.Remove(sessionId);
    }

    /// <summary>
    /// A stasis sphere for a gunner station of this client: the same clone as the ones of the remote players but without the mark that silences them, so that the patches on <c>StasisSphere.Shoot</c> and
    /// <c>OnHit</c> send its shots and hits like the rifle's. Null when the prefab was never loaded (no Stasis Rifle in the game's data). The caller keeps it for as long as it needs and ends it with
    /// <see cref="DestroySphere" />.
    /// </summary>
    public StasisSphere CreateLocalStasisSphere(string name)
    {
        if (!stasisSpherePrefab)
        {
            return null;
        }
        // It should be set to inactive automatically in Bullet.Awake
        GameObject clone = Object.Instantiate(stasisSpherePrefab);
        clone.name = name;
        return clone.GetComponent<StasisSphere>();
    }

    /// <summary>
    /// Ends a stasis sphere. A field that is still running holds what it froze (a creature's body is set kinematic and only the sphere sets it free again): the game does not do that when a sphere is destroyed,
    /// so it is done here first, or the creature stays frozen on this client until something else resets it.
    /// </summary>
    public static void DestroySphere(StasisSphere sphere)
    {
        if (!sphere)
        {
            return;
        }
        sphere.CancelAll();
        Object.Destroy(sphere.gameObject);
    }

    public IEnumerator Initialize()
    {
        TaskResult<GameObject> result = new();
        
        // Load torpedo types prefab and store them by tech type
        foreach (TechType techType in preloadedVehicleTypes)
        {
            yield return DefaultWorldEntitySpawner.RequestPrefab(techType, result);
            if (result.value && result.value.TryGetComponent(out Vehicle vehicle) && vehicle.torpedoTypes != null)
            {
                foreach (TorpedoType torpedoType in vehicle.torpedoTypes)
                {
                    torpedoPrefabByTechType[torpedoType.techType] = torpedoType.prefab;
                }
            }
        }

        // Load the stasis sphere prefab
        yield return DefaultWorldEntitySpawner.RequestPrefab(TechType.StasisRifle, result);
        StasisRifle rifle = result.value.GetComponent<StasisRifle>();
        if (rifle)
        {
            stasisSpherePrefab = rifle.effectSpherePrefab;
        }

        // Setup remote players' stasis spheres
        foreach (RemotePlayer remotePlayer in playerManager.GetAll())
        {
            EnsurePlayerHasSphere(remotePlayer.SessionId);
        }

        playerManager.OnCreate += (sessionId, _) => { EnsurePlayerHasSphere(sessionId); };
        playerManager.OnRemove += (sessionId, _) => { DestroyPlayerSphere(sessionId); };
    }

    public class RemotePlayerBullet : MonoBehaviour;
}
