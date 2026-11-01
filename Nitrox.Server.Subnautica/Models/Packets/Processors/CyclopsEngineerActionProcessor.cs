using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The buttons of the engineer console that the server has to see (<see cref="CyclopsEngineerAction" />). Sending the power to a route needs the server only: it checks the seat, keeps the route and tells
///     everybody (<see cref="CyclopsEngineerState" />). The overdrive and the vent act on the engine, so after the server has checked the seat, the route and its own cooldown (the server's clock is the only one that
///     everybody can believe) it passes them to the player whose client simulates the Cyclops, because the engine, its heat and its energy only exist there (the sonar ping works the same way). The simulator carries
///     it out and answers; only then does the server start the cooldown, tell everybody and answer the engineer, alone, and only for an action it forwarded. A modified client cannot get anything for nothing: a press that
///     the server did not forward is not answered, and a second press while one is out is turned down.
/// </summary>
internal sealed class CyclopsEngineerActionProcessor(
    CyclopsEngineerArbiter arbiter,
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsEngineerActionProcessor> logger) : IAuthPacketProcessor<CyclopsEngineerAction>
{
    private readonly record struct Pending(SessionId Engineer, SessionId Simulator, int Counter, long At);

    private readonly CyclopsEngineerArbiter arbiter = arbiter;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsEngineerActionProcessor> logger = logger;
    private readonly Lock gate = new();
    private readonly Dictionary<(NitroxId CyclopsId, CyclopsEngineerActionKind Action), long> notBefore = [];
    private readonly Dictionary<(NitroxId CyclopsId, CyclopsEngineerActionKind Action), Pending> pending = [];

    /// <summary>The clock of the floor between two presses and of the wait for an answer, in milliseconds; a test sets its own.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    public async Task Process(AuthProcessorContext context, CyclopsEngineerAction packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        switch (packet.Step)
        {
            case CyclopsEngineerActionStep.Request:
                await OnRequest(context, packet);
                break;
            case CyclopsEngineerActionStep.Result:
                await OnResult(context, packet);
                break;
            default:
                // The forward is the server's own: a client that sends one would be asking the simulator to act on behalf of somebody else
                logger.ZLogWarning($"{context.Sender.Name} sent an engineer action message of the kind {packet.Step}, which only the server may send; dropped");
                break;
        }
    }

    private async Task OnRequest(AuthProcessorContext context, CyclopsEngineerAction request)
    {
        SessionId engineer = context.Sender.SessionId;
        if (!engineer.IsPlayer)
        {
            return;
        }

        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(request.CyclopsId) || !Enum.IsDefined(request.Action) || request.Action == CyclopsEngineerActionKind.Unknown)
        {
            await Refuse(context, request, engineer, CyclopsEngineerFailure.NotAvailable);
            return;
        }
        if (!arbiter.IsEngineer(engineer, request.CyclopsId))
        {
            await Refuse(context, request, engineer, CyclopsEngineerFailure.NotSeated);
            return;
        }

        if (request.Action == CyclopsEngineerActionKind.Route)
        {
            await OnRoute(context, request, engineer);
            return;
        }

        // The cooldown and the route are the server's
        (double now, double overdriveReadyAt, double ventReadyAt) = arbiter.Cooldowns(request.CyclopsId);
        CyclopsEngineerFailure check = request.Action == CyclopsEngineerActionKind.Overdrive
            ? CyclopsEngineerRules.ServerCheckOverdrive(arbiter.RouteOf(request.CyclopsId), now, overdriveReadyAt)
            : CyclopsEngineerRules.ServerCheckVent(now, ventReadyAt);
        if (check != CyclopsEngineerFailure.None)
        {
            await Refuse(context, request, engineer, check);
            return;
        }

        Player? simulator = simulationOwnershipData.GetPlayerForLock(request.CyclopsId);
        if (simulator == null || !simulator.IsOnline)
        {
            await Refuse(context, request, engineer, CyclopsEngineerFailure.NoSimulator);
            return;
        }
        if (!TryReserve(request.CyclopsId, request.Action, request.Counter, simulator.SessionId, engineer))
        {
            await Refuse(context, request, engineer, CyclopsEngineerFailure.TooSoon);
            return;
        }

        // Also when the simulator is the engineer: one path for every case
        await context.SendAsync(CyclopsEngineerAction.Forward(request.Action, request.CyclopsId, request.Counter, engineer), simulator.SessionId);
    }

    private async Task OnRoute(AuthProcessorContext context, CyclopsEngineerAction request, SessionId engineer)
    {
        if (!CyclopsEngineerRules.IsKnownRoute(request.Value))
        {
            await Refuse(context, request, engineer, CyclopsEngineerFailure.NotAvailable);
            return;
        }
        if (!TryAccept(request.CyclopsId, CyclopsEngineerActionKind.Route))
        {
            await Refuse(context, request, engineer, CyclopsEngineerFailure.TooSoon);
            return;
        }
        if (arbiter.SetRoute(request.CyclopsId, CyclopsEngineerRules.RouteFrom(request.Value)))
        {
            logger.ZLogInformation($"{context.Sender.Name} sent the power of {request.CyclopsId} to {CyclopsEngineerRules.RouteFrom(request.Value)}");
            await context.SendToAllAsync(arbiter.StateOf(request.CyclopsId));
        }
        // (the engineer's client waits for an answer to every button; the change itself reached it with everybody else)
        await context.ReplyAsync(CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Route, request.CyclopsId, request.Counter, engineer, CyclopsEngineerFailure.None, request.Value));
    }

    private async Task OnResult(AuthProcessorContext context, CyclopsEngineerAction result)
    {
        // Not "the player who holds the lock now": the lock may have moved while the action was carried out (the pilot sat down, the simulator left); the answer counts if it is from the player who was asked
        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(result.CyclopsId) || !Enum.IsDefined(result.Failure) || (result.Action != CyclopsEngineerActionKind.Overdrive && result.Action != CyclopsEngineerActionKind.Vent))
        {
            logger.ZLogDebug($"{context.Sender.Name} reported an engineer action that cannot be one (not a live Cyclops, the upgrades off, an action or a failure that does not exist); dropped");
            return;
        }
        if (!TryComplete(result, context.Sender.SessionId, out CyclopsEngineerFailure failure))
        {
            logger.ZLogDebug($"{context.Sender.Name} answered an engineer action that nobody asked for; dropped");
            return;
        }

        if (failure == CyclopsEngineerFailure.None)
        {
            // The simulator carried it out: from now on the cooldown runs (TryComplete started it), and everybody learns of it (also when the engineer has left in the meantime, the engine has been changed)
            await context.SendToAllAsync(arbiter.StateOf(result.CyclopsId));
        }

        if (arbiter.IsEngineer(result.Engineer, result.CyclopsId))
        {
            await context.SendAsync(CyclopsEngineerAction.Result(result.Action, result.CyclopsId, result.Counter, result.Engineer, failure, CyclopsEngineerRules.ClampHeat(result.Value)), result.Engineer);
        }
    }

    private static Task Refuse(AuthProcessorContext context, CyclopsEngineerAction request, SessionId engineer, CyclopsEngineerFailure failure)
    {
        return context.ReplyAsync(CyclopsEngineerAction.Result(request.Action, request.CyclopsId, request.Counter, engineer, failure));
    }

    /// <summary>
    ///     The floor between two presses of one kind on one Cyclops: a client that does not keep its own must not make the server and the others work as often as it likes. A route and a press have floors of
    ///     their own (a click on a route a moment after a press is no repeat of it).
    /// </summary>
    private bool TryAccept(NitroxId cyclopsId, CyclopsEngineerActionKind action)
    {
        long now = Clock();
        lock (gate)
        {
            if (!FloorOpen(cyclopsId, action, now))
            {
                return false;
            }
            StampFloor(cyclopsId, action, now);
            return true;
        }
    }

    /// <summary>
    ///     Whether this action may go to the simulator now: the floor of its kind is kept and no action of the same kind of this Cyclops has been waiting for its answer for less than the wait. The floor is stamped only
    ///     when the action goes (a press that was turned down for a pending one pushes nothing out).
    /// </summary>
    private bool TryReserve(NitroxId cyclopsId, CyclopsEngineerActionKind action, int counter, SessionId simulator, SessionId engineer)
    {
        long now = Clock();
        lock (gate)
        {
            // Answers that are too old even for a late answer are forgotten
            foreach ((NitroxId CyclopsId, CyclopsEngineerActionKind Action) old in pending.Where(pair => now - pair.Value.At > CyclopsEngineerRules.LATE_RESULT_MILLISECONDS).Select(pair => pair.Key).ToList())
            {
                pending.Remove(old);
            }
            if (!FloorOpen(cyclopsId, action, now))
            {
                return false;
            }
            // An action that waits for its answer holds the next one of its kind back; after the wait it does not, and its late answer is taken until a newer press replaces it
            if (pending.TryGetValue((cyclopsId, action), out Pending waiting) && now - waiting.At <= CyclopsEngineerRules.PENDING_FORWARD_MILLISECONDS)
            {
                return false;
            }
            StampFloor(cyclopsId, action, now);
            pending[(cyclopsId, action)] = new Pending(engineer, simulator, counter, now);
            return true;
        }
    }

    /// <summary>
    ///     The answer of the simulator belongs to an action that was forwarded to it for this engineer and is not older than a late answer may be (the simulator has paid for it by then); each action is answered once.
    ///     The cooldown starts here, under the lock that takes the action off the list, so that no second press can see "nothing out and no cooldown" in between. <paramref name="failure" /> is what became of it:
    ///     the simulator's own, or <see cref="CyclopsEngineerFailure.WrongRoute" /> when the route moved away while the overdrive was out. A press that came to nothing is not forwarded again at once.
    /// </summary>
    private bool TryComplete(CyclopsEngineerAction result, SessionId simulator, out CyclopsEngineerFailure failure)
    {
        long now = Clock();
        failure = result.Failure;
        lock (gate)
        {
            (NitroxId, CyclopsEngineerActionKind) key = (result.CyclopsId, result.Action);
            if (!pending.TryGetValue(key, out Pending waiting) || waiting.Simulator != simulator || waiting.Engineer != result.Engineer || waiting.Counter != result.Counter || now - waiting.At > CyclopsEngineerRules.LATE_RESULT_MILLISECONDS)
            {
                return false;
            }
            pending.Remove(key);
            if (failure == CyclopsEngineerFailure.None)
            {
                failure = result.Action == CyclopsEngineerActionKind.Overdrive
                    ? arbiter.CommitOverdrive(result.CyclopsId, OverdriveSeconds(), OverdriveCooldownSeconds())
                    : arbiter.CommitVent(result.CyclopsId, VentCooldownSeconds());
            }
            if (failure != CyclopsEngineerFailure.None)
            {
                notBefore[key] = now + CyclopsEngineerRules.REFUSED_PRESS_FLOOR_MILLISECONDS;
            }
            return true;
        }
    }

    private bool FloorOpen(NitroxId cyclopsId, CyclopsEngineerActionKind action, long now)
    {
        return !notBefore.TryGetValue((cyclopsId, action), out long until) || now >= until;
    }

    private void StampFloor(NitroxId cyclopsId, CyclopsEngineerActionKind action, long now)
    {
        notBefore[(cyclopsId, action)] = now + CyclopsEngineerRules.MIN_SERVER_INTERVAL_MILLISECONDS;
        if (notBefore.Count > 64)
        {
            // Cyclopses come and go: forget the floors that are over
            foreach ((NitroxId CyclopsId, CyclopsEngineerActionKind Action) old in notBefore.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToList())
            {
                notBefore.Remove(old);
            }
        }
    }

    private float OverdriveSeconds()
    {
        SubnauticaServerOptions value = options.Value;
        return VehicleUpgradeSettingsRules.Clamp(nameof(value.OverdriveSeconds), value.OverdriveSeconds, VehicleUpgradeSettingsRules.MIN_OVERDRIVE_SECONDS, VehicleUpgradeSettingsRules.MAX_OVERDRIVE_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_SECONDS, null);
    }

    private float OverdriveCooldownSeconds()
    {
        SubnauticaServerOptions value = options.Value;
        return VehicleUpgradeSettingsRules.Clamp(nameof(value.OverdriveCooldownSeconds), value.OverdriveCooldownSeconds, VehicleUpgradeSettingsRules.MIN_OVERDRIVE_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_OVERDRIVE_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_COOLDOWN_SECONDS, null);
    }

    private float VentCooldownSeconds()
    {
        SubnauticaServerOptions value = options.Value;
        return VehicleUpgradeSettingsRules.Clamp(nameof(value.VentCooldownSeconds), value.VentCooldownSeconds, VehicleUpgradeSettingsRules.MIN_VENT_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_VENT_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_VENT_COOLDOWN_SECONDS, null);
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
