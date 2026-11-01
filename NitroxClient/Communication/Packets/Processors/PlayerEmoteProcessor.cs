using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerModels;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>An emote of a teammate, stamped by the server (docs/coop/PLAYER_MODELS.md): played on the custom model that he wears, or stopped.</summary>
internal sealed class PlayerEmoteProcessor(PlayerManager playerManager, LocalPlayer localPlayer) : IClientPacketProcessor<PlayerEmote>
{
    public Task Process(ClientProcessorContext context, PlayerEmote packet)
    {
        // The server never sends a player his own emote
        if (localPlayer.SessionId.HasValue && packet.SessionId == localPlayer.SessionId.Value)
        {
            return Task.CompletedTask;
        }

        PlayerEmotes.OnRemote(packet.SessionId, packet.EmoteId, packet.Playing, playerManager);

        return Task.CompletedTask;
    }
}
