using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsTowPress" /> says.</summary>
public enum CyclopsTowPressKind : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: a player aboard pressed the tow hook button of a Cyclops that another player simulates.</summary>
    Press = 1,

    /// <summary>Server to the player who simulates the Cyclops: <see cref="CyclopsTowPress.Presser" /> pressed the button, do what a press at the helm would do.</summary>
    Forward = 2,

    /// <summary>To the player who pressed (from the server, or from the simulator by way of the server): the press did nothing, and why.</summary>
    Failed = 3
}

/// <summary>Why a press of the tow hook button did nothing (the message the presser reads).</summary>
public enum CyclopsTowFailure : byte
{
    Unknown = 0,

    /// <summary>The vehicle upgrades are off on this server, or the Cyclops has no tow hook.</summary>
    NotAvailable = 1,

    /// <summary>Nobody simulates the Cyclops (nobody is near enough to run it), so nobody can do the hooking.</summary>
    NoSimulator = 2,

    /// <summary>No Seamoth or Prawn Suit that nobody sits in is within reach of the hook.</summary>
    NothingInReach = 3,

    /// <summary>The vehicle that was chosen is in use (the server refused its simulation lock).</summary>
    InUse = 4,

    /// <summary>The server did not answer in time, or the vehicle changed while the hooking was going on.</summary>
    Interrupted = 5
}

/// <summary>
/// The tow hook button of a Cyclops (feature group 2): a button on the Cyclops' lighting panel, next to the lights and the power share button, that anybody aboard can press. The hooking itself is done
/// by the client that simulates the Cyclops (it takes the towed vehicle's simulation lock and pulls it, see <see cref="VehicleTowChanged" />), so a press from any other player travels
/// <see cref="CyclopsTowPressKind.Press" /> (to the server) &#8594; <see cref="CyclopsTowPressKind.Forward" /> (to the simulator) and, when it cannot be done, <see cref="CyclopsTowPressKind.Failed" /> back to
/// the player who pressed. The result of a hooking reaches everybody in <see cref="VehicleTowChanged" />, which is also what every client's button shows.
/// </summary>
[Serializable]
public class CyclopsTowPress : Packet
{
    public CyclopsTowPressKind Kind { get; }

    /// <summary>The Cyclops whose button was pressed.</summary>
    public NitroxId CyclopsId { get; }

    /// <summary>The player who pressed. A client leaves it empty in a press (the server fills in the sender's); the simulator names the player it answers in a failure.</summary>
    public SessionId Presser { get; }

    public CyclopsTowFailure Reason { get; }

    public CyclopsTowPress(CyclopsTowPressKind kind, NitroxId cyclopsId, SessionId presser, CyclopsTowFailure reason)
    {
        Kind = kind;
        CyclopsId = cyclopsId;
        Presser = presser;
        Reason = reason;
    }

    public static CyclopsTowPress Press(NitroxId cyclopsId) => new(CyclopsTowPressKind.Press, cyclopsId, default, CyclopsTowFailure.Unknown);

    public static CyclopsTowPress Forward(NitroxId cyclopsId, SessionId presser) => new(CyclopsTowPressKind.Forward, cyclopsId, presser, CyclopsTowFailure.Unknown);

    public static CyclopsTowPress Failed(NitroxId cyclopsId, SessionId presser, CyclopsTowFailure reason) => new(CyclopsTowPressKind.Failed, cyclopsId, presser, reason);
}
