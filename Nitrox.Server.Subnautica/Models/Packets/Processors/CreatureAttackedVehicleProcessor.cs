using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Forwards a creature's hit on a vehicle to the vehicle's simulating player, if the sender really simulates the creature.
/// </summary>
internal sealed class CreatureAttackedVehicleProcessor(SimulationOwnershipData simulationOwnershipData) : IAuthPacketProcessor<CreatureAttackedVehicle>
{
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    public async Task Process(AuthProcessorContext context, CreatureAttackedVehicle packet)
    {
        // Negative damage would repair the vehicle (NaN fails the comparison)
        if (!(packet.OriginalDamage > 0f) || float.IsInfinity(packet.OriginalDamage))
        {
            return;
        }
        if (simulationOwnershipData.GetPlayerForLock(packet.CreatureId) != context.Sender)
        {
            return;
        }

        // When the sender also simulates the vehicle it applied the hit itself, and nobody can apply it when nobody simulates the vehicle
        Player? vehicleSimulator = simulationOwnershipData.GetPlayerForLock(packet.VehicleId);
        if (vehicleSimulator == null || vehicleSimulator == context.Sender)
        {
            return;
        }

        await context.SendAsync(packet, vehicleSimulator.SessionId);
    }
}
