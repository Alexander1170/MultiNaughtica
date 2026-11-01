using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A creature simulated by the sender bit a vehicle (or Cyclops) whose health only the vehicle's simulating player may change.
///     The server sends it to that player, who applies it to the vehicle.
/// </summary>
[Serializable]
public class CreatureAttackedVehicle : Packet
{
    public NitroxId VehicleId { get; }
    public NitroxId CreatureId { get; }

    /// <summary>
    ///     Damage before DamageSystem.CalculateDamage, which the receiver runs again through LiveMixin.TakeDamage.
    /// </summary>
    public float OriginalDamage { get; }

    public DamageType Type { get; }
    public NitroxVector3 Position { get; }

    public CreatureAttackedVehicle(NitroxId vehicleId, NitroxId creatureId, float originalDamage, DamageType type, NitroxVector3 position)
    {
        VehicleId = vehicleId;
        CreatureId = creatureId;
        OriginalDamage = originalDamage;
        Type = type;
        Position = position;
    }
}
