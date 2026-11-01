using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The drone bay on the server (feature group 3, station 4): a drone is loaded, taken out, docked and undocked on the word of a player aboard (load, unload) or of the operator (dock, undock), the bay holds one
///     drone, only a Scout Drone can be loaded, and what the server accepts it tells everybody; what it does not accept it leaves as it was and does not answer.
/// </summary>
[TestClass]
public class CyclopsDroneBayProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsDroneArbiter arbiter = null!;
    private CyclopsDroneBayProcessor processor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private Player outsider = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity seamoth = null!;
    private WorldEntity drone = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        arbiter = new CyclopsDroneArbiter(packetSender);
        PlayerManager noPlayers = new(null!, null!, Options.Create(serverOptions), NullLogger<PlayerManager>.Instance);
        WorldEntityManager worldEntityManager = new(packetSender, entityRegistry, null!, noPlayers, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        processor = new CyclopsDroneBayProcessor(arbiter, entityRegistry, worldEntityManager, Options.Create(serverOptions), NullLogger<CyclopsDroneBayProcessor>.Instance);
        cyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        drone = new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType(ScoutDroneRules.ITEM_TECH_TYPE_NAME), null, null, []);
        entityRegistry.AddOrUpdate(drone);
        alice = CreatePlayer(1, cyclops.Id);
        bob = CreatePlayer(2, cyclops.Id);
        outsider = CreatePlayer(3, null);
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsDroneBayKind kind, NitroxId? cyclopsId = null, NitroxId? droneId = null)
    {
        return processor.Process(new AuthProcessorContext(from, packetSender), new CyclopsDroneBay(kind, cyclopsId ?? cyclops.Id, droneId ?? drone.Id));
    }

    private CyclopsDroneState LastState() => (CyclopsDroneState)packetSender.SentToAll[^1];

    [TestMethod]
    public async Task APlayerAboardLoadsAScoutDroneAndEverybodyIsToldItIsDocked()
    {
        await Send(alice, CyclopsDroneBayKind.Load);

        arbiter.TryGetDrone(cyclops.Id, out NitroxId? held, out bool docked).Should().BeTrue();
        held.Should().Be(drone.Id);
        docked.Should().BeTrue();
        packetSender.SentToAll.Should().ContainSingle();
        CyclopsDroneState state = LastState();
        state.CyclopsId.Should().Be(cyclops.Id);
        state.DroneId.Should().Be(Optional.Of(drone.Id));
        state.Docked.Should().BeTrue();
    }

    [TestMethod]
    public async Task TheBayIsSavedOnTheDronesEntityAndADockedDroneIsPlacedWithItsCyclops()
    {
        cyclops.Transform.Position = new NitroxVector3(900f, -40f, 300f);

        await Send(alice, CyclopsDroneBayKind.Load);

        ScoutDroneMetadata loaded = drone.Metadata.Should().BeOfType<ScoutDroneMetadata>().Subject;
        loaded.CyclopsId.Should().Be(cyclops.Id);
        loaded.Docked.Should().BeTrue();
        drone.Transform.Position.Should().Be(new NitroxVector3(900f, -40f, 300f), "the drone in the dock is where its Cyclops is, so that the cells it is loaded in follow the Cyclops");

        // flown out by the operator: still the bay's drone, not docked
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        await Send(alice, CyclopsDroneBayKind.Undock);
        drone.Metadata.Should().BeOfType<ScoutDroneMetadata>().Which.Docked.Should().BeFalse();

        // flown home
        await Send(alice, CyclopsDroneBayKind.Dock);
        drone.Metadata.Should().BeOfType<ScoutDroneMetadata>().Which.Docked.Should().BeTrue();

        // picked up by a player who stands at the dock: it belongs to no bay any more, so the world is saved without one
        alice.Position = new NitroxVector3(900f, -40f, 300f);
        await Send(alice, CyclopsDroneBayKind.Unload);
        drone.Metadata.Should().BeNull();
    }

    [TestMethod]
    public async Task TheHealthOfTheDroneSurvivesEveryChangeOfItsBay()
    {
        drone.Metadata = new ScoutDroneMetadata(null, false, 35f);

        await Send(alice, CyclopsDroneBayKind.Load);
        drone.Metadata.Should().BeOfType<ScoutDroneMetadata>().Which.Health.Should().Be(35f);

        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        await Send(alice, CyclopsDroneBayKind.Undock);
        drone.Metadata.Should().BeOfType<ScoutDroneMetadata>().Which.Health.Should().Be(35f);

        await Send(alice, CyclopsDroneBayKind.Dock);
        await Send(alice, CyclopsDroneBayKind.Unload);
        ScoutDroneMetadata loose = drone.Metadata.Should().BeOfType<ScoutDroneMetadata>().Subject;
        loose.CyclopsId.Should().BeNull("the drone is in no bay");
        loose.Health.Should().Be(35f, "it is still hurt");
    }

    [TestMethod]
    public async Task ARequestThatChangesNothingLeavesTheMetadataAlone()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        ScoutDroneMetadata before = (ScoutDroneMetadata)drone.Metadata!;

        await Send(bob, CyclopsDroneBayKind.Undock);

        drone.Metadata.Should().BeSameAs(before, "bob is no operator");
    }

    [TestMethod]
    public async Task APlayerWhoIsNotAboardCannotLoadButCanPickTheDockedDroneUpFromTheHull()
    {
        await Send(outsider, CyclopsDroneBayKind.Load);
        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse("the console is inside the Cyclops");

        await Send(alice, CyclopsDroneBayKind.Load);
        packetSender.SentToAll.Clear();
        await Send(outsider, CyclopsDroneBayKind.Unload);

        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse("the dock is on the hull outside: a swimmer can take the drone from it");
        LastState().DroneId.HasValue.Should().BeFalse();
        drone.Metadata.Should().BeNull("a drone that is in no bay has no bay metadata");
    }

    [TestMethod]
    public async Task OnlyAScoutDroneCanBeLoaded()
    {
        await Send(alice, CyclopsDroneBayKind.Load, droneId: seamoth.Id);
        await Send(alice, CyclopsDroneBayKind.Load, droneId: new NitroxId());

        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse();
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == alice.SessionId && sent.Packet is CyclopsDroneState && !((CyclopsDroneState)sent.Packet).DroneId.HasValue,
                                                                "a request that is not accepted is answered with the bay as it is, to the sender alone");
    }

    [TestMethod]
    public async Task TheBayHoldsOneDroneAndTheSameDroneIsNotInTwoBays()
    {
        GlobalRootEntity second = new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType(ScoutDroneRules.ITEM_TECH_TYPE_NAME), null, null, []);
        entityRegistry.AddOrUpdate(second);
        VehicleEntity otherCyclops = AddVehicle("Cyclops");
        Player carol = CreatePlayer(4, otherCyclops.Id);

        await Send(alice, CyclopsDroneBayKind.Load);
        packetSender.SentToAll.Clear();
        await Send(alice, CyclopsDroneBayKind.Load, droneId: second.Id);
        await Send(carol, CyclopsDroneBayKind.Load, cyclopsId: otherCyclops.Id);

        arbiter.TryGetDrone(cyclops.Id, out NitroxId? held, out _).Should().BeTrue();
        held.Should().Be(drone.Id, "the second drone did not replace the first");
        arbiter.TryGetDrone(otherCyclops.Id, out _, out _).Should().BeFalse("the first drone is in another bay");
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task OnlyTheOperatorDocksAndUndocks()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        packetSender.SentToAll.Clear();

        await Send(bob, CyclopsDroneBayKind.Undock);
        arbiter.TryGetDrone(cyclops.Id, out _, out bool dockedAfterBob).Should().BeTrue();
        dockedAfterBob.Should().BeTrue("Bob does not fly it");
        packetSender.SentToAll.Should().BeEmpty();

        await Send(alice, CyclopsDroneBayKind.Undock);
        arbiter.TryGetDrone(cyclops.Id, out _, out bool dockedAfterAlice).Should().BeTrue();
        dockedAfterAlice.Should().BeFalse();
        LastState().Docked.Should().BeFalse();

        packetSender.SentToAll.Clear();
        await Send(alice, CyclopsDroneBayKind.Undock);
        packetSender.SentToAll.Should().BeEmpty("it is out already: nothing changed");

        await Send(alice, CyclopsDroneBayKind.Dock);
        LastState().Docked.Should().BeTrue();
    }

    [TestMethod]
    public async Task ADroneThatIsFlownCannotBeUnloadedAndOneInTheDockCanAndTheSeatEndsWithIt()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        await Send(alice, CyclopsDroneBayKind.Undock);
        packetSender.SentToAll.Clear();

        await Send(bob, CyclopsDroneBayKind.Unload);
        packetSender.SentToAll.Should().BeEmpty("a drone that somebody flies is not picked up");
        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeTrue();

        await Send(alice, CyclopsDroneBayKind.Dock);
        packetSender.SentToAll.Clear();
        await Send(bob, CyclopsDroneBayKind.Unload);

        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse();
        LastState().DroneId.HasValue.Should().BeFalse("the bay is empty again");
        arbiter.Count.Should().Be(0, "there is nothing to fly any more, so the seat ended with the drone");
        packetSender.SentToAll.OfType<CyclopsDroneSeat>().Should().ContainSingle(seat => seat.Message == CyclopsDroneSeatMessage.Released && seat.SessionId == alice.SessionId && seat.Reason == CyclopsDroneSeatReason.DroneDestroyed);
    }

    [TestMethod]
    public async Task ADroneThatIsOutAndFlownByNobodyCanBePickedUpByAPlayerNearIt()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        await Send(alice, CyclopsDroneBayKind.Undock);
        arbiter.Leave(cyclops.Id, alice.SessionId);
        drone.Transform.Position = new NitroxVector3(500f, -60f, 100f);
        packetSender.SentToAll.Clear();

        // a swimmer far from the drone and far from the Cyclops cannot
        outsider.Position = new NitroxVector3(-900f, -10f, 900f);
        await Send(outsider, CyclopsDroneBayKind.Unload);
        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeTrue();
        packetSender.SentToAll.Should().BeEmpty();

        // one who swam to the drone picks it up out in the water
        outsider.Position = new NitroxVector3(505f, -60f, 100f);
        await Send(outsider, CyclopsDroneBayKind.Unload);

        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse("the drone that was left adrift is out of the bay for good");
        LastState().DroneId.HasValue.Should().BeFalse();
        drone.Metadata.Should().BeNull();
    }

    [TestMethod]
    public async Task APlayerFarFromTheDroneAndTheCyclopsCannotEmptyTheBay()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        packetSender.SentToAll.Clear();
        packetSender.Sent.Clear();
        outsider.Position = new NitroxVector3(1500f, 0f, 1500f);

        await Send(outsider, CyclopsDroneBayKind.Unload);

        arbiter.TryGetDrone(cyclops.Id, out _, out bool docked).Should().BeTrue();
        docked.Should().BeTrue();
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(outsider.SessionId, "the refused player is told how the bay is");
    }

    [TestMethod]
    public async Task ADroneThatTheBayHoldsOutAndThatIsLoadedAgainIsDockedAgainUnlessSomebodyFliesIt()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        await Send(alice, CyclopsDroneBayKind.Undock);
        packetSender.SentToAll.Clear();

        // alice flies it: bob cannot dock it by putting the same drone into the bay
        await Send(bob, CyclopsDroneBayKind.Load);
        arbiter.TryGetDrone(cyclops.Id, out _, out bool dockedWhileFlown).Should().BeTrue();
        dockedWhileFlown.Should().BeFalse();
        packetSender.SentToAll.Should().BeEmpty();

        // nobody flies it any more (alice left the console); bob picked it up from the water and puts it into the dock at the console
        arbiter.Leave(cyclops.Id, alice.SessionId);
        await Send(bob, CyclopsDroneBayKind.Load);

        arbiter.TryGetDrone(cyclops.Id, out NitroxId? held, out bool dockedAgain).Should().BeTrue();
        held.Should().Be(drone.Id);
        dockedAgain.Should().BeTrue();
        LastState().Docked.Should().BeTrue();
    }

    [TestMethod]
    public async Task ARequestThatIsNotAcceptedIsAnsweredToTheSenderAloneWithTheBayAsItIs()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        packetSender.SentToAll.Clear();
        packetSender.Sent.Clear();

        await Send(bob, CyclopsDroneBayKind.Undock);

        packetSender.SentToAll.Should().BeEmpty();
        CyclopsDroneState told = (CyclopsDroneState)packetSender.Sent.Should().ContainSingle().Which.Packet;
        told.CyclopsId.Should().Be(cyclops.Id);
        told.DroneId.Should().Be(Optional.Of(drone.Id));
        told.Docked.Should().BeTrue("bob's client set right: the drone is docked, bob is no operator");
    }

    [TestMethod]
    public async Task ARequestForSomethingThatIsNoLiveCyclopsOrWithTheUpgradesOffOrOfAKindThatDoesNotExistChangesNothing()
    {
        await Send(alice, CyclopsDroneBayKind.Load, cyclopsId: seamoth.Id);
        await Send(alice, (CyclopsDroneBayKind)9);
        await Send(alice, CyclopsDroneBayKind.Unknown);
        cyclops.Metadata = new CyclopsMetadata(false, false, false, false, 0, 100, true);
        await Send(alice, CyclopsDroneBayKind.Load);
        cyclops.Metadata = null;
        serverOptions.VehicleUpgrades = false;
        await Send(alice, CyclopsDroneBayKind.Load);
        await processor.Process(new AuthProcessorContext(alice, packetSender), new CyclopsDroneBay(CyclopsDroneBayKind.Load, null!, drone.Id));

        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse();
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ADroneThatIsDestroyedEmptiesItsBayAndEndsTheSeatThatFlewIt()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);

        (NitroxId? bayCyclops, IReadOnlyList<CyclopsDroneArbiter.Seat> ended) = arbiter.DroneGone(drone.Id);

        bayCyclops.Should().Be(cyclops.Id);
        ended.Should().ContainSingle().Which.Session.Should().Be(alice.SessionId);
        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse();
        arbiter.Count.Should().Be(0);
        arbiter.DroneGone(drone.Id).CyclopsId.Should().BeNull("nothing to end twice");
    }

    [TestMethod]
    public async Task ADestroyedCyclopsForgetsItsBay()
    {
        await Send(alice, CyclopsDroneBayKind.Load);

        arbiter.CyclopsGone(cyclops.Id);

        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse();
        arbiter.StateOf(cyclops.Id).DroneId.HasValue.Should().BeFalse();
    }

    [TestMethod]
    public async Task AJoinerIsToldWhoFliesAndWhichBaysHoldADrone()
    {
        await Send(alice, CyclopsDroneBayKind.Load);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        packetSender.Sent.Clear();
        SessionId joiner = (ushort)9;

        await arbiter.SendSnapshotAsync(joiner);

        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == joiner);
        packetSender.Sent.Select(sent => sent.Packet).OfType<CyclopsDroneSeat>().Should().ContainSingle().Which.PlayerName.Should().Be(alice.Name);
        CyclopsDroneState state = packetSender.Sent.Select(sent => sent.Packet).OfType<CyclopsDroneState>().Single();
        state.DroneId.Should().Be(Optional.Of(drone.Id));
        state.Docked.Should().BeTrue();
    }

    [TestMethod]
    public void TheDronePacketsSurviveTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsDroneBay bay = new(CyclopsDroneBayKind.Undock, new NitroxId(), new NitroxId());
        CyclopsDroneState full = new(new NitroxId(), Optional.Of(new NitroxId()), true);
        CyclopsDroneState empty = new(new NitroxId(), Optional.Empty, false);

        ((CyclopsDroneBay)Packet.Deserialize(bay.Serialize())).Should().BeEquivalentTo(bay);
        ((CyclopsDroneState)Packet.Deserialize(full.Serialize())).Should().BeEquivalentTo(full);
        ((CyclopsDroneState)Packet.Deserialize(empty.Serialize())).DroneId.HasValue.Should().BeFalse();
    }

    private static Player CreatePlayer(ushort sessionId, NitroxId? subRoot)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), subRoot == null ? Optional.Empty : Optional.Of(subRoot), Perms.PLAYER,
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
