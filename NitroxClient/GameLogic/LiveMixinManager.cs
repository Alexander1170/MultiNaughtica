using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.GameLogic.FMOD;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.FMOD;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

public class LiveMixinManager
{
    /// <summary>
    ///     Bound of <see cref="syncedInstanceIds"/>: when it is full it is emptied, at worst one hit is then played silently.
    /// </summary>
    private const int MAX_TRACKED_INSTANCES = 2048;

    private readonly SimulationOwnership simulationOwnership;
    private readonly FMODWhitelist fmodWhitelist;

    /// <summary>
    ///     The vehicles and Cyclops which already got a health from <see cref="SyncRemoteHealth"/>: the first one is their spawn state, not a hit.
    /// </summary>
    private readonly HashSet<int> syncedInstanceIds = new();

    /// <summary>
    ///     Reused by <see cref="ReplayRemoteHit"/>.
    /// </summary>
    private readonly DamageInfo remoteHitInfo = new();

    private static readonly HashSet<string> broadcastDeathClassIdWhitelist = new()
    {
        "7d307502-46b7-4f86-afb0-65fe8867f893" // Crash (fish)
    };

    public bool IsRemoteHealthChanging { get; private set; }

    /// <summary>
    ///     True while <see cref="ReplayRemoteKill"/> notifies an object's components of a death which happened for another player.
    /// </summary>
    public bool IsReplayingRemoteKill { get; private set; }

    public LiveMixinManager(SimulationOwnership simulationOwnership, FMODWhitelist fmodWhitelist)
    {
        this.simulationOwnership = simulationOwnership;
        this.fmodWhitelist = fmodWhitelist;
    }

    // Currently, we only apply live mixin updates to vehicles as there is more work to implement
    // damage for regular entities like fish.
    public bool IsWhitelistedUpdateType(LiveMixin entity)
    {
        Vehicle vehicle = entity.GetComponent<Vehicle>();
        SubRoot subRoot = entity.GetComponent<SubRoot>();

        // The scout drone's health is its operator's, like a vehicle's: a creature that bites it on another client reports the hit to the one who flies it
        return vehicle || (subRoot && subRoot.isCyclops) || entity.GetComponent<NitroxClient.MonoBehaviours.Cyclops.ScoutDrone>();
    }
    
    public bool ShouldBroadcastDeath(LiveMixin liveMixin)
    {
        if (liveMixin.TryGetComponent(out UniqueIdentifier uniqueIdentifier) && !string.IsNullOrEmpty(uniqueIdentifier.classId))
        {
            return broadcastDeathClassIdWhitelist.Contains(uniqueIdentifier.classId);
        }
        
        return true;
    }

    /// <summary>
    ///     Sends the "OnKill" message which <see cref="LiveMixin.Kill"/> sent on the killer's side, for a death of which only
    ///     <see cref="CreatureDeath"/>'s part was replicated (see RemoveCreatureCorpseProcessor).
    ///     Without it, the other components (e.g. AI, animations, sounds) keep acting as if the object was alive.
    /// </summary>
    public void ReplayRemoteKill(LiveMixin liveMixin)
    {
        // Deaths broadcast with EntityDestroyed (see LiveMixin_Kill_Patch) are replicated by destroying the object
        if (liveMixin.destroyOnDeath || ShouldBroadcastDeath(liveMixin))
        {
            return;
        }

        GameObject gameObject = liveMixin.gameObject;
        // Entities which didn't die can be attached to the object (e.g. a grabbed vehicle or the local player),
        // in which case only the object's own components are notified
        bool notifyChildren = !(Player.main && Player.main.transform.IsChildOf(gameObject.transform)) &&
                              !gameObject.GetComponentInChildren<Vehicle>(true);

        IsReplayingRemoteKill = true;
        try
        {
            // The consequences of this death were already broadcast by the player for whom it happened
            using (PacketSuppressor<EntityDestroyed>.Suppress())
            using (PacketSuppressor<EntitySpawnedByClient>.Suppress())
            using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
            using (FMODSystem.SuppressSendingSounds())
            {
                // The damage type isn't known, receivers without a parameter still get the message
                if (notifyChildren)
                {
                    gameObject.BroadcastMessage("OnKill", DamageType.Normal, SendMessageOptions.DontRequireReceiver);
                }
                else
                {
                    gameObject.SendMessage("OnKill", DamageType.Normal, SendMessageOptions.DontRequireReceiver);
                }
            }
        }
        finally
        {
            IsReplayingRemoteKill = false;
        }
    }

    public bool ShouldApplyNextHealthUpdate(LiveMixin receiver, GameObject dealer = null)
    {
        if (!receiver.TryGetNitroxId(out NitroxId id))
        {
            return false;
        }

        if (!simulationOwnership.HasAnyLockType(id) && !IsRemoteHealthChanging)
        {
            return false;
        }


        // Check to see if this health change is caused by docked vehicle collisions.  If so, we don't want to apply it.
        if (!dealer)
        {
            return true;
        }

        Vehicle dealerVehicle = dealer.GetComponent<Vehicle>();
        VehicleDockingBay vehicleDockingBay = receiver.GetComponentInChildren<VehicleDockingBay>();

        if (vehicleDockingBay && dealerVehicle)
        {
            if (vehicleDockingBay.GetDockedVehicle() == dealerVehicle ||
                vehicleDockingBay.interpolatingVehicle == dealerVehicle ||
                vehicleDockingBay.nearbyVehicle == dealerVehicle)
            {
                Log.Debug($"Dealer {dealer} is vehicle and currently docked or nearby {receiver}, do not harm it!");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Applies a health which another player (or the server) reported for an object.
    /// </summary>
    /// <remarks>
    ///     A drop on a vehicle or a Cyclops which we do not simulate replays the effects of the hit (<see cref="ReplayRemoteHit"/>) and never goes through
    ///     <c>LiveMixin.TakeDamage</c>: a positive amount could reach <c>Kill</c>, which destroys the copy (Vehicle.OnKill), and would
    ///     run SubFire (camera shake), SubRoot and CyclopsExternalDamageManager. Everything else keeps the legacy negative <c>TakeDamage</c> which never kills.
    /// </remarks>
    public void SyncRemoteHealth(LiveMixin liveMixin, float remoteHealth, Vector3 position = default, DamageType damageType = DamageType.Normal)
    {
        // Only vehicles and Cyclops get their hits replayed. The first health we hear of an object is the one it spawns with
        // (initial sync, a cell streaming in), not a hit; it is marked before the equality check so that a spawn with equal health counts too.
        bool replayable = liveMixin && IsWhitelistedUpdateType(liveMixin);
        bool isFirstSync = replayable && MarkAsSynced(liveMixin);

        if (liveMixin.health == remoteHealth)
        {
            return;
        }

        float difference = remoteHealth - liveMixin.health;
        bool replayHit = false;
        if (replayable && difference < 0 && damageType == DamageType.Normal && liveMixin.gameObject.activeInHierarchy)
        {
            // An object without an id is not synced: it is treated as simulated here (no replay)
            bool simulatedHere = !liveMixin.TryGetNitroxId(out NitroxId id) || simulationOwnership.HasAnyLockType(id);
            bool initialSyncCompleted = Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted;
            replayHit = RemoteHitRules.ShouldReplayHit(liveMixin.health, remoteHealth, isFirstSync, initialSyncCompleted, simulatedHere);
        }

        IsRemoteHealthChanging = true;

        // We catch the exceptions here because we don't want IsRemoteHealthChanging to be stuck to true
        try
        {
            if (replayHit)
            {
                // The receivers and the smoke have to see the health after the hit, as on the simulating client
                liveMixin.health = remoteHealth;
                ReplayRemoteHit(liveMixin, -difference, position); // NO TakeDamage, NO Kill
            }
            else if (difference < 0)
            {
                liveMixin.TakeDamage(difference, position, damageType);
            }
            else
            {
                liveMixin.AddHealth(difference);
            }
        } catch (Exception e)
        {
            Log.Error(e, $"Encountered an expcetion while processing health update");
        }

        IsRemoteHealthChanging = false;

        // We mainly only do the above to trigger damage effects and sounds.  After those, we sync the remote value
        // to ensure that any floating point discrepencies aren't an issue.
        liveMixin.health = remoteHealth;

        if (replayable)
        {
            try
            {
                EnsureLoopingDamageEffect(liveMixin);
            } catch (Exception e)
            {
                Log.Error(e, $"[{nameof(LiveMixinManager)}] Could not update the damage effect of a vehicle");
            }
        }
    }

    private bool MarkAsSynced(LiveMixin liveMixin)
    {
        if (syncedInstanceIds.Count >= MAX_TRACKED_INSTANCES)
        {
            syncedInstanceIds.Clear();
        }

        return syncedInstanceIds.Add(liveMixin.GetInstanceID());
    }

    /// <summary>
    ///     The effects of a hit on our copy of a vehicle, without what <c>LiveMixin.TakeDamage</c> does besides them: the
    ///     hit sound, the hit burst and the cosmetic damage receivers. Nothing here changes the health, kills, creates damage points or fires, or sends a sound.
    /// </summary>
    private void ReplayRemoteHit(LiveMixin liveMixin, float damage, Vector3 position)
    {
        Vector3 hitPosition = position == default(Vector3) ? liveMixin.transform.position : position;

        // The simulating client plays and shares the real sounds, ours are local only
        using (FMODSystem.SuppressSendingSounds())
        {
            if (liveMixin.damageClip &&
                RemoteHitRules.ShouldPlayDamageSound(damage, liveMixin.minDamageForSound, fmodWhitelist.IsWhitelisted(liveMixin.damageClip.path)))
            {
                global::Utils.PlayEnvSound(liveMixin.damageClip, hitPosition, 20f);
            }

            if (liveMixin.damageEffect && RemoteHitRules.ShouldSpawnDamageEffect(damage, Time.time, liveMixin.timeLastDamageEffect))
            {
                global::Utils.SpawnPrefabAt(liveMixin.damageEffect, liveMixin.transform, hitPosition);
                liveMixin.timeLastDamageEffect = Time.time;
            }

            // DamageInfo.Clear doesn't reset the dealer
            remoteHitInfo.Clear();
            remoteHitInfo.originalDamage = damage;
            remoteHitInfo.damage = damage;
            remoteHitInfo.position = hitPosition;
            remoteHitInfo.type = DamageType.Normal;
            remoteHitInfo.dealer = null;

            // An allowlist, NOT NotifyAllAttachedDamageReceivers: SubRoot (the hull voice lines are already played by CyclopsMetadataProcessor, the 18 s timer must not run here),
            // SubFire (camera shake on our screen, fires), CyclopsExternalDamageManager (damage points) and SoundOnDamage (a sound which could double a synced one) are left out.
            // Last, because they are the ones which can throw.
            foreach (IOnTakeDamage receiver in liveMixin.GetComponents<IOnTakeDamage>())
            {
                if (receiver is not (VFXVehicleDamages or VFXSeamothDamages))
                {
                    continue;
                }

                try
                {
                    receiver.OnTakeDamage(remoteHitInfo);
                } catch (Exception e)
                {
                    Log.ErrorOnce($"[{nameof(LiveMixinManager)}] {receiver.GetType().Name}.OnTakeDamage failed on a replayed remote hit: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    ///     What LiveMixin.Start and TakeDamage do for the smoke of a damaged vehicle, driven by the health instead of by a hit so that a vehicle which streams in damaged smokes too.
    ///     LiveMixin.AddHealth already removes it when the vehicle is repaired.
    /// </summary>
    private static void EnsureLoopingDamageEffect(LiveMixin liveMixin)
    {
        GameObject effect = liveMixin.loopingDamageEffect;
        bool hasEffectPrefab = effect;
        bool hasEffectObject = liveMixin.loopingDamageEffectObj;
        if (RemoteHitRules.ShouldSpawnLoopingEffect(liveMixin.GetHealthFraction(), liveMixin.loopEffectBelowPercent, hasEffectPrefab, hasEffectObject))
        {
            liveMixin.loopingDamageEffectObj = global::Utils.SpawnZeroedAt(effect, liveMixin.transform, false);
        }
    }
}
