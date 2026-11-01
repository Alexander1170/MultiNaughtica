using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

[TestClass]
public class ContainerCapacityArbiterTest
{
    private const int LOCKER_WIDTH = 6;
    private const int LOCKER_HEIGHT = 8;

    private static InventoryItemEntity CreateItem(string techType, Entity parent = null)
    {
        InventoryItemEntity item = new(new NitroxId(), "classId", new NitroxTechType(techType), null, parent?.Id, []);
        parent?.ChildEntities.Add(item);
        return item;
    }

    private static InventoryItemEntity CreateContainerWithItems(int count, string techType = "Titanium")
    {
        InventoryItemEntity container = CreateItem("Locker");
        for (int i = 0; i < count; i++)
        {
            CreateItem(techType, container);
        }
        return container;
    }

    [TestMethod]
    public void Fits_EmptyContainer_ReturnsTrue()
    {
        ContainerCapacityArbiter arbiter = new();
        Entity container = CreateContainerWithItems(0);

        arbiter.Fits(container, CreateItem("Titanium"), 1, 1, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeTrue();
    }

    [TestMethod]
    public void Fits_LastFreeSlot_ReturnsTrue()
    {
        ContainerCapacityArbiter arbiter = new();
        Entity container = CreateContainerWithItems(LOCKER_WIDTH * LOCKER_HEIGHT - 1);

        arbiter.Fits(container, CreateItem("Titanium"), 1, 1, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeTrue();
    }

    [TestMethod]
    public void Fits_FullContainer_ReturnsFalse()
    {
        // The case of two players filling a locker at the same moment: the one whose move reaches the server last must be refused
        ContainerCapacityArbiter arbiter = new();
        Entity container = CreateContainerWithItems(LOCKER_WIDTH * LOCKER_HEIGHT);

        arbiter.Fits(container, CreateItem("Titanium"), 1, 1, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeFalse();
    }

    [TestMethod]
    public void Fits_UnknownContainerSize_ReturnsTrue()
    {
        // The player's own inventory is sent without a size: the server cannot tell and does not arbitrate
        ContainerCapacityArbiter arbiter = new();
        Entity container = CreateContainerWithItems(500);

        arbiter.Fits(container, CreateItem("Titanium"), 1, 1, 0, 0).Should().BeTrue();
    }

    [TestMethod]
    public void Fits_LargeItemsAreCountedBySizeOnceKnown()
    {
        ContainerCapacityArbiter arbiter = new();
        Entity container = CreateContainerWithItems(0);
        for (int i = 0; i < 11; i++)
        {
            // 11 x (2x2) = 44 slots of 48, each move teaches the server the size of the tech type
            InventoryItemEntity large = CreateItem("Welder");
            arbiter.Fits(container, large, 2, 2, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeTrue();
            container.ChildEntities.Add(large);
        }

        arbiter.Fits(container, CreateItem("Welder"), 2, 2, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeTrue(); // 48 of 48
        container.ChildEntities.Add(CreateItem("Welder"));
        arbiter.Fits(container, CreateItem("Titanium"), 1, 1, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeFalse();
    }

    [TestMethod]
    public void Fits_ItemThatIsAlreadyInTheContainerIsNotCountedTwice()
    {
        ContainerCapacityArbiter arbiter = new();
        Entity container = CreateContainerWithItems(LOCKER_WIDTH * LOCKER_HEIGHT - 1);
        InventoryItemEntity alreadyThere = CreateItem("Titanium", container);

        arbiter.Fits(container, alreadyThere, 1, 1, LOCKER_WIDTH, LOCKER_HEIGHT).Should().BeTrue();
    }
}
