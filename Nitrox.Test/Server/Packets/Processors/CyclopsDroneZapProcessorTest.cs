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

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The zapper of the Scout Drone on the server: the seated operator of a live Cyclops asks with the drone out of its dock and near the place named; the server keeps the cooldown on its own clock, tells everybody
///     with the radius of its setting, and refuses everything else to the sender alone. A client cannot send the server's own messages.
/// </summary>
[TestClass]
public class CyclopsDroneZapProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsDroneArbiter arbiter = null!;
    private CyclopsDroneZapProcessor processor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private VehicleEntity cyclops = null!;
    private WorldEntity drone = null!;
    private long now;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        arbiter = new CyclopsDroneArbiter(packetSender);
        now = 100_000;
        processor = new CyclopsDroneZapProcessor(arbiter, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsDroneZapProcessor>.Instance) { Clock = () => now };
        cyclops = new VehicleEntity(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "cyclops", true, new NitroxId(), new NitroxTechType("Cyclops"), null);
        entityRegistry.AddOrUpdate(cyclops);
        drone = new GlobalRootEntity(new NitroxTransform(new NitroxVector3(100f, -50f, 200f), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType(ScoutDroneRules.ITEM_TECH_TYPE_NAME), null, null, []);
        entityRegistry.AddOrUpdate(drone);
        alice = CreatePlayer(1);
        bob = CreatePlayer(2);
        // alice flies the drone, which is out
        arbiter.Load(cyclops.Id, drone.Id);
        arbiter.TrySit(cyclops.Id, alice.SessionId, alice.Name, out _);
        arbiter.SetDocked(cyclops.Id, drone.Id, false);
    }

    private Task Send(Player from, CyclopsDroneZap packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    private CyclopsDroneZap RequestAt(float x, float y, float z, int serial = 1, NitroxId? droneId = null) => CyclopsDroneZap.Request(cyclops.Id, droneId ?? drone.Id, new NitroxVector3(x, y, z), serial);

    private CyclopsDroneZap Refusal() => (CyclopsDroneZap)packetSender.Sent.Single().Packet;

    [TestMethod]
    public async Task TheOperatorsZapIsToldToEverybodyWithTheServersRadius()
    {
        serverOptions.ZapRadius = 33f;

        await Send(alice, RequestAt(105f, -50f, 195f, serial: 7));

        CyclopsDroneZap told = (CyclopsDroneZap)packetSender.SentToAll.Should().ContainSingle().Subject;
        told.Kind.Should().Be(CyclopsDroneZapKind.Apply);
        told.CyclopsId.Should().Be(cyclops.Id);
        told.DroneId.Should().Be(drone.Id);
        told.Radius.Should().Be(33f);
        told.Serial.Should().Be(7);
        told.OperatorSession.Should().Be(alice.SessionId, "the server puts the sender in");
        told.Position.Should().Be(new NitroxVector3(105f, -50f, 195f));
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task APlayerWhoDoesNotFlyTheDroneIsRefusedAlone()
    {
        await Send(bob, RequestAt(100f, -50f, 200f));

        packetSender.SentToAll.Should().BeEmpty();
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.NotOperator);
        Refusal().Kind.Should().Be(CyclopsDroneZapKind.Refused);
        packetSender.Sent.Single().SessionId.Should().Be(bob.SessionId);
    }

    [TestMethod]
    public async Task ADroneInItsDockDoesNotZap()
    {
        arbiter.SetDocked(cyclops.Id, drone.Id, true);

        await Send(alice, RequestAt(100f, -50f, 200f));

        packetSender.SentToAll.Should().BeEmpty();
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.NotOut);
    }

    [TestMethod]
    public async Task AZapAtAPlaceThatIsNotWhereTheDroneIsIsRefused()
    {
        await Send(alice, RequestAt(100f + ScoutDroneRules.ZAP_POSITION_TOLERANCE + 1f, -50f, 200f));

        packetSender.SentToAll.Should().BeEmpty();
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.TooFar);
    }

    [TestMethod]
    public async Task AZapWithAPlaceThatIsNotAPlaceInTheWorldIsRefused()
    {
        await Send(alice, RequestAt(float.NaN, -50f, 200f));
        await Send(alice, RequestAt(100f, -50f, 99999f));

        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => ((CyclopsDroneZap)sent.Packet).Failure == CyclopsDroneZapFailure.NotAvailable);
    }

    [TestMethod]
    public async Task ADroneThatIsNotTheBaysIsRefused()
    {
        GlobalRootEntity other = new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType(ScoutDroneRules.ITEM_TECH_TYPE_NAME), null, null, []);
        entityRegistry.AddOrUpdate(other);

        await Send(alice, RequestAt(0f, 0f, 0f, droneId: other.Id));

        packetSender.SentToAll.Should().BeEmpty();
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.NotAvailable);
    }

    [TestMethod]
    public async Task TheCooldownIsTheServersAndIsKeptPerCyclops()
    {
        serverOptions.ZapCooldownSeconds = 10f;
        await Send(alice, RequestAt(100f, -50f, 200f, serial: 1));
        packetSender.SentToAll.Clear();

        now += 7_000;
        await Send(alice, RequestAt(100f, -50f, 200f, serial: 2));
        packetSender.SentToAll.Should().BeEmpty("80 percent of ten seconds is eight");
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.TooSoon);
        packetSender.Sent.Clear();

        now += 1_500;
        await Send(alice, RequestAt(100f, -50f, 200f, serial: 3));
        packetSender.SentToAll.Should().ContainSingle().Which.Should().BeOfType<CyclopsDroneZap>().Which.Serial.Should().Be(3);
    }

    [TestMethod]
    public async Task TheCooldownOfOneCyclopsDoesNotHoldBackAnother()
    {
        VehicleEntity second = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "cyclops", true, new NitroxId(), new NitroxTechType("Cyclops"), null);
        entityRegistry.AddOrUpdate(second);
        GlobalRootEntity secondDrone = new GlobalRootEntity(new NitroxTransform(new NitroxVector3(0f, -20f, 0f), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType(ScoutDroneRules.ITEM_TECH_TYPE_NAME), null, null, []);
        entityRegistry.AddOrUpdate(secondDrone);
        arbiter.Load(second.Id, secondDrone.Id);
        arbiter.TrySit(second.Id, bob.SessionId, bob.Name, out _);
        arbiter.SetDocked(second.Id, secondDrone.Id, false);
        await Send(alice, RequestAt(100f, -50f, 200f));
        packetSender.SentToAll.Clear();

        await Send(bob, CyclopsDroneZap.Request(second.Id, secondDrone.Id, new NitroxVector3(0f, -20f, 0f), 1));

        packetSender.SentToAll.Should().ContainSingle().Which.Should().BeOfType<CyclopsDroneZap>().Which.CyclopsId.Should().Be(second.Id);
        await Send(alice, RequestAt(100f, -50f, 200f, serial: 2));
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.TooSoon, "alice's own Cyclops is still in its cooldown");
    }

    [TestMethod]
    public async Task APlayerCannotSendTheServersOwnMessages()
    {
        await Send(alice, CyclopsDroneZap.Apply(cyclops.Id, drone.Id, new NitroxVector3(1f, 2f, 3f), 99f, 1, alice.SessionId));
        await Send(alice, CyclopsDroneZap.Refused(cyclops.Id, drone.Id, 1, bob.SessionId, CyclopsDroneZapFailure.TooSoon));

        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task WithTheUpgradesOffNothingZaps()
    {
        serverOptions.VehicleUpgrades = false;

        await Send(alice, RequestAt(100f, -50f, 200f));

        packetSender.SentToAll.Should().BeEmpty();
        Refusal().Failure.Should().Be(CyclopsDroneZapFailure.NotAvailable);
    }

    [TestMethod]
    public void TheZapPacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsDroneZap apply = CyclopsDroneZap.Apply(new NitroxId(), new NitroxId(), new NitroxVector3(1f, -2f, 3f), 20f, 5, (ushort)3);
        CyclopsDroneZap refused = CyclopsDroneZap.Refused(new NitroxId(), new NitroxId(), 6, (ushort)4, CyclopsDroneZapFailure.TooSoon);

        ((CyclopsDroneZap)Packet.Deserialize(apply.Serialize())).Should().BeEquivalentTo(apply);
        ((CyclopsDroneZap)Packet.Deserialize(refused.Serialize())).Should().BeEquivalentTo(refused);
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
