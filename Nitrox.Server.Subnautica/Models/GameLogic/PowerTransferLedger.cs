using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The server's books of the power transfers between vehicles that different players simulate (<see cref="PowerTransfer" />): which request was passed on to which giver, so that only that
///     giver's grant is delivered, only once, and a grant that comes too late is given back instead of being delivered. It holds no energy and decides nothing about vehicles; it makes sure that
///     the energy a giver took out is delivered at most once or refunded, never both, never twice.
/// </summary>
/// <remarks>
///     A delivery is remembered too (for <see cref="PowerTransferRules.LATE_GRANT_MEMORY_SECONDS" />), with the session it was delivered to and the amount: that session may give back what it could not use, once, and
///     never more than was delivered (<see cref="TryReturn" />); a return for a number that was not delivered, from another session, a second one or a late one is ignored, so that a client cannot make energy by returning
///     what it never got.
///     An open transfer expires after <see cref="PowerTransferRules.LEDGER_EXPIRY_SECONDS" />; the expired ones are remembered for <see cref="PowerTransferRules.LATE_GRANT_MEMORY_SECONDS" /> so that
///     a late grant of the right session is recognised (and refunded once), while a grant for a number the ledger never gave out, or from another session, is ignored. The ledger leaves the entries
///     of a requester that left alone: the delivery goes to whoever simulates the receiver at that moment. The entries of a giver that left are dropped (its grant will never come).
/// </remarks>
internal sealed class PowerTransferLedger : ISessionCleaner
{
    internal readonly record struct Entry(long TransferId, SessionId Requester, SessionId GiverHolder, NitroxId GiverId, NitroxId ReceiverId, float Amount, PowerTransferMode Mode, double OpenedAt);

    /// <summary>A delivery that the server made: the session it went to, the amount, and when.</summary>
    internal readonly record struct Delivered(Entry Entry, SessionId To, float Amount, double At);

    internal enum CloseResult
    {
        /// <summary>No such transfer, or it was offered to another session: the grant is ignored.</summary>
        Unknown,

        /// <summary>The transfer was open: deliver the amount to whoever simulates the receiver now.</summary>
        Delivered,

        /// <summary>The transfer had expired: give the amount back to the giver.</summary>
        Late
    }

    private readonly Lock gate = new();
    private readonly IPacketSender? packetSender;
    private readonly Func<double> clock;
    private readonly Dictionary<long, Entry> open = [];
    private readonly Dictionary<long, Entry> expired = [];
    private readonly Dictionary<long, Delivered> delivered = [];
    private long lastTransferId;

    public PowerTransferLedger(IPacketSender packetSender) : this(packetSender, null)
    {
    }

    internal PowerTransferLedger(Func<double>? clock) : this(null, clock)
    {
    }

    internal PowerTransferLedger(IPacketSender? packetSender, Func<double>? clock)
    {
        this.packetSender = packetSender;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    /// <summary>The number of transfers that wait for a grant.</summary>
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

    /// <summary>The number of transfers a player's requests have open right now (the processor refuses more than <see cref="PowerTransferRules.MAX_OPEN_PER_REQUESTER" />).</summary>
    public int OpenCountFor(SessionId requester)
    {
        lock (gate)
        {
            Sweep(clock());
            return open.Values.Count(entry => entry.Requester == requester);
        }
    }

    /// <summary>The number of expired transfers that are still remembered.</summary>
    public int ExpiredCount
    {
        get
        {
            lock (gate)
            {
                return expired.Count;
            }
        }
    }

    /// <summary>The number of deliveries that are still remembered (their return has not come and has not expired).</summary>
    public int DeliveredCount
    {
        get
        {
            lock (gate)
            {
                return delivered.Count;
            }
        }
    }

    /// <summary>Records that <paramref name="amount" /> was asked of <paramref name="giverHolder" /> for <paramref name="requester" />'s receiver, and returns the entry (with the number to send).</summary>
    public Entry Open(SessionId requester, SessionId giverHolder, NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode)
    {
        lock (gate)
        {
            double now = clock();
            Sweep(now);
            Entry entry = new(++lastTransferId, requester, giverHolder, giverId, receiverId, amount, mode, now);
            open[entry.TransferId] = entry;
            return entry;
        }
    }

    /// <summary>
    ///     Closes the transfer a giver answered. <paramref name="amount" /> is the sanitised grant (never more than was offered, never negative); the entry is gone afterwards, so a second grant
    ///     for the same number is <see cref="CloseResult.Unknown" />.
    /// </summary>
    public CloseResult Close(long transferId, SessionId sender, float granted, out Entry entry, out float amount)
    {
        lock (gate)
        {
            Sweep(clock());
            if (open.TryGetValue(transferId, out entry))
            {
                if (entry.GiverHolder != sender)
                {
                    amount = 0f;
                    return CloseResult.Unknown;
                }
                open.Remove(transferId);
                amount = PowerTransferRules.SanitizeGrant(granted, entry.Amount);
                return CloseResult.Delivered;
            }
            if (expired.TryGetValue(transferId, out entry))
            {
                if (entry.GiverHolder != sender)
                {
                    amount = 0f;
                    return CloseResult.Unknown;
                }
                expired.Remove(transferId);
                amount = PowerTransferRules.SanitizeGrant(granted, entry.Amount);
                return CloseResult.Late;
            }
            amount = 0f;
            return CloseResult.Unknown;
        }
    }

    /// <summary>Records that the amount of a closed transfer was sent to <paramref name="to" />, the session that simulates the receiver: that session may return what it cannot use.</summary>
    public void RecordDelivery(Entry entry, SessionId to, float amount)
    {
        lock (gate)
        {
            double now = clock();
            Sweep(now);
            delivered[entry.TransferId] = new Delivered(entry, to, amount, now);
        }
    }

    /// <summary>
    ///     Takes the one return a delivery may have: only from the session it was delivered to, with an amount that makes sense, and never more than was delivered (what is more is cut off). The delivery is forgotten
    ///     afterwards, so a second return of the same number is refused. Returns false (and leaves everything as it was) when the return is not accepted; a garbled amount does not use up the delivery's return.
    /// </summary>
    public bool TryReturn(long transferId, SessionId sender, float returned, out Entry entry, out float amount)
    {
        lock (gate)
        {
            Sweep(clock());
            entry = default;
            amount = 0f;
            if (!PowerTransferRules.IsValidAmount(returned) || !delivered.TryGetValue(transferId, out Delivered record) || record.To != sender)
            {
                return false;
            }
            delivered.Remove(transferId);
            entry = record.Entry;
            amount = System.Math.Min(returned, record.Amount);
            return true;
        }
    }

    /// <summary>A session ended: the transfers that wait for that session's grant are dropped (and returned, so that the caller can tell their requesters).</summary>
    public IReadOnlyList<Entry> SessionEnded(SessionId sessionId)
    {
        lock (gate)
        {
            List<Entry> dropped = [];
            foreach (KeyValuePair<long, Entry> pair in open.Where(pair => pair.Value.GiverHolder == sessionId).ToList())
            {
                dropped.Add(pair.Value);
                open.Remove(pair.Key);
            }
            foreach (long key in expired.Where(pair => pair.Value.GiverHolder == sessionId).Select(pair => pair.Key).ToList())
            {
                expired.Remove(key);
            }
            // That session will not return anything any more
            foreach (long key in delivered.Where(pair => pair.Value.To == sessionId).Select(pair => pair.Key).ToList())
            {
                delivered.Remove(key);
            }
            return dropped;
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        // The requesters of the transfers that waited for the player who left are told at once, so that they ask again instead of waiting for their own timeout
        foreach (Entry entry in SessionEnded(args.Session.Id))
        {
            if (packetSender != null)
            {
                await packetSender.SendPacketAsync(PowerTransfer.Delivery(entry.TransferId, entry.GiverId, entry.ReceiverId, 0f, entry.Mode, PowerTransferOutcome.NoGiver), entry.Requester);
            }
        }
    }

    private void Sweep(double now)
    {
        foreach (long key in open.Where(pair => now - pair.Value.OpenedAt > PowerTransferRules.LEDGER_EXPIRY_SECONDS).Select(pair => pair.Key).ToList())
        {
            expired[key] = open[key];
            open.Remove(key);
        }
        foreach (long key in expired.Where(pair => now - pair.Value.OpenedAt > PowerTransferRules.LEDGER_EXPIRY_SECONDS + PowerTransferRules.LATE_GRANT_MEMORY_SECONDS).Select(pair => pair.Key).ToList())
        {
            expired.Remove(key);
        }
        foreach (long key in delivered.Where(pair => now - pair.Value.At > PowerTransferRules.LATE_GRANT_MEMORY_SECONDS).Select(pair => pair.Key).ToList())
        {
            delivered.Remove(key);
        }
    }
}
