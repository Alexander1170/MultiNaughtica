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
///     The shots of the gunner station (<see cref="CyclopsGunnerFire" />): the flight of a stasis sphere travels by the packets of the Stasis Rifle, so this is the permission and the payment that come before it. The
///     gunner asks (<see cref="CyclopsGunnerFireKind.Request" />); the server checks that the sender sits at the desk of a live Cyclops and that the Cyclops did not fire a moment ago
///     (<see cref="CyclopsGunnerRules.ServerMinIntervalMilliseconds" />, 80 percent of the cooldown, so that a client that does not keep its own cooldown cannot make the simulator pay and play shots as often as it likes),
///     then passes it to the player whose client simulates the Cyclops, because the energy of a Cyclops is only changed by that client (the sonar ping works the same way). The simulator pays and answers; the server
///     tells the gunner alone, and only for a shot it forwarded. The cooldown is not given back when a payment fails (a refund would let a modified client flood the simulator for nothing).
/// </summary>
internal sealed class CyclopsGunnerFireProcessor(
    CyclopsGunnerArbiter arbiter,
    SimulationOwnershipData simulationOwnershipData,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsGunnerFireProcessor> logger) : IAuthPacketProcessor<CyclopsGunnerFire>
{
    private readonly record struct Pending(SessionId Gunner, SessionId Simulator, long At);

    private readonly CyclopsGunnerArbiter arbiter = arbiter;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsGunnerFireProcessor> logger = logger;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, long> lastForwarded = [];
    private readonly Dictionary<(NitroxId CyclopsId, int Shot), Pending> pending = [];

    /// <summary>The clock of the cooldown and of the wait for an answer, in milliseconds; a test sets its own.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    public async Task Process(AuthProcessorContext context, CyclopsGunnerFire packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }

        switch (packet.Kind)
        {
            case CyclopsGunnerFireKind.Request:
                await OnRequest(context, packet);
                break;
            case CyclopsGunnerFireKind.Result:
                await OnResult(context, packet);
                break;
            default:
                // The forward is the server's own: a client that sends one would be asking for a payment on behalf of somebody else
                logger.ZLogWarning($"{context.Sender.Name} sent a gunner fire message of the kind {packet.Kind}, which only the server may send; dropped");
                break;
        }
    }

    private async Task OnRequest(AuthProcessorContext context, CyclopsGunnerFire request)
    {
        SessionId gunner = context.Sender.SessionId;
        if (!gunner.IsPlayer)
        {
            return;
        }
        if (!CyclopsGunnerRules.IsUsableCharge(request.ChargeNormalized))
        {
            logger.ZLogWarning($"{context.Sender.Name} sent a shot with a charge that is no number; dropped");
            return;
        }
        float charge = CyclopsGunnerRules.ClampCharge(request.ChargeNormalized);

        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(request.CyclopsId) || !CyclopsGunnerRules.IsKnownAmmo(request.Ammo))
        {
            await Refuse(context, request, gunner, charge, CyclopsGunnerFailure.NotAvailable);
            return;
        }
        if (!arbiter.IsGunner(gunner, request.CyclopsId))
        {
            await Refuse(context, request, gunner, charge, CyclopsGunnerFailure.NotSeated);
            return;
        }

        Player? simulator = simulationOwnershipData.GetPlayerForLock(request.CyclopsId);
        if (simulator == null || !simulator.IsOnline)
        {
            await Refuse(context, request, gunner, charge, CyclopsGunnerFailure.NoSimulator);
            return;
        }
        if (!TryReserve(request.CyclopsId, request.Shot, simulator.SessionId, gunner))
        {
            await Refuse(context, request, gunner, charge, CyclopsGunnerFailure.TooSoon);
            return;
        }

        // Also when the simulator is the gunner: one path for every case, and the cooldown is the server's in every case
        await context.SendAsync(CyclopsGunnerFire.Forward(request.CyclopsId, request.Shot, gunner, charge, request.Ammo), simulator.SessionId);
    }

    private async Task OnResult(AuthProcessorContext context, CyclopsGunnerFire result)
    {
        // Not "the player who holds the lock now": the lock may have moved while the shot was paid (the pilot sat down, the simulator left); the answer counts if it is from the player who was asked
        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(result.CyclopsId) || !Enum.IsDefined(result.Failure) || !CyclopsGunnerRules.IsKnownAmmo(result.Ammo))
        {
            logger.ZLogDebug($"{context.Sender.Name} reported a gunner shot that cannot be one (not a live Cyclops, the upgrades off, an ammo or a failure that does not exist); dropped");
            return;
        }
        if (!TryTakePending(result.CyclopsId, result.Shot, context.Sender.SessionId, result.Gunner) || !arbiter.IsGunner(result.Gunner, result.CyclopsId))
        {
            logger.ZLogDebug($"{context.Sender.Name} answered a gunner shot that nobody asked for (or whose gunner has left); dropped");
            return;
        }

        await context.SendAsync(CyclopsGunnerFire.Result(result.CyclopsId, result.Shot, result.Gunner, CyclopsGunnerRules.ClampCharge(result.ChargeNormalized), result.Ammo, result.Failure), result.Gunner);
    }

    private static Task Refuse(AuthProcessorContext context, CyclopsGunnerFire request, SessionId gunner, float charge, CyclopsGunnerFailure failure)
    {
        return context.ReplyAsync(CyclopsGunnerFire.Result(request.CyclopsId, request.Shot, gunner, charge, request.Ammo, failure));
    }

    /// <summary>Whether this Cyclops may fire now; if so, now is the time of its last shot and the answer of the simulator is expected.</summary>
    private bool TryReserve(NitroxId cyclopsId, int shot, SessionId simulator, SessionId gunner)
    {
        long now = Clock();
        long interval = CyclopsGunnerRules.ServerMinIntervalMilliseconds(CooldownSeconds());
        lock (gate)
        {
            if (lastForwarded.TryGetValue(cyclopsId, out long last) && now - last < interval)
            {
                return false;
            }
            lastForwarded[cyclopsId] = now;
            // Answers that never came are forgotten
            foreach ((NitroxId CyclopsId, int Shot) old in pending.Where(pair => now - pair.Value.At > CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS).Select(pair => pair.Key).ToList())
            {
                pending.Remove(old);
            }
            pending[(cyclopsId, shot)] = new Pending(gunner, simulator, now);
            if (lastForwarded.Count > 32)
            {
                // Cyclopses come and go: forget those whose last shot is old enough not to matter
                foreach (NitroxId old in lastForwarded.Where(pair => now - pair.Value >= Math.Max(interval, CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS)).Select(pair => pair.Key).ToList())
                {
                    lastForwarded.Remove(old);
                }
            }
            return true;
        }
    }

    /// <summary>The answer of the simulator belongs to a shot that was forwarded to it for this gunner and is not too old; each shot is answered once.</summary>
    private bool TryTakePending(NitroxId cyclopsId, int shot, SessionId simulator, SessionId gunner)
    {
        long now = Clock();
        lock (gate)
        {
            if (!pending.TryGetValue((cyclopsId, shot), out Pending waiting) || waiting.Simulator != simulator || waiting.Gunner != gunner || now - waiting.At > CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS)
            {
                return false;
            }
            pending.Remove((cyclopsId, shot));
            return true;
        }
    }

    private float CooldownSeconds()
    {
        SubnauticaServerOptions value = options.Value;
        return VehicleUpgradeSettingsRules.Clamp(nameof(value.GunnerCooldownSeconds), value.GunnerCooldownSeconds, VehicleUpgradeSettingsRules.MIN_GUNNER_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_GUNNER_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_GUNNER_COOLDOWN_SECONDS, null);
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
