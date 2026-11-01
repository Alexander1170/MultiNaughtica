using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// A player who does not simulate a base used power of its relay for something only their client runs (B10b: crafting in a fabricator). The charge of a base's generators is
/// kept by the player who simulates the base (<c>PowerSourceMetadata</c>) and overwrites everybody else's value within seconds, so the draw has to reach that player to count.
/// </summary>
[Serializable]
public class BasePowerConsumed : Packet
{
    public NitroxId BaseId { get; }
    public float Amount { get; }

    public BasePowerConsumed(NitroxId baseId, float amount)
    {
        BaseId = baseId;
        Amount = amount;
    }
}
