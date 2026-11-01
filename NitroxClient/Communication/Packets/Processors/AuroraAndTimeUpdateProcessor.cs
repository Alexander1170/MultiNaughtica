using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class AuroraAndTimeUpdateProcessor(TimeManager timeManager) : IClientPacketProcessor<AuroraAndTimeUpdate>
{
    private readonly TimeManager timeManager = timeManager;

    public Task Process(ClientProcessorContext context, AuroraAndTimeUpdate packet)
    {
        // While this client is joining, the Aurora scene may not exist yet and the join snapshot (older than this update) is applied later and would overwrite it:
        // the update waits for the snapshot (StoryGoalInitialSyncProcessor) and is applied right after it
        if (!StoryManager.AuroraSnapshotApplied || !CrashedShipExploder.main)
        {
            StoryManager.PendingAuroraUpdate.Set(packet);
            return Task.CompletedTask;
        }

        timeManager.ProcessUpdate(packet.TimeData.TimePacket);
        StoryManager.ApplyAuroraUpdate(timeManager, packet);
        return Task.CompletedTask;
    }
}
