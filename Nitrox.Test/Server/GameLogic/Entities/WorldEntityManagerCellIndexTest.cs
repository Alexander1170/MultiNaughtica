using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Bug 9, the ghost fish in the base (2026-10-24): the second generation of a batch replaced the registry's entity for an id, but the cell index kept the old object under its old cell,
///     and the cell index handed it out because the id was registered (to another object). The players who loaded that cell got a copy that nobody updates, under an id that belongs to
///     another entity. <see cref="WorldEntityManager.GetEntities" /> hands out only the registry's own object for an id, and removes the others.
/// </summary>
[TestClass]
public class WorldEntityManagerCellIndexTest
{
    private EntityRegistry registry;
    private WorldEntityManager manager;

    [TestInitialize]
    public void Init()
    {
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        // The batch spawner and the player manager are not used to read the cell index
        manager = new WorldEntityManager(Substitute.For<IPacketSender>(), registry, null, null, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
    }

    /// <summary>
    ///     What coop.3 did with an id that the generation of a batch made again: the registry's object was replaced, the new object went into the cell it stands in.
    /// </summary>
    private void ReplaceAsTheSecondGenerationDid(WorldEntity fresh)
    {
        registry.AddEntitiesIgnoringDuplicate([fresh]);
        manager.RegisterWorldEntity(fresh);
    }

    private void Track(WorldEntity entity)
    {
        registry.AddEntity(entity);
        manager.RegisterWorldEntity(entity);
    }

    [TestMethod]
    public void TheRegistrysObjectIsHandedOutOfItsCell()
    {
        WorldEntity peeper = Creature(1, "Peeper", -96f, -8f, -359f);
        Track(peeper);

        manager.GetEntities(peeper.AbsoluteEntityCell).Should().ContainSingle().Which.Should().BeSameAs(peeper);
    }

    [TestMethod]
    public void TheOldObjectOfAReplacedEntityIsNotHandedOutOfItsOldCell()
    {
        // The old object, standing at the level of the base like the stale Peeper of the host's log (22:27:14.9; "dropped" is only its name here, the decision does not look at who made it),
        // and the wild fish that the second generation made with the same id at its spawn point
        WorldEntity dropped = Creature(1, "Peeper", -176.5f, -39.4f, -331.5f, spawnedByServer: false);
        Track(dropped);
        WorldEntity wild = Creature(1, "Peeper", -95.8f, -8.4f, -359f);
        wild.AbsoluteEntityCell.Should().NotBe(dropped.AbsoluteEntityCell, "the test needs the two objects in different cells");

        ReplaceAsTheSecondGenerationDid(wild);

        manager.GetEntities(dropped.AbsoluteEntityCell).Should().BeEmpty("the id belongs to the wild fish now: a copy of the dropped one would stand in the base and nobody could ever update it");
        manager.GetEntities(wild.AbsoluteEntityCell).Should().ContainSingle().Which.Should().BeSameAs(wild);
    }

    [TestMethod]
    public void TheStaleEntryLeavesTheIndexSoItIsNotLookedAtAgain()
    {
        WorldEntity dropped = Creature(1, "Peeper", -176.5f, -39.4f, -331.5f, spawnedByServer: false);
        Track(dropped);
        ReplaceAsTheSecondGenerationDid(Creature(1, "Peeper", -95.8f, -8.4f, -359f));

        manager.GetEntities(dropped.AbsoluteEntityCell);

        manager.worldEntitiesByCell[dropped.AbsoluteEntityCell].Should().NotContainKey(Id(1));
    }

    [TestMethod]
    public void TheOtherEntitiesOfTheCellAreHandedOutNextToAStaleOne()
    {
        WorldEntity dropped = Creature(1, "Peeper", -176.5f, -39.4f, -331.5f, spawnedByServer: false);
        WorldEntity neighbour = Creature(2, "Boomerang", -176.5f, -39.4f, -331.5f);
        Track(dropped);
        Track(neighbour);
        neighbour.AbsoluteEntityCell.Should().Be(dropped.AbsoluteEntityCell, "the test needs both objects in one cell");
        ReplaceAsTheSecondGenerationDid(Creature(1, "Peeper", -95.8f, -8.4f, -359f));

        manager.GetEntities(dropped.AbsoluteEntityCell).Should().ContainSingle().Which.Should().BeSameAs(neighbour);
    }

    [TestMethod]
    public void AReplacementInTheSameCellIsTheOnlyEntryOfTheCell()
    {
        WorldEntity old = Creature(1, "Peeper", 10f, -5f, 20f);
        Track(old);
        WorldEntity fresh = Creature(1, "Peeper", 10f, -5f, 20f);
        fresh.AbsoluteEntityCell.Should().Be(old.AbsoluteEntityCell, "the test needs both objects in one cell");

        ReplaceAsTheSecondGenerationDid(fresh);

        manager.GetEntities(fresh.AbsoluteEntityCell).Should().ContainSingle().Which.Should().BeSameAs(fresh);
    }

    [TestMethod]
    public void AnEntityWhoseIdLeftTheRegistryIsNotHandedOutAndStaysInTheIndexAsBefore()
    {
        WorldEntity eaten = Creature(1, "Peeper", 10f, -5f, 20f);
        Track(eaten);
        registry.RemoveEntity(eaten.Id);

        manager.GetEntities(eaten.AbsoluteEntityCell).Should().BeEmpty();
        manager.worldEntitiesByCell[eaten.AbsoluteEntityCell].Should().ContainKey(eaten.Id, "only an id that belongs to another object is removed, the rest of the behaviour is unchanged");
    }

    [TestMethod]
    public void ACellThatTheIndexDoesNotKnowGivesNothing()
    {
        manager.GetEntities(Creature(1, "Peeper", 500f, -5f, 500f).AbsoluteEntityCell).Should().BeEmpty();
    }

    [TestMethod]
    public void TheRulesTellTheThreeStatesApart()
    {
        WorldEntity indexed = Creature(1);
        WorldEntity other = Creature(1, x: 99f);

        CellIndexRules.Classify(indexed, indexed).Should().Be(CellIndexRules.EntryState.Current);
        CellIndexRules.Classify(indexed, other).Should().Be(CellIndexRules.EntryState.Stale);
        CellIndexRules.Classify(indexed, null).Should().Be(CellIndexRules.EntryState.Hidden);
    }

    [TestMethod]
    public void TheLogLineNamesTheFirstFiveRemovedEntriesAndCountsTheRest()
    {
        List<WorldEntity> stale = [];
        for (int i = 1; i <= 7; i++)
        {
            stale.Add(Creature(i, "Peeper"));
        }

        string described = CellIndexRules.Describe(stale);

        described.Should().Contain("Peeper").And.Contain(Id(1).ToString()).And.Contain(Id(5).ToString());
        described.Should().NotContain(Id(6).ToString());
        described.Should().EndWith("and 2 more");
        CellIndexRules.Describe([stale[0]]).Should().NotContain("more");
    }

    [TestMethod]
    public void AnEntityOfAnotherTypeUnderTheIdIsStaleToo()
    {
        // A dropped fish that was picked up again is an inventory item in the registry: the world entity of the same id in the cell index is not the registry's object
        WorldEntity world = Creature(1, "Peeper");
        InventoryItemEntity item = new(world.Id, "class-Peeper", new NitroxTechType("Peeper"), null, null, []);

        CellIndexRules.Classify(world, item).Should().Be(CellIndexRules.EntryState.Stale);
    }
}
