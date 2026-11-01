using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerMovementProcessor(PlayerManager remotePlayerManager) : IClientPacketProcessor<PlayerMovement>
{
    private readonly PlayerManager remotePlayerManager = remotePlayerManager;

#if DEBUG
    private static int receivedCount;
    private static float countWindowStart;
#endif

    public Task Process(ClientProcessorContext context, PlayerMovement movement)
    {
#if DEBUG
        // Harness: how many movement packets of the other players arrive per second
        receivedCount++;
        float elapsed = UnityEngine.Time.realtimeSinceStartup - countWindowStart;
        if (elapsed >= 5f)
        {
            if (countWindowStart > 0f)
            {
                Log.Debug($"[PlayerMovement] {receivedCount / elapsed:F1} movement packets per second received");
            }
            countWindowStart = UnityEngine.Time.realtimeSinceStartup;
            receivedCount = 0;
        }
#endif
        if (remotePlayerManager.TryFind(movement.SessionId, out RemotePlayer remotePlayer))
        {
            remotePlayer.UpdatePosition(movement.Position.ToUnity(),
                                        movement.Velocity.ToUnity(),
                                        movement.BodyRotation.ToUnity(),
                                        movement.AimingRotation.ToUnity(),
                                        movement.Time);
        }
        return Task.CompletedTask;
    }
}
