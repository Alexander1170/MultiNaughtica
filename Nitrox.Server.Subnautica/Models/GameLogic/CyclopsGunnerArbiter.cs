using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The gunner seats of the Cyclopses (feature group 3, station 2): which player sits at the gunner desk of which Cyclops. One gunner per Cyclops, the first request wins, and a player sits at one desk at a
///     time; the server tells everybody (<see cref="CyclopsGunnerSeat" />) and ends a seat when the gunner leaves the desk, leaves the Cyclops, dies or disconnects and when the Cyclops is destroyed. Kept in memory
///     only, a seat is never saved.
/// </summary>
/// <remarks>The seats are kept by session, by value (never by <see cref="Player" />, who may have reconnected under another session). Every method that ends a seat returns it, and the caller broadcasts it.</remarks>
internal sealed class CyclopsGunnerArbiter(IPacketSender packetSender) : ISessionCleaner
{
    internal readonly record struct Seat(NitroxId CyclopsId, SessionId Session, string Name);

    internal enum SitResult
    {
        Seated,
        AlreadySeated,
        Occupied,
        SeatedElsewhere,
        NotAPlayer
    }

    private readonly IPacketSender packetSender = packetSender;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, Seat> seatByCyclops = [];
    private readonly Dictionary<SessionId, NitroxId> cyclopsBySession = [];

    public int Count
    {
        get
        {
            lock (gate)
            {
                return seatByCyclops.Count;
            }
        }
    }

    /// <summary>
    ///     The first request for a free desk wins. The same player asking again for the desk they have is fine (<see cref="SitResult.AlreadySeated" />); a desk with another gunner is
    ///     <see cref="SitResult.Occupied" /> (<paramref name="holder" /> is the gunner); a player who sits at the desk of another Cyclops is <see cref="SitResult.SeatedElsewhere" />.
    /// </summary>
    public SitResult TrySit(NitroxId cyclopsId, SessionId session, string name, out Seat holder)
    {
        lock (gate)
        {
            holder = default;
            if (!session.IsPlayer)
            {
                return SitResult.NotAPlayer;
            }
            if (seatByCyclops.TryGetValue(cyclopsId, out Seat seated))
            {
                holder = seated;
                return seated.Session == session ? SitResult.AlreadySeated : SitResult.Occupied;
            }
            if (cyclopsBySession.ContainsKey(session))
            {
                return SitResult.SeatedElsewhere;
            }
            holder = new Seat(cyclopsId, session, name ?? string.Empty);
            seatByCyclops[cyclopsId] = holder;
            cyclopsBySession[session] = cyclopsId;
            return SitResult.Seated;
        }
    }

    /// <summary>The gunner leaves the desk. Only the gunner of that Cyclops can.</summary>
    public bool Leave(NitroxId cyclopsId, SessionId session)
    {
        lock (gate)
        {
            if (seatByCyclops.TryGetValue(cyclopsId, out Seat seat) && seat.Session == session)
            {
                seatByCyclops.Remove(cyclopsId);
                cyclopsBySession.Remove(session);
                return true;
            }
            return false;
        }
    }

    /// <summary>Whether this session is the gunner of that Cyclops.</summary>
    public bool IsGunner(SessionId session, NitroxId cyclopsId)
    {
        lock (gate)
        {
            return cyclopsBySession.TryGetValue(session, out NitroxId seated) && seated.Equals(cyclopsId);
        }
    }

    public bool TryGetGunner(NitroxId cyclopsId, out Seat seat)
    {
        lock (gate)
        {
            return seatByCyclops.TryGetValue(cyclopsId, out seat);
        }
    }

    public IReadOnlyList<Seat> Snapshot()
    {
        lock (gate)
        {
            return seatByCyclops.Values.ToList();
        }
    }

    /// <summary>This player is done as a gunner, whatever the reason (disconnected, died): the seat, if they had one, is over.</summary>
    public IReadOnlyList<Seat> ReleaseSession(SessionId session)
    {
        lock (gate)
        {
            if (!cyclopsBySession.TryGetValue(session, out NitroxId cyclopsId) || !seatByCyclops.TryGetValue(cyclopsId, out Seat seat))
            {
                return [];
            }
            seatByCyclops.Remove(cyclopsId);
            cyclopsBySession.Remove(session);
            return [seat];
        }
    }

    /// <summary>The Cyclops was destroyed (it is a wreck, or gone): its seat is over.</summary>
    public IReadOnlyList<Seat> CyclopsGone(NitroxId cyclopsId)
    {
        lock (gate)
        {
            if (!seatByCyclops.TryGetValue(cyclopsId, out Seat seat))
            {
                return [];
            }
            seatByCyclops.Remove(cyclopsId);
            cyclopsBySession.Remove(seat.Session);
            return [seat];
        }
    }

    /// <summary>The player changed the sub root they are in: a gunner who is no longer in their Cyclops does not sit at its desk any more.</summary>
    public IReadOnlyList<Seat> PlayerMoved(SessionId session, Optional<NitroxId> nowIn)
    {
        lock (gate)
        {
            if (!cyclopsBySession.TryGetValue(session, out NitroxId cyclopsId))
            {
                return [];
            }
            if (nowIn.HasValue && nowIn.Value.Equals(cyclopsId))
            {
                return [];
            }
            if (!seatByCyclops.TryGetValue(cyclopsId, out Seat seat))
            {
                return [];
            }
            seatByCyclops.Remove(cyclopsId);
            cyclopsBySession.Remove(session);
            return [seat];
        }
    }

    /// <summary>Tells everybody that these seats are over (everybody but <paramref name="except" /> when it is given, for a player who is gone).</summary>
    public async Task BroadcastEndedAsync(IReadOnlyList<Seat> ended, CyclopsGunnerSeatReason reason, SessionId? except = null)
    {
        foreach (Seat seat in ended)
        {
            CyclopsGunnerSeat packet = new(CyclopsGunnerSeatMessage.Released, seat.CyclopsId, seat.Session, seat.Name, reason);
            if (except.HasValue)
            {
                await packetSender.SendPacketToOthersAsync(packet, except.Value);
            }
            else
            {
                await packetSender.SendPacketToAllAsync(packet);
            }
        }
    }

    /// <summary>Tells a client that joined about the desks that are taken.</summary>
    public async Task SendSnapshotAsync(SessionId session)
    {
        foreach (Seat seat in Snapshot())
        {
            await packetSender.SendPacketAsync(new CyclopsGunnerSeat(CyclopsGunnerSeatMessage.Seated, seat.CyclopsId, seat.Session, seat.Name, CyclopsGunnerSeatReason.None), session);
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        await BroadcastEndedAsync(ReleaseSession(args.Session.Id), CyclopsGunnerSeatReason.Disconnected, args.Session.Id);
    }
}
