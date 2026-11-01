using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsGunnerSeat" /> says.</summary>
public enum CyclopsGunnerSeatMessage : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: I want to sit at the gunner desk of this Cyclops.</summary>
    Take = 1,

    /// <summary>Client to server: I leave the desk.</summary>
    Leave = 2,

    /// <summary>Server to everybody (the gunner included): this player sits at the desk now. Also what a player who joins is told, once for each desk that is taken.</summary>
    Seated = 3,

    /// <summary>Server to everybody (the gunner included): this player does not sit at the desk any more; <see cref="CyclopsGunnerSeat.Reason" /> says why.</summary>
    Released = 4,

    /// <summary>Server to the player who asked: you cannot sit there; <see cref="CyclopsGunnerSeat.Reason" /> says why, and for <see cref="CyclopsGunnerSeatReason.Occupied" /> the session and the name are the occupant's.</summary>
    Denied = 5
}

/// <summary>Why a seat was given up or refused.</summary>
public enum CyclopsGunnerSeatReason : byte
{
    None = 0,

    // for Released
    Left = 1,
    LeftCyclops = 2,
    Died = 3,
    Disconnected = 4,
    CyclopsDestroyed = 5,

    // for Denied
    Unavailable = 6,
    NotAboard = 7,
    Occupied = 8,
    SeatedElsewhere = 9
}

/// <summary>
/// Who sits at the gunner desk of a Cyclops (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 2): one player at a time, the first to ask wins, the seat is given up when the player leaves the desk, leaves the
/// Cyclops, dies, disconnects or the Cyclops is destroyed. The server keeps the seats (<c>CyclopsGunnerArbiter</c>) and tells everybody, so that a player who joins later learns who sits where. The player's session and
/// name are the server's own (taken from the connection), whatever a client wrote in them. Not saved with the world.
/// </summary>
[Serializable]
public class CyclopsGunnerSeat : Packet
{
    public CyclopsGunnerSeatMessage Message { get; }

    public NitroxId CyclopsId { get; }

    /// <summary>The gunner. A client leaves it empty; the server fills it in.</summary>
    public SessionId SessionId { get; }

    /// <summary>The gunner's name, as the server knows it (empty in a request).</summary>
    public string PlayerName { get; }

    public CyclopsGunnerSeatReason Reason { get; }

    public CyclopsGunnerSeat(CyclopsGunnerSeatMessage message, NitroxId cyclopsId, SessionId sessionId, string playerName, CyclopsGunnerSeatReason reason)
    {
        Message = message;
        CyclopsId = cyclopsId;
        SessionId = sessionId;
        PlayerName = playerName ?? string.Empty;
        Reason = reason;
    }

    public static CyclopsGunnerSeat Take(NitroxId cyclopsId) => new(CyclopsGunnerSeatMessage.Take, cyclopsId, default, string.Empty, CyclopsGunnerSeatReason.None);

    public static CyclopsGunnerSeat Leave(NitroxId cyclopsId) => new(CyclopsGunnerSeatMessage.Leave, cyclopsId, default, string.Empty, CyclopsGunnerSeatReason.None);
}
