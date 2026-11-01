using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The zapper of the Scout Drone (<see cref="CyclopsDroneZap" />): the operator asks; the server checks that the vehicle upgrades are on, that the sender sits at the drone console of a live Cyclops, that the drone
///     is the one the bay holds and is out of its dock, that the place named is where the drone is (<see cref="ScoutDroneRules.ZAP_POSITION_TOLERANCE" />), and that the Cyclops did not zap a moment ago
///     (<see cref="ScoutDroneRules.ZapServerIntervalMilliseconds" />, on the server's clock). It then tells everybody, with the radius of the server's setting; or refuses the operator alone. The energy is the operator's
///     client's to pay, when it hears its own zap. The cooldown is not given back when a zap turns out to be of no use.
/// </summary>
internal sealed class CyclopsDroneZapProcessor(
    CyclopsDroneArbiter arbiter,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsDroneZapProcessor> logger) : IAuthPacketProcessor<CyclopsDroneZap>
{
    private readonly CyclopsDroneArbiter arbiter = arbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsDroneZapProcessor> logger = logger;

    /// <summary>The clock of the cooldown, in milliseconds; a test sets its own.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    public async Task Process(AuthProcessorContext context, CyclopsDroneZap packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }
        if (packet.Kind != CyclopsDroneZapKind.Request)
        {
            // The apply and the refusal are the server's own: a client that sends one would zap for everybody, or refuse somebody else
            logger.ZLogWarning($"{context.Sender.Name} sent a drone zap message of the kind {packet.Kind}, which only the server may send; dropped");
            return;
        }

        Player sender = context.Sender;
        SessionId session = sender.SessionId;
        if (!session.IsPlayer || packet.DroneId == null)
        {
            return;
        }
        if (!options.Value.VehicleUpgrades || !IsLiveCyclops(packet.CyclopsId) || !CyclopsNavRules.IsValidPosition(packet.Position.X, packet.Position.Y, packet.Position.Z))
        {
            await Refuse(context, packet, session, CyclopsDroneZapFailure.NotAvailable);
            return;
        }
        if (!arbiter.IsOperator(session, packet.CyclopsId))
        {
            await Refuse(context, packet, session, CyclopsDroneZapFailure.NotOperator);
            return;
        }
        if (!arbiter.TryGetDrone(packet.CyclopsId, out NitroxId? held, out bool docked) || !held.Equals(packet.DroneId) || !entityRegistry.TryGetEntityById(packet.DroneId, out WorldEntity? drone))
        {
            await Refuse(context, packet, session, CyclopsDroneZapFailure.NotAvailable);
            return;
        }
        if (docked)
        {
            await Refuse(context, packet, session, CyclopsDroneZapFailure.NotOut);
            return;
        }
        if (!ScoutDroneRules.IsNearDrone(packet.Position.X - drone.Transform.Position.X, packet.Position.Y - drone.Transform.Position.Y, packet.Position.Z - drone.Transform.Position.Z))
        {
            await Refuse(context, packet, session, CyclopsDroneZapFailure.TooFar);
            return;
        }
        if (!arbiter.TryStartZap(packet.CyclopsId, Clock(), ScoutDroneRules.ZapServerIntervalMilliseconds(CooldownSeconds())))
        {
            await Refuse(context, packet, session, CyclopsDroneZapFailure.TooSoon);
            return;
        }

        logger.ZLogInformation($"{sender.Name} zapped with the Scout Drone of {packet.CyclopsId}");
        await context.SendToAllAsync(CyclopsDroneZap.Apply(packet.CyclopsId, packet.DroneId, packet.Position, Radius(), packet.Serial, session));
    }

    private static Task Refuse(AuthProcessorContext context, CyclopsDroneZap request, SessionId session, CyclopsDroneZapFailure failure)
    {
        return context.ReplyAsync(CyclopsDroneZap.Refused(request.CyclopsId, request.DroneId ?? new NitroxId(), request.Serial, session, failure));
    }

    private float CooldownSeconds()
    {
        SubnauticaServerOptions value = options.Value;
        return VehicleUpgradeSettingsRules.Clamp(nameof(value.ZapCooldownSeconds), value.ZapCooldownSeconds, VehicleUpgradeSettingsRules.MIN_ZAP_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_ZAP_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_ZAP_COOLDOWN_SECONDS, null);
    }

    private float Radius()
    {
        SubnauticaServerOptions value = options.Value;
        return VehicleUpgradeSettingsRules.Clamp(nameof(value.ZapRadius), value.ZapRadius, VehicleUpgradeSettingsRules.MIN_ZAP_RADIUS, VehicleUpgradeSettingsRules.MAX_ZAP_RADIUS, VehicleUpgradeSettingsRules.DEFAULT_ZAP_RADIUS, null);
    }

    /// <summary>The entity is a Cyclops that is not a wreck.</summary>
    private bool IsLiveCyclops(NitroxId id)
    {
        return entityRegistry.TryGetEntityById(id, out Entity? entity) && PowerTransferRules.IsCyclops(entity.TechType?.Name ?? "") && entity.Metadata is not CyclopsMetadata { IsDestroyed: true };
    }
}
