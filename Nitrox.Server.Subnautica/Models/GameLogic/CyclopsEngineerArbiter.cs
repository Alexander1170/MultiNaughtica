using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The engineer consoles of the Cyclopses (feature group 3, station 3): which player sits at the engineer console of which Cyclops, and what the engineer has set on it. One engineer per Cyclops, the first
///     request wins, and a player sits at one console at a time; the server tells everybody (<see cref="CyclopsEngineerSeat" />) and ends a seat when the engineer leaves the console, leaves the Cyclops, dies or
///     disconnects and when the Cyclops is destroyed. The route of the power and the cooldowns of the overdrive and the vent are kept here too, because the server is the only clock that every player can believe:
///     it decides the cooldowns and tells everybody the seconds that are left (<see cref="CyclopsEngineerState" />). Kept in memory only, nothing here is saved.
/// </summary>
/// <remarks>The seats are kept by session, by value (never by <see cref="Player" />, who may have reconnected under another session). Every method that ends a seat returns it, and the caller broadcasts it.</remarks>
internal sealed class CyclopsEngineerArbiter(IPacketSender packetSender) : ISessionCleaner
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

    /// <summary>What has been set on one Cyclops, in seconds of the server's clock (0: never).</summary>
    private sealed class Setting
    {
        public EngineerRoute Route;
        public double OverdriveUntil;
        public double OverdriveReadyAt;
        public double VentReadyAt;
    }

    private readonly IPacketSender packetSender = packetSender;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, Seat> seatByCyclops = [];
    private readonly Dictionary<SessionId, NitroxId> cyclopsBySession = [];
    private readonly Dictionary<NitroxId, Setting> settings = [];

    /// <summary>The server's clock in milliseconds, for the cooldowns; a test sets its own.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    private double NowSeconds => Clock() / 1000.0;

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

    // ---- the seats

    /// <summary>
    ///     The first request for a free console wins. The same player asking again for the console they have is fine (<see cref="SitResult.AlreadySeated" />); a console with another engineer is
    ///     <see cref="SitResult.Occupied" /> (<paramref name="holder" /> is the engineer); a player who sits at the console of another Cyclops is <see cref="SitResult.SeatedElsewhere" />.
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

    /// <summary>The engineer leaves the console. Only the engineer of that Cyclops can.</summary>
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

    /// <summary>Whether this session is the engineer of that Cyclops.</summary>
    public bool IsEngineer(SessionId session, NitroxId cyclopsId)
    {
        lock (gate)
        {
            return cyclopsBySession.TryGetValue(session, out NitroxId seated) && seated.Equals(cyclopsId);
        }
    }

    public bool TryGetEngineer(NitroxId cyclopsId, out Seat seat)
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

    /// <summary>This player is done as an engineer, whatever the reason (disconnected, died): the seat, if they had one, is over.</summary>
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

    /// <summary>The Cyclops was destroyed (it is a wreck, or gone): its seat is over and what was set on it is forgotten.</summary>
    public IReadOnlyList<Seat> CyclopsGone(NitroxId cyclopsId)
    {
        lock (gate)
        {
            settings.Remove(cyclopsId);
            if (!seatByCyclops.TryGetValue(cyclopsId, out Seat seat))
            {
                return [];
            }
            seatByCyclops.Remove(cyclopsId);
            cyclopsBySession.Remove(seat.Session);
            return [seat];
        }
    }

    /// <summary>The player changed the sub root they are in: an engineer who is no longer in their Cyclops does not sit at its console any more.</summary>
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

    // ---- what the engineer has set

    public EngineerRoute RouteOf(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return settings.TryGetValue(cyclopsId, out Setting setting) ? setting.Route : EngineerRoute.Balanced;
        }
    }

    /// <summary>Sends the power of the Cyclops to a route. Returns true when that changed something.</summary>
    public bool SetRoute(NitroxId cyclopsId, EngineerRoute route)
    {
        lock (gate)
        {
            Setting setting = SettingOf(cyclopsId);
            if (setting.Route == route)
            {
                return false;
            }
            setting.Route = route;
            return true;
        }
    }

    /// <summary>The server's clock, in seconds, at which the next overdrive of that Cyclops can start (0: any time), and the same for the vent.</summary>
    public (double Now, double OverdriveReadyAt, double VentReadyAt) Cooldowns(NitroxId cyclopsId)
    {
        lock (gate)
        {
            double now = NowSeconds;
            return settings.TryGetValue(cyclopsId, out Setting setting) ? (now, setting.OverdriveReadyAt, setting.VentReadyAt) : (now, 0d, 0d);
        }
    }

    /// <summary>
    ///     The overdrive was carried out by the simulator: it runs for <paramref name="seconds" />, and the next one waits for <paramref name="cooldown" /> after its end. The route is looked at again: when the
    ///     engineer sent the power elsewhere while the press was out, nothing is started and no cooldown is burned (<see cref="CyclopsEngineerFailure.WrongRoute" />, which the engineer is told).
    /// </summary>
    public CyclopsEngineerFailure CommitOverdrive(NitroxId cyclopsId, float seconds, float cooldown)
    {
        lock (gate)
        {
            double now = NowSeconds;
            Setting setting = SettingOf(cyclopsId);
            CyclopsEngineerFailure check = CyclopsEngineerRules.ServerCheckOverdrive(setting.Route, now, setting.OverdriveReadyAt);
            if (check != CyclopsEngineerFailure.None)
            {
                return check;
            }
            (double until, double readyAt) = CyclopsEngineerRules.StartOverdrive(now, seconds, cooldown);
            setting.OverdriveUntil = until;
            setting.OverdriveReadyAt = readyAt;
            return CyclopsEngineerFailure.None;
        }
    }

    /// <summary>The vent was carried out by the simulator: the next one waits for <paramref name="cooldown" />. A vent that arrives while the cooldown of another one runs is not started again (<see cref="CyclopsEngineerFailure.Cooldown" />).</summary>
    public CyclopsEngineerFailure CommitVent(NitroxId cyclopsId, float cooldown)
    {
        lock (gate)
        {
            double now = NowSeconds;
            Setting setting = SettingOf(cyclopsId);
            CyclopsEngineerFailure check = CyclopsEngineerRules.ServerCheckVent(now, setting.VentReadyAt);
            if (check != CyclopsEngineerFailure.None)
            {
                return check;
            }
            setting.VentReadyAt = CyclopsEngineerRules.VentReadyAt(now, cooldown);
            return CyclopsEngineerFailure.None;
        }
    }

    /// <summary>What everybody is told about this Cyclops: the route and the seconds that are left.</summary>
    public CyclopsEngineerState StateOf(NitroxId cyclopsId)
    {
        lock (gate)
        {
            double now = NowSeconds;
            if (!settings.TryGetValue(cyclopsId, out Setting setting))
            {
                return new CyclopsEngineerState(cyclopsId, (int)EngineerRoute.Balanced, 0f, 0f, 0f);
            }
            return new CyclopsEngineerState(cyclopsId, (int)setting.Route, CyclopsEngineerRules.Remaining(now, setting.OverdriveUntil), CyclopsEngineerRules.Remaining(now, setting.OverdriveReadyAt), CyclopsEngineerRules.Remaining(now, setting.VentReadyAt));
        }
    }

    private Setting SettingOf(NitroxId cyclopsId)
    {
        if (!settings.TryGetValue(cyclopsId, out Setting setting))
        {
            setting = new Setting();
            settings[cyclopsId] = setting;
        }
        return setting;
    }

    // ---- telling people

    /// <summary>Tells everybody that these seats are over (everybody but <paramref name="except" /> when it is given, for a player who is gone).</summary>
    public async Task BroadcastEndedAsync(IReadOnlyList<Seat> ended, CyclopsEngineerSeatReason reason, SessionId? except = null)
    {
        foreach (Seat seat in ended)
        {
            CyclopsEngineerSeat packet = new(CyclopsEngineerSeatMessage.Released, seat.CyclopsId, seat.Session, seat.Name, reason);
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

    /// <summary>Tells a client that joined about the consoles that are taken and about what is set on the Cyclopses (only what differs from a Cyclops with nothing set).</summary>
    public async Task SendSnapshotAsync(SessionId session)
    {
        foreach (Seat seat in Snapshot())
        {
            await packetSender.SendPacketAsync(new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Seated, seat.CyclopsId, seat.Session, seat.Name, CyclopsEngineerSeatReason.None), session);
        }
        List<CyclopsEngineerState> states = [];
        lock (gate)
        {
            double now = NowSeconds;
            foreach ((NitroxId id, Setting setting) in settings)
            {
                if (setting.Route != EngineerRoute.Balanced || setting.OverdriveReadyAt > now || setting.VentReadyAt > now)
                {
                    states.Add(new CyclopsEngineerState(id, (int)setting.Route, CyclopsEngineerRules.Remaining(now, setting.OverdriveUntil), CyclopsEngineerRules.Remaining(now, setting.OverdriveReadyAt), CyclopsEngineerRules.Remaining(now, setting.VentReadyAt)));
                }
            }
        }
        foreach (CyclopsEngineerState state in states)
        {
            await packetSender.SendPacketAsync(state, session);
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        await BroadcastEndedAsync(ReleaseSession(args.Session.Id), CyclopsEngineerSeatReason.Disconnected, args.Session.Id);
    }
}
