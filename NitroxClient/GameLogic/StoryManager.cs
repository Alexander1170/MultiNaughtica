using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Story;
using UnityEngine;

namespace NitroxClient.GameLogic;

public static class StoryManager
{
    /// <summary>
    /// True once the Aurora state of the join snapshot is applied (<see cref="InitialSync.StoryGoalInitialSyncProcessor" />)
    /// </summary>
    public static bool AuroraSnapshotApplied { get; set; }

    /// <summary>
    /// An Aurora update (the /aurora commands, a time skip) that arrived while this client was still joining: it is newer than the join snapshot, which it must not be overwritten by
    /// (and the Aurora scene may not exist yet), so it is applied right after the snapshot
    /// </summary>
    public static readonly LatestPending<AuroraAndTimeUpdate> PendingAuroraUpdate = new();

    /// <summary>
    /// The Aurora part of an update (the clock part is the join snapshot's or <see cref="TimeManager.ProcessUpdate" />'s business)
    /// </summary>
    public static void ApplyAuroraUpdate(TimeManager timeManager, AuroraAndTimeUpdate packet)
    {
        UpdateAuroraData(packet.TimeData.AuroraEventData);
        timeManager.AuroraRealExplosionTime = packet.TimeData.AuroraEventData.AuroraRealExplosionTime;
        if (packet.Restore)
        {
            RestoreAurora();
        }
    }

    public static void ScanCompleted(NitroxId entityId, bool destroy)
    {
        PDAScanner.cachedProgress.Remove(entityId.ToString());
        if (NitroxEntity.TryGetObjectFrom(entityId, out GameObject scanObject))
        {
            // Copy the SendMessage from PDAScanner.Scan() but we don't care about the EntryData
            scanObject.SendMessage("OnScanned", null, SendMessageOptions.DontRequireReceiver);
            if (!destroy)
            {
                // Indexer: Add throws when the key exists (both players scanned the same fragment), which would abort the rest of the scan handling
                PDAScanner.fragments[entityId.ToString()] = 1f;
            }
            else
            {
                PDAScanner.fragments.Remove(entityId.ToString());
                GameObject.Destroy(scanObject);
            }
        }
    }

    public static void UpdateAuroraData(AuroraEventData auroraEventData)
    {
        CrashedShipExploder.main.timeToStartCountdown = auroraEventData.TimeToStartCountdown;
        CrashedShipExploder.main.timeToStartWarning = auroraEventData.TimeToStartWarning;
    }

    public static void RestoreAurora()
    {
        CleanAuroraEvents();
        // Same logic as in OnConsoleCommand_restoreship but without the time recalculation
        CrashedShipExploder.main.SwapModels(false);
        CrashedShipExploder.main.fxControl.StopAndDestroy(0, 0f);
        CrashedShipExploder.main.fxControl.StopAndDestroy(1, 0f);
    }

    /// <summary>
    /// Removes the aurora-related entries from StoryGoalManager's completedEvents and PDALog
    /// </summary>
    public static void CleanAuroraEvents()
    {
        foreach (string eventKey in AuroraEventData.GoalNames)
        {
            StoryGoalManager.main.completedGoals.Remove(eventKey);
            PDALog.Remove(eventKey);
        }
    }
}
