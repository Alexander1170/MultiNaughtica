using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.FMOD;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class EscapePodMetadataProcessor : EntityMetadataProcessor<EscapePodMetadata>
{
    /// <summary>
    /// The last pod and radio state received from the server, kept for <see cref="ReapplyAfterIntro" />.
    /// </summary>
    private static EscapePodMetadata latestMetadata;

    // For metadata changes outside initial sync we only care about broken -> repaired
    public override void ProcessMetadata(GameObject gameObject, EscapePodMetadata metadata)
    {
        latestMetadata = metadata;
        if (!gameObject.TryGetComponent(out EscapePod pod))
        {
            Log.Error($"[{nameof(EscapePodMetadataProcessor)}] Could not get the EscapePod component from the provided gameobject.");
            return;
        }

        if (!pod.liveMixin.IsFullHealth() && metadata.PodRepaired)
        {
            pod.liveMixin.health = pod.liveMixin.maxHealth;
            pod.healthScalar = 1;
            pod.damageEffectsShowing = true;
            pod.UpdateDamagedEffects();
            pod.OnRepair();
        }

        if (!pod.radioSpawner.spawnedObj.TryGetComponent(out Radio radio))
        {
            Log.Error($"[{nameof(EscapePodMetadataProcessor)}] Could not get Radio from EscapePod.");
            return;
        }

        if (!radio.liveMixin.IsFullHealth() && metadata.RadioRepaired)
        {
            radio.liveMixin.AddHealth(radio.liveMixin.maxHealth);
        }
    }

    /// <summary>
    /// Applies repaired state without animations and minimal audio playback
    /// </summary>
    public static void ProcessInitialSyncMetadata(EscapePod pod, Radio radio, EscapePodMetadata metadata)
    {
        latestMetadata = metadata;
        if (metadata.PodRepaired)
        {
            pod.liveMixin.health = pod.liveMixin.maxHealth;
            pod.healthScalar = 1;
            pod.damageEffectsShowing = true; // Needs to be set to true for UpdateDamagedEffects() to function
            pod.UpdateDamagedEffects();
            pod.vfxSpawner.SpawnManual(); // Spawn vfx to instantly disable it so no smoke is fading after player has joined
            pod.vfxSpawner.spawnedObj.SetActive(false);
            pod.lightingController.SnapToState(0);
        }
        else
        {
            IntroLifepodDirector introLifepodDirector = pod.GetComponent<IntroLifepodDirector>();
            introLifepodDirector.OnProtoDeserializeObjectTree(null);
            introLifepodDirector.ToggleActiveObjects(false);
            pod.lightingController.SnapToState(2);
        }

        if (metadata.RadioRepaired)
        {
            radio.liveMixin.health = radio.liveMixin.maxHealth;
            if (radio.liveMixin.loopingDamageEffectObj)
            {
                Object.Destroy(radio.liveMixin.loopingDamageEffectObj);
            }
        }
        else
        {
            pod.DamageRadio();
        }
    }

    /// <summary>
    /// A player who joins after the pod and the radio were repaired still plays their own intro, whose end (EscapePod.StopIntroCinematic) damages the radio and
    /// shows the damage effects again. Called when that intro is over: puts back the repaired state the server reported.
    /// </summary>
    public static void ReapplyAfterIntro()
    {
        if (latestMetadata == null || !(latestMetadata.PodRepaired || latestMetadata.RadioRepaired) || !EscapePod.main)
        {
            return;
        }

        EscapePod pod = EscapePod.main;
        if (!pod.radioSpawner || !pod.radioSpawner.spawnedObj || !pod.radioSpawner.spawnedObj.TryGetComponent(out Radio radio))
        {
            return;
        }

        Log.Info($"[{nameof(EscapePodMetadataProcessor)}] The intro is over, restoring the repaired state of the escape pod (pod {latestMetadata.PodRepaired}, radio {latestMetadata.RadioRepaired})");
        using FMODSoundSuppressor soundSuppressor = FMODSystem.SuppressSubnauticaSounds();
        using PacketSuppressor<EntityMetadataUpdate> packetSuppressor = PacketSuppressor<EntityMetadataUpdate>.Suppress();
        ProcessInitialSyncMetadata(pod, radio, latestMetadata);
    }
}
