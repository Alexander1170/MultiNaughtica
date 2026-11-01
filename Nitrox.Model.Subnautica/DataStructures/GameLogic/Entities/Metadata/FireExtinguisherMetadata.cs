using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
/// How much is left in a hand-held fire extinguisher (the one on the wall is <see cref="FireExtinguisherHolderMetadata"/>).
/// </summary>
[Serializable]
[DataContract]
public class FireExtinguisherMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public float Fuel { get; }

    [IgnoreConstructor]
    protected FireExtinguisherMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public FireExtinguisherMetadata(float fuel)
    {
        Fuel = fuel;
    }

    public override string ToString()
    {
        return $"[FireExtinguisherMetadata Fuel: {Fuel}]";
    }
}
