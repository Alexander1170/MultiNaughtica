using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// Another client's copy of a Reaper grabbed a vehicle: our copy grabs the same one, so that the Reaper holds it on every client (its position, the shaking, the damage over time
/// that only counts on the vehicle's owner) instead of on the client where it happened to touch it.
/// </summary>
internal sealed class ReaperGrabVehicleProcessor : IClientPacketProcessor<ReaperGrabVehicle>
{
    /// <summary>
    /// A copy that grabbed because another client said so does not let go on its own timer (the random 8 to 13 s of vanilla would differ on every client): the client that grabbed
    /// first says when. This is only the fallback for a message that never comes (that client left, was out of range).
    /// </summary>
    internal const float FALLBACK_RELEASE_SECONDS = 20f;

    public Task Process(ClientProcessorContext context, ReaperGrabVehicle packet)
    {
        if (!NitroxEntity.TryGetComponentFrom(packet.ReaperId, out ReaperLeviathan reaper) ||
            !NitroxEntity.TryGetComponentFrom(packet.VehicleId, out Vehicle vehicle))
        {
            return Task.CompletedTask;
        }

        if (reaper.IsHoldingVehicle())
        {
            // We grabbed on our own at the same moment (or the message is a repeat): it holds one already
            return Task.CompletedTask;
        }

        using (PacketSuppressor<ReaperGrabVehicle>.Suppress())
        {
            switch (vehicle)
            {
                case SeaMoth seamoth when !seamoth.docked:
                    reaper.GrabSeamoth(seamoth);
                    break;
                case Exosuit exosuit when !exosuit.docked:
                    reaper.GrabExosuit(exosuit);
                    break;
                default:
                    return Task.CompletedTask;
            }
        }

        reaper.CancelInvoke(nameof(ReaperLeviathan.ReleaseVehicle));
        reaper.Invoke(nameof(ReaperLeviathan.ReleaseVehicle), FALLBACK_RELEASE_SECONDS);
        Log.Debug($"[ReaperVehicleGrab] {reaper.name} grabbed {vehicle.name} because another client did");
        return Task.CompletedTask;
    }
}
