using System.Collections.Concurrent;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Decides whether an item still fits into an item container (locker, bioreactor, filtration machine, ...), so that two
///     players filling the same container at the same moment cannot leave it over capacity: each client checks the room only
///     against its own copy, and the other client's add arrives later and used to be applied with <c>UnsafeAdd</c> without any check.
/// </summary>
/// <remarks>
///     The server has no game data, so the sizes come from the clients: every move into a container carries the size of the item
///     and of the container. The item sizes are remembered per tech type (they are constant in the game); a child whose size has
///     not been reported yet counts as one slot, which can only make the check more permissive. The check compares slot counts,
///     the client still does the exact grid packing check before it sends anything.
/// </remarks>
public class ContainerCapacityArbiter
{
    private readonly ConcurrentDictionary<string, (int Width, int Height)> itemSizesByTechType = new();

    /// <summary>
    ///     Returns true if <paramref name="item" /> can be added to <paramref name="container" />, or if the server cannot tell.
    ///     The item must not already be a child of the container.
    /// </summary>
    public bool Fits(Entity container, Entity item, int itemWidth, int itemHeight, int containerWidth, int containerHeight)
    {
        if (itemWidth > 0 && itemHeight > 0 && item.TechType != null)
        {
            itemSizesByTechType[item.TechType.Name] = (itemWidth, itemHeight);
        }
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            return true;
        }

        int used = 0;
        foreach (Entity child in container.ChildEntities)
        {
            if (child is InventoryItemEntity && child.Id != item.Id)
            {
                used += GetArea(child);
            }
        }

        int itemArea = itemWidth > 0 && itemHeight > 0 ? itemWidth * itemHeight : GetArea(item);
        return used + itemArea <= containerWidth * containerHeight;
    }

    private int GetArea(Entity item)
    {
        if (item.TechType != null && itemSizesByTechType.TryGetValue(item.TechType.Name, out (int Width, int Height) size))
        {
            return size.Width * size.Height;
        }
        return 1;
    }
}
