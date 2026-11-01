using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Applies a hit from a creature simulated by another player: their copy of the creature hit our remote body there.
/// </summary>
internal sealed class CreatureAttackedPlayerProcessor : IClientPacketProcessor<CreatureAttackedPlayer>
{
    public Task Process(ClientProcessorContext context, CreatureAttackedPlayer packet)
    {
        if (!Player.main || !Player.main.liveMixin)
        {
            return Task.CompletedTask;
        }

        // Same conditions as for a bite on the local player in vanilla (MeleeAttack.CanDealDamageTo): not inside a base or sub, not just spawned, not invisible
        if (!Player.main.CanBeAttacked())
        {
            Log.Debug($"[CreatureAttackedPlayer] ignored a hit by creature {packet.CreatureId}: the local player can't be attacked right now");
            return Task.CompletedTask;
        }

        // The creature may not be loaded here, the dealer only matters for damage modifiers and death attribution
        NitroxEntity.TryGetObjectFrom(packet.CreatureId, out GameObject creatureObject);
        float healthBefore = Player.main.liveMixin.health;
        Player.main.liveMixin.TakeDamage(packet.OriginalDamage, packet.Position.ToUnity(), packet.Type, creatureObject);
        Log.Info($"[CreatureAttackedPlayer] bitten by creature {packet.CreatureId} ({(creatureObject ? creatureObject.name : "not loaded")}): damage {packet.OriginalDamage}, health {healthBefore:F1} -> {Player.main.liveMixin.health:F1}");
        return Task.CompletedTask;
    }
}
