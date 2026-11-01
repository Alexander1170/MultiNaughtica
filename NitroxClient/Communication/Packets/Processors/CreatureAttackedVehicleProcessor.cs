using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Applies a hit from a creature simulated by another player to a vehicle we simulate (the server only sends it to the vehicle's simulating player), unless our own copy
///     of the creature has just hit the vehicle for the same hit (<see cref="CreatureVehicleHitMatcher" />).
/// </summary>
internal sealed class CreatureAttackedVehicleProcessor(CreatureVehicleHitMatcher hitMatcher) : IClientPacketProcessor<CreatureAttackedVehicle>
{
    private readonly CreatureVehicleHitMatcher hitMatcher = hitMatcher;

    public Task Process(ClientProcessorContext context, CreatureAttackedVehicle packet)
    {
        if (!NitroxEntity.TryGetObjectFrom(packet.VehicleId, out GameObject vehicleObject) || !vehicleObject.TryGetComponent(out LiveMixin liveMixin))
        {
            return Task.CompletedTask;
        }

        if (!hitMatcher.ShouldApply(packet.VehicleId, packet.CreatureId, CreatureVehicleHitMatcher.HitOrigin.Relayed, Time.time))
        {
            return Task.CompletedTask;
        }

        // The creature may not be loaded here, the dealer only matters for damage modifiers and death attribution
        NitroxEntity.TryGetObjectFrom(packet.CreatureId, out GameObject creatureObject);
        hitMatcher.IsApplyingRelayedHit = true;
        try
        {
            liveMixin.TakeDamage(packet.OriginalDamage, packet.Position.ToUnity(), packet.Type, creatureObject);
        }
        finally
        {
            hitMatcher.IsApplyingRelayedHit = false;
        }
        return Task.CompletedTask;
    }
}
