using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsDroneSeat" /> says.</summary>
public enum CyclopsDroneSeatMessage : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: I want to sit at the drone console of this Cyclops.</summary>
    Take = 1,

    /// <summary>Client to server: I leave the console.</summary>
    Leave = 2,

    /// <summary>Server to everybody (the operator included): this player sits at the console now. Also what a player who joins is told, once for each console that is taken.</summary>
    Seated = 3,

    /// <summary>Server to everybody (the operator included): this player does not sit at the console any more; <see cref="CyclopsDroneSeat.Reason" /> says why.</summary>
    Released = 4,

    /// <summary>Server to the player who asked: you cannot sit there; <see cref="CyclopsDroneSeat.Reason" /> says why, and for <see cref="CyclopsDroneSeatReason.Occupied" /> the session and the name are the occupant's.</summary>
    Denied = 5
}

/// <summary>Why a seat was given up or refused.</summary>
public enum CyclopsDroneSeatReason : byte
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
    SeatedElsewhere = 9,

    // for Released
    /// <summary>The drone of the bay was destroyed (or taken out of the bay for good): there is nothing to fly.</summary>
    DroneDestroyed = 10
}

/// <summary>
/// Who sits at the drone console of a Cyclops (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 4): one player at a time, the first to ask wins, the seat is given up when the player leaves the console,
/// leaves the Cyclops, dies, disconnects or the Cyclops is destroyed. The server keeps the seats (<c>CyclopsDroneArbiter</c>) and tells everybody, so that a player who joins later learns who sits where. The player's
/// session and name are the server's own (taken from the connection), whatever a client wrote in them. Not saved with the world.
/// </summary>
[Serializable]
public class CyclopsDroneSeat : Packet
{
    public CyclopsDroneSeatMessage Message { get; }

    public NitroxId CyclopsId { get; }

    /// <summary>The operator. A client leaves it empty; the server fills it in.</summary>
    public SessionId SessionId { get; }

    /// <summary>The operator's name, as the server knows it (empty in a request).</summary>
    public string PlayerName { get; }

    public CyclopsDroneSeatReason Reason { get; }

    public CyclopsDroneSeat(CyclopsDroneSeatMessage message, NitroxId cyclopsId, SessionId sessionId, string playerName, CyclopsDroneSeatReason reason)
    {
        Message = message;
        CyclopsId = cyclopsId;
        SessionId = sessionId;
        PlayerName = playerName ?? string.Empty;
        Reason = reason;
    }

    public static CyclopsDroneSeat Take(NitroxId cyclopsId) => new(CyclopsDroneSeatMessage.Take, cyclopsId, default, string.Empty, CyclopsDroneSeatReason.None);

    public static CyclopsDroneSeat Leave(NitroxId cyclopsId) => new(CyclopsDroneSeatMessage.Leave, cyclopsId, default, string.Empty, CyclopsDroneSeatReason.None);
}
