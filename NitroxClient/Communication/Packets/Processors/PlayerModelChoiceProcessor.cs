using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerModels;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>A teammate's custom player model, stamped by the server (docs/coop/PLAYER_MODELS.md): remembered, and put on his body when there is one and the file is the same.</summary>
internal sealed class PlayerModelChoiceProcessor(PlayerManager playerManager, LocalPlayer localPlayer) : IClientPacketProcessor<PlayerModelChoice>
{
    public Task Process(ClientProcessorContext context, PlayerModelChoice packet)
    {
        // The server never sends a player his own choice
        if (localPlayer.SessionId.HasValue && packet.SessionId == localPlayer.SessionId.Value)
        {
            return Task.CompletedTask;
        }

        PlayerModelChoices.Choose(packet.SessionId, packet.ModelId, packet.FileBytes, playerManager);

        return Task.CompletedTask;
    }
}
