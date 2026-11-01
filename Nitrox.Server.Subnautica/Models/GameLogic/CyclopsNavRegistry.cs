using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The marks and the routes that the navigators of the Cyclopses have set (feature group 3, station 1, the navigator console): which marks each Cyclops has, and its route (the waypoints in order and which is
///     next). Kept in memory only; marks and routes are not saved, like tows. The registry decides nothing about the world; it keeps the books, so that a client that joins hears about the marks and the route that are
///     on, and that those of a destroyed Cyclops are gone.
/// </summary>
/// <remarks>
///     The marks belong to the Cyclops, not to the player who placed them, so they stay when that player leaves; any player may remove any mark (the crew shares the map). A Cyclops keeps at most
///     <see cref="CyclopsNavRules.MAX_MARKS" /> marks. Placing the same mark again (same Cyclops and id) is accepted and changes nothing, so a repeated packet is harmless.
/// </remarks>
internal sealed class CyclopsNavRegistry(IPacketSender packetSender)
{
    internal sealed record Mark(NitroxId Id, NitroxId CyclopsId, NitroxVector3 Position, byte Kind, string Label, SessionId Owner, string OwnerName);

    internal sealed record Waypoint(NitroxId Id, NitroxVector3 Position, SessionId Owner, string OwnerName);

    private sealed class Route
    {
        public readonly List<Waypoint> Points = [];
        public int Next;
    }

    private readonly IPacketSender packetSender = packetSender;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, List<Mark>> byCyclops = [];
    private readonly Dictionary<NitroxId, Route> routes = [];

    public int Count(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return byCyclops.TryGetValue(cyclopsId, out List<Mark>? marks) ? marks.Count : 0;
        }
    }

    public int TotalCount
    {
        get
        {
            lock (gate)
            {
                return byCyclops.Values.Sum(marks => marks.Count);
            }
        }
    }

    /// <summary>Every mark that is on, for a client that joined.</summary>
    public IReadOnlyList<Mark> Snapshot()
    {
        lock (gate)
        {
            return byCyclops.Values.SelectMany(marks => marks).ToList();
        }
    }

    /// <summary>The marks of one Cyclops.</summary>
    public IReadOnlyList<Mark> MarksOf(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return byCyclops.TryGetValue(cyclopsId, out List<Mark>? marks) ? marks.ToList() : [];
        }
    }

    /// <summary>
    ///     Records a mark. False (nothing recorded) when the Cyclops already has the most marks of that class (<see cref="CyclopsNavRules.MAX_MARKS" /> of the navigator's kinds, <see cref="CyclopsNavRules.MAX_DRONE_MARKS" />
    ///     of the Scout Drone's, counted apart), or when the id is already used for a mark of another Cyclops. The same mark again is accepted (<paramref name="isNew" /> is false then, so the caller does not tell the others twice).
    /// </summary>
    public bool TryAdd(Mark mark, out bool isNew)
    {
        isNew = false;
        lock (gate)
        {
            foreach (KeyValuePair<NitroxId, List<Mark>> pair in byCyclops)
            {
                if (!pair.Key.Equals(mark.CyclopsId) && pair.Value.Any(existing => existing.Id.Equals(mark.Id)))
                {
                    return false;
                }
            }
            if (!byCyclops.TryGetValue(mark.CyclopsId, out List<Mark>? marks))
            {
                marks = [];
                byCyclops[mark.CyclopsId] = marks;
            }
            if (marks.Any(existing => existing.Id.Equals(mark.Id)))
            {
                return true;
            }
            bool droneMark = CyclopsNavRules.IsDroneKind(mark.Kind);
            if (marks.Count(existing => CyclopsNavRules.IsDroneKind(existing.Kind) == droneMark) >= CyclopsNavRules.CapOf(mark.Kind))
            {
                return false;
            }
            marks.Add(mark);
            isNew = true;
            return true;
        }
    }

    /// <summary>Takes a mark away; false when the Cyclops has no such mark.</summary>
    public bool TryRemove(NitroxId cyclopsId, NitroxId markId, out Mark? removed)
    {
        lock (gate)
        {
            removed = null;
            if (!byCyclops.TryGetValue(cyclopsId, out List<Mark>? marks))
            {
                return false;
            }
            int index = marks.FindIndex(existing => existing.Id.Equals(markId));
            if (index < 0)
            {
                return false;
            }
            removed = marks[index];
            marks.RemoveAt(index);
            if (marks.Count == 0)
            {
                byCyclops.Remove(cyclopsId);
            }
            return true;
        }
    }

    /// <summary>Takes every mark of a Cyclops away; how many there were.</summary>
    public int Clear(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return byCyclops.Remove(cyclopsId, out List<Mark>? marks) ? marks.Count : 0;
        }
    }

    // ---- the route

    /// <summary>The waypoints of a Cyclops' route in order.</summary>
    public IReadOnlyList<Waypoint> RouteOf(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return routes.TryGetValue(cyclopsId, out Route? route) ? route.Points.ToList() : [];
        }
    }

    /// <summary>The index of the next waypoint of a Cyclops' route (the number of waypoints when it is finished, 0 without a route).</summary>
    public int NextOf(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return routes.TryGetValue(cyclopsId, out Route? route) ? route.Next : 0;
        }
    }

    /// <summary>
    ///     Appends a waypoint. False (nothing recorded) when the route is full, the waypoint is on top of the last one or not in the world (<see cref="NavigationRules.CanAppend" />), or its id is used. The same
    ///     waypoint again is accepted (<paramref name="isNew" /> is false then). <paramref name="slot" /> is the place of the waypoint in the route, which the server's order decides: the clients put it there, so
    ///     that two navigators who add at the same time end up with the same route.
    /// </summary>
    public bool TryAddWaypoint(NitroxId cyclopsId, Waypoint waypoint, out bool isNew, out int slot)
    {
        isNew = false;
        slot = 0;
        lock (gate)
        {
            if (routes.TryGetValue(cyclopsId, out Route? existing))
            {
                int known = existing.Points.FindIndex(point => point.Id.Equals(waypoint.Id));
                if (known >= 0)
                {
                    slot = known;
                    return true;
                }
            }
            Route route = existing ?? new Route();
            Waypoint? last = route.Points.Count > 0 ? route.Points[^1] : null;
            if (!NavigationRules.CanAppend(route.Points.Count, last != null, last?.Position.X ?? 0f, last?.Position.Y ?? 0f, last?.Position.Z ?? 0f, waypoint.Position.X, waypoint.Position.Y, waypoint.Position.Z))
            {
                return false;
            }
            route.Points.Add(waypoint);
            routes[cyclopsId] = route;
            slot = route.Points.Count - 1;
            isNew = true;
            return true;
        }
    }

    /// <summary>Takes a waypoint out of the route; false when it is not there. The next waypoint keeps pointing at the same place (<see cref="NavigationRules.NextAfterRemoval" />). <paramref name="next" /> is the new index.</summary>
    public bool TryRemoveWaypoint(NitroxId cyclopsId, NitroxId waypointId, out int next)
    {
        lock (gate)
        {
            next = 0;
            if (!routes.TryGetValue(cyclopsId, out Route? route))
            {
                return false;
            }
            int index = route.Points.FindIndex(point => point.Id.Equals(waypointId));
            if (index < 0)
            {
                return false;
            }
            route.Points.RemoveAt(index);
            route.Next = NavigationRules.NextAfterRemoval(route.Next, index, route.Points.Count);
            next = route.Next;
            if (route.Points.Count == 0)
            {
                routes.Remove(cyclopsId);
            }
            return true;
        }
    }

    /// <summary>
    ///     The Cyclops reached its next waypoint: forward only, by one (<see cref="NavigationRules.IsValidAdvance" />), and it has to be the waypoint that the server has as next, named by its id. An advance that was
    ///     decided before a removal or a clear reached the server names a waypoint that is not next any more and is refused instead of skipping one. False when that is not so.
    /// </summary>
    public bool TryAdvance(NitroxId cyclopsId, int proposed, NitroxId reachedId)
    {
        lock (gate)
        {
            if (!routes.TryGetValue(cyclopsId, out Route? route) || !NavigationRules.IsValidAdvance(route.Points.Count, route.Next, proposed) || !route.Points[route.Next].Id.Equals(reachedId))
            {
                return false;
            }
            route.Next = proposed;
            return true;
        }
    }

    /// <summary>Takes the whole route away; how many waypoints it had.</summary>
    public int ClearRoute(NitroxId cyclopsId)
    {
        lock (gate)
        {
            return routes.Remove(cyclopsId, out Route? route) ? route.Points.Count : 0;
        }
    }

    /// <summary>An entity was destroyed: if it was a Cyclops, its marks and its route are gone (the clients drop theirs when the Cyclops is destroyed on their side). How many marks that was.</summary>
    public int EntityDestroyed(NitroxId id)
    {
        ClearRoute(id);
        return Clear(id);
    }

    /// <summary>Tells a client that joined about the marks and the routes that are on.</summary>
    public async Task SendSnapshotAsync(SessionId session)
    {
        foreach (Mark mark in Snapshot())
        {
            await packetSender.SendPacketAsync(new CyclopsNavChanged(CyclopsNavChange.MarkAdded, mark.CyclopsId, mark.Id, mark.Position, mark.Kind, mark.Label, mark.Owner, mark.OwnerName), session);
        }
        List<(NitroxId CyclopsId, List<Waypoint> Points, int Next)> snapshot;
        lock (gate)
        {
            snapshot = routes.Select(pair => (pair.Key, pair.Value.Points.ToList(), pair.Value.Next)).ToList();
        }
        foreach ((NitroxId cyclopsId, List<Waypoint> points, int next) in snapshot)
        {
            for (int slot = 0; slot < points.Count; slot++)
            {
                Waypoint point = points[slot];
                await packetSender.SendPacketAsync(new CyclopsRouteChanged(CyclopsRouteChange.WaypointAdded, cyclopsId, point.Id, point.Position, slot, point.Owner, point.OwnerName), session);
            }
            if (next > 0)
            {
                await packetSender.SendPacketAsync(new CyclopsRouteChanged(CyclopsRouteChange.Advanced, cyclopsId, new NitroxId(), NitroxVector3.Zero, next, default, string.Empty), session);
            }
        }
    }
}
