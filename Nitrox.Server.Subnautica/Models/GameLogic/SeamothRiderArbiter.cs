using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The riders of the Seamoth Rider Bar (feature group 2): which player holds on to the bar of which Seamoth. One rider per Seamoth, the first request wins, and a player rides one Seamoth at a
///     time; the server tells everybody (<see cref="SeamothRiderChanged" />) and ends a ride when the Seamoth is destroyed or docks and when the rider's player leaves. Kept in memory only, a ride is
///     never saved (a rider who leaves is saved where the Seamoth was).
/// </summary>
/// <remarks>The riders are kept by session, by value (never by <see cref="Player" />, who may have reconnected under another session). Every method that ends a ride returns it, and the caller broadcasts it.</remarks>
internal sealed class SeamothRiderArbiter(IPacketSender packetSender) : ISessionCleaner
{
    internal readonly record struct Ride(NitroxId VehicleId, SessionId Session);

    private readonly IPacketSender packetSender = packetSender;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, SessionId> riderByVehicle = [];
    private readonly Dictionary<SessionId, NitroxId> vehicleBySession = [];

    public int Count
    {
        get
        {
            lock (gate)
            {
                return riderByVehicle.Count;
            }
        }
    }

    /// <summary>The first request for a free Seamoth wins; false when somebody rides it (the same player asking again is fine) or when this player rides another Seamoth.</summary>
    public bool TryHold(NitroxId vehicleId, SessionId session)
    {
        lock (gate)
        {
            if (riderByVehicle.TryGetValue(vehicleId, out SessionId rider))
            {
                return rider == session;
            }
            if (vehicleBySession.ContainsKey(session))
            {
                return false;
            }
            riderByVehicle[vehicleId] = session;
            vehicleBySession[session] = vehicleId;
            return true;
        }
    }

    /// <summary>The rider lets go. Only the rider of that Seamoth can.</summary>
    public bool LetGo(NitroxId vehicleId, SessionId session)
    {
        lock (gate)
        {
            if (riderByVehicle.TryGetValue(vehicleId, out SessionId rider) && rider == session)
            {
                riderByVehicle.Remove(vehicleId);
                vehicleBySession.Remove(session);
                return true;
            }
            return false;
        }
    }

    public bool Rides(SessionId session, NitroxId vehicleId)
    {
        lock (gate)
        {
            return vehicleBySession.TryGetValue(session, out NitroxId ridden) && ridden.Equals(vehicleId);
        }
    }

    public bool TryGetRider(NitroxId vehicleId, out SessionId session)
    {
        lock (gate)
        {
            return riderByVehicle.TryGetValue(vehicleId, out session);
        }
    }

    public IReadOnlyList<Ride> Snapshot()
    {
        lock (gate)
        {
            return riderByVehicle.Select(pair => new Ride(pair.Key, pair.Value)).ToList();
        }
    }

    /// <summary>The rider's player left: the ride is over.</summary>
    public IReadOnlyList<Ride> SessionEnded(SessionId session)
    {
        lock (gate)
        {
            if (!vehicleBySession.TryGetValue(session, out NitroxId vehicleId))
            {
                return [];
            }
            riderByVehicle.Remove(vehicleId);
            vehicleBySession.Remove(session);
            return [new Ride(vehicleId, session)];
        }
    }

    /// <summary>The Seamoth was destroyed or began to dock: its ride is over.</summary>
    public IReadOnlyList<Ride> VehicleGone(NitroxId vehicleId)
    {
        lock (gate)
        {
            if (!riderByVehicle.TryGetValue(vehicleId, out SessionId session))
            {
                return [];
            }
            riderByVehicle.Remove(vehicleId);
            vehicleBySession.Remove(session);
            return [new Ride(vehicleId, session)];
        }
    }

    /// <summary>Tells everybody that these rides are over.</summary>
    public async Task BroadcastEndedAsync(IReadOnlyList<Ride> ended, SessionId? except = null)
    {
        foreach (Ride ride in ended)
        {
            SeamothRiderChanged packet = new(RiderBarMessage.Released, ride.VehicleId, ride.Session);
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

    /// <summary>Tells a client that joined about the rides that are on.</summary>
    public async Task SendSnapshotAsync(SessionId session)
    {
        foreach (Ride ride in Snapshot())
        {
            await packetSender.SendPacketAsync(new SeamothRiderChanged(RiderBarMessage.Holding, ride.VehicleId, ride.Session), session);
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        await BroadcastEndedAsync(SessionEnded(args.Session.Id), args.Session.Id);
    }
}
