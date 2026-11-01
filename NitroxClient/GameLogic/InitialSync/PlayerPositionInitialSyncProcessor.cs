using System.Collections;
using System.Globalization;
using System.Linq;
using NitroxClient.Communication;
using NitroxClient.GameLogic.InitialSync.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using UWE;
using Math = System.Math;

namespace NitroxClient.GameLogic.InitialSync;

public sealed class PlayerPositionInitialSyncProcessor : InitialSyncProcessor
{
    /// <summary>
    /// The escape pod is spawned with the other global root entities before this processor runs, so waiting for it only matters if that went wrong
    /// </summary>
    private const float ESCAPE_POD_TIMEOUT = 5f;
    private const float ENTITIES_ARRIVAL_TIMEOUT = 5f;
    private const float ENTITIES_SPAWN_TIMEOUT = 30f;

    /// <summary>
    /// How long after the end of the initial sync the pose of a player who joined in his escape pod is judged (and corrected), and how much later it is logged once more
    /// </summary>
    private const float POSE_CHECK_SECONDS = 2f;
    private const float POSE_LATE_LOG_SECONDS = 4f;

    private static readonly Vector3 spawnRelativeToEscapePod = new(0.9f, 2.1f, 0);
    private readonly Entities entities;

    public PlayerPositionInitialSyncProcessor(Entities entities)
    {
        this.entities = entities;

        AddDependency<PlayerInitialSyncProcessor>();
        AddDependency<GlobalRootInitialSyncProcessor>();
    }

    /// <summary>
    /// How close to the pivot of a vehicle the spawn point has to be for the player to count as having left while piloting it. The server stores the pose of the vehicle as the position
    /// of its pilot, and the vehicle is a new object without a pilot now: the player would start inside its hull (V0, PO5).
    /// </summary>
    private const float VEHICLE_PIVOT_RADIUS = 0.75f;

    private static void MoveOutOfVehicleAtSpawnPoint(Vector3 spawnPosition)
    {
        foreach (Vehicle vehicle in Object.FindObjectsOfType<Vehicle>())
        {
            if (vehicle.docked || (vehicle.transform.position - spawnPosition).sqrMagnitude > VEHICLE_PIVOT_RADIUS * VEHICLE_PIVOT_RADIUS)
            {
                continue;
            }

            // The game's own routine for the pilot that leaves a vehicle: a clear spot on a ring around it
            Log.Debug($"[{nameof(PlayerPositionInitialSyncProcessor)}] Spawned at the pivot of {vehicle.name}, moving out of it");
            Player.main.SpawnNearby(3f, vehicle.gameObject);
            return;
        }
    }

    public override IEnumerator Process(InitialPlayerSync packet, WaitScreen.ManualWaitItem waitScreenItem)
    {
        // We freeze the player so that he doesn't fall before the cells around him have loaded
        // Is disabled in Terrain.WaitForWorldLoad(), handed over to FreezePlayerWhileEntitiesSpawn() or, if anything goes wrong on the way, in the finally below
        Player.main.cinematicModeActive = true;

        bool freezeHandedOver = false;
        TaskResult<GameObject> attachedEscapePod = new();
        try
        {
            yield return AttachPlayerToEscapePod(packet, attachedEscapePod);

            Vector3 position = packet.PlayerSpawnData.ToUnity();
            Quaternion rotation = packet.PlayerSpawnRotation.ToUnity();
            if (Math.Abs(position.x) < 0.0002 && Math.Abs(position.y) < 0.0002 && Math.Abs(position.z) < 0.0002)
            {
                position = Player.mainObject.transform.position;
            }
            else if (attachedEscapePod.Get() && packet.PlayerSubRootId.HasValue && packet.PlayerSubRootId.Value.Equals(packet.AssignedEscapePodId) &&
                     !IntroCinematicRules.IsPoseInEscapePod(position.ToDto(), attachedEscapePod.Get().transform.position.ToDto()))
            {
                // The server has the player in his lifepod but at a place that is not there (the pose of the intro cinematic, an old save): the pod then makes him walk in the open water, he falls to
                // the sea floor and stands "inside a base" there (2026-10-23). The spawn of the pod, which AttachPlayerToEscapePod put the player at, is where he goes
                Log.Warn($"The server has this player in his escape pod but at {position}, which is {Vector3.Distance(position, attachedEscapePod.Get().transform.position):0} m from it. Spawning at the escape pod instead");
                position = Player.mainObject.transform.position;
                rotation = Player.mainObject.transform.rotation;
            }
            Player.main.SetPosition(position, rotation);

            // Player.ValidateEscapePod is setting currentEscapePod to null if player is not inside EscapePod
            using (PacketSuppressor<EscapePodChanged>.Suppress())
            {
                Player.main.ValidateEscapePod();
            }

            Player.main.precursorOutOfWater = packet.InPrecursor;
            Player.main.SetDisplaySurfaceWater(packet.DisplaySurfaceWater);

            Optional<NitroxId> subRootId = packet.PlayerSubRootId;
            if (!subRootId.HasValue)
            {
                yield return Terrain.SafeWaitForWorldLoad(Terrain.INITIAL_SYNC_WORLD_LOAD_TIMEOUT);
                MoveOutOfVehicleAtSpawnPoint(position);
                Player.main.UpdateIsUnderwater();

                // Check if Player might fall through the map
                if (packet.InPrecursor || !Player.main.IsUnderwaterForSwimming())
                {
                    freezeHandedOver = true;
                    // This coroutine must be started fresh to release the current one
                    CoroutineHost.StartCoroutine(FreezePlayerWhileEntitiesSpawn());
                }

                yield break;
            }

            Optional<GameObject> sub = NitroxEntity.GetObjectFrom(subRootId.Value);
            if (!sub.HasValue && subRootId.Value.Equals(packet.AssignedEscapePodId) && attachedEscapePod.Get())
            {
                // A new player starts in his escape pod: if the pod the server assigned never got registered, he is in the one of the scene
                sub = Optional.Of(attachedEscapePod.Get());
            }

            if (!sub.HasValue)
            {
                Log.Error($"Could not spawn player into subroot with id: {subRootId.Value}");
                yield return Terrain.SafeWaitForWorldLoad(Terrain.INITIAL_SYNC_WORLD_LOAD_TIMEOUT);
                yield break;
            }

            if (sub.Value.TryGetComponent(out SubRoot subRoot))
            {
                Player.main.SetCurrentSub(subRoot, true);
                if (subRoot.TryGetComponent(out Base @base))
                {
                    SetupPlayerIfInWaterPark(@base);
                }
            }
            else if (sub.Value.GetComponent<EscapePod>())
            {
                Player.main.escapePod.Update(true);
                Player.main.ApplyEscapePodSkyIfNeeded();
            }
            else
            {
                Log.Error("SubRootId-GameObject has no SubRoot or EscapePod component");
            }

            // If the player's in a base/cyclops we don't need to wait for the world to load
            Player.main.UpdateIsUnderwater();
        }
        finally
        {
            if (!freezeHandedOver && Player.main)
            {
                Player.main.cinematicModeActive = false;
            }

            StartPoseWatch(packet, attachedEscapePod.Get());
        }
    }

    /// <summary>
    /// Puts the player at the spawn of the escape pod the server assigned to him. If that pod is not registered (yet), it waits for it for a few seconds, then uses the
    /// escape pod of the scene instead of throwing: the rest of the position setup (and the release of the player) must run whatever the state of the pods is.
    /// </summary>
    /// <param name="attachedEscapePod">Gets the pod the player was attached to, stays empty if there is none</param>
    private static IEnumerator AttachPlayerToEscapePod(InitialPlayerSync packet, TaskResult<GameObject> attachedEscapePod)
    {
        EscapePod assignedPod = null;
        BoundedWait wait = new(ESCAPE_POD_TIMEOUT, () => Time.unscaledTime);
        while (wait.KeepWaiting(() => TryGetRegisteredEscapePod(packet.AssignedEscapePodId, out assignedPod)))
        {
            yield return null;
        }

        bool assignedIsRegistered = assignedPod;
        bool sceneHasEscapePod = EscapePod.main;
        EscapePodSource source = ChooseEscapePod(assignedIsRegistered, sceneHasEscapePod);
        if (source == EscapePodSource.None)
        {
            Log.Error($"Escape pod {packet.AssignedEscapePodId} is not registered and the scene has no escape pod either, the player stays where the game put him. {DescribeEscapePods(packet)}");
            yield break;
        }

        if (source == EscapePodSource.SceneFallback)
        {
            Log.Error($"Escape pod {packet.AssignedEscapePodId} is still not registered after {wait.Waited:0.0} s, using EscapePod.main instead. {DescribeEscapePods(packet)}");
            assignedPod = EscapePod.main;
        }

        EscapePod.main.transform.position = assignedPod.transform.position;
        EscapePod.main.playerSpawn.position = assignedPod.transform.position + spawnRelativeToEscapePod;

        Player.main.transform.position = EscapePod.main.playerSpawn.position;
        Player.main.transform.rotation = EscapePod.main.playerSpawn.rotation;

        Player.main.currentEscapePod = assignedPod;
        attachedEscapePod.Set(assignedPod.gameObject);
    }

    private static bool TryGetRegisteredEscapePod(NitroxId escapePodId, out EscapePod escapePod)
    {
        escapePod = null;
        return NitroxEntity.TryGetObjectFrom(escapePodId, out GameObject escapePodObject) && escapePodObject.TryGetComponent(out escapePod);
    }

    /// <summary>
    /// Which escape pod the pod spawn is taken from
    /// </summary>
    /// <param name="assignedIsRegistered">The pod the server assigned is registered under its id</param>
    /// <param name="sceneHasEscapePod"><c>EscapePod.main</c>, the pod that the game put in the scene, exists</param>
    internal static EscapePodSource ChooseEscapePod(bool assignedIsRegistered, bool sceneHasEscapePod)
    {
        if (!sceneHasEscapePod)
        {
            // The spawn point that the player is moved to is the one of the scene's pod
            return EscapePodSource.None;
        }

        return assignedIsRegistered ? EscapePodSource.Assigned : EscapePodSource.SceneFallback;
    }

    internal enum EscapePodSource
    {
        /// <summary>The pod the server assigned</summary>
        Assigned,
        /// <summary><c>EscapePod.main</c>, because the assigned pod is not registered</summary>
        SceneFallback,
        /// <summary>No pod to put the player at</summary>
        None
    }

    /// <summary>
    /// The pods the server sent against the ones that are in the scene, to tell a pod the server never sent from one that was spawned under another id
    /// </summary>
    private static string DescribeEscapePods(InitialPlayerSync packet)
    {
        string sent = string.Join(", ", packet.GlobalRootEntities.OfType<EscapePodEntity>().Select(pod => pod.Id));
        string inScene = string.Join(", ", Object.FindObjectsOfType<EscapePod>().Select(pod => pod.TryGetNitroxId(out NitroxId id) ? id.ToString() : "no id"));
        return $"Escape pods in the initial sync: [{sent}], escape pods in the scene: [{inScene}]";
    }

    private static void SetupPlayerIfInWaterPark(Base @base)
    {
        foreach (Transform baseChild in @base.transform)
        {
            if (baseChild.TryGetComponent(out WaterPark waterPark))
            {
                if (waterPark is LargeRoomWaterPark)
                {
                    // LargeRoomWaterPark.VerifyPlayerWaterPark sets Player.main.currentWaterPark to the right value
                    waterPark.VerifyPlayerWaterPark(Player.main);
                }
                else if (waterPark.IsPointInside(Player.main.transform.position))
                {
                    Player.main.currentWaterPark = waterPark;
                }
            }

            if (Player.main.currentWaterPark)
            {
                return;
            }
        }
    }

    private IEnumerator FreezePlayerWhileEntitiesSpawn()
    {
        Player.main.cinematicModeActive = true;
        try
        {
            // Either wait for SpawnEntities packet to arrive or time out this freeze because we don't expect entities to arrive after that
            BoundedWait arrival = new(ENTITIES_ARRIVAL_TIMEOUT, () => Time.unscaledTime);
            while (arrival.KeepWaiting(() => entities.SpawningEntities))
            {
                yield return null;
            }

            BoundedWait spawning = new(ENTITIES_SPAWN_TIMEOUT, () => Time.unscaledTime);
            while (spawning.KeepWaiting(() => !entities.SpawningEntities))
            {
                yield return null;
            }

            if (spawning.TimedOut)
            {
                Log.Warn($"Entities were still spawning after {ENTITIES_SPAWN_TIMEOUT:0} s, releasing the player anyway");
            }
        }
        finally
        {
            if (Player.main)
            {
                Player.main.cinematicModeActive = false;
            }
        }
    }

    private static string F1(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string Vec(Vector3 value) => $"{F1(value.x)},{F1(value.y)},{F1(value.z)}";

    /// <summary>
    /// Logs where the player is at the end of the initial sync and a moment later, in the Release build too ("[PoseAfterSync]" lines of the client log): in the first live session the second player of a
    /// new world apparently was not at her lifepod right after joining, and the logs could not say where she was. A player who joined in his escape pod and is not in it 2 s later is put back at its
    /// spawn (<see cref="IntroCinematicRules.ShouldReplaceAfterSync" />), which does nothing for a player who is where he belongs.
    /// </summary>
    /// <param name="escapePodObject">The pod the player was attached to, null if there was none</param>
    private static void StartPoseWatch(InitialPlayerSync packet, GameObject escapePodObject)
    {
        // Runs in the finally of the initial sync: whatever goes wrong here must not take the initial sync down with it
        try
        {
            bool joinedInEscapePod = packet.PlayerSubRootId.HasValue && packet.PlayerSubRootId.Value.Equals(packet.AssignedEscapePodId);
            CoroutineHost.StartCoroutine(WatchPoseAfterSync(escapePodObject, joinedInEscapePod));
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[PoseAfterSync] Starting the watch of the pose failed");
        }
    }

    private static IEnumerator WatchPoseAfterSync(GameObject escapePodObject, bool joinedInEscapePod)
    {
        LogPoseAfterSync("sync-end", escapePodObject, joinedInEscapePod);
        yield return new WaitForSecondsRealtime(POSE_CHECK_SECONDS);
        CheckPoseAfterSync(escapePodObject, joinedInEscapePod);
        yield return new WaitForSecondsRealtime(POSE_LATE_LOG_SECONDS);
        LogPoseAfterSync($"sync+{POSE_CHECK_SECONDS + POSE_LATE_LOG_SECONDS:0}s", escapePodObject, joinedInEscapePod);
    }

    private static void CheckPoseAfterSync(GameObject escapePodObject, bool joinedInEscapePod)
    {
        try
        {
            LogPoseAfterSync($"sync+{POSE_CHECK_SECONDS:0}s", escapePodObject, joinedInEscapePod);

            Player player = Player.main;
            if (!player || !escapePodObject || !escapePodObject.TryGetComponent(out EscapePod escapePod))
            {
                return;
            }

            bool cinematicRunning = player.cinematicModeActive || IntroCinematicRules.IsRunning(NitroxServiceLocator.LocateService<LocalPlayer>().IntroCinematicMode);
            if (!IntroCinematicRules.ShouldReplaceAfterSync(joinedInEscapePod, cinematicRunning, player.transform.position.ToDto(), escapePodObject.transform.position.ToDto()))
            {
                return;
            }

            Vector3 away = player.transform.position - escapePodObject.transform.position;
            Log.Warn($"[PoseAfterSync] This player joined in his escape pod but is {away.magnitude:0} m from it {POSE_CHECK_SECONDS:0} s after the initial sync (height difference {F1(away.y)} m), putting him back at the spawn of the pod");
            PutAtEscapePodSpawn(player, escapePod);
            LogPoseAfterSync("replaced", escapePodObject, joinedInEscapePod);
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[PoseAfterSync] Checking the pose of the player failed");
        }
    }

    /// <summary>
    /// The placement of <see cref="AttachPlayerToEscapePod" /> again, for a player who is already in the world: position, pod mode and the report of the pod to the server (the setter of
    /// <c>Player.currentEscapePod</c> is patched to send it), so the server's parent for him is the pod again if he had let go of it in the meantime.
    /// </summary>
    private static void PutAtEscapePodSpawn(Player player, EscapePod escapePod)
    {
        player.SetPosition(escapePod.transform.position + spawnRelativeToEscapePod, player.transform.rotation);
        if (player.groundMotor && player.groundMotor.movement != null)
        {
            // He may have been falling for the 2 s: the spawn is on the pod's floor, a fall speed of the open water would take him through it
            player.groundMotor.movement.velocity = Vector3.zero;
        }

        player.currentEscapePod = escapePod;
        player.escapePod.Update(true);
        player.ApplyEscapePodSkyIfNeeded();
        player.UpdateIsUnderwater();
    }

    private static void LogPoseAfterSync(string moment, GameObject escapePodObject, bool joinedInEscapePod)
    {
        try
        {
            Player player = Player.main;
            if (!player)
            {
                Log.Info($"[PoseAfterSync] {moment}: no player");
                return;
            }

            Vector3 position = player.transform.position;
            string pod = "none";
            string podDistance = "none";
            string heightAbovePod = "none";
            string inPod = "none";
            if (escapePodObject)
            {
                Vector3 podPosition = escapePodObject.transform.position;
                Vector3 delta = position - podPosition;
                pod = Vec(podPosition);
                podDistance = F1(new Vector3(delta.x, 0f, delta.z).magnitude);
                heightAbovePod = F1(delta.y);
                inPod = IntroCinematicRules.IsPoseInEscapePod(position.ToDto(), podPosition.ToDto()).ToString();
            }

            IntroCinematicMode introMode = NitroxServiceLocator.LocateService<LocalPlayer>().IntroCinematicMode;
            bool introShowing = uGUI.main && uGUI.main.intro && uGUI.main.intro.showing;
            Log.Info($"[PoseAfterSync] {moment}: pos={Vec(position)} pod={pod} poddist={podDistance} dy={heightAbovePod} inpod={inPod} joinedinpod={joinedInEscapePod} motor={player.motorMode} currentpod={player.currentEscapePod != null} podflag={player.escapePod.value} underwater={player.IsUnderwaterForSwimming()} swimming={player.IsSwimming()} cinematic={player.cinematicModeActive} intro={introMode} introshowing={introShowing}");
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[PoseAfterSync] {moment}: failed {exception.GetType().Name}: {exception.Message}");
        }
    }
}
