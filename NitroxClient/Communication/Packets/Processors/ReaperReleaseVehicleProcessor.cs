using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class ReaperReleaseVehicleProcessor : IClientPacketProcessor<ReaperReleaseVehicle>
{
    public Task Process(ClientProcessorContext context, ReaperReleaseVehicle packet)
    {
        if (!NitroxEntity.TryGetComponentFrom(packet.ReaperId, out ReaperLeviathan reaper) || !reaper.IsHoldingVehicle())
        {
            return Task.CompletedTask;
        }

        using (PacketSuppressor<ReaperReleaseVehicle>.Suppress())
        {
            reaper.ReleaseVehicle();
        }
        Log.Debug($"[ReaperVehicleGrab] {reaper.name} let go because another client did");
        return Task.CompletedTask;
    }
}
