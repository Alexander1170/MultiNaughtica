using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace NitroxClient.GameLogic;

[TestClass]
public class PendingParentEntitiesTest
{
    private static InventoryItemEntity Item(NitroxId parentId, string techType = "Titanium")
    {
        return new InventoryItemEntity(new NitroxId(), "classId", new NitroxTechType(techType), null, parentId, []);
    }

    private static WorldEntity WorldEntityAt(NitroxVector3 position, NitroxId parentId)
    {
        // The cell of a world entity follows its position
        return new WorldEntity(position, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("Quartz"), 0, "classId", false, new NitroxId(), null) { ParentId = parentId };
    }

    [TestMethod]
    public void Add_TheSameEntityAgain_IsHeldOnce()
    {
        // The cell is replayed every time it loads: 30 warps used to leave 1149 entries of 194 entities
        PendingParentEntities pending = new();
        NitroxId parentId = new();
        InventoryItemEntity item = Item(parentId);

        for (int i = 0; i < 30; i++)
        {
            pending.Add(item, i);
        }

        pending.Count.Should().Be(1);
        pending.ParentCount.Should().Be(1);
    }

    [TestMethod]
    public void Add_TheNewestCopyReplacesTheOldOne()
    {
        PendingParentEntities pending = new();
        NitroxId parentId = new();
        InventoryItemEntity older = Item(parentId);
        InventoryItemEntity newer = new(older.Id, "classId", older.TechType, null, parentId, []);

        pending.Add(older, 0f);
        pending.Add(newer, 1f);

        pending.Take(parentId, []).Should().ContainSingle().Which.Should().BeSameAs(newer);
    }

    [TestMethod]
    public void Add_AnEntityThatChangedParent_IsHeldUnderTheNewParentOnly()
    {
        PendingParentEntities pending = new();
        NitroxId firstParent = new();
        NitroxId secondParent = new();
        InventoryItemEntity item = Item(firstParent);
        pending.Add(item, 0f);
        item.ParentId = secondParent;
        pending.Add(item, 1f);

        pending.Count.Should().Be(1);
        pending.Take(firstParent, []).Should().BeEmpty();
        pending.Take(secondParent, []).Should().ContainSingle();
    }

    [TestMethod]
    public void Take_ReturnsWhatWaitedForTheParentExceptWhatTheParentBringsItself()
    {
        PendingParentEntities pending = new();
        NitroxId parentId = new();
        InventoryItemEntity brought = Item(parentId);
        InventoryItemEntity extra = Item(parentId);
        InventoryItemEntity otherParents = Item(new NitroxId());
        pending.Add(brought, 0f);
        pending.Add(extra, 0f);
        pending.Add(otherParents, 0f);

        List<Entity> taken = pending.Take(parentId, [brought.Id]);

        taken.Should().ContainSingle().Which.Should().BeSameAs(extra);
        pending.Count.Should().Be(1, "the entry of the other parent stays and the ones of this parent are gone, also the one the parent brings itself");
    }

    [TestMethod]
    public void Remove_AnEntityThatWasSpawnedByAnotherRoute_StopsWaiting()
    {
        PendingParentEntities pending = new();
        NitroxId parentId = new();
        InventoryItemEntity item = Item(parentId);
        pending.Add(item, 0f);

        pending.Remove(item.Id).Should().BeTrue();
        pending.Remove(item.Id).Should().BeFalse();
        pending.Count.Should().Be(0);
        pending.ParentCount.Should().Be(0);
    }

    [TestMethod]
    public void RemoveParent_DropsEverythingThatWaitedForIt()
    {
        PendingParentEntities pending = new();
        NitroxId parentId = new();
        pending.Add(Item(parentId), 0f);
        pending.Add(Item(parentId), 0f);
        pending.Add(Item(new NitroxId()), 0f);

        pending.RemoveParent(parentId).Should().Be(2);
        pending.Count.Should().Be(1);
    }

    [TestMethod]
    public void RemoveCell_DropsTheWorldEntitiesOfThatCellOnly()
    {
        PendingParentEntities pending = new();
        WorldEntity inAsleepCell = WorldEntityAt(new NitroxVector3(-160f, -30f, 385f), new NitroxId());
        WorldEntity inAwakeCell = WorldEntityAt(new NitroxVector3(366f, -6f, -203f), new NitroxId());
        AbsoluteEntityCell asleep = inAsleepCell.AbsoluteEntityCell;
        asleep.Should().NotBe(inAwakeCell.AbsoluteEntityCell, "the two entities must be in different cells for the test to mean anything");
        pending.Add(inAsleepCell, 0f);
        pending.Add(inAwakeCell, 0f);
        pending.Add(Item(new NitroxId()), 0f);

        pending.RemoveCell(asleep).Should().Be(1);

        pending.Count.Should().Be(2);
        pending.Entries.Select(entry => entry.Entity).Should().NotContain(inAsleepCell);
    }

    [TestMethod]
    public void RemoveCell_KeepsGlobalRootEntitiesAndDoesNotThrowForThem()
    {
        // A vehicle, module or base piece waiting for its moonpool or base is not part of any cell, and its level (100) has no cell size: AbsoluteEntityCell throws for it
        PendingParentEntities pending = new();
        NitroxVector3 position = new(-160f, -30f, 385f);
        NitroxTransform transform = new(position, NitroxQuaternion.Identity, NitroxVector3.One);
        GlobalRootEntity globalRoot = new(transform, GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType("Seamoth"), null, new NitroxId(), []);
        WorldEntity inCell = WorldEntityAt(position, new NitroxId());
        pending.Add(globalRoot, 0f);
        pending.Add(inCell, 0f);

        int dropped = 0;
        Action sleep = () => dropped = pending.RemoveCell(inCell.AbsoluteEntityCell);
        sleep.Should().NotThrow();

        dropped.Should().Be(1);
        pending.Entries.Select(entry => entry.Entity).Should().ContainSingle().Which.Should().BeSameAs(globalRoot);
    }

    [TestMethod]
    public void Add_AnEntityWithoutParent_IsIgnoredAndTheTableToleratesNullIds()
    {
        PendingParentEntities pending = new();
        InventoryItemEntity noParent = Item(null);

        pending.Add(noParent, 0f);

        pending.Count.Should().Be(0);
        pending.HasWaitersFor(null).Should().BeFalse();
        pending.Take(null, []).Should().BeEmpty();
        pending.Remove(null).Should().BeFalse();
        pending.RemoveParent(null).Should().Be(0);
    }

    [TestMethod]
    public void TryGet_FindsAWaitingEntityAndHasWaitersForItsParent()
    {
        PendingParentEntities pending = new();
        NitroxId parentId = new();
        InventoryItemEntity item = Item(parentId);
        pending.Add(item, 0f);

        pending.TryGet(item.Id, out Entity found).Should().BeTrue();
        found.Should().BeSameAs(item);
        pending.HasWaitersFor(parentId).Should().BeTrue();
        pending.TryGet(new NitroxId(), out _).Should().BeFalse();
        pending.HasWaitersFor(new NitroxId()).Should().BeFalse();

        pending.Take(parentId, []);
        pending.TryGet(item.Id, out _).Should().BeFalse();
    }

    [TestMethod]
    public void DescribeOlderThan_NamesTheKindsAndTheNumberOfParentsOfWhatWouldBeDropped()
    {
        PendingParentEntities pending = new();
        NitroxId parentA = new();
        NitroxId parentB = new();
        pending.Add(Item(parentA, "Titanium"), 0f);
        pending.Add(Item(parentA, "Titanium"), 0f);
        pending.Add(Item(parentB, "Quartz"), 0f);
        pending.Add(Item(parentB, "Copper"), 500f);

        string description = pending.DescribeOlderThan(100f);

        description.Should().StartWith("2 parents;").And.Contain("InventoryItemEntity/Titanium x2").And.Contain("InventoryItemEntity/Quartz x1").And.NotContain("Copper");
    }

    [TestMethod]
    public void RemoveOlderThan_DropsOnlyTheEntriesThatWaitedTooLong()
    {
        PendingParentEntities pending = new();
        InventoryItemEntity old = Item(new NitroxId());
        InventoryItemEntity recent = Item(new NitroxId());
        pending.Add(old, 10f);
        pending.Add(recent, 400f);

        pending.RemoveOlderThan(400f - PendingParentEntities.MAX_AGE_SECONDS + 50f).Should().Be(1);

        pending.Entries.Select(entry => entry.Entity).Should().ContainSingle().Which.Should().BeSameAs(recent);
    }
}
