using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>A teammate's quick ping, stamped by the server (docs/coop/QUICK_PING.md): it appears on the HUD and replaces that player's earlier one.</summary>
internal sealed class PlayerPingCreatedProcessor(LocalPlayer localPlayer) : IClientPacketProcessor<PlayerPingCreated>
{
    public Task Process(ClientProcessorContext context, PlayerPingCreated packet)
    {
        // The server never sends a player their own ping; a stamped one with our own session would only be a copy of the marker that we made ourselves
        if (localPlayer.SessionId.HasValue && packet.SessionId == localPlayer.SessionId.Value)
        {
            return Task.CompletedTask;
        }

        PlayerPingManager.CreateRemotePing(packet);

        return Task.CompletedTask;
    }
}
