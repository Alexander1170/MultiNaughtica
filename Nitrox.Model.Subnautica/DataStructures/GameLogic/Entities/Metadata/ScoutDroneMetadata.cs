using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;
using Nitrox.Model.DataStructures;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
///     What the world keeps of a Scout Drone (docs/coop/VEHICLE_UPGRADES.md, station 4): the bay it belongs to and whether it sits in the dock, and its health. The bay (<see cref="CyclopsId" /> and
///     <see cref="Docked" />) is the server's: it writes them whenever the bay changes and keeps them when a client reports (a client report is only its <see cref="Health" />), so a world that is saved with a drone in
///     the bay loads with the bay holding it again (the bays themselves are in memory only). The health is the lock holder's, as a vehicle's: it travels to the other players and is saved, so a drone that changes hands
///     does not come back healed.
/// </summary>
[Serializable]
[DataContract]
public class ScoutDroneMetadata : EntityMetadata
{
    /// <summary>The health of a drone that is not known to be hurt (it has its full health).</summary>
    public const float HEALTH_UNKNOWN = -1f;

    /// <summary>The Cyclops whose bay holds the drone (whose dock it belongs to, docked or out flying). Null: the drone is in no bay.</summary>
    [DataMember(Order = 1)]
    public NitroxId CyclopsId { get; }

    /// <summary>The drone is in the dock (false: it is out, flown or adrift).</summary>
    [DataMember(Order = 2)]
    public bool Docked { get; }

    /// <summary>The health of the drone (<see cref="HEALTH_UNKNOWN" />: not known, which is full).</summary>
    [DataMember(Order = 3)]
    public float Health { get; }

    [IgnoreConstructor]
    protected ScoutDroneMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public ScoutDroneMetadata(NitroxId cyclopsId, bool docked, float health = HEALTH_UNKNOWN)
    {
        CyclopsId = cyclopsId;
        Docked = docked;
        Health = health;
    }

    public override string ToString()
    {
        return $"[ScoutDroneMetadata CyclopsId: {CyclopsId}, Docked: {Docked}, Health: {Health}]";
    }
}
