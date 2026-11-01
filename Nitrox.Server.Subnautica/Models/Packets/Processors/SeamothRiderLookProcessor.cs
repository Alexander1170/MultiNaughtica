using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Passes the look of a Seamoth's rider on to the other players, while the sender really rides that Seamoth (<see cref="SeamothRiderArbiter" />). A look from anybody else is dropped; without this
///     processor the default one would send it to everybody.
/// </summary>
internal sealed class SeamothRiderLookProcessor(SeamothRiderArbiter arbiter) : IAuthPacketProcessor<SeamothRiderLook>
{
    private readonly SeamothRiderArbiter arbiter = arbiter;

    public async Task Process(AuthProcessorContext context, SeamothRiderLook packet)
    {
        if (packet.VehicleId != null && IsRotation(packet.Look) && arbiter.Rides(context.Sender.SessionId, packet.VehicleId))
        {
            await context.SendToOthersAsync(new SeamothRiderLook(context.Sender.SessionId, packet.VehicleId, packet.Look, packet.Time));
        }
    }

    /// <summary>A look is a unit quaternion: NaN, infinity or a zero quaternion would reach every other client's rig (and its IK) as it is.</summary>
    internal static bool IsRotation(Nitrox.Model.DataStructures.Unity.NitroxQuaternion look)
    {
        float lengthSquared = look.X * look.X + look.Y * look.Y + look.Z * look.Z + look.W * look.W;
        return float.IsFinite(lengthSquared) && lengthSquared is >= 0.9f and <= 1.1f;
    }
}
