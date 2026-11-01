using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="SeamothRiderChanged" /> says. The client asks with <see cref="Hold" /> and <see cref="LetGo" />; the server answers and tells everybody with the rest.</summary>
public enum RiderBarMessage : byte
{
    /// <summary>Not a message: the value of a field that was never filled in. Dropped.</summary>
    Unknown = 0,

    /// <summary>Client to server: a swimming player wants to hold on to the bar of this Seamoth.</summary>
    Hold = 1,

    /// <summary>Client to server: the rider lets go.</summary>
    LetGo = 2,

    /// <summary>Server to everybody (the rider included): this player holds on to the bar of this Seamoth now.</summary>
    Holding = 3,

    /// <summary>Server to everybody (the rider included): this player does not hold on to the bar of this Seamoth any more (let go, docked, destroyed, disconnected).</summary>
    Released = 4,

    /// <summary>Server to the player who asked: somebody holds the bar already, or the Seamoth cannot be held on to.</summary>
    Denied = 5
}

/// <summary>
/// The Seamoth Rider Bar (feature group 2): a swimming player holds on to the bar on the hull of a Seamoth and rides along. The server lets one rider per Seamoth (the first request wins) and tells
/// everybody; every client then puts the rider's body on the bar of that Seamoth (the body is a child of the bar, so it moves with the Seamoth on every client without any pose of its own, see
/// <see cref="SeamothRiderLook" /> for the look). The server also ends a ride when the Seamoth is destroyed or docks and when the rider's player leaves.
/// </summary>
[Serializable]
public class SeamothRiderChanged : Packet
{
    public RiderBarMessage Message { get; }

    /// <summary>The Seamoth.</summary>
    public NitroxId VehicleId { get; }

    /// <summary>The rider (the sender in a request; the server fills it in for everybody else).</summary>
    public SessionId SessionId { get; }

    public SeamothRiderChanged(RiderBarMessage message, NitroxId vehicleId, SessionId sessionId)
    {
        Message = message;
        VehicleId = vehicleId;
        SessionId = sessionId;
    }
}
