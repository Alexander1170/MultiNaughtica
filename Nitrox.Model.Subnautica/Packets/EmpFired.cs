using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// A Crabsquid fired its EMP blast (K18). Only the client that simulates the creature runs the attack; the blast it makes only touches what is on that client (vehicles, tools, a
/// Cyclops), so the other clients make the same blast at their copy of the creature.
/// </summary>
[Serializable]
public class EmpFired : Packet
{
    public NitroxId CreatureId { get; }

    public EmpFired(NitroxId creatureId)
    {
        CreatureId = creatureId;
    }
}
