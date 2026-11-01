using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Extensions;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>One waypoint of the route of a Cyclops, as this client knows it.</summary>
public sealed class NavWaypoint
{
    public NavWaypoint(NitroxId id, NitroxId cyclopsId, Vector3 position, SessionId owner, string ownerName)
    {
        Id = id;
        CyclopsId = cyclopsId;
        Position = position;
        Owner = owner;
        OwnerName = ownerName;
    }

    public NitroxId Id { get; }
    public NitroxId CyclopsId { get; }

    /// <summary>Where the waypoint is: on the terrain where it was clicked (or the Cyclops' own depth, see <see cref="RouteClearance.PinYFor" />). The map draws it here.</summary>
    public Vector3 Position { get; }

    /// <summary>Where the helm aims for this waypoint: the clearance above <see cref="Position" /> (<see cref="RouteClearance.AimY" />), so that the Cyclops does not dive into the rock. The guidance uses this.</summary>
    public Vector3 AimPosition => new(Position.x, RouteClearance.AimY(Position.y), Position.z);

    public SessionId Owner { get; }
    public string OwnerName { get; }
}

/// <summary>
/// The route that a navigator set for each Cyclops (docs/coop/VEHICLE_UPGRADES.md, station 1): the waypoints in order and which of them is next, as this client knows them. What the navigator does (append a
/// waypoint, take one out, clear the route) is applied here at once and told to the server, which keeps the books and tells the others; the advance to the next waypoint is made by the client of the player who holds
/// the Cyclops (<see cref="RouteGuide" />), so that the clients cannot disagree. The state is by the Cyclops' id, like the marks'. Not saved with the world.
/// </summary>
public static class NavRoute
{
    private sealed class State
    {
        public readonly List<NavWaypoint> Points = [];
        public int Next;
    }

    private static readonly Dictionary<NitroxId, State> byCyclops = [];
    private static bool subscribed;

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    public static void Reset()
    {
        byCyclops.Clear();
    }

    public static IReadOnlyList<NavWaypoint> PointsOf(NitroxId cyclopsId)
    {
        return byCyclops.TryGetValue(cyclopsId, out State state) ? state.Points : System.Array.Empty<NavWaypoint>();
    }

    /// <summary>The index of the next waypoint; the number of waypoints when the route is finished, 0 without a route.</summary>
    public static int NextOf(NitroxId cyclopsId) => byCyclops.TryGetValue(cyclopsId, out State state) ? state.Next : 0;

    public static int CountOf(NitroxId cyclopsId) => byCyclops.TryGetValue(cyclopsId, out State state) ? state.Points.Count : 0;

    public static bool IsFinished(NitroxId cyclopsId) => byCyclops.TryGetValue(cyclopsId, out State state) && NavigationRules.IsFinished(state.Points.Count, state.Next);

    /// <summary>The next waypoint, or null when there is no route or it is finished.</summary>
    public static NavWaypoint NextWaypoint(NitroxId cyclopsId)
    {
        return byCyclops.TryGetValue(cyclopsId, out State state) && state.Next >= 0 && state.Next < state.Points.Count ? state.Points[state.Next] : null;
    }

    // ---- what the server says

    public static void OnPacket(CyclopsRouteChanged packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }
        switch (packet.Change)
        {
            case CyclopsRouteChange.WaypointAdded:
                if (packet.WaypointId != null)
                {
                    // the server's order is the route's order: the waypoint goes to the slot the server gave it (a late joiner may hear of a later waypoint before an earlier one)
                    Place(new NavWaypoint(packet.WaypointId, packet.CyclopsId, packet.Position.ToUnity(), packet.Owner, packet.OwnerName ?? string.Empty), packet.Index);
                }
                break;
            case CyclopsRouteChange.WaypointRemoved:
                if (packet.WaypointId != null)
                {
                    RemoveLocal(packet.CyclopsId, packet.WaypointId);
                    // a removal that the server relays says which waypoint is next now (a refusal of ours says -1: nothing to take over)
                    if (packet.Index >= 0 && byCyclops.TryGetValue(packet.CyclopsId, out State afterRemoval))
                    {
                        afterRemoval.Next = Mathf.Clamp(packet.Index, 0, afterRemoval.Points.Count);
                    }
                }
                break;
            case CyclopsRouteChange.RouteCleared:
                byCyclops.Remove(packet.CyclopsId);
                break;
            case CyclopsRouteChange.Advanced:
                if (byCyclops.TryGetValue(packet.CyclopsId, out State state))
                {
                    state.Next = Mathf.Clamp(packet.Index, 0, state.Points.Count);
                }
                break;
        }
    }

    // ---- what the player does

    /// <summary>The navigator set a waypoint at <paramref name="world" />: it is on the route at once and the server is told. Null when the route has no room, or the point is on top of the last one.</summary>
    public static NavWaypoint Append(NitroxId cyclopsId, Vector3 world)
    {
        IReadOnlyList<NavWaypoint> points = PointsOf(cyclopsId);
        Vector3 last = points.Count > 0 ? points[points.Count - 1].Position : Vector3.zero;
        if (!NavigationRules.CanAppend(points.Count, points.Count > 0, last.x, last.y, last.z, world.x, world.y, world.z))
        {
            return null;
        }
        SessionId session = NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;
        string name = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName ?? string.Empty;
        NavWaypoint waypoint = new(new NitroxId(), cyclopsId, world, session, name);
        Append(waypoint);
        Send(CyclopsRouteChange.WaypointAdded, cyclopsId, waypoint.Id, world, 0);
        Log.Info($"[VehicleUpgrades] navigator: waypoint {CountOf(cyclopsId)} at {world.ToString("F1")} on Cyclops {cyclopsId}");
        return waypoint;
    }

    /// <summary>The navigator took the last waypoint out.</summary>
    public static bool RemoveLastByPlayer(NitroxId cyclopsId)
    {
        IReadOnlyList<NavWaypoint> points = PointsOf(cyclopsId);
        if (points.Count == 0)
        {
            return false;
        }
        NavWaypoint last = points[points.Count - 1];
        RemoveLocal(cyclopsId, last.Id);
        Send(CyclopsRouteChange.WaypointRemoved, cyclopsId, last.Id, last.Position, 0);
        return true;
    }

    public static void ClearByPlayer(NitroxId cyclopsId)
    {
        if (CountOf(cyclopsId) == 0)
        {
            return;
        }
        byCyclops.Remove(cyclopsId);
        Send(CyclopsRouteChange.RouteCleared, cyclopsId, new NitroxId(), Vector3.zero, 0);
    }

    /// <summary>The Cyclops reached its next waypoint (the client of the player who holds the Cyclops says so): the next one is next, and the server is told.</summary>
    public static bool AdvanceByHolder(NitroxId cyclopsId)
    {
        if (!byCyclops.TryGetValue(cyclopsId, out State state) || !NavigationRules.IsValidAdvance(state.Points.Count, state.Next, state.Next + 1))
        {
            return false;
        }
        // the waypoint that was reached is named, so that the server can tell an advance that was decided before a removal or a clear from one for the waypoint that is next now
        NitroxId reached = state.Points[state.Next].Id;
        state.Next++;
        Send(CyclopsRouteChange.Advanced, cyclopsId, reached, Vector3.zero, state.Next);
        Log.Info($"[VehicleUpgrades] navigator: reached waypoint {state.Next} of {state.Points.Count} on Cyclops {cyclopsId}{(state.Next >= state.Points.Count ? ": the route is finished" : string.Empty)}");
        return true;
    }

    /// <summary>The Cyclops is gone from this client: its route is forgotten.</summary>
    public static void ForgetCyclops(NitroxId cyclopsId)
    {
        byCyclops.Remove(cyclopsId);
    }

    // ---- the state

    private static void Append(NavWaypoint waypoint)
    {
        if (!byCyclops.TryGetValue(waypoint.CyclopsId, out State state))
        {
            state = new State();
            byCyclops[waypoint.CyclopsId] = state;
        }
        foreach (NavWaypoint existing in state.Points)
        {
            if (existing.Id.Equals(waypoint.Id))
            {
                return;
            }
        }
        if (state.Points.Count < NavigationRules.MAX_WAYPOINTS)
        {
            state.Points.Add(waypoint);
        }
    }

    /// <summary>A waypoint that the server says is in this slot of the route (a slot past the end is the end).</summary>
    private static void Place(NavWaypoint waypoint, int slot)
    {
        if (!byCyclops.TryGetValue(waypoint.CyclopsId, out State state))
        {
            state = new State();
            byCyclops[waypoint.CyclopsId] = state;
        }
        foreach (NavWaypoint existing in state.Points)
        {
            if (existing.Id.Equals(waypoint.Id))
            {
                return;
            }
        }
        if (state.Points.Count < NavigationRules.MAX_WAYPOINTS)
        {
            state.Points.Insert(Mathf.Clamp(slot, 0, state.Points.Count), waypoint);
        }
    }

    private static void RemoveLocal(NitroxId cyclopsId, NitroxId waypointId)
    {
        if (!byCyclops.TryGetValue(cyclopsId, out State state))
        {
            return;
        }
        int index = state.Points.FindIndex(point => point.Id.Equals(waypointId));
        if (index < 0)
        {
            return;
        }
        state.Points.RemoveAt(index);
        state.Next = NavigationRules.NextAfterRemoval(state.Next, index, state.Points.Count);
        if (state.Points.Count == 0)
        {
            byCyclops.Remove(cyclopsId);
        }
    }

    private static void Send(CyclopsRouteChange change, NitroxId cyclopsId, NitroxId waypointId, Vector3 position, int index)
    {
        SessionId session = NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new CyclopsRouteChanged(change, cyclopsId, waypointId, position.ToDto(), index, session, string.Empty));
    }
}
