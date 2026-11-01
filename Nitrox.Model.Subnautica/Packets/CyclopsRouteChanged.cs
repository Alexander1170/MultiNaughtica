using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsRouteChanged" /> says happened to the route of a Cyclops.</summary>
public enum CyclopsRouteChange : byte
{
    /// <summary>A waypoint was appended at the end of the route.</summary>
    WaypointAdded = 0,

    /// <summary>The waypoint <see cref="CyclopsRouteChanged.WaypointId" /> was taken out of the route.</summary>
    WaypointRemoved = 1,

    /// <summary>The whole route was taken away.</summary>
    RouteCleared = 2,

    /// <summary>The Cyclops reached a waypoint: the next one is now <see cref="CyclopsRouteChanged.Index" /> (the number of waypoints means the route is finished).</summary>
    Advanced = 3
}

/// <summary>
/// The route that a navigator set for a Cyclops (docs/coop/VEHICLE_UPGRADES.md, station 1): a list of waypoints that the pilot follows, and which of them is next. The navigator's client sends the changes of the list,
/// and the client of the player who holds the Cyclops (the pilot, whose client simulates it) sends the advance when the Cyclops reaches a waypoint, so that the clients cannot disagree about it. The server checks them,
/// keeps the route of each Cyclops (<c>CyclopsNavRegistry</c>) and passes it on to everybody else; a client that joins later is sent the route as it stands. Not saved with the world. Like the marks, the server answers a refusal
/// with the opposite change to the sender alone.
/// </summary>
[Serializable]
public class CyclopsRouteChanged : Packet
{
    public CyclopsRouteChange Change { get; }

    public NitroxId CyclopsId { get; }

    /// <summary>The waypoint that was added or removed (made up by the client that added it).</summary>
    public NitroxId WaypointId { get; }

    public NitroxVector3 Position { get; }

    /// <summary>For <see cref="CyclopsRouteChange.Advanced" />: the index of the next waypoint.</summary>
    public int Index { get; }

    /// <summary>The player who made the change (the server puts the sender's own session in).</summary>
    public SessionId Owner { get; }

    public string OwnerName { get; }

    public CyclopsRouteChanged(CyclopsRouteChange change, NitroxId cyclopsId, NitroxId waypointId, NitroxVector3 position, int index, SessionId owner, string ownerName)
    {
        Change = change;
        CyclopsId = cyclopsId;
        WaypointId = waypointId;
        Position = position;
        Index = index;
        Owner = owner;
        OwnerName = ownerName;
    }
}
