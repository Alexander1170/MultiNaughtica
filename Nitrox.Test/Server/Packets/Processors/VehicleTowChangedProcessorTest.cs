using System.Collections.Generic;
using System.Net;
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
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The tow hooks on the server (feature group 2): a tow can only be true when the sender simulates both vehicles, nobody sits in the towed one and the kinds of vehicle fit; the server keeps the books
///     of the tows that are on, passes them on to the others, ends them when a player leaves, a vehicle is destroyed or a vehicle is taken over, and tells a player who joins.
/// </summary>
[TestClass]
public class VehicleTowChangedProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private VehicleTowRegistry registry = null!;
    private VehicleTowChangedProcessor processor = null!;
    private Player towerPlayer = null!;
    private Player other = null!;
    private VehicleEntity seamoth = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity prawn = null!;
    private VehicleEntity secondSeamoth = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        registry = new VehicleTowRegistry(packetSender);
        towerPlayer = CreatePlayer(1);
        other = CreatePlayer(2);
        seamoth = AddVehicle("Seamoth");
        cyclops = AddVehicle("Cyclops");
        prawn = AddVehicle("Exosuit");
        secondSeamoth = AddVehicle("Seamoth");
        processor = NewProcessor();
    }

    private VehicleTowChangedProcessor NewProcessor()
    {
        return new VehicleTowChangedProcessor(registry, ownership, entityRegistry, Options.Create(serverOptions), NullLogger<VehicleTowChangedProcessor>.Instance);
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private void HoldBoth(VehicleEntity tower, VehicleEntity towed, Player player)
    {
        ownership.TryToAcquire(tower.Id, player, SimulationLockType.EXCLUSIVE);
        ownership.TryToAcquire(towed.Id, player, SimulationLockType.TRANSIENT);
    }

    private Task Send(Player from, VehicleEntity tower, VehicleEntity towed, bool attached) => processor.Process(new AuthProcessorContext(from, packetSender), new VehicleTowChanged(tower.Id, towed.Id, attached, from.SessionId));

    // ---- a tow that can be

    [TestMethod]
    public async Task ASeamothThatSimulatesBothVehiclesCanTowASeamothAndTheOthersAreTold()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(1);
        packetSender.SentToOthers.Should().ContainSingle();
        VehicleTowChanged relayed = (VehicleTowChanged)packetSender.SentToOthers[0].Packet;
        packetSender.SentToOthers[0].Excluded.Should().Be(towerPlayer.SessionId, "the tower's own client knows");
        relayed.Attached.Should().BeTrue();
        relayed.TowerId.Should().Be(seamoth.Id);
        relayed.TowedId.Should().Be(secondSeamoth.Id);
        relayed.SessionId.Should().Be(towerPlayer.SessionId, "the server puts the sender's own session in, not whatever the packet said");
    }

    [TestMethod]
    public async Task ACyclopsCanTowAPrawnSuit()
    {
        HoldBoth(cyclops, prawn, towerPlayer);

        await Send(towerPlayer, cyclops, prawn, true);

        registry.Count.Should().Be(1);
    }

    // ---- a tow that cannot be

    [TestMethod]
    public async Task ARefusedTowIsAnsweredWithADetachedSoThatTheSenderLetsGo()
    {
        ownership.TryToAcquire(seamoth.Id, towerPlayer, SimulationLockType.EXCLUSIVE);
        // The towed vehicle is simulated by another player

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(0);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle();
        VehicleTowChanged answer = (VehicleTowChanged)packetSender.Sent[0].Packet;
        packetSender.Sent[0].SessionId.Should().Be(towerPlayer.SessionId);
        answer.Attached.Should().BeFalse();
        answer.TowedId.Should().Be(secondSeamoth.Id);
    }

    [TestMethod]
    public async Task APlayerWhoDoesNotSimulateTheTowerCannotTow()
    {
        ownership.TryToAcquire(seamoth.Id, other, SimulationLockType.EXCLUSIVE);
        ownership.TryToAcquire(secondSeamoth.Id, towerPlayer, SimulationLockType.TRANSIENT);

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task AVehicleThatTheSenderPilotsCannotBeTowedByTheSender()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        towerPlayer.PlayerContext = new PlayerContext("Player1", (ushort)1, new NitroxId(), false, new PlayerSettings(new NitroxColor(1f, 0f, 0f)), false, SubnauticaGameMode.SURVIVAL, secondSeamoth.Id, IntroCinematicMode.NONE, null!);

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(0, "only an unpiloted vehicle can be towed");
    }

    [TestMethod]
    public async Task AVehicleThatAnotherPlayerPilotsCannotBeTowedBecauseItsPilotHoldsTheLock()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        ownership.RevokeIfOwner(secondSeamoth.Id, towerPlayer);
        ownership.TryToAcquire(secondSeamoth.Id, other, SimulationLockType.EXCLUSIVE);

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(0);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(towerPlayer.SessionId);
    }

    [TestMethod]
    public async Task ThePrawnSuitCannotTowAndNothingCanTowACyclops()
    {
        HoldBoth(prawn, secondSeamoth, towerPlayer);
        await Send(towerPlayer, prawn, secondSeamoth, true);

        HoldBoth(seamoth, cyclops, towerPlayer);
        await Send(towerPlayer, seamoth, cyclops, true);

        registry.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task ATowerCannotTowItself()
    {
        HoldBoth(seamoth, seamoth, towerPlayer);

        await processor.Process(new AuthProcessorContext(towerPlayer, packetSender), new VehicleTowChanged(seamoth.Id, seamoth.Id, true, towerPlayer.SessionId));

        registry.Count.Should().Be(0);
        packetSender.Sent.Should().BeEmpty("a packet that makes no sense is dropped");
    }

    [TestMethod]
    public async Task AnIdTheServerDoesNotKnowIsRefused()
    {
        ownership.TryToAcquire(seamoth.Id, towerPlayer, SimulationLockType.EXCLUSIVE);
        NitroxId stranger = new();
        ownership.TryToAcquire(stranger, towerPlayer, SimulationLockType.TRANSIENT);

        await processor.Process(new AuthProcessorContext(towerPlayer, packetSender), new VehicleTowChanged(seamoth.Id, stranger, true, towerPlayer.SessionId));

        registry.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task WithTheVehicleUpgradesSwitchedOffNoTowIsAccepted()
    {
        serverOptions.VehicleUpgrades = false;
        HoldBoth(seamoth, secondSeamoth, towerPlayer);

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task AVehicleIsTowedByOneTowerAndATowerTowsOneVehicle()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        ownership.TryToAcquire(prawn.Id, towerPlayer, SimulationLockType.TRANSIENT);
        await Send(towerPlayer, seamoth, secondSeamoth, true);

        await Send(towerPlayer, seamoth, prawn, true);
        registry.Count.Should().Be(1, "a Seamoth that already tows cannot hook a second vehicle");

        VehicleEntity thirdSeamoth = AddVehicle("Seamoth");
        ownership.TryToAcquire(thirdSeamoth.Id, towerPlayer, SimulationLockType.EXCLUSIVE);
        await Send(towerPlayer, thirdSeamoth, secondSeamoth, true);
        registry.Count.Should().Be(1, "and a vehicle is towed by one tower");
    }

    // ---- letting go

    [TestMethod]
    public async Task TheTowerLettingGoEndsItForEverybody()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        packetSender.SentToOthers.Clear();

        await Send(towerPlayer, seamoth, secondSeamoth, false);

        registry.Count.Should().Be(0);
        packetSender.SentToOthers.Should().ContainSingle().Which.Packet.Should().BeOfType<VehicleTowChanged>().Which.Attached.Should().BeFalse();
    }

    [TestMethod]
    public async Task AnotherPlayerCannotEndATowOfSomebodyElse()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        packetSender.SentToOthers.Clear();

        await Send(other, seamoth, secondSeamoth, false);

        registry.Count.Should().Be(1);
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ALateLetGoOfAnEarlierTowDoesNotEndANewerOneOfAnotherTower()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        await Send(towerPlayer, seamoth, secondSeamoth, false);
        // Another player hooks the same vehicle with a Cyclops
        ownership.RevokeAllForOwner(towerPlayer.SessionId);
        HoldBoth(cyclops, secondSeamoth, other);
        await Send(other, cyclops, secondSeamoth, true);
        packetSender.SentToOthers.Clear();

        // The first tower's client says once more that it let go
        await Send(towerPlayer, seamoth, secondSeamoth, false);

        registry.Count.Should().Be(1);
        packetSender.SentToOthers.Should().BeEmpty();
    }

    // ---- the books

    [TestMethod]
    public async Task TheTowsThatAreOnAreToldToAPlayerWhoJoins()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        packetSender.Sent.Clear();

        await registry.SendSnapshotAsync(other.SessionId);

        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].SessionId.Should().Be(other.SessionId);
        VehicleTowChanged told = (VehicleTowChanged)packetSender.Sent[0].Packet;
        told.Attached.Should().BeTrue();
        told.TowerId.Should().Be(seamoth.Id);
        told.TowedId.Should().Be(secondSeamoth.Id);
    }

    [TestMethod]
    public async Task ATowedVehicleDoesNotTowAndATowerIsNotTowedSoThereAreNoChainsOrRings()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        ownership.TryToAcquire(prawn.Id, towerPlayer, SimulationLockType.TRANSIENT);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        await Send(towerPlayer, secondSeamoth, prawn, true);
        await Send(towerPlayer, secondSeamoth, seamoth, true);

        registry.Count.Should().Be(1, "the second Seamoth is towed and cannot tow: no chain, no ring");
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(2);
        packetSender.Sent.Should().OnlyContain(sent => !((VehicleTowChanged)sent.Packet).Attached);
    }

    [TestMethod]
    public async Task ATowOfAPlayerWhoHasLeftIsNotLeftBehind()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        towerPlayer.IsOnline = false;

        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.Count.Should().Be(0);
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task WhenTheTowingPlayerLeavesTheTowIsEndedForEverybodyElse()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        packetSender.SentToOthers.Clear();

        await registry.OnEventAsync(new ISessionCleaner.Args(new SessionManager.Session(towerPlayer.SessionId, new IPEndPoint(IPAddress.Loopback, 1)), 1));

        registry.Count.Should().Be(0);
        packetSender.SentToOthers.Should().ContainSingle().Which.Packet.Should().BeOfType<VehicleTowChanged>().Which.Attached.Should().BeFalse();
    }

    [TestMethod]
    public async Task WhenAVehicleOfTheTowIsDestroyedTheTowIsEnded()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);
        packetSender.SentToAll.Clear();

        await registry.BroadcastEndedAsync(registry.EntityDestroyed(secondSeamoth.Id));

        registry.Count.Should().Be(0);
        packetSender.SentToAll.Should().ContainSingle().Which.Should().BeOfType<VehicleTowChanged>();
    }

    [TestMethod]
    public async Task WhenAnotherPlayerGetsTheLockOfTheTowedVehicleTheTowIsEnded()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);

        IReadOnlyList<VehicleTowRegistry.Entry> ended = registry.LockTaken(secondSeamoth.Id, other.SessionId);

        ended.Should().ContainSingle();
        registry.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task WhenTheTowingPlayerItselfRequestsALockOfTheTowIsKept()
    {
        HoldBoth(seamoth, secondSeamoth, towerPlayer);
        await Send(towerPlayer, seamoth, secondSeamoth, true);

        registry.LockTaken(secondSeamoth.Id, towerPlayer.SessionId).Should().BeEmpty("the downgrade of the lock to transient is the tower's own request");
        registry.LockTaken(seamoth.Id, towerPlayer.SessionId).Should().BeEmpty();
        registry.LockTaken(prawn.Id, other.SessionId).Should().BeEmpty("a vehicle that is not in the tow");
        registry.Count.Should().Be(1);
    }

    [TestMethod]
    public async Task WhenAnotherPlayerTakesTheTowerOverTheTowIsEnded()
    {
        HoldBoth(cyclops, secondSeamoth, towerPlayer);
        await Send(towerPlayer, cyclops, secondSeamoth, true);

        registry.LockTaken(cyclops.Id, other.SessionId).Should().ContainSingle();
        registry.Count.Should().Be(0);
    }

    // ---- the packet

    [TestMethod]
    public void ThePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId tower = new();
        NitroxId towed = new();

        VehicleTowChanged received = (VehicleTowChanged)Packet.Deserialize(new VehicleTowChanged(tower, towed, true, (ushort)7).Serialize());

        received.TowerId.Should().Be(tower);
        received.TowedId.Should().Be(towed);
        received.Attached.Should().BeTrue();
        received.SessionId.Should().Be((SessionId)(ushort)7);
    }

    [TestMethod]
    public void AClientCannotMakeTheServerSendAnEndOfTowToOthersThroughTheDefaultProcessor()
    {
        // The tow has a processor of its own, so the default processor (which relays every packet type without one) never sees it
        processor.Should().BeAssignableTo<IAuthPacketProcessor<VehicleTowChanged>>();
    }

    // ---- helpers

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private sealed class RecordingPacketSender : IPacketSender
    {
        public List<(Packet Packet, SessionId SessionId)> Sent { get; } = [];
        public List<(Packet Packet, SessionId Excluded)> SentToOthers { get; } = [];
        public List<Packet> SentToAll { get; } = [];

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add((packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet
        {
            SentToAll.Add(packet);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet
        {
            SentToOthers.Add((packet, excludedSessionId));
            return ValueTask.CompletedTask;
        }
    }
}
