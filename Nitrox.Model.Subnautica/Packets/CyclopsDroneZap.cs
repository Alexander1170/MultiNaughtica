using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsDroneZap" /> says.</summary>
public enum CyclopsDroneZapKind : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: the operator of the Scout Drone pressed the zap key.</summary>
    Request = 1,

    /// <summary>Server to everybody: the zap happens, at this place, with this radius. Every client plays it and applies it to the creatures it simulates.</summary>
    Apply = 2,

    /// <summary>Server to the operator alone: the zap was refused, for <see cref="CyclopsDroneZap.Failure" />.</summary>
    Refused = 3
}

/// <summary>Why a zap did not happen.</summary>
public enum CyclopsDroneZapFailure : byte
{
    None = 0,

    /// <summary>The vehicle upgrades are off, the entity is no live Cyclops, or the drone is not the bay's.</summary>
    NotAvailable = 1,

    /// <summary>The sender does not sit at the drone console of this Cyclops.</summary>
    NotOperator = 2,

    /// <summary>The drone sits in its dock (it zaps only when it is out).</summary>
    NotOut = 3,

    /// <summary>The Cyclops zapped a moment ago (the server keeps the cooldown).</summary>
    TooSoon = 4,

    /// <summary>The place named is not where the drone is.</summary>
    TooFar = 5
}

/// <summary>
/// The Scout Drone's zapper (docs/coop/VEHICLE_UPGRADES.md, station 4): the operator asks (<see cref="CyclopsDroneZapKind.Request" />), the server checks the seat, that the drone is out and where it is, and the cooldown
/// on its own clock, then tells everybody (<see cref="CyclopsDroneZapKind.Apply" />, with the radius from the server's settings); or refuses the operator alone (<see cref="CyclopsDroneZapKind.Refused" />). The
/// operator's client pays the energy from the drone's battery when it hears its own zap applied. Every client plays the effect and applies it to the creatures and the Lava Larvae it simulates (a creature is simulated by
/// one client, so each is scared once).
/// </summary>
[Serializable]
public class CyclopsDroneZap : Packet
{
    public CyclopsDroneZapKind Kind { get; }

    public NitroxId CyclopsId { get; }

    public NitroxId DroneId { get; }

    /// <summary>Where the drone is (a request: the operator's client; an apply: as the server accepted it).</summary>
    public NitroxVector3 Position { get; }

    /// <summary>How far the zap reaches (m); the server's setting, filled in by the server in an apply.</summary>
    public float Radius { get; }

    /// <summary>The operator's own count of its requests, echoed in the apply and the refusal.</summary>
    public int Serial { get; }

    /// <summary>The operator, in an apply (the server puts the sender's own session in).</summary>
    public SessionId OperatorSession { get; }

    public CyclopsDroneZapFailure Failure { get; }

    public CyclopsDroneZap(CyclopsDroneZapKind kind, NitroxId cyclopsId, NitroxId droneId, NitroxVector3 position, float radius, int serial, SessionId operatorSession, CyclopsDroneZapFailure failure)
    {
        Kind = kind;
        CyclopsId = cyclopsId;
        DroneId = droneId;
        Position = position;
        Radius = radius;
        Serial = serial;
        OperatorSession = operatorSession;
        Failure = failure;
    }

    public static CyclopsDroneZap Request(NitroxId cyclopsId, NitroxId droneId, NitroxVector3 position, int serial) => new(CyclopsDroneZapKind.Request, cyclopsId, droneId, position, 0f, serial, default, CyclopsDroneZapFailure.None);

    public static CyclopsDroneZap Apply(NitroxId cyclopsId, NitroxId droneId, NitroxVector3 position, float radius, int serial, SessionId operatorSession) => new(CyclopsDroneZapKind.Apply, cyclopsId, droneId, position, radius, serial, operatorSession, CyclopsDroneZapFailure.None);

    public static CyclopsDroneZap Refused(NitroxId cyclopsId, NitroxId droneId, int serial, SessionId operatorSession, CyclopsDroneZapFailure failure) => new(CyclopsDroneZapKind.Refused, cyclopsId, droneId, NitroxVector3.Zero, 0f, serial, operatorSession, failure);
}
