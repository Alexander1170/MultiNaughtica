using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// A tow hook hooked a vehicle to a Seamoth or a Cyclops, or let go (feature group 2). The player who tows sends it and the server passes it on to everybody else, so that every client draws the cable
/// between the two vehicles and a client that joins later hears about the tows that are on. The towing player's client also simulates the towed vehicle (it took its simulation lock, so the movement
/// of the towed vehicle reaches everybody like that of any simulated vehicle); this packet only says who is hooked to whom. A tow is not saved: a server that restarts has no tows.
/// The server also sends it on its own, with <see cref="Attached" /> false, when a tow ended without the towing player saying so (the player left, a vehicle was destroyed, the lock of a vehicle
/// moved to another player), to everybody including the player who towed.
/// </summary>
[Serializable]
public class VehicleTowChanged : Packet
{
    /// <summary>The Seamoth or the Cyclops that tows.</summary>
    public NitroxId TowerId { get; }

    /// <summary>The Seamoth or Prawn Suit that is towed.</summary>
    public NitroxId TowedId { get; }

    public bool Attached { get; }

    /// <summary>The player who tows (so that the others can say who hooked the vehicle).</summary>
    public SessionId SessionId { get; }

    public VehicleTowChanged(NitroxId towerId, NitroxId towedId, bool attached, SessionId sessionId)
    {
        TowerId = towerId;
        TowedId = towedId;
        Attached = attached;
        SessionId = sessionId;
    }
}
