using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

public class RadiationLeakEntitySpawner : SyncEntitySpawner<RadiationLeakEntity>
{
    // This constant is defined by Subnautica and should never be modified (same as for SubnauticaWorldModifier)
    private const int TOTAL_LEAKS = 11;
    private readonly TimeManager timeManager;
    private readonly List<float> registeredLeaksFixTime = new();

    private readonly EntityMetadataManager entityMetadataManager;

    public RadiationLeakEntitySpawner(TimeManager timeManager, EntityMetadataManager entityMetadataManager)
    {
        this.timeManager = timeManager;
        this.entityMetadataManager = entityMetadataManager;
        // The first player joins with a frozen clock: the radius computed from it while the world loaded is computed again when the clock runs (RadiationRadiusModel.ShouldRecalculateWhenClockRuns)
        timeManager.ClockReleased += RecalculateWhenClockRuns;
    }

    private void RecalculateWhenClockRuns()
    {
        CrashedShipExploder crashedShipExploder = CrashedShipExploder.main;
        LeakingRadiation leakingRadiation = LeakingRadiation.main;
        if (!crashedShipExploder || !leakingRadiation)
        {
            return;
        }

        if (RadiationRadiusModel.ShouldRecalculateWhenClockRuns(crashedShipExploder.IsExploded(), registeredLeaksFixTime.Count, TOTAL_LEAKS))
        {
            float before = leakingRadiation.currentRadius;
            RecalculateRadiationRadius(leakingRadiation);
            Log.Info($"[{nameof(RadiationLeakEntitySpawner)}] The clock runs: the radius of the radiation field is computed again, {before:F3} -> {leakingRadiation.currentRadius:F3} m");
        }
    }

    protected override IEnumerator SpawnAsync(RadiationLeakEntity entity, TaskResult<Optional<GameObject>> result)
    {
        SpawnSync(entity, result);
        yield break;
    }

    protected override bool SpawnSync(RadiationLeakEntity entity, TaskResult<Optional<GameObject>> result)
    {
        // This script is located under (Aurora Scene) //Aurora-Main/Aurora so it's a good starting point to search through the GameObjects
        CrashedShipExploder crashedShipExploder = CrashedShipExploder.main;
        LeakingRadiation leakingRadiation = LeakingRadiation.main;
        if (!crashedShipExploder || !leakingRadiation || entity.Metadata is not RadiationMetadata metadata)
        {
            return true;
        }
        Transform radiationLeaksHolder = crashedShipExploder.transform.Find("radiationleaks").GetChild(0);
        RadiationLeak radiationLeak = radiationLeaksHolder.GetChild(entity.ObjectIndex).GetComponent<RadiationLeak>();
        NitroxEntity.SetNewId(radiationLeak.gameObject, entity.Id);
        radiationLeak.liveMixin.health = metadata.Health;
        registeredLeaksFixTime.Add(metadata.FixRealTime);
        // An update that reached this client between its snapshot and the binding of the leak was kept for it (nothing else applies metadata of this spawner: it sets no result)
        entityMetadataManager.ApplyMetadata(radiationLeak.gameObject, metadata);

        // We can only calculate the radiation increment and dissipation once we got all radiation leaks info
        if (crashedShipExploder.IsExploded() && registeredLeaksFixTime.Count == TOTAL_LEAKS)
        {
            RecalculateRadiationRadius(leakingRadiation);
        }

        return true;
    }

    public void RecalculateRadiationRadius(LeakingRadiation leakingRadiation)
    {
        // The maths is RadiationRadiusModel (unit-tested): the radius grows from the explosion while any leak leaks and shrinks once all are fixed
        RadiationRadiusModel.Result result = RadiationRadiusModel.Compute((float)timeManager.RealTimeElapsed, timeManager.AuroraRealExplosionTime, registeredLeaksFixTime, TOTAL_LEAKS,
                                                                          leakingRadiation.kStartRadius, leakingRadiation.kGrowRate, leakingRadiation.kNaturalDissipation, leakingRadiation.kMaxRadius,
                                                                          leakingRadiation.currentRadius);
        if (result.AllFixed)
        {
            leakingRadiation.radiationFixed = true;
        }

        if (result.ApplyRadius)
        {
            leakingRadiation.currentRadius = result.Radius;
            leakingRadiation.damagePlayerInRadius.damageRadius = result.Radius;
            leakingRadiation.radiatePlayerInRange.radiateRadius = result.Radius;
        }
    }

    protected override bool SpawnsOwnChildren(RadiationLeakEntity entity) => false;
}
