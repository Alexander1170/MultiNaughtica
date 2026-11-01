using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Crafting from lockers (item 3): the decision of the server about a claim. Two players who craft at the same moment can pick the same last items out of a locker (each
///     client only sees its own copy); the claim is all or nothing and an item goes to one of them.
/// </summary>
[TestClass]
public class CraftingClaimArbiterTest
{
    private EntityRegistry registry = null!;
    private CraftingClaimArbiter arbiter = null!;
    private BuildEntity baseEntity = null!;
    private ModuleEntity locker = null!;
    private ModuleEntity secondLocker = null!;

    [TestInitialize]
    public void Init()
    {
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        arbiter = new CraftingClaimArbiter();
        baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        registry.AddOrUpdate(baseEntity);
        locker = AddModule(baseEntity);
        secondLocker = AddModule(baseEntity);
    }

    private Entity? Remove(NitroxId id)
    {
        Optional<Entity> removed = registry.RemoveEntity(id);
        return removed.HasValue ? removed.Value : null;
    }

    private CraftingClaimDecision Claim(NitroxId? scope, params NitroxId[] items) => arbiter.Claim(registry, scope, items, Remove);

    private ModuleEntity AddModule(Entity parent)
    {
        ModuleEntity module = ModuleEntity.MakeEmpty();
        module.Id = new NitroxId();
        module.ParentId = parent.Id;
        registry.AddOrUpdate(module);
        return module;
    }

    private InventoryItemEntity AddItem(Entity container, string techType = "Titanium")
    {
        InventoryItemEntity item = new(new NitroxId(), "classId", new NitroxTechType(techType), null, container.Id, []);
        registry.AddOrUpdate(item);
        return item;
    }

    private static PlayerEntity NewPlayer(Entity? parent)
    {
        return new PlayerEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, "player", true, new NitroxId(), new NitroxTechType("Player"), null, parent?.Id, []);
    }

    // ---- the happy path

    [TestMethod]
    public void ItemsInALockerOfTheBaseAreGrantedAndRemoved()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(secondLocker);

        CraftingClaimDecision decision = Claim(baseEntity.Id, a.Id, b.Id);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Granted);
        decision.Removed.Should().BeEquivalentTo(new[] { a, b });
        registry.TryGetEntityById(a.Id, out Entity? _).Should().BeFalse();
        registry.TryGetEntityById(b.Id, out Entity? _).Should().BeFalse();
        locker.ChildEntities.Should().BeEmpty("the removal leaves the locker's list too");
    }

    [TestMethod]
    public void TheRemovalOrderIsTheOrderOfTheClaim()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        InventoryItemEntity c = AddItem(locker);
        List<NitroxId> removalOrder = [];

        arbiter.Claim(registry, baseEntity.Id, [c.Id, a.Id, b.Id], id =>
        {
            removalOrder.Add(id);
            return Remove(id);
        });

        removalOrder.Should().Equal(c.Id, a.Id, b.Id);
    }

    [TestMethod]
    public void ItemsInAnInventoryEntityOfACyclopsAreGranted()
    {
        // The built-in lockers of a Cyclops are children of the Cyclops entity (PathBasedChildEntity), the items are children of those
        VehicleEntity cyclops = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "cyclops", true, new NitroxId(), new NitroxTechType("Cyclops"), null);
        registry.AddOrUpdate(cyclops);
        PathBasedChildEntity cyclopsLocker = new("Locker01", new NitroxId(), null, null, cyclops.Id, []);
        registry.AddOrUpdate(cyclopsLocker);
        InventoryItemEntity item = AddItem(cyclopsLocker, "Quartz");

        CraftingClaimDecision decision = Claim(cyclops.Id, item.Id);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    [TestMethod]
    public void ItemsAtTheTopOfTheScopeItselfAreGranted()
    {
        // an item that is a direct child of the scope (a container entity that is the base)
        InventoryItemEntity item = AddItem(baseEntity);

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    // ---- two players, the same items

    [TestMethod]
    public void TheSecondClaimForTheSameItemFindsItGone()
    {
        InventoryItemEntity last = AddItem(locker);

        CraftingClaimDecision first = Claim(baseEntity.Id, last.Id);
        CraftingClaimDecision second = Claim(baseEntity.Id, last.Id);

        first.Outcome.Should().Be(CraftingClaimOutcome.Granted);
        second.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        second.Unavailable.Should().Equal(last.Id);
        second.Removed.Should().BeEmpty();
    }

    [TestMethod]
    public void AClaimThatOverlapsAnEarlierOneTakesNothingAtAll()
    {
        // the first player's claim took b; the second player wanted a and b: a stays in the locker for them to try again
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        Claim(baseEntity.Id, b.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);

        CraftingClaimDecision second = Claim(baseEntity.Id, a.Id, b.Id);

        second.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        second.Unavailable.Should().Equal(b.Id);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? stillThere).Should().BeTrue("all or nothing: the free item is not taken");
        stillThere!.ParentId.Should().Be(locker.Id);
    }

    [TestMethod]
    public void TwoClaimsForTheSameLastItemsRunInParallelAndExactlyOneWins()
    {
        for (int round = 0; round < 50; round++)
        {
            registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
            arbiter = new CraftingClaimArbiter();
            baseEntity = BuildEntity.MakeEmpty();
            baseEntity.Id = new NitroxId();
            registry.AddOrUpdate(baseEntity);
            locker = AddModule(baseEntity);
            NitroxId[] items = Enumerable.Range(0, 5).Select(_ => AddItem(locker).Id).ToArray();
            NitroxId[] reversed = Enumerable.Reverse(items).ToArray();

            // Both claims start at the same moment, name the items in opposite orders and give the other thread a chance between two removals: without the gate each would take
            // some of the items and fail on the rest
            using Barrier start = new(2);
            Entity? Slow(NitroxId id)
            {
                Thread.Yield();
                return Remove(id);
            }
            Task<CraftingClaimDecision> alice = Task.Run(() =>
            {
                start.SignalAndWait();
                return arbiter.Claim(registry, baseEntity.Id, items, Slow);
            });
            Task<CraftingClaimDecision> bob = Task.Run(() =>
            {
                start.SignalAndWait();
                return arbiter.Claim(registry, baseEntity.Id, reversed, Slow);
            });
            Task.WaitAll(alice, bob);

            CraftingClaimOutcome[] outcomes = [alice.Result.Outcome, bob.Result.Outcome];
            outcomes.Count(outcome => outcome == CraftingClaimOutcome.Granted).Should().Be(1, "exactly one of two claims for the same items is granted");
            outcomes.Count(outcome => outcome == CraftingClaimOutcome.Taken).Should().Be(1);
            locker.ChildEntities.Should().BeEmpty();
        }
    }

    [TestMethod]
    public async Task TheDeliveryOfOneClaimIsOutBeforeTheNextClaimIsDecided()
    {
        InventoryItemEntity last = AddItem(locker);
        List<string> events = [];
        int inDelivery = 0;
        int overlaps = 0;

        async Task Deliver(string who, CraftingClaimDecision decision)
        {
            if (Interlocked.Increment(ref inDelivery) > 1)
            {
                Interlocked.Increment(ref overlaps);
            }
            lock (events)
            {
                events.Add($"{who}:{decision.Outcome}:start");
            }
            await Task.Delay(30);
            lock (events)
            {
                events.Add($"{who}:{decision.Outcome}:end");
            }
            Interlocked.Decrement(ref inDelivery);
        }

        Task<CraftingClaimDecision> alice = arbiter.ClaimAsync(registry, baseEntity.Id, [last.Id], Remove, decision => Deliver("alice", decision));
        Task<CraftingClaimDecision> bob = arbiter.ClaimAsync(registry, baseEntity.Id, [last.Id], Remove, decision => Deliver("bob", decision));
        await Task.WhenAll(alice, bob);

        overlaps.Should().Be(0, "the second claim is not decided while the first one is still being delivered");
        events.Should().HaveCount(4);
        events[0].Should().EndWith(":Granted:start");
        events[1].Should().EndWith(":Granted:end");
        events[2].Should().EndWith(":Taken:start");
        events[3].Should().EndWith(":Taken:end");
    }

    [TestMethod]
    public void AClaimForOtherItemsAfterTheRefusalGoesThrough()
    {
        // the retry of the loser: the lockers hold more than the recipe needs
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        Claim(baseEntity.Id, a.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        CraftingClaimDecision refused = Claim(baseEntity.Id, a.Id);
        refused.Outcome.Should().Be(CraftingClaimOutcome.Taken);

        Claim(baseEntity.Id, b.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    // ---- what is not claimable

    [TestMethod]
    public void AnItemThatIsNotInTheRegistryIsUnavailable()
    {
        InventoryItemEntity a = AddItem(locker);
        NitroxId unknown = new();

        CraftingClaimDecision decision = Claim(baseEntity.Id, a.Id, unknown);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        decision.Unavailable.Should().Equal(unknown);
    }

    [TestMethod]
    public void AnItemInAnotherBaseIsUnavailable()
    {
        BuildEntity otherBase = BuildEntity.MakeEmpty();
        otherBase.Id = new NitroxId();
        registry.AddOrUpdate(otherBase);
        InventoryItemEntity item = AddItem(AddModule(otherBase));

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void AnItemTheOtherPlayerMovedIntoTheirInventoryIsUnavailable()
    {
        // the item was in the locker when the claiming client looked; the other player picked it up, which the server learned as a reparent to their player entity
        InventoryItemEntity item = AddItem(locker);
        PlayerEntity bob = NewPlayer(baseEntity);
        registry.AddOrUpdate(bob);
        registry.ReparentEntity(item.Id, bob.Id);

        CraftingClaimDecision decision = Claim(baseEntity.Id, item.Id);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue("a refused claim removes nothing");
    }

    [TestMethod]
    public void AnItemInAPlayersInventoryUnderTheBaseIsNeverClaimable()
    {
        // the player is parented to the base (or the Cyclops) he stands in: the walk up from his items reaches the scope but passes through the player
        PlayerEntity bob = NewPlayer(baseEntity);
        registry.AddOrUpdate(bob);
        InventoryItemEntity item = AddItem(bob);

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void AnItemInAPlanterIsNotClaimable()
    {
        PlanterEntity planter = new(new NitroxId(), baseEntity.Id);
        registry.AddOrUpdate(planter);
        InventoryItemEntity seed = AddItem(planter, "CreepvineSeed");

        Claim(baseEntity.Id, seed.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void AnItemInAVehicleDockedInTheBaseIsNotClaimable()
    {
        VehicleEntity seamoth = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, "seamoth", true, new NitroxId(), new NitroxTechType("Seamoth"), null, baseEntity.Id, []);
        registry.AddOrUpdate(seamoth);
        InventoryItemEntity item = AddItem(seamoth);

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void TheContentsOfAMachineThatIsPartOfTheBaseAreNotClaimable()
    {
        // bioreactors, reactors, water parks and filtration machines are interior pieces of the base (IBaseModule), the scanner room a map room entity: none of them is a locker
        InteriorPieceEntity bioreactor = InteriorPieceEntity.MakeEmpty();
        bioreactor.Id = new NitroxId();
        bioreactor.ParentId = baseEntity.Id;
        registry.AddOrUpdate(bioreactor);
        InventoryItemEntity fuel = AddItem(bioreactor, "CreepvineSeedCluster");
        MapRoomEntity scannerRoom = new(new NitroxId(), baseEntity.Id, new NitroxInt3(0, 0, 0));
        registry.AddOrUpdate(scannerRoom);
        InventoryItemEntity chip = AddItem(scannerRoom, "MapRoomUpgradeScanRange");
        InventoryItemEntity inLocker = AddItem(locker);

        Claim(baseEntity.Id, fuel.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        Claim(baseEntity.Id, chip.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        Claim(baseEntity.Id, inLocker.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        registry.TryGetEntityById(fuel.Id, out Entity? _).Should().BeTrue();
        registry.TryGetEntityById(chip.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("Aquarium")]
    [DataRow("Trashcans")]
    [DataRow("LabTrashcan")]
    public void TheContentsOfAModuleThatIsNoLockerAreNotClaimable(string techType)
    {
        ModuleEntity notALocker = AddModule(baseEntity);
        notALocker.TechType = new NitroxTechType(techType);
        InventoryItemEntity item = AddItem(notALocker, "Bladderfish");

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void OnlyItemEntitiesCanBeClaimed()
    {
        // a module of the base is not an item, whatever id the client names
        Claim(baseEntity.Id, locker.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        registry.TryGetEntityById(locker.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void AParentChainThatNeverReachesTheScopeIsGivenUp()
    {
        // a loop of parents must not hang the server
        InventoryItemEntity a = AddItem(locker);
        InventoryEntity first = new(0, new NitroxId(), new NitroxTechType("None"), null!, null!, []);
        InventoryEntity second = new(0, new NitroxId(), new NitroxTechType("None"), null!, first.Id, []);
        first.ParentId = second.Id;
        registry.AddEntity(first);
        registry.AddEntity(second);
        registry.ReparentEntity(a.Id, first.Id);

        Claim(baseEntity.Id, a.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    // ---- scopes that are not a base or a Cyclops

    [TestMethod]
    public void APlayerAsTheScopeIsInvalidAndHisItemsStayWhereTheyAre()
    {
        // the walk up from an item stops at the scope before it looks at what it passes: a player as the scope must not open his own inventory
        PlayerEntity bob = NewPlayer(baseEntity);
        registry.AddOrUpdate(bob);
        InventoryItemEntity item = AddItem(bob);

        Claim(bob.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
        bob.ChildEntities.Should().Contain(item);
    }

    [TestMethod]
    public void APlanterAsTheScopeIsInvalid()
    {
        PlanterEntity planter = new(new NitroxId(), baseEntity.Id);
        registry.AddOrUpdate(planter);
        InventoryItemEntity seed = AddItem(planter, "CreepvineSeed");

        Claim(planter.Id, seed.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(seed.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void AVehicleThatIsNoCyclopsAsTheScopeIsInvalid()
    {
        VehicleEntity seamoth = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "seamoth", true, new NitroxId(), new NitroxTechType("Seamoth"), null);
        registry.AddOrUpdate(seamoth);
        InventoryItemEntity item = AddItem(seamoth);

        Claim(seamoth.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void AModuleAsTheScopeIsInvalid()
    {
        InventoryItemEntity item = AddItem(locker);

        Claim(locker.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    // ---- requests that make no sense

    [TestMethod]
    public void AnUnknownScopeIsInvalid()
    {
        InventoryItemEntity item = AddItem(locker);

        Claim(new NitroxId(), item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(null, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void NoItemsOrANullListIsInvalid()
    {
        arbiter.Claim(registry, baseEntity.Id, [], Remove).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        arbiter.Claim(registry, baseEntity.Id, null, Remove).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
    }

    [TestMethod]
    public void TheSameItemTwiceIsInvalidAndTakesNothing()
    {
        InventoryItemEntity item = AddItem(locker);

        Claim(baseEntity.Id, item.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void ANullItemIdIsInvalid()
    {
        arbiter.Claim(registry, baseEntity.Id, [AddItem(locker).Id, null!], Remove).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
    }

    [TestMethod]
    public void MoreThanTheLimitIsInvalid()
    {
        NitroxId[] tooMany = Enumerable.Range(0, CraftingClaimRequest.MAX_ITEMS + 1).Select(_ => AddItem(locker).Id).ToArray();
        NitroxId[] atTheLimit = tooMany.Take(CraftingClaimRequest.MAX_ITEMS).ToArray();

        Claim(baseEntity.Id, tooMany).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(baseEntity.Id, atTheLimit).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    [TestMethod]
    public void AnOutcomeThatWasNeverFilledInIsNotAGrant()
    {
        default(CraftingClaimOutcome).Should().Be(CraftingClaimOutcome.Unknown);
        CraftingClaimOutcome.Granted.Should().NotBe(default(CraftingClaimOutcome));
    }

    // ---- a removal that fails half way

    [TestMethod]
    public void AnItemThatVanishesBetweenTheCheckAndTheRemovalPutsTheOthersBack()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        InventoryItemEntity c = AddItem(secondLocker);

        // the second item is taken by some other code path right before the arbiter gets to it
        CraftingClaimDecision decision = arbiter.Claim(registry, baseEntity.Id, [a.Id, b.Id, c.Id], id => id.Equals(b.Id) ? null : Remove(id));

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        decision.Unavailable.Should().Equal(b.Id);
        decision.Removed.Should().BeEmpty();
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue("the item that had been removed is put back");
        aBack!.ParentId.Should().Be(locker.Id);
        locker.ChildEntities.Should().Contain(a, "and back in its locker's list");
    }

    [TestMethod]
    public void SeveralItemsThatWereRemovedAllComeBackToTheirOwnLockers()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(secondLocker);
        InventoryItemEntity c = AddItem(locker);
        InventoryItemEntity d = AddItem(secondLocker);

        CraftingClaimDecision decision = arbiter.Claim(registry, baseEntity.Id, [a.Id, b.Id, c.Id, d.Id], id => id.Equals(d.Id) ? null : Remove(id));

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        foreach ((InventoryItemEntity item, ModuleEntity owner) in new[] { (a, locker), (b, secondLocker), (c, locker), (d, secondLocker) })
        {
            registry.TryGetEntityById(item.Id, out InventoryItemEntity? back).Should().BeTrue();
            back!.ParentId.Should().Be(owner.Id);
            owner.ChildEntities.Should().Contain(item);
        }
        locker.ChildEntities.Should().HaveCount(2);
        secondLocker.ChildEntities.Should().HaveCount(2);
    }

    [TestMethod]
    public void ARemovalThatThrowsPutsTheEarlierOnesBackAndTheClaimIsInvalid()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);

        CraftingClaimDecision decision = arbiter.Claim(registry, baseEntity.Id, [a.Id, b.Id], id => id.Equals(b.Id) ? throw new InvalidOperationException("boom") : Remove(id));

        decision.Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue();
        aBack!.ParentId.Should().Be(locker.Id);
        locker.ChildEntities.Should().BeEquivalentTo(new[] { a, b });
    }

    [TestMethod]
    public void AnItemThatMovedIntoAnInventoryAfterTheCheckIsNotRemoved()
    {
        // the check passed, then the other player picked the item up (EntityReparented) before the arbiter got to it
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        PlayerEntity bob = NewPlayer(baseEntity);
        registry.AddOrUpdate(bob);

        CraftingClaimDecision decision = arbiter.Claim(registry, baseEntity.Id, [a.Id, b.Id], id =>
        {
            if (id.Equals(a.Id))
            {
                Entity? removed = Remove(id);
                registry.ReparentEntity(b.Id, bob.Id);
                return removed;
            }
            return Remove(id);
        });

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        decision.Unavailable.Should().Equal(b.Id);
        registry.TryGetEntityById(b.Id, out InventoryItemEntity? bStill).Should().BeTrue("the item the other player holds now is not touched");
        bStill!.ParentId.Should().Be(bob.Id);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue("and the one taken before is put back");
        aBack!.ParentId.Should().Be(locker.Id);
    }

    [TestMethod]
    public void AnItemWithChildrenComesBackWithThem()
    {
        // a tool with a battery in it: the battery is a child of the item entity and leaves the registry with it
        InventoryItemEntity tool = AddItem(locker, "Welder");
        InstalledBatteryEntity battery = new(0, new NitroxId(), new NitroxTechType("Battery"), null, tool.Id, []);
        registry.AddOrUpdate(battery);
        InventoryItemEntity other = AddItem(locker);

        arbiter.Claim(registry, baseEntity.Id, [tool.Id, other.Id], id => id.Equals(other.Id) ? null : Remove(id)).Outcome.Should().Be(CraftingClaimOutcome.Taken);

        registry.TryGetEntityById(tool.Id, out InventoryItemEntity? toolBack).Should().BeTrue();
        toolBack!.ParentId.Should().Be(locker.Id);
        locker.ChildEntities.Should().Contain(tool);
        registry.TryGetEntityById(battery.Id, out Entity? batteryBack).Should().BeTrue();
        batteryBack!.ParentId.Should().Be(tool.Id);
        tool.ChildEntities.Should().Contain(battery);
    }
}
