using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
/// What a scanner room scans for (the resource type picked on its screen).
/// </summary>
[Serializable]
[DataContract]
public class MapRoomMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public NitroxTechType TypeToScan { get; }

    [IgnoreConstructor]
    protected MapRoomMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public MapRoomMetadata(NitroxTechType typeToScan)
    {
        TypeToScan = typeToScan;
    }

    public override string ToString()
    {
        return $"[MapRoomMetadata TypeToScan: {TypeToScan}]";
    }
}
