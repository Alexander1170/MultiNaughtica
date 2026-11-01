using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>The step of a power transfer between two vehicles that different players simulate (see <see cref="PowerTransfer" />).</summary>
public enum PowerTransferKind : byte
{
    /// <summary>Not a step: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: the player who simulates the RECEIVER asks for an amount.</summary>
    Request = 1,

    /// <summary>Server to the player who simulates the GIVER: take out what you can, up to this amount, and answer with a <see cref="Grant" />.</summary>
    Offer = 2,

    /// <summary>Client to server: the giver's player has taken the energy out of the giver and says how much it was (<see cref="PowerTransfer.Amount" />, possibly 0).</summary>
    Grant = 3,

    /// <summary>Server to the player who simulates the receiver NOW: add this amount. Always carries the outcome, also when nothing is delivered.</summary>
    Delivery = 4,

    /// <summary>
    /// Client to server: the receiver's player could not use all of a delivery (the receiver was full meanwhile) and gives the rest back to the giver. It carries the number of that delivery
    /// (<see cref="PowerTransfer.TransferId" />): the server takes one return per delivery, from the player it delivered to, of at most the delivered amount.
    /// </summary>
    Return = 5,

    /// <summary>Server to the player who simulates the giver: put this amount back into the giver (nobody could use it).</summary>
    Refund = 6
}

/// <summary>What a transfer is for. The giver and the receiver are different kinds of vehicle in the two modes.</summary>
public enum PowerTransferMode : byte
{
    Unknown = 0,

    /// <summary>A Cyclops with the power share module charges a Seamoth or a Prawn Suit near it: giver = the Cyclops, receiver = the vehicle.</summary>
    Forward = 1,

    /// <summary>A Cyclops that runs low draws on a vehicle docked in it: giver = the vehicle, receiver = the Cyclops.</summary>
    Emergency = 2
}

/// <summary>How a transfer ended, as the requester hears it in the <see cref="PowerTransferKind.Delivery" />.</summary>
public enum PowerTransferOutcome : byte
{
    /// <summary>Not an answer: the value of a field that was never filled in. Never taken for a grant.</summary>
    Unknown = 0,

    /// <summary>The giver took the energy out and the amount of the packet is here to be added.</summary>
    Granted = 1,

    /// <summary>The giver gave nothing (not enough power, the module is gone, out of range, the vehicle is at its reserve) or the answer was late.</summary>
    Nothing = 2,

    /// <summary>Nobody simulates the giver, or the player who does left.</summary>
    NoGiver = 3,

    /// <summary>The request makes no sense (an amount that is not a positive number, the wrong kinds of vehicle, the sender does not simulate the receiver) or the vehicle upgrades are off.</summary>
    Invalid = 4
}

/// <summary>
/// Moves energy from one vehicle to another when different players simulate them (feature group 2, the Cyclops power share). A vehicle's energy may only be changed by the player who simulates
/// it, so the transfer is a request and a grant between the two simulators, through the server: the simulator of the RECEIVER asks (<see cref="PowerTransferKind.Request" />), the server passes it
/// on to the simulator of the GIVER (<see cref="PowerTransferKind.Offer" />), who takes out what it can and says how much (<see cref="PowerTransferKind.Grant" />); the server then delivers that
/// amount to whoever simulates the receiver at that moment (<see cref="PowerTransferKind.Delivery" />). Energy is taken out first and added second, so a lost packet loses energy and never makes
/// any; what the receiver cannot use goes back (<see cref="PowerTransferKind.Return" />, <see cref="PowerTransferKind.Refund" />). When one player simulates both nothing is sent at all.
/// One class for all six steps so that a client can send only the two it may (the server drops the rest).
/// </summary>
[Serializable]
public class PowerTransfer : Packet
{
    public PowerTransferKind Kind { get; }

    /// <summary>The server's number of the transfer (0 in a request); the grant of the giver carries it back, and so does the return of what a delivery could not use.</summary>
    public long TransferId { get; }

    /// <summary>The vehicle (or the Cyclops) that gives the energy.</summary>
    public NitroxId GiverId { get; }

    /// <summary>The vehicle (or the Cyclops) that gets it.</summary>
    public NitroxId ReceiverId { get; }

    /// <summary>The energy: asked for, offered, granted, delivered, returned or refunded, depending on <see cref="Kind" />.</summary>
    public float Amount { get; }

    public PowerTransferMode Mode { get; }

    /// <summary>For a <see cref="PowerTransferKind.Delivery" />: how the transfer ended.</summary>
    public PowerTransferOutcome Outcome { get; }

    public PowerTransfer(PowerTransferKind kind, long transferId, NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode, PowerTransferOutcome outcome)
    {
        Kind = kind;
        TransferId = transferId;
        GiverId = giverId;
        ReceiverId = receiverId;
        Amount = amount;
        Mode = mode;
        Outcome = outcome;
    }

    public static PowerTransfer Request(NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode) => new(PowerTransferKind.Request, 0, giverId, receiverId, amount, mode, PowerTransferOutcome.Unknown);

    public static PowerTransfer Offer(long transferId, NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode) => new(PowerTransferKind.Offer, transferId, giverId, receiverId, amount, mode, PowerTransferOutcome.Unknown);

    public static PowerTransfer Grant(long transferId, NitroxId giverId, NitroxId receiverId, float granted, PowerTransferMode mode) => new(PowerTransferKind.Grant, transferId, giverId, receiverId, granted, mode, PowerTransferOutcome.Unknown);

    public static PowerTransfer Delivery(long transferId, NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode, PowerTransferOutcome outcome) => new(PowerTransferKind.Delivery, transferId, giverId, receiverId, amount, mode, outcome);

    public static PowerTransfer Return(long transferId, NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode) => new(PowerTransferKind.Return, transferId, giverId, receiverId, amount, mode, PowerTransferOutcome.Unknown);

    public static PowerTransfer Refund(NitroxId giverId, NitroxId receiverId, float amount, PowerTransferMode mode) => new(PowerTransferKind.Refund, 0, giverId, receiverId, amount, mode, PowerTransferOutcome.Unknown);
}
