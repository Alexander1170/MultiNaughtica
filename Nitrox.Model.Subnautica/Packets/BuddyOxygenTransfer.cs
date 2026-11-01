using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>The step of a buddy oxygen transfer (see <see cref="BuddyOxygenTransfer" />).</summary>
public enum BuddyOxygenKind : byte
{
    /// <summary>Not a step: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: the giver has taken <see cref="BuddyOxygenTransfer.Amount" /> out of their own tank and wants it to go to <see cref="BuddyOxygenTransfer.Receiver" />.</summary>
    Give = 1,

    /// <summary>Server to the receiver's client: add this amount to your own tank as far as it fits, and answer with an <see cref="Answer" />.</summary>
    Deliver = 2,

    /// <summary>Client to server: the receiver's client has added what fitted; <see cref="BuddyOxygenTransfer.Amount" /> is how much (possibly 0).</summary>
    Answer = 3,

    /// <summary>
    /// Server to the giver's client: how the step ended. <see cref="BuddyOxygenTransfer.Amount" /> is what the receiver took and <see cref="BuddyOxygenTransfer.Refund" /> is what goes back into the giver's
    /// tank (the rest of the step, or all of it when the server turned it down).
    /// </summary>
    Settle = 4
}

/// <summary>Why a step ended with less than it was given for, or with nothing.</summary>
public enum BuddyOxygenReason : byte
{
    None = 0,

    /// <summary>The receiver's tank was full, or filled up with part of the step.</summary>
    Full = 1,

    /// <summary>The receiver's client could not take air: the player is dead, at the surface, in a vehicle or a base, or the game is not ready.</summary>
    CannotBreatheMore = 2,

    /// <summary>The two players are too far apart (the server's position of them).</summary>
    OutOfRange = 3,

    /// <summary>The giver asked for more air per second than the rate allows.</summary>
    TooFast = 4,

    /// <summary>There is no such player, or they left, or the receiver is the giver.</summary>
    NoReceiver = 5,

    /// <summary>The giver already has as many steps waiting for an answer as the server allows.</summary>
    Busy = 6,

    /// <summary>The buddy oxygen is switched off on this server (<c>BuddyOxygenRate</c> 0).</summary>
    Off = 7
}

/// <summary>
/// Air that one diver gives to another (docs/coop/BUDDY_OXYGEN.md). A player's oxygen belongs to their own game, so it is a request and an answer between the two players, through the server, in small steps:
/// the giver takes the air out of their own tank first and says so (<see cref="BuddyOxygenKind.Give" />); the server checks who, how far apart and how fast, and passes it to the receiver
/// (<see cref="BuddyOxygenKind.Deliver" />), whose game adds as much as fits and answers (<see cref="BuddyOxygenKind.Answer" />); the server then tells the giver what the receiver took and what to put back
/// (<see cref="BuddyOxygenKind.Settle" />). Air is taken out first and added second, so a lost packet loses air and never makes any; what the giver lost is what the receiver got. One class for all four steps
/// so that a client can send only the two it may (the server drops the rest).
/// </summary>
[Serializable]
public class BuddyOxygenTransfer : Packet
{
    public BuddyOxygenKind Kind { get; }

    /// <summary>The server's number of the step (0 in a give).</summary>
    public long TransferId { get; }

    /// <summary>The giver. A client leaves it empty; the server puts the one of the connection in.</summary>
    public SessionId Giver { get; }

    /// <summary>The receiver: chosen by the giver's client in a give, the real one in everything else.</summary>
    public SessionId Receiver { get; }

    /// <summary>The name of the other player, for the text on the HUD: the giver's in a deliver, the receiver's in a settle. Empty in what a client sends.</summary>
    public string Name { get; }

    /// <summary>The air, in seconds of breathing: taken from the giver (give, deliver), accepted by the receiver (answer, settle).</summary>
    public float Amount { get; }

    /// <summary>For a <see cref="BuddyOxygenKind.Settle" />: what goes back into the giver's tank.</summary>
    public float Refund { get; }

    public BuddyOxygenReason Reason { get; }

    public BuddyOxygenTransfer(BuddyOxygenKind kind, long transferId, SessionId giver, SessionId receiver, string name, float amount, float refund, BuddyOxygenReason reason)
    {
        Kind = kind;
        TransferId = transferId;
        Giver = giver;
        Receiver = receiver;
        Name = name;
        Amount = amount;
        Refund = refund;
        Reason = reason;
    }

    public static BuddyOxygenTransfer Give(SessionId receiver, float amount) => new(BuddyOxygenKind.Give, 0, default, receiver, string.Empty, amount, 0f, BuddyOxygenReason.None);

    public static BuddyOxygenTransfer Deliver(long transferId, SessionId giver, SessionId receiver, string giverName, float amount) => new(BuddyOxygenKind.Deliver, transferId, giver, receiver, giverName, amount, 0f, BuddyOxygenReason.None);

    public static BuddyOxygenTransfer Answer(long transferId, float accepted, BuddyOxygenReason reason) => new(BuddyOxygenKind.Answer, transferId, default, default, string.Empty, accepted, 0f, reason);

    public static BuddyOxygenTransfer Settle(long transferId, SessionId giver, SessionId receiver, string receiverName, float accepted, float refund, BuddyOxygenReason reason) => new(BuddyOxygenKind.Settle, transferId, giver, receiver, receiverName, accepted, refund, reason);
}
