using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsGunnerFire" /> says.</summary>
public enum CyclopsGunnerFireKind : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: the gunner released the charge and wants to fire.</summary>
    Request = 1,

    /// <summary>Server to the player who simulates the Cyclops (which may be the gunner): pay for this shot of <see cref="CyclopsGunnerFire.Gunner" /> and say whether it was paid.</summary>
    Forward = 2,

    /// <summary>The answer: from the simulator by way of the server, or the server's own refusal, to the gunner alone. <see cref="CyclopsGunnerFailure.None" /> means paid: fire now.</summary>
    Result = 3
}

/// <summary>Why a shot did not happen.</summary>
public enum CyclopsGunnerFailure : byte
{
    /// <summary>No failure: the shot is paid, the gunner may fire.</summary>
    None = 0,

    /// <summary>The vehicle upgrades are off, the entity is no live Cyclops or the ammo is not known.</summary>
    NotAvailable = 1,

    /// <summary>The sender does not sit at the gunner desk of this Cyclops.</summary>
    NotSeated = 2,

    /// <summary>Nobody simulates the Cyclops, so nobody can pay for the shot.</summary>
    NoSimulator = 3,

    /// <summary>The Cyclops has no Gunner Station module (checked by the simulator, which can see the upgrade console).</summary>
    NoModule = 4,

    /// <summary>The Cyclops has not the power that the shot costs.</summary>
    NoPower = 5,

    /// <summary>The Cyclops fired a moment ago (the server keeps the cooldown, and so does the simulator).</summary>
    TooSoon = 6
}

/// <summary>What the gunner fires. Only the stasis sphere exists; this is the place for a second kind (decoys) later, so that the packet does not change.</summary>
public enum CyclopsGunnerAmmo : byte
{
    StasisSphere = 0
}

/// <summary>
/// A shot of the gunner station (docs/coop/VEHICLE_UPGRADES.md, station 2). The flight of the sphere travels by the packets of the Stasis Rifle (<see cref="StasisSphereShot" /> and <see cref="StasisSphereHit" />);
/// this packet is the permission and the payment that come before it. The gunner asks (<see cref="CyclopsGunnerFireKind.Request" />); the server checks the seat and the cooldown and passes it to the player whose
/// client simulates the Cyclops (<see cref="CyclopsGunnerFireKind.Forward" />), because the energy of a Cyclops is only changed by that client (the sonar ping works the same way); the simulator pays and answers
/// (<see cref="CyclopsGunnerFireKind.Result" />) by way of the server, which tells the gunner alone. Only when the answer is <see cref="CyclopsGunnerFailure.None" /> does the gunner's client launch the sphere.
/// </summary>
[Serializable]
public class CyclopsGunnerFire : Packet
{
    public CyclopsGunnerFireKind Kind { get; }

    public NitroxId CyclopsId { get; }

    /// <summary>The gunner's own count of its requests, echoed in the forward and the result, so that an answer is matched to the request it answers (a late answer to an old request is not taken for the new one).</summary>
    public int Shot { get; }

    /// <summary>The gunner. A client leaves it empty in a request (the server uses the connection); the simulator names the gunner it answers in the result.</summary>
    public SessionId Gunner { get; }

    /// <summary>How far the shot was charged, 0 to 1; the energy of the shot is worked out from it (<c>GunnerRules.ShotEnergy</c>).</summary>
    public float ChargeNormalized { get; }

    public CyclopsGunnerAmmo Ammo { get; }

    /// <summary>For a <see cref="CyclopsGunnerFireKind.Result" />: <see cref="CyclopsGunnerFailure.None" /> when the shot was paid.</summary>
    public CyclopsGunnerFailure Failure { get; }

    public CyclopsGunnerFire(CyclopsGunnerFireKind kind, NitroxId cyclopsId, int shot, SessionId gunner, float chargeNormalized, CyclopsGunnerAmmo ammo, CyclopsGunnerFailure failure)
    {
        Kind = kind;
        CyclopsId = cyclopsId;
        Shot = shot;
        Gunner = gunner;
        ChargeNormalized = chargeNormalized;
        Ammo = ammo;
        Failure = failure;
    }

    public static CyclopsGunnerFire Request(NitroxId cyclopsId, int shot, float chargeNormalized, CyclopsGunnerAmmo ammo) => new(CyclopsGunnerFireKind.Request, cyclopsId, shot, default, chargeNormalized, ammo, CyclopsGunnerFailure.None);

    public static CyclopsGunnerFire Forward(NitroxId cyclopsId, int shot, SessionId gunner, float chargeNormalized, CyclopsGunnerAmmo ammo) => new(CyclopsGunnerFireKind.Forward, cyclopsId, shot, gunner, chargeNormalized, ammo, CyclopsGunnerFailure.None);

    public static CyclopsGunnerFire Result(NitroxId cyclopsId, int shot, SessionId gunner, float chargeNormalized, CyclopsGunnerAmmo ammo, CyclopsGunnerFailure failure) => new(CyclopsGunnerFireKind.Result, cyclopsId, shot, gunner, chargeNormalized, ammo, failure);
}
