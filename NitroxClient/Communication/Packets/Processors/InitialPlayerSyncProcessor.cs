using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerModels;
using NitroxClient.GameLogic.InitialSync.Abstract;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class InitialPlayerSyncProcessor(IEnumerable<IInitialSyncProcessor> processors) : IClientPacketProcessor<InitialPlayerSync>
{
    private readonly HashSet<Type> alreadyRan = [];
    private readonly HashSet<IInitialSyncProcessor> processors = processors.ToSet();

    private int cumulativeProcessorsRan;

    private WaitScreen.ManualWaitItem loadingMultiplayerWaitItem;
    private InitialPlayerSync packet;
    private int processorsRanLastCycle;
    private WaitScreen.ManualWaitItem subWaitScreenItem;

    public Task Process(ClientProcessorContext context, InitialPlayerSync packet)
    {
        this.packet = packet;

        // An Aurora update that reached this client before the snapshot is older than the snapshot, one that arrives while the processors run is newer (StoryManager.PendingAuroraUpdate)
        StoryManager.AuroraSnapshotApplied = false;
        StoryManager.PendingAuroraUpdate.Clear();

        loadingMultiplayerWaitItem = WaitScreen.Add(Language.main.Get("Nitrox_SyncingWorld"));
        Log.InGame(Language.main.Get("Nitrox_SyncingWorld"));

        cumulativeProcessorsRan = 0;
        Multiplayer.Main.StartCoroutine(ProcessInitialSyncPacket(context));
        return Task.CompletedTask;
    }

    private IEnumerator ProcessInitialSyncPacket(ClientProcessorContext context)
    {
        bool moreProcessorsToRun;
        do
        {
            yield return Multiplayer.Main.StartCoroutine(RunPendingProcessors());

            moreProcessorsToRun = alreadyRan.Count < processors.Count;
            if (moreProcessorsToRun && processorsRanLastCycle == 0)
            {
                throw new Exception($"Detected circular dependencies in initial packet sync between: {GetRemainingProcessorsText()}");
            }
        } while (moreProcessorsToRun);

        WaitScreen.Remove(loadingMultiplayerWaitItem);
        Multiplayer.Main.InitialSyncCompleted = true;

        // When the player finishes loading, we can take back his invincibility
        Player.main.liveMixin.invincible = false;
        Player.main.UnfreezeStats();

        context.Send(new PlayerSyncFinished());

        // The others are told which custom player model this player wears (docs/coop/PLAYER_MODELS.md); the server tells him theirs
        PlayerModelAnnouncer.AnnounceAtJoin();
    }

    private IEnumerator RunPendingProcessors()
    {
        processorsRanLastCycle = 0;

        foreach (IInitialSyncProcessor processor in processors)
        {
            if (IsWaitingToRun(processor.GetType()) && HasDependenciesSatisfied(processor))
            {
                loadingMultiplayerWaitItem.SetProgress(cumulativeProcessorsRan, processors.Count);

                alreadyRan.Add(processor.GetType());
                processorsRanLastCycle++;
                cumulativeProcessorsRan++;

                Log.Info($"Running {processor.GetType()}");
                subWaitScreenItem = WaitScreen.Add($"Running {processor.GetType().Name}");
                yield return Multiplayer.Main.StartCoroutine(processor.Process(packet, subWaitScreenItem));
                WaitScreen.Remove(subWaitScreenItem);
            }
        }
    }

    private bool HasDependenciesSatisfied(IInitialSyncProcessor processor)
    {
        foreach (Type dependentType in processor.DependentProcessors)
        {
            if (IsWaitingToRun(dependentType))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsWaitingToRun(Type processor)
    {
        return !alreadyRan.Contains(processor);
    }

    private string GetRemainingProcessorsText()
    {
        string remaining = "";

        foreach (IInitialSyncProcessor processor in processors)
        {
            if (IsWaitingToRun(processor.GetType()))
            {
                remaining += $" {processor.GetType()}";
            }
        }

        return remaining;
    }
}
