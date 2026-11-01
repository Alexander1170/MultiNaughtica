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
///     The Seamoth Rider Bar on the server (feature group 2): one rider per Seamoth, the first request wins, a player rides one Seamoth at a time, only a swimming player can hold on, and the ride
///     ends for everybody when the Seamoth is destroyed or docks and when the rider's player leaves. The look of the rider is passed on only while the sender really rides.
/// </summary>
[TestClass]
public class SeamothRiderChangedProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private SeamothRiderArbiter arbiter = null!;
    private SeamothRiderChangedProcessor processor = null!;
    private SeamothRiderLookProcessor lookProcessor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private VehicleEntity seamoth = null!;
    private VehicleEntity secondSeamoth = null!;
    private VehicleEntity prawn = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        arbiter = new SeamothRiderArbiter(packetSender);
        processor = new SeamothRiderChangedProcessor(arbiter, entityRegistry, Options.Create(serverOptions), NullLogger<SeamothRiderChangedProcessor>.Instance);
        lookProcessor = new SeamothRiderLookProcessor(arbiter);
        alice = CreatePlayer(1);
        bob = CreatePlayer(2);
        seamoth = AddVehicle("Seamoth");
        secondSeamoth = AddVehicle("Seamoth");
        prawn = AddVehicle("Exosuit");
        InstallBar(seamoth);
        InstallBar(secondSeamoth);
    }

    /// <summary>The Rider Bar in a slot of the Seamoth, as the server holds it after an install: a child entity of the vehicle named by the TechType number.</summary>
    private static void InstallBar(VehicleEntity vehicle, string slot = "SeamothModule1")
    {
        vehicle.ChildEntities.Add(new InstalledModuleEntity(slot, "class", new NitroxId(), new NitroxTechType(CoopItemTechNames.SEAMOTH_RIDER_BAR), null, vehicle.Id, []));
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Hold(Player player, VehicleEntity vehicle) => processor.Process(new AuthProcessorContext(player, packetSender), new SeamothRiderChanged(RiderBarMessage.Hold, vehicle.Id, player.SessionId));

    private Task LetGo(Player player, VehicleEntity vehicle) => processor.Process(new AuthProcessorContext(player, packetSender), new SeamothRiderChanged(RiderBarMessage.LetGo, vehicle.Id, player.SessionId));

    private static SeamothRiderChanged Only(List<Packet> packets) => (SeamothRiderChanged)packets.Single();

    // ---- holding on

    [TestMethod]
    public async Task TheFirstRequestForAFreeSeamothWinsAndEverybodyIsTold()
    {
        await Hold(alice, seamoth);

        SeamothRiderChanged told = Only(packetSender.SentToAll);
        told.Message.Should().Be(RiderBarMessage.Holding);
        told.VehicleId.Should().Be(seamoth.Id);
        told.SessionId.Should().Be(alice.SessionId, "the rider's own client climbs on only when it hears this");
        arbiter.Rides(alice.SessionId, seamoth.Id).Should().BeTrue();
    }

    [TestMethod]
    public async Task TheSecondRequestForTheSameSeamothIsDeniedToTheSecondPlayerOnly()
    {
        await Hold(alice, seamoth);
        packetSender.SentToAll.Clear();

        await Hold(bob, seamoth);

        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].SessionId.Should().Be(bob.SessionId);
        ((SeamothRiderChanged)packetSender.Sent[0].Packet).Message.Should().Be(RiderBarMessage.Denied);
        arbiter.Rides(bob.SessionId, seamoth.Id).Should().BeFalse();
        arbiter.Rides(alice.SessionId, seamoth.Id).Should().BeTrue("the first rider stays");
    }

    [TestMethod]
    public async Task TheSamePlayerAskingTwiceIsToldTwiceAndStillRidesOnce()
    {
        await Hold(alice, seamoth);
        await Hold(alice, seamoth);

        arbiter.Count.Should().Be(1);
        packetSender.SentToAll.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task APlayerRidesOneSeamothAtATime()
    {
        await Hold(alice, seamoth);
        packetSender.SentToAll.Clear();

        await Hold(alice, secondSeamoth);

        arbiter.Rides(alice.SessionId, secondSeamoth.Id).Should().BeFalse();
        ((SeamothRiderChanged)packetSender.Sent.Single().Packet).Message.Should().Be(RiderBarMessage.Denied);
    }

    [TestMethod]
    public async Task TwoPlayersCanRideTwoSeamoths()
    {
        await Hold(alice, seamoth);
        await Hold(bob, secondSeamoth);

        arbiter.Count.Should().Be(2);
    }

    [TestMethod]
    public async Task OnlyASeamothHasABar()
    {
        await Hold(alice, prawn);

        arbiter.Count.Should().Be(0);
        ((SeamothRiderChanged)packetSender.Sent.Single().Packet).Message.Should().Be(RiderBarMessage.Denied);
    }

    [TestMethod]
    public async Task AnUnknownVehicleIsDenied()
    {
        await processor.Process(new AuthProcessorContext(alice, packetSender), new SeamothRiderChanged(RiderBarMessage.Hold, new NitroxId(), alice.SessionId));

        arbiter.Count.Should().Be(0);
        ((SeamothRiderChanged)packetSender.Sent.Single().Packet).Message.Should().Be(RiderBarMessage.Denied);
    }

    [TestMethod]
    public async Task APilotAndAPlayerInsideASubmarineCannotHoldOn()
    {
        alice.PlayerContext = new PlayerContext("Player1", (ushort)1, new NitroxId(), false, new PlayerSettings(new NitroxColor(1f, 0f, 0f)), false, SubnauticaGameMode.SURVIVAL, secondSeamoth.Id, IntroCinematicMode.NONE, null!);
        bob.SubRootId = Optional.Of(new NitroxId());

        await Hold(alice, seamoth);
        await Hold(bob, seamoth);

        arbiter.Count.Should().Be(0);
        packetSender.Sent.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task ASeamothWithoutTheBarModuleCannotBeHeld()
    {
        VehicleEntity plain = AddVehicle("Seamoth");

        await Hold(alice, plain);

        arbiter.Count.Should().Be(0);
        ((SeamothRiderChanged)packetSender.Sent.Single().Packet).Message.Should().Be(RiderBarMessage.Denied);
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ASeamothWithOnlyOtherModulesCannotBeHeld()
    {
        VehicleEntity other = AddVehicle("Seamoth");
        other.ChildEntities.Add(new InstalledModuleEntity("SeamothModule1", "class", new NitroxId(), new NitroxTechType("20011"), null, other.Id, []));
        other.ChildEntities.Add(new InstalledModuleEntity("SeamothModule2", "class", new NitroxId(), new NitroxTechType("SeamothSonarModule"), null, other.Id, []));

        await Hold(alice, other);

        arbiter.Count.Should().Be(0);
        ((SeamothRiderChanged)packetSender.Sent.Single().Packet).Message.Should().Be(RiderBarMessage.Denied);
    }

    [TestMethod]
    public async Task TheBarModuleInAnySlotCounts()
    {
        VehicleEntity third = AddVehicle("Seamoth");
        InstallBar(third, "SeamothModule4");

        await Hold(alice, third);

        arbiter.Rides(alice.SessionId, third.Id).Should().BeTrue();
    }

    [TestMethod]
    public void TheNameTheServerComparesIsTheNumberOfTheClientsItem()
    {
        CoopItemTechNames.SEAMOTH_RIDER_BAR.Should().Be(NitroxClient.GameLogic.VehicleUpgrades.CoopItems.SEAMOTH_RIDER_BAR.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task APlayerWhoIsStillNearTheirLifepodSwimsAndCanHoldOn()
    {
        // The game keeps the lifepod as the sub root until the player is 15 m from it, but a lifepod is not a submarine
        NitroxId lifepodId = new();
        entityRegistry.AddOrUpdate(new EscapePodEntity(NitroxVector3.Zero, lifepodId, null));
        bob.SubRootId = Optional.Of(lifepodId);

        await Hold(bob, seamoth);

        arbiter.Rides(bob.SessionId, seamoth.Id).Should().BeTrue();
        Only(packetSender.SentToAll).Message.Should().Be(RiderBarMessage.Holding);
    }

    [TestMethod]
    public async Task APlayerInsideACyclopsThatTheServerKnowsCannotHoldOn()
    {
        VehicleEntity cyclops = AddVehicle("Cyclops");
        bob.SubRootId = Optional.Of(cyclops.Id);

        await Hold(bob, seamoth);

        arbiter.Count.Should().Be(0);
        Only(packetSender.Sent.Select(sent => sent.Packet).ToList()).Message.Should().Be(RiderBarMessage.Denied);
    }

    [TestMethod]
    public async Task APlayerWhoHasLeftIsNotLeftOnTheBar()
    {
        bob.IsOnline = false;

        await Hold(bob, seamoth);

        arbiter.Count.Should().Be(0, "no ride is kept for a session that is gone");
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ALookThatIsNotARotationIsNotPassedOn()
    {
        await Hold(alice, seamoth);
        packetSender.SentToOthers.Clear();

        foreach (NitroxQuaternion garbage in new[] { new NitroxQuaternion(float.NaN, 0f, 0f, 1f), new NitroxQuaternion(0f, 0f, 0f, 0f), new NitroxQuaternion(float.PositiveInfinity, 0f, 0f, 1f), new NitroxQuaternion(5f, 5f, 5f, 5f) })
        {
            await lookProcessor.Process(new AuthProcessorContext(alice, packetSender), new SeamothRiderLook(alice.SessionId, seamoth.Id, garbage, 1.0));
        }
        packetSender.SentToOthers.Should().BeEmpty();

        await lookProcessor.Process(new AuthProcessorContext(alice, packetSender), new SeamothRiderLook(alice.SessionId, seamoth.Id, new NitroxQuaternion(0f, 0.7071f, 0f, 0.7071f), 2.0));
        packetSender.SentToOthers.Should().ContainSingle();
    }

    [TestMethod]
    public async Task WithTheVehicleUpgradesSwitchedOffNobodyHoldsOn()
    {
        serverOptions.VehicleUpgrades = false;

        await Hold(alice, seamoth);

        arbiter.Count.Should().Be(0);
    }

    // ---- letting go

    [TestMethod]
    public async Task TheRiderLettingGoIsToldToEverybody()
    {
        await Hold(alice, seamoth);
        packetSender.SentToAll.Clear();

        await LetGo(alice, seamoth);

        SeamothRiderChanged told = Only(packetSender.SentToAll);
        told.Message.Should().Be(RiderBarMessage.Released);
        told.SessionId.Should().Be(alice.SessionId);
        arbiter.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task OnlyTheRiderCanLetGo()
    {
        await Hold(alice, seamoth);
        packetSender.SentToAll.Clear();

        await LetGo(bob, seamoth);

        packetSender.SentToAll.Should().BeEmpty();
        arbiter.Rides(alice.SessionId, seamoth.Id).Should().BeTrue();
    }

    [TestMethod]
    public async Task ABarThatWasLetGoCanBeHeldByAnother()
    {
        await Hold(alice, seamoth);
        await LetGo(alice, seamoth);
        packetSender.SentToAll.Clear();

        await Hold(bob, seamoth);

        Only(packetSender.SentToAll).SessionId.Should().Be(bob.SessionId);
    }

    [TestMethod]
    [DataRow(RiderBarMessage.Holding)]
    [DataRow(RiderBarMessage.Released)]
    [DataRow(RiderBarMessage.Denied)]
    [DataRow(RiderBarMessage.Unknown)]
    public async Task AClientCannotSendTheServersOwnMessages(RiderBarMessage message)
    {
        await processor.Process(new AuthProcessorContext(alice, packetSender), new SeamothRiderChanged(message, seamoth.Id, bob.SessionId));

        packetSender.SentToAll.Should().BeEmpty("a forged Holding would put a body on somebody else's bar on every client");
        packetSender.Sent.Should().BeEmpty();
        arbiter.Count.Should().Be(0);
    }

    // ---- the end of a ride

    [TestMethod]
    public async Task WhenTheRidersPlayerLeavesTheRideIsEndedForTheOthers()
    {
        await Hold(alice, seamoth);
        packetSender.SentToAll.Clear();

        await arbiter.OnEventAsync(new ISessionCleaner.Args(new SessionManager.Session(alice.SessionId, new IPEndPoint(IPAddress.Loopback, 1)), 1));

        arbiter.Count.Should().Be(0);
        packetSender.SentToOthers.Should().ContainSingle();
        packetSender.SentToOthers[0].Excluded.Should().Be(alice.SessionId);
        ((SeamothRiderChanged)packetSender.SentToOthers[0].Packet).Message.Should().Be(RiderBarMessage.Released);
    }

    [TestMethod]
    public async Task WhenTheSeamothIsDestroyedOrDocksTheRideIsEnded()
    {
        await Hold(alice, seamoth);
        await Hold(bob, secondSeamoth);
        packetSender.SentToAll.Clear();

        await arbiter.BroadcastEndedAsync(arbiter.VehicleGone(seamoth.Id));

        arbiter.Rides(alice.SessionId, seamoth.Id).Should().BeFalse();
        arbiter.Rides(bob.SessionId, secondSeamoth.Id).Should().BeTrue("the other ride goes on");
        Only(packetSender.SentToAll).SessionId.Should().Be(alice.SessionId);
        arbiter.VehicleGone(prawn.Id).Should().BeEmpty();
    }

    [TestMethod]
    public async Task AJoinerIsToldWhoHoldsOnToWhat()
    {
        await Hold(alice, seamoth);
        packetSender.Sent.Clear();

        await arbiter.SendSnapshotAsync(bob.SessionId);

        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].SessionId.Should().Be(bob.SessionId);
        SeamothRiderChanged told = (SeamothRiderChanged)packetSender.Sent[0].Packet;
        told.Message.Should().Be(RiderBarMessage.Holding);
        told.SessionId.Should().Be(alice.SessionId);
        told.VehicleId.Should().Be(seamoth.Id);
    }

    [TestMethod]
    public void TheRiderOfASeamothCanBeAskedFor()
    {
        arbiter.TryHold(seamoth.Id, alice.SessionId).Should().BeTrue();

        arbiter.TryGetRider(seamoth.Id, out SessionId rider).Should().BeTrue();
        rider.Should().Be(alice.SessionId);
        arbiter.TryGetRider(secondSeamoth.Id, out _).Should().BeFalse();
    }

    // ---- the look

    [TestMethod]
    public async Task TheLookOfTheRiderIsPassedOnToTheOthersWithTheSendersOwnSession()
    {
        await Hold(alice, seamoth);

        await lookProcessor.Process(new AuthProcessorContext(alice, packetSender), new SeamothRiderLook(bob.SessionId, seamoth.Id, NitroxQuaternion.Identity, 12.5));

        packetSender.SentToOthers.Should().ContainSingle();
        SeamothRiderLook relayed = (SeamothRiderLook)packetSender.SentToOthers[0].Packet;
        relayed.SessionId.Should().Be(alice.SessionId, "whatever the packet said");
        relayed.VehicleId.Should().Be(seamoth.Id);
        relayed.Time.Should().Be(12.5);
    }

    [TestMethod]
    public async Task TheLookOfAPlayerWhoDoesNotRideThatSeamothIsDropped()
    {
        await Hold(alice, seamoth);

        await lookProcessor.Process(new AuthProcessorContext(bob, packetSender), new SeamothRiderLook(bob.SessionId, seamoth.Id, NitroxQuaternion.Identity, 1.0));
        await lookProcessor.Process(new AuthProcessorContext(alice, packetSender), new SeamothRiderLook(alice.SessionId, secondSeamoth.Id, NitroxQuaternion.Identity, 1.0));

        packetSender.SentToOthers.Should().BeEmpty();
    }

    // ---- the packets

    [TestMethod]
    public void BothPacketsSurviveTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId vehicle = new();

        SeamothRiderChanged changed = (SeamothRiderChanged)Packet.Deserialize(new SeamothRiderChanged(RiderBarMessage.Holding, vehicle, (ushort)5).Serialize());
        SeamothRiderLook look = (SeamothRiderLook)Packet.Deserialize(new SeamothRiderLook((ushort)5, vehicle, new NitroxQuaternion(0f, 0.7071f, 0f, 0.7071f), 33.25).Serialize());

        changed.Message.Should().Be(RiderBarMessage.Holding);
        changed.VehicleId.Should().Be(vehicle);
        changed.SessionId.Should().Be((SessionId)(ushort)5);
        look.VehicleId.Should().Be(vehicle);
        look.Look.Y.Should().BeApproximately(0.7071f, 0.0001f);
        look.Time.Should().Be(33.25);
    }

    [TestMethod]
    public void TheLookIsAnUnreliableSequencedPacketOnTheMovementChannel()
    {
        SeamothRiderLook look = new((ushort)5, new NitroxId(), NitroxQuaternion.Identity, 1.0);

        look.DeliveryMethod.Should().Be(Nitrox.Model.Networking.NitroxDeliveryMethod.DeliveryMethod.UNRELIABLE_SEQUENCED);
        look.UdpChannel.Should().Be(Packet.UdpChannelId.MOVEMENTS);
        look.Serialize().Length.Should().BeLessThan(200, "a sequenced packet must fit one datagram");
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
