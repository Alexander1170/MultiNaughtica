using System.Diagnostics;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The quick ping (<see cref="PlayerPingCreated" />, docs/coop/QUICK_PING.md). A client's ping is not relayed as it came: the sender (session and name) is the one of the connection, never the packet's, so
///     that a client cannot ping as another player; the text is cleaned to what the HUD can show; a ping that is not a place in the world is dropped; and a player gets one ping every
///     <see cref="QuickPingRules.SERVER_INTERVAL_SECONDS" /> seconds at most (the client keeps two, so an honest client is never turned down). What is accepted goes to everybody but the sender, who has
///     its own marker already. Nothing is kept: pings are short-lived, and a player who joins later sees none.
/// </summary>
internal sealed class PlayerPingCreatedProcessor(ILogger<PlayerPingCreatedProcessor> logger) : IAuthPacketProcessor<PlayerPingCreated>
{
    private readonly ILogger<PlayerPingCreatedProcessor> logger = logger;
    private readonly PerSenderRateLimiter<SessionId> limiter = new(QuickPingRules.SERVER_INTERVAL_SECONDS);

    /// <summary>The clock of the rate limit, in seconds from any steady source; a test sets its own.</summary>
    internal Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public async Task Process(AuthProcessorContext context, PlayerPingCreated packet)
    {
        Player sender = context.Sender;
        if (!sender.IsOnline || packet.PingId == null)
        {
            return;
        }
        if (!QuickPingRules.IsValidPosition(packet.Position.X, packet.Position.Y, packet.Position.Z))
        {
            logger.ZLogWarning($"{sender.Name} sent a ping that is not a place in the world; dropped");
            return;
        }
        if (!limiter.TryAccept(sender.SessionId, Clock()))
        {
            logger.ZLogDebug($"{sender.Name} pinged again within {QuickPingRules.SERVER_INTERVAL_SECONDS} seconds; dropped");
            return;
        }

        // The sender is the connection's, whatever the packet says
        string target = QuickPingRules.TargetLabel(packet.Text, null);
        await context.SendToOthersAsync(new PlayerPingCreated(sender.SessionId, target, packet.Position, packet.PingId, sender.Name));
    }
}
