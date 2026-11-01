using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A creature simulated by the sender hit the sender's copy of another player. The server sends it to the victim, who
///     applies the hit to their own player: remote players' bodies are invincible, so without it only the creature's
///     simulating player could ever be hurt.
/// </summary>
[Serializable]
public class CreatureAttackedPlayer : Packet
{
    public SessionId VictimSessionId { get; }
    public NitroxId CreatureId { get; }

    /// <summary>
    ///     Damage before DamageSystem.CalculateDamage, which the victim runs again through LiveMixin.TakeDamage.
    /// </summary>
    public float OriginalDamage { get; }

    public DamageType Type { get; }
    public NitroxVector3 Position { get; }

    public CreatureAttackedPlayer(SessionId victimSessionId, NitroxId creatureId, float originalDamage, DamageType type, NitroxVector3 position)
    {
        VictimSessionId = victimSessionId;
        CreatureId = creatureId;
        OriginalDamage = originalDamage;
        Type = type;
        Position = position;
    }
}
