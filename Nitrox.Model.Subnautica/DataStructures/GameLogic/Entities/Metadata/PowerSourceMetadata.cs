using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
///     The stored power of a generator of a base (solar panel, bioreactor, nuclear reactor). Sent by the player who simulates the base so that every
///     client shows the same charge and a generator that spawns (join, remote build, resync) restores it instead of starting full. A bioreactor or a nuclear reactor also sends
///     how much of its current fuel item or rod it has burned (<see cref="ToConsume" />), so that the player who takes over the base carries on from there instead of
///     starting the item or rod again.
/// </summary>
[Serializable]
[DataContract]
public class PowerSourceMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public float Power { get; }

    [DataMember(Order = 2)]
    public float ToConsume { get; }

    [IgnoreConstructor]
    protected PowerSourceMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public PowerSourceMetadata(float power, float toConsume = 0f)
    {
        Power = power;
        ToConsume = toConsume;
    }

    public override string ToString()
    {
        return $"[{nameof(PowerSourceMetadata)} {nameof(Power)}: {Power}, {nameof(ToConsume)}: {ToConsume}]";
    }
}
