using System.Diagnostics;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The custom player model of a player (<see cref="PlayerModelChoice" />, docs/coop/PLAYER_MODELS.md). The session in the packet is not believed: the sender is the connection, so a client cannot choose
///     for another player; the id is cleaned (anything that is not a model name is the vanilla body) and the length goes with it; a player gets one choice every
///     <see cref="PlayerModelRules.SERVER_INTERVAL_SECONDS" /> seconds at most (a client keeps a little more, so an honest one is never turned down). What is accepted is remembered (the latest of every
///     session, <see cref="PlayerModelLedger" />, in memory only) and goes to everybody but the sender, who changed his own Options menu.
/// </summary>
internal sealed class PlayerModelChoiceProcessor(PlayerModelLedger ledger, ILogger<PlayerModelChoiceProcessor> logger) : IAuthPacketProcessor<PlayerModelChoice>
{
    private readonly PlayerModelLedger ledger = ledger;
    private readonly ILogger<PlayerModelChoiceProcessor> logger = logger;
    private readonly PerSenderRateLimiter<SessionId> limiter = new(PlayerModelRules.SERVER_INTERVAL_SECONDS);

    /// <summary>The clock of the rate limit, in seconds from any steady source; a test sets its own.</summary>
    internal Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public async Task Process(AuthProcessorContext context, PlayerModelChoice packet)
    {
        Player sender = context.Sender;
        if (!sender.IsOnline)
        {
            return;
        }
        if (!limiter.TryAccept(sender.SessionId, Clock()))
        {
            logger.ZLogDebug($"{sender.Name} chose a player model again within {PlayerModelRules.SERVER_INTERVAL_SECONDS} seconds; dropped");
            return;
        }

        string modelId = PlayerModelRules.SanitizeId(packet.ModelId);
        if (!string.IsNullOrEmpty(packet.ModelId) && modelId.Length == 0)
        {
            logger.ZLogWarning($"{sender.Name} sent a player model id that is not a model name; it is taken as the vanilla body");
        }
        long fileBytes = PlayerModelRules.SanitizeBytes(modelId, packet.FileBytes);

        // The sender is the connection's, whatever the packet says
        ledger.Set(sender.SessionId, modelId, fileBytes);
        await context.SendToOthersAsync(new PlayerModelChoice(sender.SessionId, modelId, fileBytes));
    }
}
