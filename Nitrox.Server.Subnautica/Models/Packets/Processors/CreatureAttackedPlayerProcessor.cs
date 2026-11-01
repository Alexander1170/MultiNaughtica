using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Forwards a creature's hit on a player to that player, if the sender really simulates the creature.
///     Unlike <see cref="PvPAttackProcessor" /> this does not depend on the PvP setting, creatures are not players.
/// </summary>
internal sealed class CreatureAttackedPlayerProcessor(SimulationOwnershipData simulationOwnershipData) : IAuthPacketProcessor<CreatureAttackedPlayer>
{
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    public async Task Process(AuthProcessorContext context, CreatureAttackedPlayer packet)
    {
        // Negative damage would heal the victim (NaN fails the comparison)
        if (!(packet.OriginalDamage > 0f) || float.IsInfinity(packet.OriginalDamage))
        {
            return;
        }
        if (packet.VictimSessionId == context.Sender.SessionId || simulationOwnershipData.GetPlayerForLock(packet.CreatureId) != context.Sender)
        {
            return;
        }

        await context.SendAsync(packet, packet.VictimSessionId);
    }
}
