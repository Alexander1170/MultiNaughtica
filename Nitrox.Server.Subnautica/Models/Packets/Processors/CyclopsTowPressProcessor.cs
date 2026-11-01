using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The tow hook button of a Cyclops (<see cref="CyclopsTowPress" />): the hooking is done by the player who simulates the Cyclops, so a press of another player is passed to that player with the
///     presser's name on it (taken from the connection, never from the packet), and a failure that the simulator reports goes on to the player who pressed. The server decides nothing about the tow
///     itself (that is <see cref="VehicleTowChangedProcessor" />); it makes sure that a press only goes to the Cyclops' real simulator and that only the simulator can send a failure.
/// </summary>
internal sealed class CyclopsTowPressProcessor(
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsTowPressProcessor> logger) : IAuthPacketProcessor<CyclopsTowPress>
{
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsTowPressProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CyclopsTowPress packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        switch (packet.Kind)
        {
            case CyclopsTowPressKind.Press:
                await OnPress(context, packet);
                break;
            case CyclopsTowPressKind.Failed:
                await OnFailed(context, packet);
                break;
            default:
                // The forward is the server's own: a client that sends one would be pressing a button for somebody else
                logger.ZLogWarning($"{context.Sender.Name} sent a tow button message of the kind {packet.Kind}, which only the server may send; dropped");
                break;
        }
    }

    private async Task OnPress(AuthProcessorContext context, CyclopsTowPress press)
    {
        if (!options.Value.VehicleUpgrades || !IsCyclops(press))
        {
            await context.ReplyAsync(CyclopsTowPress.Failed(press.CyclopsId, context.Sender.SessionId, CyclopsTowFailure.NotAvailable));
            return;
        }

        Player? simulator = simulationOwnershipData.GetPlayerForLock(press.CyclopsId);
        if (simulator == null || !simulator.IsOnline)
        {
            await context.ReplyAsync(CyclopsTowPress.Failed(press.CyclopsId, context.Sender.SessionId, CyclopsTowFailure.NoSimulator));
            return;
        }

        // Also when the presser is the simulator itself (a lock moved while the press was on its way): its client handles the forward like any other
        await context.SendAsync(CyclopsTowPress.Forward(press.CyclopsId, context.Sender.SessionId), simulator.SessionId);
    }

    private async Task OnFailed(AuthProcessorContext context, CyclopsTowPress failed)
    {
        if (failed.Reason == CyclopsTowFailure.Unknown || simulationOwnershipData.GetPlayerForLock(failed.CyclopsId) != context.Sender)
        {
            logger.ZLogDebug($"{context.Sender.Name} reported a failed tow button press of a Cyclops it does not simulate; dropped");
            return;
        }
        await context.SendAsync(CyclopsTowPress.Failed(failed.CyclopsId, failed.Presser, failed.Reason), failed.Presser);
    }

    private bool IsCyclops(CyclopsTowPress press)
    {
        return entityRegistry.TryGetEntityById(press.CyclopsId, out Entity? cyclops) && (cyclops.TechType?.Name ?? "") == PowerTransferRules.CYCLOPS_TECH;
    }
}
