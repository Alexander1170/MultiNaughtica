using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
/// What the server does with the installs of two players for the same module slot (V0, U4). The processor is built with the real registry, world entity manager and entity
/// simulation (the pattern of <c>EscapePodManagerTest</c>); no player is connected, so the forwarding to other players is not observable, only what is sent to the sender, to
/// everybody else and to everybody, and what the registry holds.
/// </summary>
[TestClass]
public class EntitySpawnedByClientModuleSlotTest
{
    private const string SLOT = "SeamothModule1";

    private RecordingPacketSender packetSender;
    private EntityRegistry entityRegistry;
    private SimulationOwnershipData simulationOwnershipData;
    private EntitySpawnedByClientProcessor processor;
    private long now;
    private Player alice;
    private Player bob;
    private InventoryItemEntity vehicle;

    [TestInitialize]
    public void Init()
    {
        now = 0;
        packetSender = new RecordingPacketSender();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        simulationOwnershipData = new SimulationOwnershipData();
        // The batch spawner and the player manager are not used to destroy or register an entity that is no world entity
        WorldEntityManager worldEntityManager = new(packetSender, entityRegistry, null, null, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        PlayerManager playerManager = new(null, null, Options.Create(new SubnauticaServerOptions()), NullLogger<PlayerManager>.Instance);
        EntitySimulation entitySimulation = new(packetSender, entityRegistry, worldEntityManager, simulationOwnershipData, playerManager, NullLogger<EntitySimulation>.Instance);
        processor = new EntitySpawnedByClientProcessor(playerManager, entityRegistry, worldEntityManager, entitySimulation, new ModuleSlotArbiter(() => now), NullLogger<EntitySpawnedByClientProcessor>.Instance);

        alice = CreatePlayer(1);
        bob = CreatePlayer(2);
        vehicle = new InventoryItemEntity(new NitroxId(), "classId", new NitroxTechType("Seamoth"), null, null, []);
        entityRegistry.AddOrUpdate(vehicle);
    }

    private InstalledModuleEntity CreateModule(string slot = SLOT, string tech = "VehicleArmorPlating") => new(slot, "classId", new NitroxId(), new NitroxTechType(tech), null, vehicle.Id, []);

    private IEnumerable<T> SentOfType<T>() where T : Packet => packetSender.Sent.Select(sent => sent.Packet).OfType<T>();

    private async Task Send(Player sender, Entity entity, long atMs)
    {
        now = atMs;
        await processor.Process(new AuthProcessorContext(sender, packetSender), new EntitySpawnedByClient(entity, true));
    }

    /// <summary>
    /// The registry holds the module that the player carries as an item of his inventory, as it does until the install is accepted.
    /// </summary>
    private InventoryItemEntity RegisterAsInventoryItem(InstalledModuleEntity module, Player owner)
    {
        InventoryItemEntity item = new(module.Id, module.ClassId, module.TechType, null, owner.GameObjectId, []);
        entityRegistry.AddOrUpdate(item);
        return item;
    }

    [TestMethod]
    public async Task Process_FirstInstallOfASlot_IsRegisteredUnderTheOwnerAndTakesTheTransientLock()
    {
        InstalledModuleEntity module = CreateModule();
        RegisterAsInventoryItem(module, alice);

        await Send(alice, module, 0);

        entityRegistry.TryGetEntityById(module.Id, out InstalledModuleEntity registered).Should().BeTrue();
        registered.Should().BeSameAs(module);
        vehicle.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(module);
        SentOfType<ModuleSlotRejected>().Should().BeEmpty();
        simulationOwnershipData.TryGetLock(module.Id, out SimulationOwnershipData.PlayerLock playerLock).Should().BeTrue();
        playerLock.Player.Should().BeSameAs(alice);
    }

    [TestMethod]
    public async Task Process_SecondPlayerSameSlotInsideTheWindow_IsSentBackAndNothingIsRegisteredOrForwarded()
    {
        InstalledModuleEntity winner = CreateModule();
        InstalledModuleEntity loser = CreateModule();
        InventoryItemEntity inBobsInventory = RegisterAsInventoryItem(loser, bob);
        await Send(alice, winner, 0);
        packetSender.Sent.Clear();

        await Send(bob, loser, 40);

        // Only Bob hears about it, and only that his install was refused
        packetSender.Sent.Should().ContainSingle();
        SentPacket reply = packetSender.Sent[0];
        reply.Route.Should().Be(Route.ToSession);
        reply.Session.Should().Be(bob.SessionId);
        ModuleSlotRejected rejected = reply.Packet.Should().BeOfType<ModuleSlotRejected>().Subject;
        rejected.ItemId.Should().Be(loser.Id);
        rejected.ParentId.Should().Be(vehicle.Id);
        rejected.Slot.Should().Be(SLOT);

        // The server keeps what it had: Alice's module in the slot, Bob's module as the item of his inventory, and no second module for the slot
        vehicle.ChildEntities.Should().ContainSingle().Which.Id.Should().Be(winner.Id);
        entityRegistry.TryGetEntityById(loser.Id, out Entity registered).Should().BeTrue();
        registered.Should().BeSameAs(inBobsInventory);
        simulationOwnershipData.TryGetLock(loser.Id, out SimulationOwnershipData.PlayerLock _).Should().BeFalse("a refused install takes no simulation lock");
    }

    [TestMethod]
    public async Task Process_RefusedInstallOfAModuleTheServerForgot_IsRegisteredInTheInventoryOfTheSenderAndNobodyElseIsToldOnlyTheSenderGetsTheReply()
    {
        // A module moved from another slot: the destroy of its old slot was processed first, the contested install is the second half of the move. The sender's client puts the
        // module into his inventory without telling anybody, so the server has to register it there or it would lose the module
        InstalledModuleEntity winner = CreateModule();
        InstalledModuleEntity moved = CreateModule();
        await Send(alice, winner, 0);
        packetSender.Sent.Clear();

        await Send(bob, moved, 40);

        entityRegistry.TryGetEntityById(moved.Id, out InventoryItemEntity inventoryItem).Should().BeTrue();
        inventoryItem.ParentId.Should().Be(bob.GameObjectId);
        // the other players have no container of the sender's to spawn an inventory item into: only the reply goes out
        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].Route.Should().Be(Route.ToSession);
        packetSender.Sent[0].Session.Should().Be(bob.SessionId);
        packetSender.Sent[0].Packet.Should().BeOfType<ModuleSlotRejected>();
        SentOfType<SpawnEntities>().Should().BeEmpty();
        vehicle.ChildEntities.Should().ContainSingle().Which.Id.Should().Be(winner.Id);
    }

    [TestMethod]
    public async Task Process_SecondPlayerAfterTheWindow_WinsAndTheOldModuleIsRemovedForEverybodyBeforeTheNewOneIsForwarded()
    {
        InstalledModuleEntity old = CreateModule();
        InstalledModuleEntity newcomer = CreateModule();
        RegisterAsInventoryItem(newcomer, bob);
        await Send(alice, old, 0);
        packetSender.Sent.Clear();

        await Send(bob, newcomer, ModuleSlotArbiter.CONTESTED_WINDOW_MS + 1);

        SentOfType<ModuleSlotRejected>().Should().BeEmpty();
        packetSender.Sent[0].Route.Should().Be(Route.ToOthers);
        packetSender.Sent[0].Session.Should().Be(bob.SessionId, "the sender is not told, his slot was empty");
        packetSender.Sent[0].Packet.Should().BeOfType<EntityDestroyed>().Which.Id.Should().Be(old.Id);
        packetSender.Sent.Should().HaveCount(2);
        packetSender.Sent[1].Route.Should().Be(Route.ToAll);
        packetSender.Sent[1].Packet.Should().BeOfType<SimulationOwnershipChange>("Bob's lock follows the removal");
        entityRegistry.TryGetEntityById(old.Id, out Entity _).Should().BeFalse();
        vehicle.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(newcomer);
        simulationOwnershipData.TryGetLock(old.Id, out SimulationOwnershipData.PlayerLock _).Should().BeFalse("the lock of the removed module is dropped");
    }

    [TestMethod]
    public async Task Process_ModuleThatTheServerLoadedFromTheSave_IsReplacedByTheInstallOfAPlayer()
    {
        InstalledModuleEntity saved = CreateModule();
        entityRegistry.AddOrUpdate(saved);
        InstalledModuleEntity installed = CreateModule();

        await Send(alice, installed, 0);

        packetSender.Sent[0].Packet.Should().BeOfType<EntityDestroyed>().Which.Id.Should().Be(saved.Id);
        vehicle.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(installed);
    }

    [TestMethod]
    public async Task Process_SamePlayerSwapsTheModuleOfASlot_IsNeverSentBack()
    {
        InstalledModuleEntity first = CreateModule();
        InstalledModuleEntity second = CreateModule();
        await Send(alice, first, 0);
        packetSender.Sent.Clear();

        await Send(alice, second, 10);

        SentOfType<ModuleSlotRejected>().Should().BeEmpty();
        vehicle.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(second);
    }

    [TestMethod]
    public async Task Process_OtherSlotOfTheSameVehicle_IsIndependent()
    {
        InstalledModuleEntity inFirstSlot = CreateModule();
        InstalledModuleEntity inSecondSlot = CreateModule("SeamothModule2");
        await Send(alice, inFirstSlot, 0);
        packetSender.Sent.Clear();

        await Send(bob, inSecondSlot, 10);

        SentOfType<ModuleSlotRejected>().Should().BeEmpty();
        SentOfType<EntityDestroyed>().Should().BeEmpty();
        vehicle.ChildEntities.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Process_AnythingThatIsNoModuleOfASlot_IsLeftToTheOldPath()
    {
        InventoryItemEntity item = new(new NitroxId(), "classId", new NitroxTechType("Titanium"), null, vehicle.Id, []);

        await Send(alice, item, 0);

        SentOfType<ModuleSlotRejected>().Should().BeEmpty();
        SentOfType<EntityDestroyed>().Should().BeEmpty();
        entityRegistry.TryGetEntityById(item.Id, out Entity _).Should().BeTrue();
    }

    [TestMethod]
    public void IsArbitratedModule_OnlyAModuleWithItsOwnerAndSlotQualifies()
    {
        InstalledModuleEntity module = CreateModule();
        EntitySpawnedByClientProcessor.IsArbitratedModule(new EntitySpawnedByClient(module, true), out InstalledModuleEntity found).Should().BeTrue();
        found.Should().BeSameAs(module);

        InstalledModuleEntity withoutOwner = new(SLOT, "classId", new NitroxId(), new NitroxTechType("VehicleArmorPlating"), null, null, []);
        InstalledModuleEntity withoutSlot = new(null, "classId", new NitroxId(), new NitroxTechType("VehicleArmorPlating"), null, vehicle.Id, []);
        EntitySpawnedByClientProcessor.IsArbitratedModule(new EntitySpawnedByClient(withoutOwner, true), out InstalledModuleEntity _).Should().BeFalse();
        EntitySpawnedByClientProcessor.IsArbitratedModule(new EntitySpawnedByClient(withoutSlot, true), out InstalledModuleEntity _).Should().BeFalse();

        // A spawner's creature is introduced with OnlyIfNew, and other kinds of entities are no modules
        EntitySpawnedByClientProcessor.IsArbitratedModule(new EntitySpawnedByClient(module, onlyIfNew: true), out InstalledModuleEntity _).Should().BeFalse();
        InventoryItemEntity item = new(new NitroxId(), "classId", new NitroxTechType("Titanium"), null, vehicle.Id, []);
        EntitySpawnedByClientProcessor.IsArbitratedModule(new EntitySpawnedByClient(item, true), out InstalledModuleEntity notAModule).Should().BeFalse();
        notAModule.Should().BeNull();
    }

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private enum Route
    {
        ToSession,
        ToAll,
        ToOthers
    }

    /// <param name="Session">The addressee for <see cref="Route.ToSession" />, the session that is left out for <see cref="Route.ToOthers" />.</param>
    private sealed record SentPacket(Route Route, Packet Packet, SessionId Session);

    // ---- the Rider Bar rule reads what an install leaves behind

    [TestMethod]
    public async Task AnAcceptedInstallOfTheRiderBarIsWhatLetsAPlayerHoldOnAndTakingItOutEndsThat()
    {
        // The Seamoth Rider Bar checks the server's view of the Seamoth's modules: the module that the real install path registered as a child of the Seamoth
        InstalledModuleEntity bar = CreateModule(SLOT, CoopItemTechNames.SEAMOTH_RIDER_BAR);
        RegisterAsInventoryItem(bar, alice);
        await Send(alice, bar, 0);
        SeamothRiderArbiter arbiter = new(packetSender);
        SeamothRiderChangedProcessor riders = new(arbiter, entityRegistry, Options.Create(new SubnauticaServerOptions()), NullLogger<SeamothRiderChangedProcessor>.Instance);
        packetSender.Sent.Clear();

        await riders.Process(new AuthProcessorContext(bob, packetSender), new SeamothRiderChanged(RiderBarMessage.Hold, vehicle.Id, bob.SessionId));

        arbiter.Rides(bob.SessionId, vehicle.Id).Should().BeTrue();
        SentOfType<SeamothRiderChanged>().Should().ContainSingle().Which.Message.Should().Be(RiderBarMessage.Holding);

        // Taken out again (the destroy of the module is what the server gets): the next one is denied
        await riders.Process(new AuthProcessorContext(bob, packetSender), new SeamothRiderChanged(RiderBarMessage.LetGo, vehicle.Id, bob.SessionId));
        entityRegistry.RemoveEntity(bar.Id);
        packetSender.Sent.Clear();

        await riders.Process(new AuthProcessorContext(bob, packetSender), new SeamothRiderChanged(RiderBarMessage.Hold, vehicle.Id, bob.SessionId));

        arbiter.Count.Should().Be(0);
        SentOfType<SeamothRiderChanged>().Should().ContainSingle().Which.Message.Should().Be(RiderBarMessage.Denied);
    }

    [TestMethod]
    public async Task AnotherModuleInTheSlotDoesNotLetAPlayerHoldOn()
    {
        InstalledModuleEntity other = CreateModule();
        RegisterAsInventoryItem(other, alice);
        await Send(alice, other, 0);
        SeamothRiderArbiter arbiter = new(packetSender);
        SeamothRiderChangedProcessor riders = new(arbiter, entityRegistry, Options.Create(new SubnauticaServerOptions()), NullLogger<SeamothRiderChangedProcessor>.Instance);
        packetSender.Sent.Clear();

        await riders.Process(new AuthProcessorContext(bob, packetSender), new SeamothRiderChanged(RiderBarMessage.Hold, vehicle.Id, bob.SessionId));

        arbiter.Count.Should().Be(0);
        SentOfType<SeamothRiderChanged>().Should().ContainSingle().Which.Message.Should().Be(RiderBarMessage.Denied);
    }

    private sealed class RecordingPacketSender : IPacketSender
    {
        public List<SentPacket> Sent { get; } = [];

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add(new SentPacket(Route.ToSession, packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet
        {
            Sent.Add(new SentPacket(Route.ToAll, packet, default));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet
        {
            Sent.Add(new SentPacket(Route.ToOthers, packet, excludedSessionId));
            return ValueTask.CompletedTask;
        }
    }
}
