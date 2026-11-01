using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Sync test harness (Debug builds): server asks the receiving client to hit the nearest creature of a tech type with its local player as the
///     dealer, like a knife hit would. Used to test the creature health and death sync across lock hand-overs. Sent by <c>/damagecreature</c>.
/// </summary>
[Serializable]
public class DebugDamageCreature(string techType, float damage, float radius, int hits, Optional<NitroxId> targetId) : Packet
{
    public string TechType { get; } = techType;
    public float Damage { get; } = damage;

    /// <summary>
    ///     Creatures further than this (meters) from the local player are ignored.
    /// </summary>
    public float Radius { get; } = radius;

    /// <summary>
    ///     Number of hits dealt back to back in the same frame (a burst, like a knife or drill hitting repeatedly).
    /// </summary>
    public int Hits { get; } = hits;

    /// <summary>
    ///     The creature to hit, when the server knows it; otherwise the nearest creature of the tech type within <see cref="Radius" />.
    /// </summary>
    public Optional<NitroxId> TargetId { get; } = targetId;
}
