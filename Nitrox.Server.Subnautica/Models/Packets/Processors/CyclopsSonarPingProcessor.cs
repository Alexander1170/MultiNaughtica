using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The sonar ping of the navigator console (<see cref="CyclopsSonarPing" />): the energy of a Cyclops is only changed by the player who simulates it, so a press of another player is passed to that player with the
///     presser's session on it (taken from the connection, never from the packet), and the result that the simulator reports goes on to everybody (a fired ping) or to the player who pressed (a failure). The server
///     makes sure that a press only goes to the Cyclops' real simulator, that a Cyclops is not pinged more than once every <see cref="MIN_INTERVAL_MILLISECONDS" /> (the client keeps five seconds; a client that
///     does not would otherwise make the simulator pay and play a ping as often as it likes) and that only the simulator can send a result, for a Cyclops, with a failure that exists.
/// </summary>
internal sealed class CyclopsSonarPingProcessor(
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsSonarPingProcessor> logger) : IAuthPacketProcessor<CyclopsSonarPing>
{
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsSonarPingProcessor> logger = logger;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, long> lastForwarded = [];

    /// <summary>The least time between two pings of one Cyclops (a little under the five seconds that the client keeps, so that the two never disagree about a press that is just in time).</summary>
    public const long MIN_INTERVAL_MILLISECONDS = 4000;

    /// <summary>The clock of the rate limit, in milliseconds; a test sets its own.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    public async Task Process(AuthProcessorContext context, CyclopsSonarPing packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        switch (packet.Kind)
        {
            case CyclopsSonarPingKind.Press:
                await OnPress(context, packet);
                break;
            case CyclopsSonarPingKind.Result:
                await OnResult(context, packet);
                break;
            default:
                // The forward is the server's own: a client that sends one would be pressing a button for somebody else
                logger.ZLogWarning($"{context.Sender.Name} sent a sonar ping message of the kind {packet.Kind}, which only the server may send; dropped");
                break;
        }
    }

    private async Task OnPress(AuthProcessorContext context, CyclopsSonarPing press)
    {
        if (!options.Value.VehicleUpgrades || !IsCyclops(press))
        {
            await context.ReplyAsync(CyclopsSonarPing.Result(press.CyclopsId, context.Sender.SessionId, CyclopsSonarFailure.NotAvailable));
            return;
        }

        Player? simulator = simulationOwnershipData.GetPlayerForLock(press.CyclopsId);
        if (simulator == null || !simulator.IsOnline)
        {
            await context.ReplyAsync(CyclopsSonarPing.Result(press.CyclopsId, context.Sender.SessionId, CyclopsSonarFailure.NoSimulator));
            return;
        }

        if (!TryReserve(press.CyclopsId))
        {
            await context.ReplyAsync(CyclopsSonarPing.Result(press.CyclopsId, context.Sender.SessionId, CyclopsSonarFailure.TooSoon));
            return;
        }

        await context.SendAsync(CyclopsSonarPing.Forward(press.CyclopsId, context.Sender.SessionId), simulator.SessionId);
    }

    /// <summary>Whether a ping of this Cyclops may go ahead now; if so, now is the time of its last one.</summary>
    private bool TryReserve(NitroxId cyclopsId)
    {
        long now = Clock();
        lock (gate)
        {
            if (lastForwarded.TryGetValue(cyclopsId, out long last) && now - last < MIN_INTERVAL_MILLISECONDS)
            {
                return false;
            }
            lastForwarded[cyclopsId] = now;
            if (lastForwarded.Count > 32)
            {
                // Cyclopses come and go: forget those whose last ping is old enough not to matter
                foreach (NitroxId old in lastForwarded.Where(pair => now - pair.Value >= MIN_INTERVAL_MILLISECONDS).Select(pair => pair.Key).ToList())
                {
                    lastForwarded.Remove(old);
                }
            }
            return true;
        }
    }

    private async Task OnResult(AuthProcessorContext context, CyclopsSonarPing result)
    {
        if (simulationOwnershipData.GetPlayerForLock(result.CyclopsId) != context.Sender)
        {
            logger.ZLogDebug($"{context.Sender.Name} reported a sonar ping of a Cyclops it does not simulate; dropped");
            return;
        }
        if (!options.Value.VehicleUpgrades || !IsCyclops(result) || !Enum.IsDefined(result.Failure))
        {
            logger.ZLogDebug($"{context.Sender.Name} reported a sonar ping that cannot be one (not a Cyclops, the upgrades off or a failure that does not exist); dropped");
            return;
        }

        if (result.Failure == CyclopsSonarFailure.None)
        {
            // Everybody else sees the wave (the one who fired it has played it already)
            await context.SendToOthersAsync(CyclopsSonarPing.Result(result.CyclopsId, result.Presser, CyclopsSonarFailure.None));
            return;
        }
        // A failure only matters to the player who asked, and only when somebody else did
        if (result.Presser.IsPlayer && result.Presser != context.Sender.SessionId)
        {
            await context.SendAsync(CyclopsSonarPing.Result(result.CyclopsId, result.Presser, result.Failure), result.Presser);
        }
    }

    private bool IsCyclops(CyclopsSonarPing packet)
    {
        return entityRegistry.TryGetEntityById(packet.CyclopsId, out Entity? cyclops) && PowerTransferRules.IsCyclops(cyclops.TechType?.Name ?? "");
    }
}
