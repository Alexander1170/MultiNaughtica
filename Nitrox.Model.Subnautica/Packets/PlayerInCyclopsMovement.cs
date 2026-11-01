using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Networking;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class PlayerInCyclopsMovement : Packet
{
    public SessionId SessionId { get; }
    public NitroxVector3 LocalPosition { get; }

    /// <summary>
    /// Walking velocity in the space of the Cyclops (the part of the movement that is not the Cyclops's own).
    /// </summary>
    public NitroxVector3 LocalVelocity { get; }

    public NitroxQuaternion LocalRotation { get; }

    /// <summary>
    /// The sender's server-synced real time (<c>TimeManager.RealTimeElapsed</c>) when the snapshot was taken, like <see cref="PlayerMovement.Time" />.
    /// </summary>
    public double Time { get; }

    public PlayerInCyclopsMovement(SessionId sessionId, NitroxVector3 localPosition, NitroxVector3 localVelocity, NitroxQuaternion localRotation, double time)
    {
        SessionId = sessionId;
        LocalPosition = localPosition;
        LocalVelocity = localVelocity;
        LocalRotation = localRotation;
        Time = time;
        DeliveryMethod = NitroxDeliveryMethod.DeliveryMethod.UNRELIABLE_SEQUENCED;
        UdpChannel = UdpChannelId.MOVEMENTS;
    }
}
