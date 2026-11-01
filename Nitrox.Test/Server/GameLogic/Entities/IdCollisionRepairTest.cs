using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Items in the lockers of a base that share an id with another entity after a batch was generated a second time (the world "Test": a Quartz and a Sandshark egg, a Salt and a Peeper, a Salt and
///     a Hoopfish, two Quartz). Nothing is deleted: the entity that matches the entity list keeps the id, the inventory item gets a new one.
/// </summary>
[TestClass]
public class IdCollisionRepairTest
{
    private static GlobalRootEntity Locker(int id, NitroxId? parent = null)
    {
        return new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "locker-class", false, Id(id), new NitroxTechType("Locker"), null, parent, []);
    }

    private static InventoryItemEntity Item(int id, NitroxId parent, string tech = "Quartz")
    {
        return new InventoryItemEntity(Id(id), $"class-{tech}", new NitroxTechType(tech), null, parent, []);
    }

    /// <summary>
    ///     A base with two lockers; the item in the first has the id of the entity in the second.
    /// </summary>
    private static (GlobalRootEntity Root, InventoryItemEntity Stale, Entity Keeper, List<Entity> Flat) Clash(Func<NitroxId, Entity> makeKeeper)
    {
        GlobalRootEntity root = Locker(1);
        GlobalRootEntity first = Locker(2, root.Id);
        GlobalRootEntity second = Locker(3, root.Id);
        root.ChildEntities.Add(first);
        root.ChildEntities.Add(second);
        InventoryItemEntity stale = Item(50, first.Id);
        first.ChildEntities.Add(stale);
        Entity keeper = makeKeeper(second.Id);
        second.ChildEntities.Add(keeper);
        // the entity list holds the entry that is registered under the id: the keeper
        List<Entity> flat = [makeKeeper(second.Id)];
        return (root, stale, keeper, flat);
    }

    [TestMethod]
    public void AnItemThatSharesItsIdWithAnItemInAnotherLockerGetsANewId()
    {
        (GlobalRootEntity root, InventoryItemEntity stale, Entity keeper, List<Entity> flat) = Clash(parent => Item(50, parent));

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], flat, []);

        plan.Fixes.Should().ContainSingle().Which.Item.Should().BeSameAs(stale);
        plan.Skips.Should().BeEmpty();
        List<NitroxId> assigned = IdCollisionRepair.Apply(plan);
        assigned.Should().ContainSingle();
        stale.Id.Should().Be(assigned[0]).And.NotBe(Id(50));
        keeper.Id.Should().Be(Id(50));
    }

    [TestMethod]
    public void AGeneratedEggInALockerKeepsTheIdAndTheItemThatWasReplacedGetsANewOne()
    {
        // the world "Test": the Quartz of the first locker and a Sandshark egg of the second have one id, the entity list holds the egg
        (GlobalRootEntity root, InventoryItemEntity stale, Entity keeper, List<Entity> flat) = Clash(parent => new WorldEntity(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("SandsharkEgg"), 0, "egg-class", true, Id(50), new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 100, "x", false, parent, new NitroxTechType("Locker"), null, null, [])));

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], flat, []);

        plan.Fixes.Should().ContainSingle().Which.Item.Should().BeSameAs(stale);
        IdCollisionRepair.Apply(plan);
        keeper.Id.Should().Be(Id(50), "the egg is left where it is: nothing is deleted");
        stale.Id.Should().NotBe(Id(50));
    }

    [TestMethod]
    public void AnIdThatAPlayerPointsAtIsLeftAlone()
    {
        (GlobalRootEntity root, InventoryItemEntity stale, _, List<Entity> flat) = Clash(parent => Item(50, parent));

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], flat, [Id(50)]);

        plan.Fixes.Should().BeEmpty();
        plan.Skips.Should().ContainSingle().Which.Id.Should().Be(Id(50));
        stale.Id.Should().Be(Id(50));
    }

    [TestMethod]
    public void WhenNoOccurrenceMatchesTheEntityListNothingIsChanged()
    {
        (GlobalRootEntity root, InventoryItemEntity stale, _, _) = Clash(parent => Item(50, parent));
        List<Entity> flat = [Item(50, Id(999))];   // under another parent: neither occurrence matches

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], flat, []);

        plan.Fixes.Should().BeEmpty();
        plan.Skips.Should().ContainSingle();
        stale.Id.Should().Be(Id(50));
    }

    [TestMethod]
    public void WhenTheEntityListHasNoEntryNothingIsChanged()
    {
        (GlobalRootEntity root, _, _, _) = Clash(parent => Item(50, parent));

        IdCollisionRepair.Plan([root], [], []).Fixes.Should().BeEmpty();
    }

    [TestMethod]
    public void AnItemWithChildrenIsNotRenamed()
    {
        (GlobalRootEntity root, InventoryItemEntity stale, _, List<Entity> flat) = Clash(parent => Item(50, parent));
        stale.ChildEntities.Add(new InstalledBatteryEntity(0, Id(60), new NitroxTechType("Battery"), null, stale.Id, []));

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], flat, []);

        plan.Fixes.Should().BeEmpty();
        plan.Skips.Should().ContainSingle();
    }

    [TestMethod]
    public void TheSameEntityListedTwiceUnderOneParentIsNotACollision()
    {
        // the Cyclops of the world "Test" lists 8 of its path children twice
        GlobalRootEntity root = Locker(1);
        InventoryItemEntity once = Item(70, root.Id);
        InventoryItemEntity again = Item(70, root.Id);
        root.ChildEntities.Add(once);
        root.ChildEntities.Add(again);

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], [once], []);

        plan.Fixes.Should().BeEmpty();
        plan.Skips.Should().ContainSingle();
    }

    [TestMethod]
    public void ARepairedTreeHasNothingLeftToRepair()
    {
        (GlobalRootEntity root, _, _, List<Entity> flat) = Clash(parent => Item(50, parent));
        IdCollisionRepair.RepairPlan first = IdCollisionRepair.Plan([root], flat, []);
        IdCollisionRepair.Apply(first);

        IdCollisionRepair.RepairPlan second = IdCollisionRepair.Plan([root], flat, []);

        first.Fixes.Should().ContainSingle();
        second.Fixes.Should().BeEmpty();
        second.Skips.Should().BeEmpty();
    }

    [TestMethod]
    public void AHealthyTreeHasNothingToRepair()
    {
        GlobalRootEntity root = Locker(1);
        GlobalRootEntity locker = Locker(2, root.Id);
        root.ChildEntities.Add(locker);
        for (int i = 0; i < 20; i++)
        {
            locker.ChildEntities.Add(Item(100 + i, locker.Id));
        }

        IdCollisionRepair.RepairPlan plan = IdCollisionRepair.Plan([root], Flat(root), []);

        plan.Fixes.Should().BeEmpty();
        plan.Skips.Should().BeEmpty();
    }
}
