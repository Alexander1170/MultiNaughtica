using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The server's books of the buddy oxygen (<see cref="BuddyOxygenTransfer" />, docs/coop/BUDDY_OXYGEN.md): which steps of air were passed on to which receiver and are waiting for the answer, and how fast each
///     giver has been taking air. It holds no air and decides nothing about divers; it makes sure that the air a giver took out of their tank is accounted for exactly once: the receiver answers with what it took
///     (<see cref="TryClose" />) and the rest goes back to the giver, or, when the receiver leaves or never answers, all of it does (<see cref="SessionEnded" />, <see cref="TakeStale" />).
/// </summary>
/// <remarks>
///     Two givers on one receiver are allowed: each has steps of their own, and the receiver's tank caps the total (what does not fit goes back to the giver whose step did not fit). A step is answered by the
///     receiver only: the answer of anybody else, a second answer, and an answer to a step that was dropped are ignored. A step that nobody answers for <see cref="BuddyOxygenRules.STALE_SECONDS" /> is given back
///     to the giver; the receiver that answers after that gets nothing counted (it has the air and the giver has it back too: once, for a step of a quarter of a second, and only for a client that was stalled
///     for half a minute).
/// </remarks>
internal sealed class BuddyOxygenLedger : ISessionCleaner
{
    internal readonly record struct Entry(long TransferId, SessionId Giver, SessionId Receiver, string ReceiverName, float Amount, double OpenedAt);

    internal enum OpenResult
    {
        Opened,

        /// <summary>The giver has as many steps waiting as the server allows.</summary>
        Busy,

        /// <summary>The giver is taking air faster than the rate allows.</summary>
        TooFast
    }

    private readonly Lock gate = new();
    private readonly IPacketSender? packetSender;
    private readonly Func<double> clock;
    private readonly Dictionary<long, Entry> open = [];
    private readonly GiveBudget<SessionId> budget = new();
    private long lastTransferId;

    public BuddyOxygenLedger(IPacketSender packetSender) : this(packetSender, null)
    {
    }

    internal BuddyOxygenLedger(IPacketSender? packetSender, Func<double>? clock)
    {
        this.packetSender = packetSender;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    /// <summary>The number of steps that wait for an answer.</summary>
    public int OpenCount
    {
        get
        {
            lock (gate)
            {
                return open.Count;
            }
        }
    }

    public int OpenCountFor(SessionId giver)
    {
        lock (gate)
        {
            return open.Values.Count(entry => entry.Giver == giver);
        }
    }

    /// <summary>
    ///     Records a step of <paramref name="amount" /> from <paramref name="giver" /> to <paramref name="receiver" /> that is about to be passed on, unless the giver has too many waiting or takes air faster than
    ///     <paramref name="ratePerSecond" /> allows (the step is then not recorded and must be given back).
    /// </summary>
    public OpenResult TryOpen(SessionId giver, SessionId receiver, string receiverName, float amount, float ratePerSecond, out Entry entry)
    {
        lock (gate)
        {
            entry = default;
            if (open.Values.Count(waiting => waiting.Giver == giver) >= BuddyOxygenRules.MAX_OPEN_PER_GIVER)
            {
                return OpenResult.Busy;
            }
            double now = clock();
            if (!budget.TryTake(giver, amount, now, ratePerSecond))
            {
                return OpenResult.TooFast;
            }
            entry = new Entry(++lastTransferId, giver, receiver, receiverName, amount, now);
            open[entry.TransferId] = entry;
            return OpenResult.Opened;
        }
    }

    /// <summary>
    ///     Closes the step that <paramref name="answerer" /> answers: only the receiver the step was passed to may, once. <paramref name="accepted" /> comes out sanitised (never negative, never NaN, never more than
    ///     the step carried). False (and nothing changes) for an unknown step, another player's answer or a second answer.
    /// </summary>
    public bool TryClose(long transferId, SessionId answerer, float accepted, out Entry entry, out float acceptedSanitised)
    {
        lock (gate)
        {
            acceptedSanitised = 0f;
            if (!open.TryGetValue(transferId, out entry) || entry.Receiver != answerer)
            {
                entry = default;
                return false;
            }
            open.Remove(transferId);
            acceptedSanitised = BuddyOxygenRules.SanitizeAccepted(accepted, entry.Amount);
            return true;
        }
    }

    /// <summary>Takes the steps that nobody has answered for too long out of the books (the caller gives them back to their givers).</summary>
    public IReadOnlyList<Entry> TakeStale()
    {
        lock (gate)
        {
            double now = clock();
            List<Entry> stale = open.Values.Where(entry => now - entry.OpenedAt > BuddyOxygenRules.STALE_SECONDS).ToList();
            foreach (Entry entry in stale)
            {
                open.Remove(entry.TransferId);
            }
            return stale;
        }
    }

    /// <summary>
    ///     A session ended. The steps that waited for that player's answer are dropped and returned (so that their givers get the air back); the steps that player gave are dropped (nobody is left to give back to).
    /// </summary>
    public IReadOnlyList<Entry> SessionEnded(SessionId session)
    {
        lock (gate)
        {
            List<Entry> toRefund = open.Values.Where(entry => entry.Receiver == session).ToList();
            foreach (Entry entry in toRefund)
            {
                open.Remove(entry.TransferId);
            }
            foreach (long key in open.Where(pair => pair.Value.Giver == session).Select(pair => pair.Key).ToList())
            {
                open.Remove(key);
            }
            budget.Forget(session);
            return toRefund;
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        // The givers of the steps that waited for the player who left get their air back at once
        foreach (Entry entry in SessionEnded(args.Session.Id))
        {
            if (packetSender != null)
            {
                await packetSender.SendPacketAsync(BuddyOxygenTransfer.Settle(entry.TransferId, entry.Giver, entry.Receiver, entry.ReceiverName, 0f, entry.Amount, BuddyOxygenReason.NoReceiver), entry.Giver);
            }
        }
    }
}
