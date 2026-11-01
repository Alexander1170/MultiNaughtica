using System;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// One end of an energy transfer: a Cyclops, or a Seamoth or Prawn Suit. The Unity-backed ends are in <see cref="PowerShare" />; the arithmetic of a transfer is here, so that it can be tested
/// with plain numbers. A <b>giver</b> is only asked to <see cref="Take" />, a <b>receiver</b> to <see cref="Give" />; both ends of a vehicle have both, because a refund puts energy back into a giver.
/// </summary>
public interface IPowerEndpoint
{
    /// <summary>
    /// Takes out up to <paramref name="offered" /> energy, as far as the giver may part with it (a Cyclops keeps its floor, a docked vehicle its reserve), and returns what it really took out.
    /// Never more than offered, never negative.
    /// </summary>
    float Take(float offered);

    /// <summary>Adds up to <paramref name="amount" /> energy and returns what was really stored (less when it is nearly full). Never more than given, never negative.</summary>
    float Give(float amount);
}

/// <summary>
/// The order of a transfer, which is what makes it safe: the energy is taken out of the giver first and added to the receiver second, and what the receiver could not use goes back, so that
/// energy is never made (a receiver gets only what a giver lost) and, as long as the packets arrive, never lost either.
/// </summary>
public static class PowerTransfers
{
    /// <summary>
    /// A transfer where one client simulates both ends (nothing is sent). Returns the energy the receiver stored. A giver that gave nothing, or a receiver that stored nothing, leaves both
    /// ends as they were.
    /// </summary>
    public static float Move(IPowerEndpoint giver, IPowerEndpoint receiver, float requested)
    {
        if (!PowerTransferRules.IsValidAmount(requested))
        {
            return 0f;
        }

        float taken = Clean(giver.Take(requested), requested);
        if (taken <= 0f)
        {
            return 0f;
        }

        float stored = Clean(receiver.Give(taken), taken);
        float rest = taken - stored;
        if (rest > 0f)
        {
            giver.Give(rest);
        }
        return stored;
    }

    /// <summary>What a receiver does with a delivery: stores what it can and returns how much of it was not used, which has to go back to the giver (0 when all was stored).</summary>
    public static float Deliver(IPowerEndpoint receiver, float delivered)
    {
        if (!(delivered > 0f))
        {
            return 0f;
        }
        float stored = Clean(receiver.Give(delivered), delivered);
        return delivered - stored;
    }

    /// <summary>
    /// The giver's side of a transfer between two clients: takes the energy out of the giver (at most the offer, and at most <paramref name="mostAllowed" />, the most the giver's rules let one request
    /// ask for) and returns the grant to send. A giver that is not there, or an offer that is no amount, grants 0, which the server needs all the same: it ends the transfer.
    /// </summary>
    public static PowerTransfer AnswerOffer(PowerTransfer offer, IPowerEndpoint giver, float mostAllowed)
    {
        float granted = 0f;
        if (giver != null && PowerTransferRules.IsValidAmount(offer.Amount))
        {
            granted = Clean(giver.Take(Math.Min(offer.Amount, mostAllowed)), offer.Amount);
        }
        return PowerTransfer.Grant(offer.TransferId, offer.GiverId, offer.ReceiverId, granted, offer.Mode);
    }

    /// <summary>
    /// The receiver's side: adds a delivery to the receiver and returns the packet that gives back what it could not use (null when all was used or nothing was delivered). <paramref name="stored" />
    /// is what the receiver took.
    /// </summary>
    public static PowerTransfer ApplyDelivery(PowerTransfer delivery, IPowerEndpoint receiver, out float stored)
    {
        stored = 0f;
        if (delivery.Outcome != PowerTransferOutcome.Granted || !PowerTransferRules.IsValidAmount(delivery.Amount) || receiver == null)
        {
            return null;
        }
        float rest = Deliver(receiver, delivery.Amount);
        stored = delivery.Amount - rest;
        return rest >= PowerTransferRules.MIN_AMOUNT ? PowerTransfer.Return(delivery.TransferId, delivery.GiverId, delivery.ReceiverId, rest, delivery.Mode) : null;
    }

    /// <summary>The giver's side of a refund: puts the energy back into the giver. Returns what it took.</summary>
    public static float ApplyRefund(PowerTransfer refund, IPowerEndpoint giver)
    {
        return giver != null && PowerTransferRules.IsValidAmount(refund.Amount) ? Clean(giver.Give(refund.Amount), refund.Amount) : 0f;
    }

    /// <summary>What an end reports is clamped to what it was asked: a number that is not positive, or NaN, is 0, and nothing is more than the limit.</summary>
    private static float Clean(float reported, float limit)
    {
        return reported > 0f ? Math.Min(reported, limit) : 0f;
    }
}
