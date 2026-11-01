using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness: starts or stops the <see cref="SyncSampler" /> on request of the server (/sync-start, /sync-stop).
///     The sampler component only exists in Debug builds, so this is a no-op in Release.
/// </summary>
internal sealed class DebugSyncSamplerControlProcessor : IClientPacketProcessor<DebugSyncSamplerControl>
{
    public Task Process(ClientProcessorContext context, DebugSyncSamplerControl packet)
    {
#if DEBUG
        SyncSampler sampler = SyncSampler.Instance;
        if (!sampler)
        {
            Log.Warn("[SyncSampler] control packet received but no sampler is attached");
            return Task.CompletedTask;
        }
        if (packet.Enabled)
        {
            sampler.StartSampling(packet.Label, packet.CreatureRadius);
        }
        else
        {
            sampler.StopSampling();
        }
#endif
        return Task.CompletedTask;
    }
}
