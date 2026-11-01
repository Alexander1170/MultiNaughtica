using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

[TestClass]
public class ModuleSlotArbiterTest
{
    private const string SLOT_1 = "SeamothModule1";
    private const string SLOT_2 = "SeamothModule2";

    private static readonly SessionId alice = (ushort)1;
    private static readonly SessionId bob = (ushort)2;
    private static readonly SessionId carol = (ushort)3;

    private sealed class FakeClock
    {
        public long Now { get; set; }

        public long Read() => Now;
    }

    private FakeClock clock = null!;
    private EntityRegistry registry = null!;
    private ModuleSlotArbiter arbiter = null!;
    private InventoryItemEntity vehicle = null!;

    [TestInitialize]
    public void Init()
    {
        clock = new FakeClock();
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        arbiter = new ModuleSlotArbiter(clock.Read);
        vehicle = CreateEntity("Seamoth");
        registry.AddOrUpdate(vehicle);
    }

    private static InventoryItemEntity CreateEntity(string techType) => new(new NitroxId(), "classId", new NitroxTechType(techType), null, null, []);

    private static InstalledModuleEntity CreateModule(Entity? parent, string slot, string techType = "VehicleArmorPlating")
    {
        return new InstalledModuleEntity(slot, "classId", new NitroxId(), new NitroxTechType(techType), null, parent?.Id, []);
    }

    /// <summary>
    /// The server accepted the install and registered the module under its owner, as the processor does after a positive decision.
    /// </summary>
    private InstalledModuleEntity Install(SessionId sender, Entity? parent, string slot, long atMs, string techType = "VehicleArmorPlating")
    {
        clock.Now = atMs;
        InstalledModuleEntity module = CreateModule(parent, slot, techType);
        ModuleSlotDecision decision = arbiter.Decide(sender, module, registry);
        decision.Rejected.Should().BeFalse();
        registry.AddOrUpdate(module);
        return module;
    }

    private ModuleSlotDecision Decide(SessionId sender, InstalledModuleEntity module, long atMs)
    {
        clock.Now = atMs;
        return arbiter.Decide(sender, module, registry);
    }

    [TestMethod]
    public void Decide_EmptySlot_AcceptsWithNoStaleOccupant()
    {
        ModuleSlotDecision decision = Decide(alice, CreateModule(vehicle, SLOT_1), 0);

        decision.Rejected.Should().BeFalse();
        decision.WinnerId.Should().BeNull();
        decision.StaleOccupantId.Should().BeNull();
        arbiter.ClaimCount.Should().Be(1);
    }

    [TestMethod]
    public void Decide_OtherPlayerDifferentModuleSameSlotInsideWindow_RejectsAndNamesTheWinner()
    {
        // Alice and Bob both passed the vanilla empty-slot check; Alice's install reached the server first
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 0);

        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 50);

        decision.Rejected.Should().BeTrue();
        decision.WinnerId.Should().Be(winner.Id);
        decision.StaleOccupantId.Should().BeNull();
    }

    [TestMethod]
    public void Decide_RejectionLeavesTheClaimOfTheWinner_ThirdPlayerIsRejectedAgainstTheWinnerToo()
    {
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 0);
        Decide(bob, CreateModule(vehicle, SLOT_1), 20).Rejected.Should().BeTrue();

        ModuleSlotDecision decision = Decide(carol, CreateModule(vehicle, SLOT_1), 40);

        decision.Rejected.Should().BeTrue();
        decision.WinnerId.Should().Be(winner.Id, "the loser never becomes the holder of the slot");
        arbiter.ClaimCount.Should().Be(1);
    }

    [TestMethod]
    public void Decide_RejectionDoesNotExtendTheWindow()
    {
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 0);

        Decide(bob, CreateModule(vehicle, SLOT_1), 2900).Rejected.Should().BeTrue();
        ModuleSlotDecision later = Decide(bob, CreateModule(vehicle, SLOT_1), 3100);

        // Measured from Alice's install, not from Bob's rejected attempt; her module is what the server still holds
        later.Rejected.Should().BeFalse();
        later.StaleOccupantId.Should().Be(winner.Id);
    }

    [TestMethod]
    public void Decide_WinnerReinstallsTheSameModule_AcceptedAndTheSameItemFromAnySenderIsNeverAConflict()
    {
        InstalledModuleEntity module = Install(alice, vehicle, SLOT_1, 0);

        // The same item id again (a re-send, a forced respawn): never a second module
        Decide(alice, module, 10).Rejected.Should().BeFalse();
        ModuleSlotDecision fromAnotherSender = Decide(bob, module, 20);
        Decide(alice, module, 30).Rejected.Should().BeFalse();

        fromAnotherSender.Rejected.Should().BeFalse();
        fromAnotherSender.StaleOccupantId.Should().BeNull("the registry holds this very module, not another one");
    }

    [TestMethod]
    public void Decide_SamePlayerOtherModule_AcceptedAndNamesHisOldRegisteredModuleAsStale()
    {
        InstalledModuleEntity first = Install(alice, vehicle, SLOT_1, 0);

        // His unequip never reached the server: his client had an empty slot, so his local truth wins
        ModuleSlotDecision decision = Decide(alice, CreateModule(vehicle, SLOT_1), 10);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().Be(first.Id);
    }

    [TestMethod]
    public void Decide_SinglePlayerSwapsManyTimesInsideWindow_NeverRejected()
    {
        for (int i = 0; i < 30; i++)
        {
            // Each swap is unequip (the server forgets the module) and equip of another module, 10 ms apart
            InstalledModuleEntity module = Install(alice, vehicle, SLOT_1, i * 10L);
            registry.RemoveEntity(module.Id);
        }

        arbiter.ClaimCount.Should().Be(1);
    }

    [TestMethod]
    [DataRow(SLOT_2, false)]
    [DataRow(SLOT_1, true)]
    [DataRow(SLOT_2, true)]
    public void Decide_OtherSlotOrOtherParent_AreIndependent(string slot, bool otherParent)
    {
        Install(alice, vehicle, SLOT_1, 0);
        InventoryItemEntity prawn = CreateEntity("Exosuit");
        registry.AddOrUpdate(prawn);

        ModuleSlotDecision decision = Decide(bob, CreateModule(otherParent ? prawn : vehicle, slot), 50);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().BeNull();
        arbiter.ClaimCount.Should().Be(2);
    }

    [TestMethod]
    public void Decide_ClaimExactlyAtWindowEdge_IsStale()
    {
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 1000);

        Decide(bob, CreateModule(vehicle, SLOT_1), 1000 + ModuleSlotArbiter.CONTESTED_WINDOW_MS - 1).Rejected.Should().BeTrue();
        ModuleSlotDecision atEdge = Decide(bob, CreateModule(vehicle, SLOT_1), 1000 + ModuleSlotArbiter.CONTESTED_WINDOW_MS);

        atEdge.Rejected.Should().BeFalse();
        atEdge.StaleOccupantId.Should().Be(winner.Id);
    }

    [TestMethod]
    public void Decide_ClaimOlderThanWindow_NewcomerWinsAndNamesTheRegisteredOccupantAsStale()
    {
        InstalledModuleEntity old = Install(alice, vehicle, SLOT_1, 0);

        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 60_000);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().Be(old.Id);
    }

    [TestMethod]
    public void Decide_NewcomerThatWonOverAStaleOccupant_HoldsTheSlotForTheNextRace()
    {
        InstalledModuleEntity old = Install(alice, vehicle, SLOT_1, 0);
        InstalledModuleEntity newcomer = CreateModule(vehicle, SLOT_1);
        Decide(bob, newcomer, 60_000).StaleOccupantId.Should().Be(old.Id);
        registry.RemoveEntity(old.Id);
        registry.AddOrUpdate(newcomer);

        ModuleSlotDecision lateComer = Decide(carol, CreateModule(vehicle, SLOT_1), 60_100);

        lateComer.Rejected.Should().BeTrue();
        lateComer.WinnerId.Should().Be(newcomer.Id);
    }

    [TestMethod]
    public void Decide_OccupantLoadedFromSaveWithoutClaim_NewcomerWinsAndNamesItAsStale()
    {
        // A module that was in the slot when the server started: no claim, only the registry knows it
        InstalledModuleEntity saved = CreateModule(vehicle, SLOT_1);
        registry.AddOrUpdate(saved);

        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 0);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().Be(saved.Id);
    }

    [TestMethod]
    public void Decide_StaleClaimWhoseModuleIsGone_AcceptsWithoutStaleOccupant()
    {
        InstalledModuleEntity gone = Install(alice, vehicle, SLOT_1, 0);
        registry.RemoveEntity(gone.Id);

        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 10_000);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().BeNull();
    }

    [TestMethod]
    public void Decide_WinnerRemovedInsideWindow_StillRejectsNewcomer_ThenAcceptsAfterWindow()
    {
        // Alice installs and removes again, Bob's client has taken his module out of the slot (or will): the server has to agree with it, the claim is a tombstone
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 0);
        clock.Now = 1000;
        registry.RemoveEntity(winner.Id);

        ModuleSlotDecision inside = Decide(bob, CreateModule(vehicle, SLOT_1), 1500);
        ModuleSlotDecision after = Decide(bob, CreateModule(vehicle, SLOT_1), ModuleSlotArbiter.CONTESTED_WINDOW_MS);

        inside.Rejected.Should().BeTrue();
        inside.WinnerId.Should().Be(winner.Id);
        after.Rejected.Should().BeFalse();
        after.StaleOccupantId.Should().BeNull();
    }

    [TestMethod]
    public void Decide_ParentMissingFromRegistry_StillArbitratesByClaim_AndStaleFoundByClaimItem()
    {
        // The registry does not hold the vehicle (nothing to scan for children), only the module itself
        InventoryItemEntity unknownVehicle = CreateEntity("Seamoth");
        InstalledModuleEntity winner = CreateModule(unknownVehicle, SLOT_1);
        Decide(alice, winner, 0).Rejected.Should().BeFalse();
        registry.AddOrUpdate(winner);

        ModuleSlotDecision inside = Decide(bob, CreateModule(unknownVehicle, SLOT_1), 100);
        ModuleSlotDecision after = Decide(bob, CreateModule(unknownVehicle, SLOT_1), 3500);

        inside.Rejected.Should().BeTrue();
        inside.WinnerId.Should().Be(winner.Id);
        after.Rejected.Should().BeFalse();
        after.StaleOccupantId.Should().Be(winner.Id);
    }

    [TestMethod]
    public void Decide_ClaimedModuleThatMovedToAnotherSlot_IsNotStaleForTheOldSlot()
    {
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 0);
        // Alice dragged it to the other slot: the server holds it there now (same id, other slot)
        InstalledModuleEntity moved = new(SLOT_2, winner.ClassId, winner.Id, winner.TechType, null, vehicle.Id, []);
        registry.AddOrUpdate(moved);

        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 10_000);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().BeNull("the module in the other slot is not what Bob's install replaces");
    }

    [TestMethod]
    public void Decide_OtherEntitiesAmongTheChildrenAreNeverStale()
    {
        // An item that is no module of this slot: a module in another slot, an inventory item
        registry.AddOrUpdate(CreateModule(vehicle, SLOT_2));
        InventoryItemEntity cargo = new(new NitroxId(), "classId", new NitroxTechType("Titanium"), null, vehicle.Id, []);
        registry.AddOrUpdate(cargo);

        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 0);

        decision.Rejected.Should().BeFalse();
        decision.StaleOccupantId.Should().BeNull();
    }

    [TestMethod]
    public void Decide_ManyClaims_AreTrimmedAfterTheWindow()
    {
        for (int i = 0; i < 300; i++)
        {
            Decide(alice, CreateModule(vehicle, $"Slot{i}"), 0).Rejected.Should().BeFalse();
        }
        arbiter.ClaimCount.Should().Be(300, "all of them are inside the window and still decide");

        Decide(bob, CreateModule(vehicle, "Fresh"), ModuleSlotArbiter.CONTESTED_WINDOW_MS).Rejected.Should().BeFalse();

        arbiter.ClaimCount.Should().Be(1, "older claims decide nothing, the registry tells what is stale");
    }

    [TestMethod]
    public void Decide_ClaimsInsideTheWindowSurviveATrim()
    {
        for (int i = 0; i < 300; i++)
        {
            Decide(alice, CreateModule(vehicle, $"Slot{i}"), 0);
        }
        InstalledModuleEntity winner = Install(alice, vehicle, SLOT_1, 2000);

        // The trim of this call drops the 300 old claims but keeps the one of SLOT_1
        Decide(bob, CreateModule(vehicle, "Fresh"), 3500);

        arbiter.ClaimCount.Should().Be(2);
        ModuleSlotDecision decision = Decide(bob, CreateModule(vehicle, SLOT_1), 3600);
        decision.Rejected.Should().BeTrue();
        decision.WinnerId.Should().Be(winner.Id);
    }

    [TestMethod]
    public void Decide_TwoSendersAtOnce_ExactlyOneIsAccepted()
    {
        for (int round = 0; round < 200; round++)
        {
            ModuleSlotArbiter racing = new(() => 0L);
            EntityRegistry racingRegistry = new(NullLogger<EntityRegistry>.Instance);
            InventoryItemEntity owner = CreateEntity("Seamoth");
            racingRegistry.AddOrUpdate(owner);
            InstalledModuleEntity fromAlice = CreateModule(owner, SLOT_1);
            InstalledModuleEntity fromBob = CreateModule(owner, SLOT_1);
            ModuleSlotDecision aliceResult = default;
            ModuleSlotDecision bobResult = default;

            Parallel.Invoke(
                () => aliceResult = racing.Decide(alice, fromAlice, racingRegistry),
                () => bobResult = racing.Decide(bob, fromBob, racingRegistry));

            int accepted = (aliceResult.Rejected ? 0 : 1) + (bobResult.Rejected ? 0 : 1);
            accepted.Should().Be(1, "exactly one of the two installs wins");
            ModuleSlotDecision loser = aliceResult.Rejected ? aliceResult : bobResult;
            InstalledModuleEntity winner = aliceResult.Rejected ? fromBob : fromAlice;
            loser.WinnerId.Should().Be(winner.Id);
        }
    }

    [TestMethod]
    public void Decide_NoOwnerOrNoSlot_IsNotArbitrated()
    {
        InstalledModuleEntity withoutOwner = CreateModule(null, SLOT_1);
        InstalledModuleEntity withoutSlot = CreateModule(vehicle, null!);

        for (int i = 0; i < 3; i++)
        {
            Decide(i % 2 == 0 ? alice : bob, withoutOwner, i).Rejected.Should().BeFalse();
            Decide(i % 2 == 0 ? alice : bob, withoutSlot, i).Rejected.Should().BeFalse();
        }

        arbiter.ClaimCount.Should().Be(0);
    }

    [TestMethod]
    public void Constructor_WithoutClock_UsesTheRealClock()
    {
        // The one the dependency injection uses: the window has to run on the real time
        ModuleSlotArbiter real = new();
        InstalledModuleEntity first = CreateModule(vehicle, SLOT_1);

        real.Decide(alice, first, registry).Rejected.Should().BeFalse();
        ModuleSlotDecision second = real.Decide(bob, CreateModule(vehicle, SLOT_1), registry);

        second.Rejected.Should().BeTrue("a few microseconds later is inside the 3 s window");
        second.WinnerId.Should().Be(first.Id);
    }
}
