using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Networking;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class PlayerMovement : Movement
{
    public PlayerMovement(SessionId sessionId, NitroxVector3 position, NitroxVector3 velocity, NitroxQuaternion bodyRotation, NitroxQuaternion aimingRotation, double time)
    {
        SessionId = sessionId;
        Position = position;
        Velocity = velocity;
        BodyRotation = bodyRotation;
        AimingRotation = aimingRotation;
        Time = time;
        DeliveryMethod = NitroxDeliveryMethod.DeliveryMethod.UNRELIABLE_SEQUENCED;
        UdpChannel = UdpChannelId.MOVEMENTS;
    }

    public override SessionId SessionId { get; }
    public override NitroxVector3 Position { get; }
    public override NitroxVector3 Velocity { get; }
    public override NitroxQuaternion BodyRotation { get; }
    public override NitroxQuaternion AimingRotation { get; }

    /// <summary>
    /// The sender's server-synced real time (<c>TimeManager.RealTimeElapsed</c>) when the snapshot was taken, like <c>VehicleMovements</c>: the receiver knows how old it is. A double: a float step is 3.9 ms from 9 h of play (bug 7).
    /// </summary>
    public double Time { get; }
}
