using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Routes the power transfers between vehicles that different players simulate (<see cref="PowerTransfer" />, the Cyclops power share): the request of the player who simulates the receiver goes to the
///     player who simulates the giver as an offer, the giver's grant goes to whoever simulates the receiver by then, and what nobody can use goes back to the giver. The server decides nothing about
///     energy; it checks who may say what (only the simulator of the receiver may ask, only the player the offer went to may grant, only once) and keeps the books in <see cref="PowerTransferLedger" />.
///     A client may only send the requests, grants and returns; the other kinds are the server's and are dropped.
/// </summary>
internal sealed class PowerTransferProcessor(
    PowerTransferLedger ledger,
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<PowerTransferProcessor> logger) : IAuthPacketProcessor<PowerTransfer>
{
    private readonly PowerTransferLedger ledger = ledger;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<PowerTransferProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, PowerTransfer packet)
    {
        switch (packet.Kind)
        {
            case PowerTransferKind.Request:
                await OnRequest(context, packet);
                break;
            case PowerTransferKind.Grant:
                await OnGrant(context, packet);
                break;
            case PowerTransferKind.Return:
                await OnReturn(context, packet);
                break;
            default:
                // The offers, deliveries and refunds are the server's own: a client that sends one would be handing out energy to a vehicle
                logger.ZLogWarning($"{context.Sender.Name} sent a power transfer of the kind {packet.Kind}, which only the server may send; dropped");
                break;
        }
    }

    private async Task OnRequest(AuthProcessorContext context, PowerTransfer request)
    {
        if (!options.Value.VehicleUpgrades || !IsSane(request) || !RolesFit(request) || simulationOwnershipData.GetPlayerForLock(request.ReceiverId) != context.Sender)
        {
            await context.ReplyAsync(Answer(request, 0, 0f, PowerTransferOutcome.Invalid));
            return;
        }

        Player? giverHolder = simulationOwnershipData.GetPlayerForLock(request.GiverId);
        if (giverHolder == null || !giverHolder.IsOnline)
        {
            await context.ReplyAsync(Answer(request, 0, 0f, PowerTransferOutcome.NoGiver));
            return;
        }
        if (giverHolder == context.Sender)
        {
            // The sender simulates both ends by now (a lock moved while its request was on the way): it moves the energy itself at its next turn
            await context.ReplyAsync(Answer(request, 0, 0f, PowerTransferOutcome.Nothing));
            return;
        }

        if (ledger.OpenCountFor(context.Sender.SessionId) >= PowerTransferRules.MAX_OPEN_PER_REQUESTER)
        {
            logger.ZLogDebug($"{context.Sender.Name} has {PowerTransferRules.MAX_OPEN_PER_REQUESTER} power transfers open already; the request is answered with nothing");
            await context.ReplyAsync(Answer(request, 0, 0f, PowerTransferOutcome.Nothing));
            return;
        }

        PowerTransferLedger.Entry entry = ledger.Open(context.Sender.SessionId, giverHolder.SessionId, request.GiverId, request.ReceiverId, request.Amount, request.Mode);
        await context.SendAsync(PowerTransfer.Offer(entry.TransferId, entry.GiverId, entry.ReceiverId, entry.Amount, entry.Mode), giverHolder.SessionId);
    }

    private async Task OnGrant(AuthProcessorContext context, PowerTransfer grant)
    {
        switch (ledger.Close(grant.TransferId, context.Sender.SessionId, grant.Amount, out PowerTransferLedger.Entry entry, out float amount))
        {
            case PowerTransferLedger.CloseResult.Delivered:
                if (!(amount > 0f))
                {
                    // Nothing to deliver: tell the player who asked, so that it can ask again at once
                    await context.SendAsync(Answer(entry, amount, PowerTransferOutcome.Nothing), entry.Requester);
                    return;
                }

                Player? receiverHolder = simulationOwnershipData.GetPlayerForLock(entry.ReceiverId);
                if (receiverHolder != null && receiverHolder.IsOnline)
                {
                    // Remembered before it is sent: what this player gives back is checked against it
                    ledger.RecordDelivery(entry, receiverHolder.SessionId, amount);
                    await context.SendAsync(Answer(entry, amount, PowerTransferOutcome.Granted), receiverHolder.SessionId);
                }
                else
                {
                    // Nobody simulates the receiver any more: the energy goes back where it came from
                    logger.ZLogDebug($"Power transfer {entry.TransferId}: nobody simulates the receiver, refunding {amount} to whoever simulates the giver");
                    await RefundAsync(context, entry, amount);
                }
                break;
            case PowerTransferLedger.CloseResult.Late:
                if (amount > 0f)
                {
                    logger.ZLogDebug($"Power transfer {entry.TransferId}: the grant of {context.Sender.Name} came too late, refunding {amount}");
                    await RefundAsync(context, entry, amount);
                }
                break;
            default:
                logger.ZLogDebug($"{context.Sender.Name} granted power for the unknown transfer {grant.TransferId}; ignored");
                break;
        }
    }

    /// <summary>The energy a giver took out goes back to whoever simulates the giver now (its lock may have moved while the energy was on its way), else to the player who granted it.</summary>
    private async Task RefundAsync(AuthProcessorContext context, PowerTransferLedger.Entry entry, float amount)
    {
        Player? holder = simulationOwnershipData.GetPlayerForLock(entry.GiverId);
        SessionId target = holder != null && holder.IsOnline ? holder.SessionId : context.Sender.SessionId;
        await context.SendAsync(PowerTransfer.Refund(entry.GiverId, entry.ReceiverId, amount, entry.Mode), target);
    }

    private async Task OnReturn(AuthProcessorContext context, PowerTransfer returned)
    {
        // Only what the server delivered to this very player can come back, once, and at most as much as was delivered; the vehicles and the mode are the delivery's own, not the packet's
        if (!ledger.TryReturn(returned.TransferId, context.Sender.SessionId, returned.Amount, out PowerTransferLedger.Entry entry, out float amount))
        {
            logger.ZLogDebug($"{context.Sender.Name} returned {returned.Amount} for the transfer {returned.TransferId}, which was not delivered to them or was returned before; ignored");
            return;
        }

        // It goes to whoever simulates the giver now
        Player? giverHolder = simulationOwnershipData.GetPlayerForLock(entry.GiverId);
        if (giverHolder == null || !giverHolder.IsOnline)
        {
            return;
        }
        await context.SendAsync(PowerTransfer.Refund(entry.GiverId, entry.ReceiverId, amount, entry.Mode), giverHolder.SessionId);
    }

    private static bool IsSane(PowerTransfer request)
    {
        return PowerTransferRules.IsValidAmount(request.Amount) && request.GiverId != null && request.ReceiverId != null && !request.GiverId.Equals(request.ReceiverId);
    }

    /// <summary>The giver and the receiver have to be the right kinds of vehicle for the mode (a Cyclops gives to a Seamoth or Prawn Suit, or the other way round).</summary>
    private bool RolesFit(PowerTransfer request)
    {
        return entityRegistry.TryGetEntityById(request.GiverId, out Entity? giver) &&
               entityRegistry.TryGetEntityById(request.ReceiverId, out Entity? receiver) &&
               PowerTransferRules.RolesFit(request.Mode, giver.TechType?.Name ?? "", receiver.TechType?.Name ?? "");
    }

    private static PowerTransfer Answer(PowerTransfer request, long transferId, float amount, PowerTransferOutcome outcome)
    {
        return PowerTransfer.Delivery(transferId, request.GiverId, request.ReceiverId, amount, request.Mode, outcome);
    }

    private static PowerTransfer Answer(PowerTransferLedger.Entry entry, float amount, PowerTransferOutcome outcome)
    {
        return PowerTransfer.Delivery(entry.TransferId, entry.GiverId, entry.ReceiverId, amount, entry.Mode, outcome);
    }
}
