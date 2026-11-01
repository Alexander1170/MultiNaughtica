using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Networking;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// Where the rider of a Seamoth looks, relative to the Seamoth (the Seamoth Rider Bar). The rider's body is a child of the bar on every client, so its place needs no packet and cannot jitter against
/// the moving hull; this is only the direction of the head (and the arms that follow it). Sent by the rider about 15 times a second while the look changes, unreliably and in order like the
/// movement packets; the server passes it on only while the sender really rides that Seamoth.
/// </summary>
[Serializable]
public class SeamothRiderLook : Packet
{
    public SessionId SessionId { get; }
    public NitroxId VehicleId { get; }

    /// <summary>The rider's aiming rotation in the space of the bar (the aiming rotation of the player is world space; the receiver turns it back with its own copy of the Seamoth).</summary>
    public NitroxQuaternion Look { get; }

    /// <summary>The sender's server-synced real time, like <see cref="PlayerInCyclopsMovement.Time" />.</summary>
    public double Time { get; }

    public SeamothRiderLook(SessionId sessionId, NitroxId vehicleId, NitroxQuaternion look, double time)
    {
        SessionId = sessionId;
        VehicleId = vehicleId;
        Look = look;
        Time = time;
        DeliveryMethod = NitroxDeliveryMethod.DeliveryMethod.UNRELIABLE_SEQUENCED;
        UdpChannel = UdpChannelId.MOVEMENTS;
    }
}
