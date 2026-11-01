using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Forwards the power a player used from a base they do not simulate to the player who does (B10b), so that the draw is part of the charge that player keeps.
/// </summary>
internal sealed class BasePowerConsumedProcessor(SimulationOwnershipData simulationOwnershipData) : IAuthPacketProcessor<BasePowerConsumed>
{
    /// <summary>
    ///     Most that one report may take. The fabricator takes 5 per craft; the cap only keeps a broken or hostile client from draining a base with one packet.
    /// </summary>
    public const float MAX_AMOUNT = 100f;

    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    public async Task Process(AuthProcessorContext context, BasePowerConsumed packet)
    {
        // A negative amount would charge the base (NaN fails the comparison)
        if (!(packet.Amount > 0f) || packet.Amount > MAX_AMOUNT)
        {
            return;
        }

        // When the sender also simulates the base it used the power itself, and nobody can book it when nobody simulates the base
        Player? baseSimulator = simulationOwnershipData.GetPlayerForLock(packet.BaseId);
        if (baseSimulator == null || baseSimulator == context.Sender)
        {
            return;
        }

        await context.SendAsync(packet, baseSimulator.SessionId);
    }
}
