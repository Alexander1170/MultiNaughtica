using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class EntityReparented : Packet
{
    public NitroxId Id { get; }

    public NitroxId NewParentId { get; }

    /// <summary>
    ///     Size in inventory slots of the moved item. 0 when unknown.
    /// </summary>
    public int ItemWidth { get; }

    public int ItemHeight { get; }

    /// <summary>
    ///     Size in inventory slots of the container the item was moved into. 0 when the new parent is not an item container,
    ///     in which case the server does not check the capacity.
    /// </summary>
    public int ContainerWidth { get; }

    public int ContainerHeight { get; }

    public EntityReparented(NitroxId id, NitroxId newParentId, int itemWidth = 0, int itemHeight = 0, int containerWidth = 0, int containerHeight = 0)
    {
        Id = id;
        NewParentId = newParentId;
        ItemWidth = itemWidth;
        ItemHeight = itemHeight;
        ContainerWidth = containerWidth;
        ContainerHeight = containerHeight;
    }
}
