using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     A freshly generated batch must never replace what the registry holds (<see cref="EntityRegistry.AddEntitiesKeepingExisting" />): a batch that is generated a second time came out with ids
///     that were one off after a skipped databox, and the replacement reset the entities that kept their id and registered the others as second copies under the ids of other entities.
/// </summary>
[TestClass]
public class EntityRegistryKeepExistingTest
{
    private static EntityRegistry NewRegistry() => new(NullLogger<EntityRegistry>.Instance);

    private static Entity Registered(EntityRegistry registry, int id)
    {
        registry.TryGetEntityById(Id(id), out Entity? entity).Should().BeTrue();
        return entity!;
    }

    [TestMethod]
    public void AnEntityWithAFreeIdIsRegistered()
    {
        EntityRegistry registry = NewRegistry();
        WorldEntity generated = Creature(1);

        int dropped = registry.AddEntitiesKeepingExisting([generated]);

        dropped.Should().Be(0);
        Registered(registry, 1).Should().BeSameAs(generated);
    }

    [TestMethod]
    public void TheRegisteredEntityKeepsItsStateWhenAGeneratedOneHasTheSameId()
    {
        EntityRegistry registry = NewRegistry();
        PlaceholderGroupWorldEntity saved = Group(1, "crash-home-class");
        saved.Metadata = new CrashHomeMetadata(24546.87f);
        registry.AddEntity(saved);
        PlaceholderGroupWorldEntity generated = Group(1, "crash-home-class");
        generated.Metadata = new CrashHomeMetadata(0f);
        List<Entity> ignored = [];

        int dropped = registry.AddEntitiesKeepingExisting([generated], ignored);

        dropped.Should().Be(1);
        ignored.Should().ContainSingle().Which.Should().BeSameAs(generated);
        Registered(registry, 1).Should().BeSameAs(saved);
        saved.Metadata.Should().BeOfType<CrashHomeMetadata>().Which.SpawnTime.Should().Be(24546.87f, "the state a client wrote is not reset");
    }

    [TestMethod]
    public void WhatWasGeneratedBelowADroppedEntityGoesWithItWhateverItsId()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(1));
        PlaceholderGroupWorldEntity group = Group(1);
        PrefabPlaceholderEntity kid = Kid(500, group);
        Kid(501, kid);
        List<Entity> generated = Flat(group);
        List<Entity> ignored = [];

        int dropped = registry.AddEntitiesKeepingExisting(generated, ignored);

        dropped.Should().Be(3);
        generated.Should().OnlyContain(entity => ignored.Contains(entity));
        registry.TryGetEntityById(Id(500), out Entity? _).Should().BeFalse("its parent was dropped");
        registry.TryGetEntityById(Id(501), out Entity? _).Should().BeFalse();
    }

    [TestMethod]
    public void ADroppedChildLeavesTheChildListOfItsKeptParent()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(7));
        PlaceholderGroupWorldEntity group = Group(1);
        PrefabPlaceholderEntity collides = Kid(7, group);
        PrefabPlaceholderEntity free = Kid(8, group);

        registry.AddEntitiesKeepingExisting(Flat(group));

        Registered(registry, 1).Should().BeSameAs(group);
        Registered(registry, 8).Should().BeSameAs(free);
        group.ChildEntities.Should().Equal(free);
        group.ChildEntities.Should().NotContain(collides, "it would be sent to the clients under the id of another entity");
        Registered(registry, 7).Should().NotBeSameAs(collides);
    }

    [TestMethod]
    public void TheSameObjectListedTwiceIsNotACollision()
    {
        // the cell root children of a batch are in the list as children and again as promoted entities
        EntityRegistry registry = NewRegistry();
        WorldEntity creature = Creature(1);

        int dropped = registry.AddEntitiesKeepingExisting([creature, creature]);

        dropped.Should().Be(0);
        Registered(registry, 1).Should().BeSameAs(creature);
    }

    [TestMethod]
    public void ChildrenThatAreNotInTheListAreRegisteredWhenTheirIdsAreFree()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(30));
        PlaceholderGroupWorldEntity group = Group(1);
        PrefabPlaceholderEntity free = Kid(2, group);
        PrefabPlaceholderEntity taken = Kid(30, group);

        registry.AddEntitiesKeepingExisting([group]);

        Registered(registry, 2).Should().BeSameAs(free);
        Registered(registry, 30).Should().NotBeSameAs(taken);
        group.ChildEntities.Should().NotContain(entity => ReferenceEquals(entity, taken)).And.Contain(free);
    }

    [TestMethod]
    public void TheSecondRunOfABatchAddsNothingAndChangesNothing()
    {
        // the case that happened: the batch is generated, then generated again with the same ids (and a state was written in between)
        EntityRegistry registry = NewRegistry();
        List<Entity> first = Flat(MakeBatch());
        registry.AddEntitiesKeepingExisting(first);
        Registered(registry, 2).Metadata = new SupplyCrateMetadata(true);
        List<Entity> second = Flat(MakeBatch());
        List<Entity> ignored = [];

        int dropped = registry.AddEntitiesKeepingExisting(second, ignored);

        dropped.Should().Be(second.Count);
        registry.GetAllEntities().Should().HaveCount(first.Count);
        Registered(registry, 2).Metadata.Should().BeOfType<SupplyCrateMetadata>();
        registry.GetAllEntities().Should().OnlyContain(entity => first.Contains(entity), "every registered object is one of the first run");

        static PlaceholderGroupWorldEntity MakeBatch()
        {
            PlaceholderGroupWorldEntity group = Group(1);
            Kid(2, group);
            Kid(3, group);
            return group;
        }
    }

    [TestMethod]
    public void ReplacingIsWhatTheOtherMethodStillDoesBecauseItsCallersNeedIt()
    {
        // AddEntitiesIgnoringDuplicate is used for AddOrUpdate (new children of an entity that is sent again) and for the loading of the save (children that are in the entity data and in the global
        // root data are registered as the objects of the global root tree): both rely on the replacement
        EntityRegistry registry = NewRegistry();
        WorldEntity old = Creature(1);
        registry.AddEntity(old);
        WorldEntity replacement = Creature(1);

        registry.AddEntitiesIgnoringDuplicate([replacement]);

        Registered(registry, 1).Should().BeSameAs(replacement);
    }
}
