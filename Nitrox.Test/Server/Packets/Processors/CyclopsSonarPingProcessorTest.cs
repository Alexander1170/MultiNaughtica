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
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The sonar ping of the navigator console on the server (feature group 3, station 1): a press goes to the player who simulates the Cyclops, with the presser's session from the connection; only the simulator can
///     report the result, a fired ping reaches everybody else and a failure only the player who asked.
/// </summary>
[TestClass]
public class CyclopsSonarPingProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsSonarPingProcessor processor = null!;
    private Player pilot = null!;
    private Player navigator = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity seamoth = null!;
    private long clock;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        pilot = CreatePlayer(1);
        navigator = CreatePlayer(2);
        cyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE);
        clock = 100_000;
        processor = new CyclopsSonarPingProcessor(ownership, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsSonarPingProcessor>.Instance) { Clock = () => clock };
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsSonarPing packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    [TestMethod]
    public async Task APressIsPassedToTheSimulatorWithThePressersSessionFromTheConnection()
    {
        // the packet says that somebody else pressed: the server does not believe it
        await Send(navigator, new CyclopsSonarPing(CyclopsSonarPingKind.Press, cyclops.Id, pilot.SessionId, CyclopsSonarFailure.None));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
        CyclopsSonarPing forwarded = (CyclopsSonarPing)packetSender.Sent[0].Packet;
        forwarded.Kind.Should().Be(CyclopsSonarPingKind.Forward);
        forwarded.Presser.Should().Be(navigator.SessionId);
        forwarded.CyclopsId.Should().Be(cyclops.Id);
    }

    [TestMethod]
    public async Task APressThatComesTooSoonAfterTheLastOneIsTurnedDownToThePresserAndNeverReachesTheSimulator()
    {
        await Send(navigator, CyclopsSonarPing.Press(cyclops.Id));
        packetSender.Sent.Clear();

        clock += CyclopsSonarPingProcessor.MIN_INTERVAL_MILLISECONDS - 1;
        await Send(navigator, CyclopsSonarPing.Press(cyclops.Id));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(navigator.SessionId);
        ((CyclopsSonarPing)packetSender.Sent[0].Packet).Failure.Should().Be(CyclopsSonarFailure.TooSoon);
        packetSender.Sent.Clear();

        clock += 1;
        await Send(navigator, CyclopsSonarPing.Press(cyclops.Id));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "the interval is over: the press is passed on");
        ((CyclopsSonarPing)packetSender.Sent[0].Packet).Kind.Should().Be(CyclopsSonarPingKind.Forward);
    }

    [TestMethod]
    public async Task AFloodOfPressesMakesTheSimulatorPingOnceAndOtherCyclopsesAreNotHeldBack()
    {
        VehicleEntity other = AddVehicle("Cyclops");
        ownership.TryToAcquire(other.Id, pilot, SimulationLockType.EXCLUSIVE);

        for (int i = 0; i < 50; i++)
        {
            clock += 20;
            await Send(navigator, CyclopsSonarPing.Press(cyclops.Id));
        }
        await Send(navigator, CyclopsSonarPing.Press(other.Id));

        List<(Packet Packet, SessionId SessionId)> toTheSimulator = packetSender.Sent.Where(sent => sent.SessionId == pilot.SessionId).ToList();
        toTheSimulator.Should().HaveCount(2, "one for each Cyclops");
        toTheSimulator.Select(sent => ((CyclopsSonarPing)sent.Packet).CyclopsId).Should().BeEquivalentTo([cyclops.Id, other.Id]);
        packetSender.Sent.Count(sent => sent.SessionId == navigator.SessionId).Should().Be(49, "the other presses were turned down");
    }

    [TestMethod]
    public async Task APressWithNobodySimulatingTheCyclopsIsAnsweredToThePresserAlone()
    {
        ownership.RevokeIfOwner(cyclops.Id, pilot);

        await Send(navigator, CyclopsSonarPing.Press(cyclops.Id));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(navigator.SessionId);
        CyclopsSonarPing answer = (CyclopsSonarPing)packetSender.Sent[0].Packet;
        answer.Kind.Should().Be(CyclopsSonarPingKind.Result);
        answer.Failure.Should().Be(CyclopsSonarFailure.NoSimulator);
    }

    [TestMethod]
    public async Task APressOnSomethingThatIsNoCyclopsOrWithTheUpgradesOffIsRefused()
    {
        await Send(navigator, CyclopsSonarPing.Press(seamoth.Id));
        serverOptions.VehicleUpgrades = false;
        await Send(navigator, CyclopsSonarPing.Press(cyclops.Id));

        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == navigator.SessionId);
        packetSender.Sent.Select(sent => ((CyclopsSonarPing)sent.Packet).Failure).Should().OnlyContain(failure => failure == CyclopsSonarFailure.NotAvailable);
    }

    [TestMethod]
    public async Task AFiredPingIsToldToEverybodyElseAndOnlyTheSimulatorCanSayIt()
    {
        await Send(pilot, CyclopsSonarPing.Result(cyclops.Id, navigator.SessionId, CyclopsSonarFailure.None));

        packetSender.SentToOthers.Should().ContainSingle();
        packetSender.SentToOthers[0].Excluded.Should().Be(pilot.SessionId, "the simulator played it already");
        ((CyclopsSonarPing)packetSender.SentToOthers[0].Packet).Failure.Should().Be(CyclopsSonarFailure.None);

        packetSender.SentToOthers.Clear();
        await Send(navigator, CyclopsSonarPing.Result(cyclops.Id, navigator.SessionId, CyclopsSonarFailure.None));

        packetSender.SentToOthers.Should().BeEmpty("a player who does not simulate the Cyclops cannot fire its ping");
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AResultForSomethingThatIsNoCyclopsWithTheUpgradesOffOrWithAFailureThatDoesNotExistIsDropped()
    {
        // the player who simulates the Seamoth is not the Cyclops' simulator, and a Seamoth is no Cyclops: nobody gets a wave
        ownership.TryToAcquire(seamoth.Id, navigator, SimulationLockType.EXCLUSIVE);
        await Send(navigator, CyclopsSonarPing.Result(seamoth.Id, pilot.SessionId, CyclopsSonarFailure.None));
        await Send(pilot, new CyclopsSonarPing(CyclopsSonarPingKind.Result, cyclops.Id, navigator.SessionId, (CyclopsSonarFailure)99));
        serverOptions.VehicleUpgrades = false;
        await Send(pilot, CyclopsSonarPing.Result(cyclops.Id, navigator.SessionId, CyclopsSonarFailure.None));

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ANullCyclopsIdIsDroppedWithoutAnError()
    {
        await Send(navigator, new CyclopsSonarPing(CyclopsSonarPingKind.Press, null!, default, CyclopsSonarFailure.None));
        await Send(pilot, new CyclopsSonarPing(CyclopsSonarPingKind.Result, null!, default, CyclopsSonarFailure.None));

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AFailureOfTheSimulatorGoesToThePlayerWhoAskedAlone()
    {
        await Send(pilot, CyclopsSonarPing.Result(cyclops.Id, navigator.SessionId, CyclopsSonarFailure.NoSonarModule));

        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(navigator.SessionId);
        ((CyclopsSonarPing)packetSender.Sent[0].Packet).Failure.Should().Be(CyclopsSonarFailure.NoSonarModule);
    }

    [TestMethod]
    public async Task AFailureThatTheSimulatorAddressesToItselfOrAnythingTheServerAloneMaySendIsDropped()
    {
        await Send(pilot, CyclopsSonarPing.Result(cyclops.Id, pilot.SessionId, CyclopsSonarFailure.NoPower));
        await Send(navigator, CyclopsSonarPing.Forward(cyclops.Id, pilot.SessionId));
        await Send(navigator, new CyclopsSonarPing(CyclopsSonarPingKind.Unknown, cyclops.Id, default, CyclopsSonarFailure.None));

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public void ThePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsSonarPing packet = CyclopsSonarPing.Result(new NitroxId(), (SessionId)(ushort)4, CyclopsSonarFailure.NoPower);

        CyclopsSonarPing received = (CyclopsSonarPing)Packet.Deserialize(packet.Serialize());

        received.Should().BeEquivalentTo(packet);
    }

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
