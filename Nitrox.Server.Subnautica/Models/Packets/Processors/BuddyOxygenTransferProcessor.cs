using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The buddy oxygen on the server (<see cref="BuddyOxygenTransfer" />, docs/coop/BUDDY_OXYGEN.md): a diver who gives air to a teammate sends a step; the server checks that the sharing is on, that the receiver
///     is another player who is online, that the two are close enough (<c>BuddyOxygenRange</c> and a little slack, by the positions the server knows) and that the giver does not take air faster than
///     <c>BuddyOxygenRate</c> allows, then passes the step to the receiver, whose answer says how much fitted. The giver is told how the step ended and gets back what was not taken. A step that is turned down
///     is answered with all of its air given back, so that air is never lost to a refusal. The giver is the connection's, never the packet's; only the receiver's answer closes a step; the server's own steps
///     (deliver, settle) are dropped when a client sends them.
/// </summary>
internal sealed class BuddyOxygenTransferProcessor(
    BuddyOxygenLedger ledger,
    PlayerManager playerManager,
    IOptions<SubnauticaServerOptions> options,
    ILogger<BuddyOxygenTransferProcessor> logger) : IAuthPacketProcessor<BuddyOxygenTransfer>
{
    private readonly BuddyOxygenLedger ledger = ledger;
    private readonly PlayerManager playerManager = playerManager;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<BuddyOxygenTransferProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, BuddyOxygenTransfer packet)
    {
        switch (packet.Kind)
        {
            case BuddyOxygenKind.Give:
                await OnGive(context, packet);
                break;
            case BuddyOxygenKind.Answer:
                await OnAnswer(context, packet);
                break;
            default:
                // Deliver and settle are the server's own: a client that sends one would be making air for itself or for somebody else
                logger.ZLogWarning($"{context.Sender.Name} sent a buddy oxygen message of the kind {packet.Kind}, which only the server may send; dropped");
                break;
        }
    }

    private async Task OnGive(AuthProcessorContext context, BuddyOxygenTransfer give)
    {
        Player giver = context.Sender;
        if (!giver.IsOnline)
        {
            return;
        }
        if (!BuddyOxygenRules.IsValidAmount(give.Amount))
        {
            logger.ZLogWarning($"{giver.Name} sent a step of air that cannot be one; dropped");
            return;
        }

        await GiveBackStale(context);

        SubnauticaServerOptions current = options.Value;
        float rate = BuddyOxygenRules.ClampRate(current.BuddyOxygenRate);
        float range = BuddyOxygenRules.ClampRange(current.BuddyOxygenRange);
        if (!BuddyOxygenRules.IsEnabled(rate))
        {
            await Refuse(context, give, string.Empty, BuddyOxygenReason.Off);
            return;
        }
        if (!playerManager.TryGetPlayerBySessionId(give.Receiver, out Player? receiver) || !receiver.IsOnline || receiver.SessionId == giver.SessionId)
        {
            await Refuse(context, give, string.Empty, BuddyOxygenReason.NoReceiver);
            return;
        }
        if (!BuddyOxygenRules.InRange(Distance(giver.Position, receiver.Position), range, BuddyOxygenRules.RANGE_SLACK))
        {
            await Refuse(context, give, receiver.Name, BuddyOxygenReason.OutOfRange);
            return;
        }

        switch (ledger.TryOpen(giver.SessionId, receiver.SessionId, receiver.Name, give.Amount, rate, out BuddyOxygenLedger.Entry entry))
        {
            case BuddyOxygenLedger.OpenResult.Busy:
                await Refuse(context, give, receiver.Name, BuddyOxygenReason.Busy);
                return;
            case BuddyOxygenLedger.OpenResult.TooFast:
                logger.ZLogDebug($"{giver.Name} takes air faster than {rate} per second; a step is given back");
                await Refuse(context, give, receiver.Name, BuddyOxygenReason.TooFast);
                return;
        }
        await context.SendAsync(BuddyOxygenTransfer.Deliver(entry.TransferId, giver.SessionId, receiver.SessionId, giver.Name, entry.Amount), receiver.SessionId);
    }

    private async Task OnAnswer(AuthProcessorContext context, BuddyOxygenTransfer answer)
    {
        Player receiver = context.Sender;
        if (!ledger.TryClose(answer.TransferId, receiver.SessionId, answer.Amount, out BuddyOxygenLedger.Entry entry, out float accepted))
        {
            logger.ZLogDebug($"{receiver.Name} answered a step of air that is not theirs to answer or is closed already; ignored");
            return;
        }

        float refund = BuddyOxygenRules.Refund(entry.Amount, accepted);
        BuddyOxygenReason reason = Enum.IsDefined(answer.Reason) ? answer.Reason : BuddyOxygenReason.None;
        if (refund > 0f && reason == BuddyOxygenReason.None)
        {
            // The receiver took less than it was given and says nothing more: its tank was full
            reason = BuddyOxygenReason.Full;
        }
        if (playerManager.TryGetPlayerBySessionId(entry.Giver, out Player? giver) && giver.IsOnline)
        {
            await context.SendAsync(BuddyOxygenTransfer.Settle(entry.TransferId, entry.Giver, entry.Receiver, receiver.Name, accepted, refund, reason), entry.Giver);
        }
    }

    /// <summary>The air of a step that nobody answered for too long goes back to its giver.</summary>
    private async Task GiveBackStale(AuthProcessorContext context)
    {
        IReadOnlyList<BuddyOxygenLedger.Entry> stale = ledger.TakeStale();
        foreach (BuddyOxygenLedger.Entry entry in stale)
        {
            if (playerManager.TryGetPlayerBySessionId(entry.Giver, out Player? giver) && giver.IsOnline)
            {
                logger.ZLogDebug($"A step of air from {giver.Name} to {entry.ReceiverName} was never answered; given back");
                await context.SendAsync(BuddyOxygenTransfer.Settle(entry.TransferId, entry.Giver, entry.Receiver, entry.ReceiverName, 0f, entry.Amount, BuddyOxygenReason.NoReceiver), entry.Giver);
            }
        }
    }

    private static Task Refuse(AuthProcessorContext context, BuddyOxygenTransfer give, string receiverName, BuddyOxygenReason reason)
    {
        return context.ReplyAsync(BuddyOxygenTransfer.Settle(0, context.Sender.SessionId, give.Receiver, receiverName, 0f, give.Amount, reason));
    }

    private static float Distance(NitroxVector3 a, NitroxVector3 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
