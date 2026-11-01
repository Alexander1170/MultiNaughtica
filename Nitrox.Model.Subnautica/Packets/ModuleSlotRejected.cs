using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Server to the one client whose module install into a slot of a vehicle, Cyclops console, charger, ... lost against another player's install of the same slot at the same moment
///     (V0, U4). The server did not register the install and did not forward it: the client takes the module back out of the slot and puts it into the player's inventory.
/// </summary>
[Serializable]
public class ModuleSlotRejected : Packet
{
    /// <summary>
    ///     The module that the client installed and that has to leave the slot again.
    /// </summary>
    public NitroxId ItemId { get; }

    /// <summary>
    ///     The owner of the equipment (vehicle, upgrade console, ...) the install was meant for.
    /// </summary>
    public NitroxId ParentId { get; }

    public string Slot { get; }

    public ModuleSlotRejected(NitroxId itemId, NitroxId parentId, string slot)
    {
        ItemId = itemId;
        ParentId = parentId;
        Slot = slot;
    }
}
