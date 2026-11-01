using System.Diagnostics;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     An emote of a player who wears a custom player model (<see cref="PlayerEmote" />, docs/coop/PLAYER_MODELS.md). The session in the packet is not believed: the sender is the connection, so a client cannot
///     make another player dance; the id is cleaned (anything that is not a clip name is dropped); a player may start one emote every <see cref="PlayerEmoteRules.SERVER_START_INTERVAL_SECONDS" /> seconds at
///     most and stop every <see cref="PlayerEmoteRules.SERVER_STOP_INTERVAL_SECONDS" /> (a client keeps a little more, so an honest one is never turned down). Both go to everybody but the sender. Nothing is
///     remembered: an emote lasts seconds, so a player who joins later does not see one that is already running.
/// </summary>
internal sealed class PlayerEmoteProcessor(ILogger<PlayerEmoteProcessor> logger) : IAuthPacketProcessor<PlayerEmote>
{
    private readonly ILogger<PlayerEmoteProcessor> logger = logger;
    private readonly PerSenderRateLimiter<SessionId> startLimiter = new(PlayerEmoteRules.SERVER_START_INTERVAL_SECONDS);
    private readonly PerSenderRateLimiter<SessionId> stopLimiter = new(PlayerEmoteRules.SERVER_STOP_INTERVAL_SECONDS);

    /// <summary>The clock of the rate limits, in seconds from any steady source; a test sets its own.</summary>
    internal Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public async Task Process(AuthProcessorContext context, PlayerEmote packet)
    {
        Player sender = context.Sender;
        if (!sender.IsOnline)
        {
            return;
        }

        if (packet.Playing)
        {
            string emoteId = PlayerEmoteRules.SanitizeId(packet.EmoteId);
            if (emoteId.Length == 0)
            {
                logger.ZLogWarning($"{sender.Name} sent an emote id that is not a clip name; dropped");
                return;
            }
            if (!startLimiter.TryAccept(sender.SessionId, Clock()))
            {
                logger.ZLogDebug($"{sender.Name} started an emote again within {PlayerEmoteRules.SERVER_START_INTERVAL_SECONDS} seconds; dropped");
                return;
            }
            // The sender is the connection's, whatever the packet says
            await context.SendToOthersAsync(new PlayerEmote(sender.SessionId, emoteId, true));
            return;
        }

        if (!stopLimiter.TryAccept(sender.SessionId, Clock()))
        {
            return;
        }
        await context.SendToOthersAsync(new PlayerEmote(sender.SessionId, string.Empty, false));
    }
}
