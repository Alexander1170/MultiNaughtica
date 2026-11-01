using System.Collections;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

public class PlayerEntitySpawner : SyncEntitySpawner<PlayerEntity>
{
    private readonly PlayerManager playerManager;
    private readonly ILocalNitroxPlayer localPlayer;

    public PlayerEntitySpawner(PlayerManager playerManager, ILocalNitroxPlayer localPlayer)
    {
        this.playerManager = playerManager;
        this.localPlayer = localPlayer;
    }

    protected override IEnumerator SpawnAsync(PlayerEntity entity, TaskResult<Optional<GameObject>> result)
    {
        SpawnSync(entity, result);
        return null;
    }

    protected override bool SpawnSync(PlayerEntity entity, TaskResult<Optional<GameObject>> result)
    {
        if (Player.main.TryGetNitroxId(out NitroxId localPlayerId) && localPlayerId == entity.Id)
        {
            // No special setup for the local player.  Simply return saying it is spawned.
            result.Set(Player.main.gameObject);
            return true;
        }

        Optional<RemotePlayer> remotePlayer = playerManager.Find(entity.Id);
        Optional<GameObject> parent = entity.ParentId != null ? NitroxEntity.GetObjectFrom(entity.ParentId) : Optional.Empty;

        // The server may send us a player entity but they are not guarenteed to be actively connected at the moment - don't spawn them.  In the
        // future, we could make this configurable to be able to spawn disconnected players in the world.
        if (!remotePlayer.HasValue || remotePlayer.Value.Body)
        {
            result.Set(Optional.Empty);
            return true;
        }

        GameObject remotePlayerBody = CloneLocalPlayerBodyPrototype();
        // Before InitializeGameObject adds components which read the position (SkyApplier, the rigidbody) and before AttachToParent creates the pawn of a Cyclops from the local position
        PlaceBody(remotePlayerBody, entity, parent);
        remotePlayer.Value.InitializeGameObject(remotePlayerBody);

        if (parent.HasValue)
        {
            AttachToParent(remotePlayer.Value, parent.Value);
        }

        result.Set(Optional.Of(remotePlayerBody));
        return true;
    }

    protected override bool SpawnsOwnChildren(PlayerEntity entity) => false;

    private GameObject CloneLocalPlayerBodyPrototype()
    {
        // Without a parent and in local space, so the clone is where the prototype is under Multiplayer.Main: the local offset of the body in Player.main, about the world origin
        GameObject clone = Object.Instantiate(localPlayer.BodyPrototype, null, false);
        clone.SetActive(true);
        return clone;
    }

    /// <summary>
    /// Puts the body where the server last had the player, until his first movement packet arrives (nothing else sends a pose of a player who does not broadcast: dead, freecam, joined a moment ago).
    /// No snapshot is given to the <see cref="RemotePlayerMovementReplicator" />: it draws the body only from snapshots which expire after <see cref="RemotePlayerMovementReplicator.EXPIRATION_TIME" />,
    /// so one would only hold the body where it already is, and the first real snapshot places it (at once if it is more than <see cref="RemotePlayerMovementReplicator.RESUME_SNAP_DISTANCE" /> off).
    /// </summary>
    private static void PlaceBody(GameObject body, PlayerEntity entity, Optional<GameObject> parent)
    {
        if (entity.Transform == null)
        {
            return;
        }

        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(entity.ParentId != null, entity.Transform.LocalPosition, entity.Transform.LocalRotation);
        if (pose.Space == RemoteBodySpawnPose.PoseSpace.NONE || (pose.Space == RemoteBodySpawnPose.PoseSpace.PARENT && !parent.HasValue))
        {
            Log.Warn($"[{nameof(PlayerEntitySpawner)}] Entity {entity.Id} of a remote player has no usable position ({entity.Transform.LocalPosition} in {(entity.ParentId != null ? $"parent {entity.ParentId}" : "world")}), the body stays at the prototype position until its first movement packet");
            return;
        }

        Vector3 position = pose.Position.ToUnity();
        Quaternion rotation = pose.HasRotation ? pose.Rotation.ToUnity() : body.transform.rotation;
        if (pose.Space == RemoteBodySpawnPose.PoseSpace.PARENT)
        {
            // In world space and parented later by AttachToParent, which keeps the world pose: the pawn of a player in a Cyclops starts from the local position this results in
            Transform parentTransform = parent.Value.transform;
            position = parentTransform.TransformPoint(position);
            if (pose.HasRotation)
            {
                rotation = parentTransform.rotation * rotation;
            }
        }
        body.transform.SetPositionAndRotation(position, rotation);
    }

    private void AttachToParent(RemotePlayer remotePlayer, GameObject parent)
    {
        if (parent.TryGetComponent(out SubRoot subRoot))
        {
            Log.Debug($"Found sub root for {remotePlayer.PlayerName}. Will add him and update animation.");
            remotePlayer.SetSubRoot(subRoot);
            // The pawn is created at the local position of the body only, its handle has no rotation and MaintainPosition copies that onto the body in the next frame
            remotePlayer.Pawn?.SetReference();
        }
        else if (parent.TryGetComponent(out EscapePod escapePod))
        {
            Log.Debug($"Found EscapePod for {remotePlayer.PlayerName}.");
            remotePlayer.SetEscapePod(escapePod);
        }
        else
        {
            Log.Error($"Found neither SubRoot component nor EscapePod on {parent.name} for {remotePlayer.PlayerName}.");
        }
    }
}
