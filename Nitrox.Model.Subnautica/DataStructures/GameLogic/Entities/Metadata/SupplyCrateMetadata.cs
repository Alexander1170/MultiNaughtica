using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
/// Whether a supply crate (the Aurora and Degasi crates with a first aid kit, a battery, disinfected water) is open: the open crate, and the item that its player took, were only
/// seen by the player who opened it.
/// </summary>
[Serializable]
[DataContract]
public class SupplyCrateMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public bool Open { get; }

    [IgnoreConstructor]
    protected SupplyCrateMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public SupplyCrateMetadata(bool open)
    {
        Open = open;
    }

    public override string ToString()
    {
        return $"[{nameof(SupplyCrateMetadata)} Open: {Open}]";
    }
}
