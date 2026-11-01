using System.Collections;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Story;
using UnityEngine;
using UWE;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class StoryGoalExecutedClientProcessor(IPacketSender packetSender) : IClientPacketProcessor<StoryGoalExecuted>
{
    private readonly IPacketSender packetSender = packetSender;

    public Task Process(ClientProcessorContext context, StoryGoalExecuted packet)
    {
        // A client that is still joining gets the packet while its world scene loads (StoryGoalScheduler.main does not exist yet): wait for the end of the join instead of throwing
        if (StoryPacketRules.ShouldWait(IsInitialSyncCompleted(), StorySystemsLoaded()))
        {
            CoroutineHost.StartCoroutine(ApplyWhenLoaded(packet, StoryPacketSession.Generation));
            return Task.CompletedTask;
        }

        Apply(packet);
        return Task.CompletedTask;
    }

    private static bool IsInitialSyncCompleted() => Multiplayer.Main != null && Multiplayer.Main.InitialSyncCompleted;

    private static bool StorySystemsLoaded() => StoryGoalScheduler.main != null && StoryGoalManager.main != null;

    private static IEnumerator ApplyWhenLoaded(StoryGoalExecuted packet, int generation)
    {
        float waited = 0f;
        while (StoryPacketRules.ShouldWait(IsInitialSyncCompleted(), StorySystemsLoaded()))
        {
            if (StoryPacketRules.SessionEnded(generation, StoryPacketSession.Generation))
            {
                Log.Debug($"[{nameof(StoryGoalExecutedClientProcessor)}] The goal {packet.Key} that waited for the end of the join is dropped: its session ended");
                yield break;
            }

            if (StoryPacketRules.GiveUp(waited))
            {
                Log.Warn($"[{nameof(StoryGoalExecutedClientProcessor)}] The goal {packet.Key} was not applied: the client did not finish joining within {StoryPacketRules.WAIT_SECONDS:F0} s");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.25f);
            waited += 0.25f;
        }

        Log.Debug($"[{nameof(StoryGoalExecutedClientProcessor)}] Applying the goal {packet.Key} that arrived while this client was joining");
        Apply(packet);
    }

    private static void Apply(StoryGoalExecuted packet)
    {
        StoryGoalScheduler.main.schedule.RemoveAllFast(packet.Key, static (goal, packetGoalKey) => goal.goalKey == packetGoalKey);

        using (PacketSuppressor<StoryGoalExecuted>.Suppress())
        using (PacketSuppressor<PDALogEntryAdd>.Suppress())
        using (PacketSuppressor<KnownTechEntryAdd>.Suppress()) // StoryGoalManager => OnGoalUnlockTracker => UnlockBlueprintData => KnownTech.Add
        using (PacketSuppressor<PDAEncyclopediaEntryAdd>.Suppress())
        {
            StoryGoal.Execute(packet.Key, packet.Type.ToUnity());
            TeleporterSync.ApplyRemoteGoal(packet.Key);
        }
    }
}
