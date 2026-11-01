using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class SetIntroCinematicModeProcessor(PlayerManager playerManager, PlayerCinematics playerCinematics, LocalPlayer localPlayer) : IClientPacketProcessor<SetIntroCinematicMode>
{
    private readonly LocalPlayer localPlayer = localPlayer;
    private readonly PlayerCinematics playerCinematics = playerCinematics;
    private readonly PlayerManager playerManager = playerManager;

    public Task Process(ClientProcessorContext context, SetIntroCinematicMode packet)
    {
        if (localPlayer.SessionId == packet.SessionId)
        {
            if (localPlayer.IntroCinematicMode == IntroCinematicMode.COMPLETED && packet.Mode != IntroCinematicMode.COMPLETED)
            {
                // The intro of this player is over (he skipped it, or the server sent him past it) and the server paired him a moment before it heard so: nothing starts any more
                Log.Debug($"Ignoring intro mode {packet.Mode} from the server: the intro of this player is completed");
                return Task.CompletedTask;
            }

            if (packet.PartnerId.HasValue)
            {
                playerCinematics.IntroCinematicPartnerId = packet.PartnerId;
            }

            localPlayer.IntroCinematicMode = packet.Mode;
#if DEBUG
            Log.Info($"[SyncHarness] intro mode (from the server) {packet.Mode}, partner {packet.PartnerId?.ToString() ?? "none"}");
#endif
            return Task.CompletedTask;
        }

        if (playerManager.TryFind(packet.SessionId, out RemotePlayer remotePlayer))
        {
            remotePlayer.PlayerContext.IntroCinematicMode = packet.Mode;
            return Task.CompletedTask;
        }

        Log.Debug($"SetIntroCinematicMode couldn't find Player with id {packet.SessionId}. This is normal if player has not yet officially joined.");
        return Task.CompletedTask;
    }
}
