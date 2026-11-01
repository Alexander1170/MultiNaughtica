using System.Collections.Generic;
using System.Linq;
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
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The engineer console on the server (feature group 3, station 3): one engineer per Cyclops, the first request wins, a player sits at one console at a time, only a player aboard can sit, and the seat ends for everybody
///     when the engineer leaves the console or the Cyclops, dies, disconnects or the Cyclops is destroyed. A joiner is told who sits where.
/// </summary>
[TestClass]
public class CyclopsEngineerSeatProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsEngineerArbiter arbiter = null!;
    private CyclopsEngineerSeatProcessor processor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity secondCyclops = null!;
    private VehicleEntity seamoth = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        arbiter = new CyclopsEngineerArbiter(packetSender);
        processor = new CyclopsEngineerSeatProcessor(arbiter, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsEngineerSeatProcessor>.Instance);
        cyclops = AddVehicle("Cyclops");
        secondCyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        alice = CreatePlayer(1, cyclops.Id);
        bob = CreatePlayer(2, cyclops.Id);
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsEngineerSeat packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    private CyclopsEngineerSeat LastToAll() => (CyclopsEngineerSeat)packetSender.SentToAll[^1];

    // ---- taking the seat

    [TestMethod]
    public async Task TheFirstPlayerToAskSitsAndEverybodyIsToldWithTheNameFromTheServer()
    {
        // the packet claims to be Bob with another name: the server uses the connection
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Take, cyclops.Id, bob.SessionId, "Mallory", CyclopsEngineerSeatReason.None));

        packetSender.SentToAll.Should().ContainSingle();
        CyclopsEngineerSeat told = LastToAll();
        told.Message.Should().Be(CyclopsEngineerSeatMessage.Seated);
        told.SessionId.Should().Be(alice.SessionId);
        told.PlayerName.Should().Be(alice.Name);
        told.CyclopsId.Should().Be(cyclops.Id);
        arbiter.IsEngineer(alice.SessionId, cyclops.Id).Should().BeTrue();
        arbiter.IsEngineer(bob.SessionId, cyclops.Id).Should().BeFalse();
    }

    [TestMethod]
    public async Task ASecondPlayerIsDeniedAndToldWhoSits()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        packetSender.SentToAll.Clear();

        await Send(bob, CyclopsEngineerSeat.Take(cyclops.Id));

        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(bob.SessionId);
        CyclopsEngineerSeat denied = (CyclopsEngineerSeat)packetSender.Sent[0].Packet;
        denied.Message.Should().Be(CyclopsEngineerSeatMessage.Denied);
        denied.Reason.Should().Be(CyclopsEngineerSeatReason.Occupied);
        denied.SessionId.Should().Be(alice.SessionId, "the occupant");
        denied.PlayerName.Should().Be(alice.Name);
    }

    [TestMethod]
    public async Task ThePlayerWhoSitsAsksAgainAndIsToldAgainAloneWithoutLosingTheSeat()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        packetSender.Sent.Clear();

        for (int i = 0; i < 5; i++)
        {
            await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        }

        packetSender.SentToAll.Should().ContainSingle("only the first grant is broadcast: a client that asks in a loop cannot make the server send packets to everybody");
        packetSender.Sent.Should().HaveCount(5).And.OnlyContain(sent => sent.SessionId == alice.SessionId);
        ((CyclopsEngineerSeat)packetSender.Sent[0].Packet).Message.Should().Be(CyclopsEngineerSeatMessage.Seated);
        arbiter.IsEngineer(alice.SessionId, cyclops.Id).Should().BeTrue();
        arbiter.Count.Should().Be(1);
    }

    [TestMethod]
    public async Task APlayerSitsAtOneConsoleAtATime()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        alice.SubRootId = Optional.Of(secondCyclops.Id);
        packetSender.Sent.Clear();

        await Send(alice, CyclopsEngineerSeat.Take(secondCyclops.Id));

        ((CyclopsEngineerSeat)packetSender.Sent.Should().ContainSingle().Subject.Packet).Reason.Should().Be(CyclopsEngineerSeatReason.SeatedElsewhere);
        arbiter.IsEngineer(alice.SessionId, secondCyclops.Id).Should().BeFalse();
    }

    [TestMethod]
    public async Task OnlyAPlayerAboardTheCyclopsCanSit()
    {
        Player swimmer = CreatePlayer(3, null);
        Player elsewhere = CreatePlayer(4, secondCyclops.Id);

        await Send(swimmer, CyclopsEngineerSeat.Take(cyclops.Id));
        await Send(elsewhere, CyclopsEngineerSeat.Take(cyclops.Id));

        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(2);
        packetSender.Sent.Select(sent => ((CyclopsEngineerSeat)sent.Packet).Reason).Should().OnlyContain(reason => reason == CyclopsEngineerSeatReason.NotAboard);
        arbiter.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task OnlyALiveCyclopsWithTheUpgradesOnHasAConsole()
    {
        Player onSeamoth = CreatePlayer(3, seamoth.Id);
        await Send(onSeamoth, CyclopsEngineerSeat.Take(seamoth.Id));
        cyclops.Metadata = new CyclopsMetadata(false, false, false, false, 0, 100, true);
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        cyclops.Metadata = null;
        serverOptions.VehicleUpgrades = false;
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));

        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(3);
        packetSender.Sent.Select(sent => ((CyclopsEngineerSeat)sent.Packet).Reason).Should().OnlyContain(reason => reason == CyclopsEngineerSeatReason.Unavailable);
    }

    [TestMethod]
    public async Task APlayerWhoseSessionIsGoneIsIgnored()
    {
        // the session cleaner has run already: the player is offline (a player that is offline has the server's session, which is no player) when their request arrives. The guard for a player who goes
        // offline in the middle of a request (after the aboard check, before the seat is written) has no seam to be tested through; it is the one of the rider bar's processor.
        Player gone = CreatePlayer(5, cyclops.Id);
        gone.IsOnline = false;

        await Send(gone, CyclopsEngineerSeat.Take(cyclops.Id));

        arbiter.Count.Should().Be(0);
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().BeEmpty();
    }

    // ---- the hooks of the other processors, through the processors

    [TestMethod]
    public async Task TheDeathOfTheEngineerEndsTheSeatThroughTheDeathProcessor()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        packetSender.SentToAll.Clear();
        PlayerDeathEventProcessor death = new(Options.Create(serverOptions), new CyclopsGunnerArbiter(packetSender), arbiter, new CyclopsDroneArbiter(packetSender));

        await death.Process(new AuthProcessorContext(alice, packetSender), new PlayerDeathEvent(alice.SessionId, NitroxVector3.Zero));

        arbiter.Count.Should().Be(0);
        CyclopsEngineerSeat told = LastToAll();
        told.Message.Should().Be(CyclopsEngineerSeatMessage.Released);
        told.Reason.Should().Be(CyclopsEngineerSeatReason.Died);
        told.SessionId.Should().Be(alice.SessionId);
    }

    // ---- leaving

    [TestMethod]
    public async Task OnlyTheEngineerCanLeaveAndEverybodyIsToldWhy()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        packetSender.SentToAll.Clear();

        await Send(bob, CyclopsEngineerSeat.Leave(cyclops.Id));
        packetSender.SentToAll.Should().BeEmpty("somebody else's leave changes nothing");
        arbiter.IsEngineer(alice.SessionId, cyclops.Id).Should().BeTrue();

        await Send(alice, CyclopsEngineerSeat.Leave(cyclops.Id));

        CyclopsEngineerSeat told = LastToAll();
        told.Message.Should().Be(CyclopsEngineerSeatMessage.Released);
        told.Reason.Should().Be(CyclopsEngineerSeatReason.Left);
        told.SessionId.Should().Be(alice.SessionId);
        arbiter.Count.Should().Be(0);
        await Send(bob, CyclopsEngineerSeat.Take(cyclops.Id));
        arbiter.IsEngineer(bob.SessionId, cyclops.Id).Should().BeTrue("the console is free again");
    }

    [TestMethod]
    public async Task AClientCannotSendTheServersOwnMessagesOrNothingAtAll()
    {
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Seated, cyclops.Id, bob.SessionId, "Bob", CyclopsEngineerSeatReason.None));
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Released, cyclops.Id, bob.SessionId, "Bob", CyclopsEngineerSeatReason.Died));
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Denied, cyclops.Id, bob.SessionId, "Bob", CyclopsEngineerSeatReason.Occupied));
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Unknown, cyclops.Id, default, string.Empty, CyclopsEngineerSeatReason.None));
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Take, null!, default, string.Empty, CyclopsEngineerSeatReason.None));
        await Send(alice, new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Leave, null!, default, string.Empty, CyclopsEngineerSeatReason.None));

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
        arbiter.Count.Should().Be(0);
    }

    // ---- every other way a seat ends

    [TestMethod]
    public async Task TheSeatEndsWhenTheEngineerDisconnectsAndTheOthersAreTold()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        packetSender.SentToAll.Clear();

        await arbiter.OnEventAsync(new ISessionCleaner.Args(new SessionManager.Session(alice.SessionId, new IPEndPoint(IPAddress.Loopback, 1)), 1));

        arbiter.Count.Should().Be(0);
        packetSender.SentToOthers.Should().ContainSingle().Which.Excluded.Should().Be(alice.SessionId, "the departed player is not told");
        CyclopsEngineerSeat told = (CyclopsEngineerSeat)packetSender.SentToOthers[0].Packet;
        told.Message.Should().Be(CyclopsEngineerSeatMessage.Released);
        told.Reason.Should().Be(CyclopsEngineerSeatReason.Disconnected);
        told.PlayerName.Should().Be(alice.Name);
    }

    [TestMethod]
    public async Task TheSeatEndsWhenTheEngineerDiesLeavesTheCyclopsOrTheCyclopsIsDestroyed()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        packetSender.SentToAll.Clear();

        await arbiter.BroadcastEndedAsync(arbiter.ReleaseSession(alice.SessionId), CyclopsEngineerSeatReason.Died);
        LastToAll().Reason.Should().Be(CyclopsEngineerSeatReason.Died);
        arbiter.Count.Should().Be(0);
        arbiter.ReleaseSession(alice.SessionId).Should().BeEmpty("nothing to release twice");

        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        arbiter.PlayerMoved(alice.SessionId, Optional.Of(cyclops.Id)).Should().BeEmpty("still in the Cyclops");
        await arbiter.BroadcastEndedAsync(arbiter.PlayerMoved(alice.SessionId, Optional.Empty), CyclopsEngineerSeatReason.LeftCyclops);
        LastToAll().Reason.Should().Be(CyclopsEngineerSeatReason.LeftCyclops);
        arbiter.Count.Should().Be(0);

        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        arbiter.PlayerMoved(alice.SessionId, Optional.Of(secondCyclops.Id)).Should().ContainSingle("another Cyclops is not this one");

        await Send(bob, CyclopsEngineerSeat.Take(cyclops.Id));
        await arbiter.BroadcastEndedAsync(arbiter.CyclopsGone(cyclops.Id), CyclopsEngineerSeatReason.CyclopsDestroyed);
        LastToAll().Reason.Should().Be(CyclopsEngineerSeatReason.CyclopsDestroyed);
        LastToAll().SessionId.Should().Be(bob.SessionId);
        arbiter.Count.Should().Be(0);
        arbiter.CyclopsGone(cyclops.Id).Should().BeEmpty();
    }

    [TestMethod]
    public async Task APlayerWhoJoinsIsToldWhoSitsAtEveryConsoleThatIsTaken()
    {
        await Send(alice, CyclopsEngineerSeat.Take(cyclops.Id));
        await Send(CreatePlayer(7, secondCyclops.Id), CyclopsEngineerSeat.Take(secondCyclops.Id));
        packetSender.Sent.Clear();
        SessionId joiner = (ushort)9;

        await arbiter.SendSnapshotAsync(joiner);

        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == joiner);
        List<CyclopsEngineerSeat> told = packetSender.Sent.Select(sent => (CyclopsEngineerSeat)sent.Packet).ToList();
        told.Should().OnlyContain(seat => seat.Message == CyclopsEngineerSeatMessage.Seated);
        told.Select(seat => seat.CyclopsId).Should().BeEquivalentTo([cyclops.Id, secondCyclops.Id]);
        told.Single(seat => seat.CyclopsId.Equals(cyclops.Id)).PlayerName.Should().Be(alice.Name);
    }

    [TestMethod]
    public void TheArbiterRefusesASessionThatIsNoPlayer()
    {
        arbiter.TrySit(cyclops.Id, default, "Nobody", out _).Should().Be(CyclopsEngineerArbiter.SitResult.NotAPlayer);
        arbiter.Count.Should().Be(0);
    }

    [TestMethod]
    public void TheSeatPacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsEngineerSeat packet = new(CyclopsEngineerSeatMessage.Denied, new NitroxId(), (SessionId)(ushort)4, "Alice", CyclopsEngineerSeatReason.Occupied);

        CyclopsEngineerSeat received = (CyclopsEngineerSeat)Packet.Deserialize(packet.Serialize());

        received.Should().BeEquivalentTo(packet);
        new CyclopsEngineerSeat(CyclopsEngineerSeatMessage.Take, new NitroxId(), default, null!, CyclopsEngineerSeatReason.None).PlayerName.Should().BeEmpty("a name is never null");
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
