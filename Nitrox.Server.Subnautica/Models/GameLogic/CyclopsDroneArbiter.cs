using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The drone bays of the Cyclopses (feature group 3, station 4): which player flies the scout drone of which Cyclops, and which drone each bay holds and whether it is in the dock. One operator per Cyclops, the first
///     request wins, and a player sits at one drone console at a time; the server tells everybody (<see cref="CyclopsDroneSeat" />) and ends a seat when the operator leaves the console, leaves the Cyclops, dies,
///     disconnects, when the Cyclops is destroyed and when the drone is. The bay (the drone it holds and whether that one is docked) is kept here too and told to everybody with
///     <see cref="CyclopsDroneState" />, because the dock is on the hull and every client has to put the drone there the same way. Kept in memory only, nothing here is saved.
/// </summary>
/// <remarks>The seats are kept by session, by value (never by <see cref="Player" />, who may have reconnected under another session). Every method that ends a seat returns it, and the caller broadcasts it.</remarks>
internal sealed class CyclopsDroneArbiter(IPacketSender packetSender) : ISessionCleaner
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

    /// <summary>What a bay holds.</summary>
    private sealed class Bay
    {
        public NitroxId DroneId;
        public bool Docked;
    }

    private readonly IPacketSender packetSender = packetSender;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, Seat> seatByCyclops = [];
    private readonly Dictionary<SessionId, NitroxId> cyclopsBySession = [];
    private readonly Dictionary<NitroxId, Bay> bays = [];
    private readonly Dictionary<NitroxId, long> lastZap = [];
    private readonly Lock restoreGate = new();
    private bool restored;

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
    ///     The first request for a free console wins. The same player asking again for the console they have is fine (<see cref="SitResult.AlreadySeated" />); a console with another operator is
    ///     <see cref="SitResult.Occupied" /> (<paramref name="holder" /> is the operator); a player who sits at the console of another Cyclops is <see cref="SitResult.SeatedElsewhere" />.
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

    /// <summary>The operator leaves the console. Only the operator of that Cyclops can.</summary>
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

    /// <summary>Whether this session is the operator of that Cyclops.</summary>
    public bool IsOperator(SessionId session, NitroxId cyclopsId)
    {
        lock (gate)
        {
            return cyclopsBySession.TryGetValue(session, out NitroxId seated) && seated.Equals(cyclopsId);
        }
    }

    public bool TryGetOperator(NitroxId cyclopsId, out Seat seat)
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

    /// <summary>This player is done as an operator, whatever the reason (disconnected, died): the seat, if they had one, is over.</summary>
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

    /// <summary>The Cyclops was destroyed (it is a wreck, or gone): its seat is over and its bay is forgotten (the drone is a loose item of the world now).</summary>
    public IReadOnlyList<Seat> CyclopsGone(NitroxId cyclopsId)
    {
        lock (gate)
        {
            bays.Remove(cyclopsId);
            lastZap.Remove(cyclopsId);
            if (!seatByCyclops.TryGetValue(cyclopsId, out Seat seat))
            {
                return [];
            }
            seatByCyclops.Remove(cyclopsId);
            cyclopsBySession.Remove(seat.Session);
            return [seat];
        }
    }

    /// <summary>The player changed the sub root they are in: an operator who is no longer in their Cyclops does not sit at its console any more.</summary>
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

    // ---- the bays

    /// <summary>What the bay of that Cyclops holds: false when it holds nothing.</summary>
    public bool TryGetDrone(NitroxId cyclopsId, out NitroxId droneId, out bool docked)
    {
        lock (gate)
        {
            if (bays.TryGetValue(cyclopsId, out Bay bay))
            {
                droneId = bay.DroneId;
                docked = bay.Docked;
                return true;
            }
            droneId = null;
            docked = false;
            return false;
        }
    }

    /// <summary>The drone that sits in the dock of that Cyclops (false when the bay holds none or it is out).</summary>
    public bool TryGetDockedDrone(NitroxId cyclopsId, out NitroxId droneId)
    {
        lock (gate)
        {
            if (bays.TryGetValue(cyclopsId, out Bay bay) && bay.Docked)
            {
                droneId = bay.DroneId;
                return true;
            }
            droneId = null;
            return false;
        }
    }

    /// <summary>
    ///     Rebuilds the bays from the saved world: what it knows of a bay is the metadata on the drone's entity (<see cref="ScoutDroneMetadata" />, which only the server writes). Runs once (the first call; later
    ///     calls do nothing), when the first player has joined and every entity is loaded, and a bay that a request built before that is not touched. A drone whose Cyclops is gone (a wreck is not saved) or that
    ///     is no Scout Drone loses the metadata and is a loose item. Returns the number of bays rebuilt.
    /// </summary>
    public int RestoreFrom(EntityRegistry entityRegistry)
    {
        // Held for the whole rebuild: a second player who joins meanwhile waits for it and is told of the bays that were rebuilt
        lock (restoreGate)
        {
            if (restored)
            {
                return 0;
            }
            int rebuilt = 0;
            foreach (Entity entity in entityRegistry.GetAllEntities())
            {
                if (entity.Metadata is not ScoutDroneMetadata saved || saved.CyclopsId == null)
                {
                    continue;
                }
                if (!entityRegistry.TryGetEntityById(saved.CyclopsId, out Entity cyclops) || entity.TechType?.Name != ScoutDroneRules.ITEM_TECH_TYPE_NAME ||
                    !PowerTransferRules.IsCyclops(cyclops.TechType?.Name ?? string.Empty) || cyclops.Metadata is CyclopsMetadata { IsDestroyed: true })
                {
                    entity.Metadata = saved.Health == ScoutDroneMetadata.HEALTH_UNKNOWN ? null : new ScoutDroneMetadata(null, false, saved.Health);
                    continue;
                }
                lock (gate)
                {
                    if (bays.ContainsKey(saved.CyclopsId) || bays.Values.Any(other => other.DroneId.Equals(entity.Id)))
                    {
                        continue;
                    }
                    bays[saved.CyclopsId] = new Bay { DroneId = entity.Id, Docked = saved.Docked };
                    rebuilt++;
                }
            }
            restored = true;
            return rebuilt;
        }
    }

    /// <summary>
    ///     Whether this Cyclops may zap now: it did not zap within <paramref name="intervalMilliseconds" /> before <paramref name="nowMilliseconds" /> (the server's clock). If so, now is the time of its last zap. The
    ///     cooldown is not given back when the zap turns out to be of no use: a refund would let a modified client flood the others for nothing.
    /// </summary>
    public bool TryStartZap(NitroxId cyclopsId, long nowMilliseconds, long intervalMilliseconds)
    {
        lock (gate)
        {
            if (lastZap.TryGetValue(cyclopsId, out long last) && nowMilliseconds - last < intervalMilliseconds)
            {
                return false;
            }
            lastZap[cyclopsId] = nowMilliseconds;
            if (lastZap.Count > 32)
            {
                // Cyclopses come and go: forget those whose last zap is old enough not to matter
                foreach (NitroxId old in lastZap.Where(pair => nowMilliseconds - pair.Value >= Math.Max(intervalMilliseconds, 120000L)).Select(pair => pair.Key).ToList())
                {
                    lastZap.Remove(old);
                }
            }
            return true;
        }
    }

    /// <summary>A drone was put into the bay (docked). False when the bay holds a drone already (the bay holds one) or that drone is in another bay.</summary>
    public bool Load(NitroxId cyclopsId, NitroxId droneId)
    {
        lock (gate)
        {
            if (bays.ContainsKey(cyclopsId) || bays.Values.Any(other => other.DroneId.Equals(droneId)))
            {
                return false;
            }
            bays[cyclopsId] = new Bay { DroneId = droneId, Docked = true };
            return true;
        }
    }

    /// <summary>
    ///     The drone was taken out of the bay for good (picked up). False when the bay does not hold that drone, or when it is out and <paramref name="allowOut" /> is false (a drone that somebody flies is not picked
    ///     up: the caller says whether it is flown).
    /// </summary>
    public bool Unload(NitroxId cyclopsId, NitroxId droneId, bool allowOut = false)
    {
        lock (gate)
        {
            if (!bays.TryGetValue(cyclopsId, out Bay bay) || !bay.DroneId.Equals(droneId) || (!bay.Docked && !allowOut))
            {
                return false;
            }
            bays.Remove(cyclopsId);
            return true;
        }
    }

    /// <summary>The seat of that Cyclops ends (its drone left the bay, so there is nothing to fly): the seat that ended, if there was one.</summary>
    public IReadOnlyList<Seat> EndSeatOf(NitroxId cyclopsId)
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

    /// <summary>The drone went into the dock or out of it. False when the bay does not hold that drone, or it is in that state already.</summary>
    public bool SetDocked(NitroxId cyclopsId, NitroxId droneId, bool docked)
    {
        lock (gate)
        {
            if (!bays.TryGetValue(cyclopsId, out Bay bay) || !bay.DroneId.Equals(droneId) || bay.Docked == docked)
            {
                return false;
            }
            bay.Docked = docked;
            return true;
        }
    }

    /// <summary>
    ///     A drone was destroyed (the entity is gone): when a bay held it, the bay is empty and the seat that flew it is over. Returns the Cyclops whose bay it was (null when no bay held it) and the seat that ended.
    /// </summary>
    public (NitroxId? CyclopsId, IReadOnlyList<Seat> Ended) DroneGone(NitroxId droneId)
    {
        lock (gate)
        {
            foreach ((NitroxId cyclopsId, Bay bay) in bays)
            {
                if (!bay.DroneId.Equals(droneId))
                {
                    continue;
                }
                bays.Remove(cyclopsId);
                if (seatByCyclops.TryGetValue(cyclopsId, out Seat seat))
                {
                    seatByCyclops.Remove(cyclopsId);
                    cyclopsBySession.Remove(seat.Session);
                    return (cyclopsId, [seat]);
                }
                return (cyclopsId, []);
            }
            return (null, []);
        }
    }

    /// <summary>What everybody is told about the bay of this Cyclops.</summary>
    public CyclopsDroneState StateOf(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return bays.TryGetValue(cyclopsId, out Bay bay)
                ? new CyclopsDroneState(cyclopsId, Optional.Of(bay.DroneId), bay.Docked)
                : new CyclopsDroneState(cyclopsId, Optional.Empty, false);
        }
    }

    // ---- telling people

    /// <summary>Tells everybody that these seats are over (everybody but <paramref name="except" /> when it is given, for a player who is gone).</summary>
    public async Task BroadcastEndedAsync(IReadOnlyList<Seat> ended, CyclopsDroneSeatReason reason, SessionId? except = null)
    {
        foreach (Seat seat in ended)
        {
            CyclopsDroneSeat packet = new(CyclopsDroneSeatMessage.Released, seat.CyclopsId, seat.Session, seat.Name, reason);
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

    /// <summary>Tells a client that joined about the consoles that are taken and about the bays that hold a drone.</summary>
    public async Task SendSnapshotAsync(SessionId session)
    {
        foreach (Seat seat in Snapshot())
        {
            await packetSender.SendPacketAsync(new CyclopsDroneSeat(CyclopsDroneSeatMessage.Seated, seat.CyclopsId, seat.Session, seat.Name, CyclopsDroneSeatReason.None), session);
        }
        List<NitroxId> cyclopses;
        lock (gate)
        {
            cyclopses = [.. bays.Keys];
        }
        foreach (NitroxId cyclopsId in cyclopses)
        {
            await packetSender.SendPacketAsync(StateOf(cyclopsId), session);
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        await BroadcastEndedAsync(ReleaseSession(args.Session.Id), CyclopsDroneSeatReason.Disconnected, args.Session.Id);
    }
}
