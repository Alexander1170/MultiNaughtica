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
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The tow hook button of a Cyclops (feature group 2): anybody aboard can press it, but the hooking is done by the player who simulates the Cyclops, so the server passes the press to that player, with the
///     presser's name taken from the connection, and passes a failure of the simulator on to the presser; a press nobody can carry out is answered at once.
/// </summary>
[TestClass]
public class CyclopsTowPressProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsTowPressProcessor processor = null!;
    private Player simulator = null!;
    private Player presser = null!;
    private Player bystander = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity seamoth = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        processor = new CyclopsTowPressProcessor(ownership, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsTowPressProcessor>.Instance);
        simulator = CreatePlayer(1);
        presser = CreatePlayer(2);
        bystander = CreatePlayer(3);
        cyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsTowPress packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    // ---- the press

    [TestMethod]
    public async Task APressGoesToThePlayerWhoSimulatesTheCyclopsWithThePressersOwnSession()
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);

        // The packet names somebody else as the presser: the server takes the sender's session instead
        await Send(presser, new CyclopsTowPress(CyclopsTowPressKind.Press, cyclops.Id, bystander.SessionId, CyclopsTowFailure.Unknown));

        (Packet packet, SessionId session) = packetSender.Sent.Should().ContainSingle().Subject;
        session.Should().Be(simulator.SessionId);
        CyclopsTowPress forward = packet.Should().BeOfType<CyclopsTowPress>().Subject;
        forward.Kind.Should().Be(CyclopsTowPressKind.Forward);
        forward.CyclopsId.Should().Be(cyclops.Id);
        forward.Presser.Should().Be(presser.SessionId);
    }

    [TestMethod]
    public async Task ThePressOfTheSimulatorItselfIsForwardedToItToo()
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.EXCLUSIVE);

        await Send(simulator, CyclopsTowPress.Press(cyclops.Id));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(simulator.SessionId, "a lock that moved while the press was on its way: its client handles the forward like any other");
    }

    [TestMethod]
    public async Task APressOfACyclopsNobodySimulatesIsAnsweredWithNoSimulator()
    {
        await Send(presser, CyclopsTowPress.Press(cyclops.Id));

        (Packet packet, SessionId session) = packetSender.Sent.Should().ContainSingle().Subject;
        session.Should().Be(presser.SessionId);
        CyclopsTowPress answer = (CyclopsTowPress)packet;
        answer.Kind.Should().Be(CyclopsTowPressKind.Failed);
        answer.Reason.Should().Be(CyclopsTowFailure.NoSimulator);
        answer.Presser.Should().Be(presser.SessionId);
    }

    [TestMethod]
    public async Task APressOfACyclopsWhoseSimulatorLeftIsAnsweredWithNoSimulator()
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);
        simulator.IsOnline = false;

        await Send(presser, CyclopsTowPress.Press(cyclops.Id));

        ((CyclopsTowPress)packetSender.Sent.Should().ContainSingle().Subject.Packet).Reason.Should().Be(CyclopsTowFailure.NoSimulator);
        packetSender.Sent[0].SessionId.Should().Be(presser.SessionId);
    }

    [TestMethod]
    public async Task WithTheVehicleUpgradesSwitchedOffTheButtonDoesNothing()
    {
        serverOptions.VehicleUpgrades = false;
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);

        await Send(presser, CyclopsTowPress.Press(cyclops.Id));

        CyclopsTowPress answer = (CyclopsTowPress)packetSender.Sent.Should().ContainSingle().Subject.Packet;
        answer.Reason.Should().Be(CyclopsTowFailure.NotAvailable);
        packetSender.Sent[0].SessionId.Should().Be(presser.SessionId);
    }

    [TestMethod]
    public async Task OnlyACyclopsHasATowHookButton()
    {
        ownership.TryToAcquire(seamoth.Id, simulator, SimulationLockType.TRANSIENT);

        await Send(presser, CyclopsTowPress.Press(seamoth.Id));

        ((CyclopsTowPress)packetSender.Sent.Should().ContainSingle().Subject.Packet).Reason.Should().Be(CyclopsTowFailure.NotAvailable);
        packetSender.Sent[0].SessionId.Should().Be(presser.SessionId, "nothing reaches the player who simulates the Seamoth");
    }

    [TestMethod]
    public async Task APressOfSomethingThatDoesNotExistIsAnsweredWithNotAvailable()
    {
        await Send(presser, CyclopsTowPress.Press(new NitroxId()));

        ((CyclopsTowPress)packetSender.Sent.Should().ContainSingle().Subject.Packet).Reason.Should().Be(CyclopsTowFailure.NotAvailable);
    }

    // ---- the failure

    [TestMethod]
    public async Task AFailureOfTheSimulatorGoesToThePlayerWhoPressed()
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);

        await Send(simulator, CyclopsTowPress.Failed(cyclops.Id, presser.SessionId, CyclopsTowFailure.NothingInReach));

        (Packet packet, SessionId session) = packetSender.Sent.Should().ContainSingle().Subject;
        session.Should().Be(presser.SessionId);
        CyclopsTowPress failure = (CyclopsTowPress)packet;
        failure.Kind.Should().Be(CyclopsTowPressKind.Failed);
        failure.Reason.Should().Be(CyclopsTowFailure.NothingInReach);
    }

    [TestMethod]
    public async Task OnlyTheSimulatorOfTheCyclopsCanReportAFailure()
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);

        await Send(bystander, CyclopsTowPress.Failed(cyclops.Id, presser.SessionId, CyclopsTowFailure.InUse));

        packetSender.Sent.Should().BeEmpty("a player who does not simulate the Cyclops cannot put words on somebody else's screen");
    }

    [TestMethod]
    public async Task AFailureWithoutAReasonIsDropped()
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);

        await Send(simulator, CyclopsTowPress.Failed(cyclops.Id, presser.SessionId, CyclopsTowFailure.Unknown));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(CyclopsTowPressKind.Forward)]
    [DataRow(CyclopsTowPressKind.Unknown)]
    public async Task AClientCannotSendTheServersOwnKinds(CyclopsTowPressKind kind)
    {
        ownership.TryToAcquire(cyclops.Id, simulator, SimulationLockType.TRANSIENT);

        await Send(presser, new CyclopsTowPress(kind, cyclops.Id, bystander.SessionId, CyclopsTowFailure.Unknown));

        packetSender.Sent.Should().BeEmpty("a forged forward would press the button for somebody else");
    }

    [TestMethod]
    public async Task APressWithoutACyclopsIsDropped()
    {
        await Send(presser, new CyclopsTowPress(CyclopsTowPressKind.Press, null!, default, CyclopsTowFailure.Unknown));

        packetSender.Sent.Should().BeEmpty();
    }

    // ---- serialisation

    [TestMethod]
    public void EveryKindOfThePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId id = new();

        foreach (CyclopsTowPressKind kind in System.Enum.GetValues<CyclopsTowPressKind>())
        {
            CyclopsTowPress sent = new(kind, id, (ushort)7, CyclopsTowFailure.InUse);

            CyclopsTowPress received = (CyclopsTowPress)Packet.Deserialize(sent.Serialize());

            received.Kind.Should().Be(kind);
            received.CyclopsId.Should().Be(id);
            received.Presser.Should().Be((SessionId)(ushort)7);
            received.Reason.Should().Be(CyclopsTowFailure.InUse);
        }
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

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add((packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => throw new AssertFailedException("The tow button must only reach the players it concerns");

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => throw new AssertFailedException("The tow button must only reach the players it concerns");
    }
}
