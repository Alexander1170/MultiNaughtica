using System.Collections;
using Nitrox.Model.DataStructures;
using NitroxClient.Communication;
using NitroxClient.GameLogic.FMOD;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.Metadata.Processor;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.CinematicController;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning;

public class EscapePodEntitySpawner : SyncEntitySpawner<EscapePodEntity>
{
    /*
     * When creating additional escape pods (multiple users with multiple pods)
     * we want to suppress the escape pod's awake method so it doesn't override
     * EscapePod.main to the new escape pod.
     */
    public static bool SuppressEscapePodAwakeMethod;

#if DEBUG
    /// <summary>
    /// Sync test harness only (Debug builds, the harness sets <c>NITROX_HARNESS_UNREGISTERED_POD=1</c>): the pod is spawned but never registered under its id, which is
    /// what a joining player sees in upstream 2898, to watch the initial sync (PL3) fall back to the pod of the scene instead of throwing and leaving the player frozen
    /// </summary>
    private static readonly bool HarnessLeavePodUnregistered = System.Environment.GetEnvironmentVariable("NITROX_HARNESS_UNREGISTERED_POD") == "1";
#else
    private const bool HarnessLeavePodUnregistered = false;
#endif

    private readonly LocalPlayer localPlayer;

    public EscapePodEntitySpawner(LocalPlayer localPlayer)
    {
        this.localPlayer = localPlayer;
    }

    protected override IEnumerator SpawnAsync(EscapePodEntity entity, TaskResult<Optional<GameObject>> result)
    {
        SpawnSync(entity, result);
        return null;
    }

    protected override bool SpawnSync(EscapePodEntity entity, TaskResult<Optional<GameObject>> result)
    {
        SuppressEscapePodAwakeMethod = true;

        GameObject escapePod = CreateNewEscapePod(entity);

        SuppressEscapePodAwakeMethod = false;

        result.Set(Optional.Of(escapePod));
        return true;
    }

    protected override bool SpawnsOwnChildren(EscapePodEntity entity) => false;

    private GameObject CreateNewEscapePod(EscapePodEntity escapePodEntity)
    {
        // TODO: When we want to implement multiple escape pods, instantiate the prefab. Backlog task: #1945
        //       This will require some work as instantiating the prefab as-is will not make it visible.
        //GameObject escapePod = Object.Instantiate(EscapePod.main.gameObject);
        GameObject escapePod = EscapePod.main.gameObject;
        EscapePod pod = escapePod.GetComponent<EscapePod>();

        Object.DestroyImmediate(escapePod.GetComponent<NitroxEntity>()); // if template has a pre-existing NitroxEntity, remove.
        if (!HarnessLeavePodUnregistered)
        {
            NitroxEntity.SetNewId(escapePod, escapePodEntity.Id);
        }

        if (escapePod.TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
        else
        {
            Log.Error("Escape pod did not have a rigid body!");
        }

        pod.anchorPosition = escapePod.transform.position = escapePodEntity.Transform.Position.ToUnity();

        pod.ForceSkyApplier();
        pod.escapePodCinematicControl.StopAll();

        // Player is not new and has completed the intro cinematic. If not EscapePod repair status is handled by the intro cinematic.
        if (escapePodEntity.Metadata is EscapePodMetadata metadata && localPlayer.IntroCinematicMode == IntroCinematicMode.COMPLETED)
        {
            using FMODSoundSuppressor soundSuppressor = FMODSystem.SuppressSubnauticaSounds();
            using PacketSuppressor<EntityMetadataUpdate> packetSuppressor = PacketSuppressor<EntityMetadataUpdate>.Suppress();

            Radio radio = pod.radioSpawner.spawnedObj.GetComponent<Radio>();
            EscapePodMetadataProcessor.ProcessInitialSyncMetadata(pod, radio, metadata);
            // NB: Entities.SpawnBatchAsync (which is the function calling the current spawner)
            // will still apply the metadata another time but we don't care as it's not destructive
        }

        FixStartMethods(escapePod);

        return escapePod;
    }

    /// <summary>
    /// Start() isn't executed for the EscapePod and children (Why? Idk, maybe because it's a scene...) so we call the components here where we have patches in Start.
    /// </summary>
    private static void FixStartMethods(GameObject escapePod)
    {
        foreach (FMOD_CustomEmitter customEmitter in escapePod.GetComponentsInChildren<FMOD_CustomEmitter>(true))
        {
            customEmitter.Start();
        }

        foreach (FMOD_StudioEventEmitter studioEventEmitter in escapePod.GetComponentsInChildren<FMOD_StudioEventEmitter>(true))
        {
            studioEventEmitter.Start();
        }

        MultiplayerCinematicReference reference = escapePod.EnsureComponent<MultiplayerCinematicReference>();
        foreach (PlayerCinematicController controller in escapePod.GetComponentsInChildren<PlayerCinematicController>(true))
        {
            reference.AddController(controller);
        }
    }
}
