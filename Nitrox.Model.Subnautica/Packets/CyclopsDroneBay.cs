using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsDroneBay" /> asks.</summary>
public enum CyclopsDroneBayKind : byte
{
    /// <summary>Not a request: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>A player aboard put a Scout Drone into the bay of the Cyclops (the item was dropped at the dock and the dock took it).</summary>
    Load = 1,

    /// <summary>A player aboard took the drone out of the bay (picked it up from the dock).</summary>
    Unload = 2,

    /// <summary>The operator flew the drone home: it is in the dock.</summary>
    Dock = 3,

    /// <summary>The operator flew the drone out: it left the dock.</summary>
    Undock = 4
}

/// <summary>
/// What a client reports about the drone bay of a Cyclops (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 4): a drone was loaded, taken out, docked or undocked. Client to server only. The server checks who
/// asks (a player aboard the Cyclops for a load or an unload, the operator of the Cyclops for a dock or an undock) and which drone (the one the bay holds; for a load, a Scout Drone that exists), keeps the state of the
/// bay (<c>CyclopsDroneArbiter</c>) and tells everybody what it decided with <see cref="CyclopsDroneState" />. A request the server does not accept changes nothing and is not answered: the state it tells next is the truth.
/// </summary>
[Serializable]
public class CyclopsDroneBay : Packet
{
    public CyclopsDroneBayKind Kind { get; }

    public NitroxId CyclopsId { get; }

    /// <summary>The entity id of the drone.</summary>
    public NitroxId DroneId { get; }

    public CyclopsDroneBay(CyclopsDroneBayKind kind, NitroxId cyclopsId, NitroxId droneId)
    {
        Kind = kind;
        CyclopsId = cyclopsId;
        DroneId = droneId;
    }
}
