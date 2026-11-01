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
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The shots of the gunner station on the server (feature group 3, station 2): only the seated gunner can ask, a shot goes to the player who simulates the Cyclops to be paid for and the answer goes back to the
///     gunner alone, and the server keeps the cooldown (80 percent of the setting) so that a client that does not keep its own cannot make the simulator pay as often as it likes.
/// </summary>
[TestClass]
public class CyclopsGunnerFireProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsGunnerArbiter arbiter = null!;
    private CyclopsGunnerFireProcessor processor = null!;
    private Player pilot = null!;
    private Player gunner = null!;
    private Player bystander = null!;
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
        arbiter = new CyclopsGunnerArbiter(packetSender);
        pilot = CreatePlayer(1);
        gunner = CreatePlayer(2);
        bystander = CreatePlayer(3);
        cyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE);
        arbiter.TrySit(cyclops.Id, gunner.SessionId, gunner.Name, out _);
        clock = 100_000;
        processor = new CyclopsGunnerFireProcessor(arbiter, ownership, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsGunnerFireProcessor>.Instance) { Clock = () => clock };
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsGunnerFire packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    private int shot;

    private Task Request(Player from, float charge = 1f) => Send(from, CyclopsGunnerFire.Request(cyclops.Id, ++shot, charge, CyclopsGunnerAmmo.StasisSphere));

    private CyclopsGunnerFire SentTo(Player player) => (CyclopsGunnerFire)packetSender.Sent.Last(sent => sent.SessionId == player.SessionId).Packet;

    // ---- the request

    [TestMethod]
    public async Task TheSeatedGunnersRequestGoesToThePlayerWhoSimulatesTheCyclopsWithTheGunnerFromTheConnection()
    {
        // the packet names somebody else as the gunner: the server does not believe it
        await Send(gunner, new CyclopsGunnerFire(CyclopsGunnerFireKind.Request, cyclops.Id, 1, bystander.SessionId, 0.75f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
        CyclopsGunnerFire forwarded = (CyclopsGunnerFire)packetSender.Sent[0].Packet;
        forwarded.Kind.Should().Be(CyclopsGunnerFireKind.Forward);
        forwarded.Gunner.Should().Be(gunner.SessionId);
        forwarded.ChargeNormalized.Should().Be(0.75f);
        forwarded.CyclopsId.Should().Be(cyclops.Id);
        forwarded.Ammo.Should().Be(CyclopsGunnerAmmo.StasisSphere);
    }

    [TestMethod]
    public async Task ThePlayerWhoSimulatesTheCyclopsAndIsTheGunnerIsAskedToPayLikeAnyoneElse()
    {
        ownership.RevokeIfOwner(cyclops.Id, pilot);
        ownership.TryToAcquire(cyclops.Id, gunner, SimulationLockType.EXCLUSIVE);

        await Request(gunner);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(gunner.SessionId);
        ((CyclopsGunnerFire)packetSender.Sent[0].Packet).Kind.Should().Be(CyclopsGunnerFireKind.Forward);
    }

    [TestMethod]
    public async Task AChargeIsKeptInsideZeroAndOneAndANumberThatIsNoneIsDropped()
    {
        await Request(gunner, 7f);
        ((CyclopsGunnerFire)packetSender.Sent[0].Packet).ChargeNormalized.Should().Be(1f);
        packetSender.Sent.Clear();

        clock += 10_000;
        await Request(gunner, float.NaN);
        await Request(gunner, float.PositiveInfinity);

        packetSender.Sent.Should().BeEmpty("a modified client's charge is dropped without an answer");
    }

    [TestMethod]
    public async Task APlayerWhoDoesNotSitAtTheDeskIsTurnedDownEvenIfTheyPilotTheCyclops()
    {
        await Request(bystander);
        await Request(pilot);

        packetSender.Sent.Should().HaveCount(2);
        packetSender.Sent.Select(sent => ((CyclopsGunnerFire)sent.Packet).Failure).Should().OnlyContain(failure => failure == CyclopsGunnerFailure.NotSeated);
        packetSender.Sent.Select(sent => sent.SessionId).Should().BeEquivalentTo([bystander.SessionId, pilot.SessionId], "each is told alone");
    }

    [TestMethod]
    public async Task ARequestForSomethingThatIsNoLiveCyclopsOrWithTheUpgradesOffOrWithUnknownAmmoIsRefused()
    {
        await Send(gunner, CyclopsGunnerFire.Request(seamoth.Id, 1, 1f, CyclopsGunnerAmmo.StasisSphere));
        await Send(gunner, new CyclopsGunnerFire(CyclopsGunnerFireKind.Request, cyclops.Id, 1, default, 1f, (CyclopsGunnerAmmo)9, CyclopsGunnerFailure.None));
        cyclops.Metadata = new CyclopsMetadata(false, false, false, false, 0, 100, true);
        await Request(gunner);
        cyclops.Metadata = null;
        serverOptions.VehicleUpgrades = false;
        await Request(gunner);

        packetSender.Sent.Should().HaveCount(4).And.OnlyContain(sent => sent.SessionId == gunner.SessionId);
        packetSender.Sent.Select(sent => ((CyclopsGunnerFire)sent.Packet).Failure).Should().OnlyContain(failure => failure == CyclopsGunnerFailure.NotAvailable);
    }

    [TestMethod]
    public async Task WithNobodySimulatingTheCyclopsTheGunnerIsToldAndTheCooldownIsNotBurnt()
    {
        ownership.RevokeIfOwner(cyclops.Id, pilot);

        await Request(gunner);

        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.NoSimulator);
        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE);
        packetSender.Sent.Clear();

        await Request(gunner);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "the refusal that never reached the simulator did not start the cooldown");
    }

    // ---- the cooldown

    [TestMethod]
    public async Task AShotThatComesTooSoonIsTurnedDownToTheGunnerAndNeverReachesTheSimulator()
    {
        await Request(gunner);
        packetSender.Sent.Clear();
        long interval = CyclopsGunnerRules.ServerMinIntervalMilliseconds(VehicleUpgradeSettingsRules.DEFAULT_GUNNER_COOLDOWN_SECONDS);
        interval.Should().Be(2400, "80 percent of the three seconds");

        clock += interval - 1;
        await Request(gunner);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(gunner.SessionId);
        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.TooSoon);
        packetSender.Sent.Clear();

        clock += 1;
        await Request(gunner);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "the interval is over: the request is passed on");
    }

    [TestMethod]
    public async Task AFloodOfRequestsMakesTheSimulatorPayOnce()
    {
        for (int i = 0; i < 50; i++)
        {
            clock += 20;
            await Request(gunner);
        }

        packetSender.Sent.Count(sent => sent.SessionId == pilot.SessionId).Should().Be(1);
        packetSender.Sent.Count(sent => sent.SessionId == gunner.SessionId).Should().Be(49);
    }

    [TestMethod]
    public async Task TheCooldownOfTheSettingIsUsedAndAZeroCooldownIsStillNoHose()
    {
        serverOptions.GunnerCooldownSeconds = 10f;
        await Request(gunner);
        packetSender.Sent.Clear();

        clock += 7_999;
        await Request(gunner);
        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.TooSoon);
        clock += 1;
        packetSender.Sent.Clear();
        await Request(gunner);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);

        serverOptions.GunnerCooldownSeconds = 0f;
        packetSender.Sent.Clear();
        clock += 10_000;
        await Request(gunner);
        packetSender.Sent.Clear();
        clock += CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS - 1;
        await Request(gunner);
        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.TooSoon, "even a host who wants no cooldown does not let a client hose the simulator");
    }

    [TestMethod]
    public async Task ACooldownOfOtherCyclopsesIsTheirOwn()
    {
        VehicleEntity other = AddVehicle("Cyclops");
        ownership.TryToAcquire(other.Id, pilot, SimulationLockType.EXCLUSIVE);
        Player otherGunner = CreatePlayer(4);
        arbiter.TrySit(other.Id, otherGunner.SessionId, otherGunner.Name, out _);

        await Request(gunner);
        await Send(otherGunner, CyclopsGunnerFire.Request(other.Id, 1, 1f, CyclopsGunnerAmmo.StasisSphere));

        packetSender.Sent.Count(sent => ((CyclopsGunnerFire)sent.Packet).Kind == CyclopsGunnerFireKind.Forward).Should().Be(2);
    }

    // ---- the answer

    [TestMethod]
    public async Task TheSimulatorsAnswerGoesToTheGunnerAloneAndOnlyOnceForOneShot()
    {
        await Request(gunner, 0.5f);
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 0.5f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(gunner.SessionId);
        CyclopsGunnerFire answer = SentTo(gunner);
        answer.Kind.Should().Be(CyclopsGunnerFireKind.Result);
        answer.Failure.Should().Be(CyclopsGunnerFailure.None);
        answer.ChargeNormalized.Should().Be(0.5f);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 0.5f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        packetSender.Sent.Should().BeEmpty("one forwarded shot is answered once: no second grant");
    }

    [TestMethod]
    public async Task TwoShotsInTheAirAreAnsweredEachByItsOwnNumberInAnyOrder()
    {
        serverOptions.GunnerCooldownSeconds = 0f;
        await Request(gunner);
        int first = shot;
        clock += CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS + 1;
        await Request(gunner);
        int second = shot;
        packetSender.Sent.Clear();

        // both are forwarded and waiting (inside the time that the server waits): the answers come in the wrong order, and each is delivered with its own number
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, second, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, first, gunner.SessionId, 0.5f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.NoPower));

        List<CyclopsGunnerFire> told = packetSender.Sent.Where(sent => sent.SessionId == gunner.SessionId).Select(sent => (CyclopsGunnerFire)sent.Packet).ToList();
        told.Should().HaveCount(2);
        told[0].Shot.Should().Be(second);
        told[0].Failure.Should().Be(CyclopsGunnerFailure.None);
        told[1].Shot.Should().Be(first);
        told[1].Failure.Should().Be(CyclopsGunnerFailure.NoPower);
        packetSender.Sent.Clear();

        // an answer for a number that was never forwarded, and a second answer for one that was answered, are dropped
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, second + 1, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, second, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnAnswerThatCameAfterTheWaitIsNotTakenForTheNewShot()
    {
        await Request(gunner);
        int first = shot;
        clock += 10_000;
        await Request(gunner);
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, first, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        packetSender.Sent.Should().BeEmpty("the first shot was forgotten when the second was asked for, long after the server's wait");
    }

    [TestMethod]
    [DataRow(1000f, 47_999L, 48_000L)]
    [DataRow(float.NaN, 2_399L, 2_400L)]
    [DataRow(-5f, 249L, 250L)]
    public async Task TheServerKeepsTheCooldownThatTheClientsClampToNotTheRawOption(float raw, long tooSoonAt, long forwardedAt)
    {
        serverOptions.GunnerCooldownSeconds = raw;
        await Request(gunner);
        packetSender.Sent.Clear();

        clock += tooSoonAt;
        await Request(gunner);
        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.TooSoon);
        packetSender.Sent.Clear();

        clock += forwardedAt - tooSoonAt;
        await Request(gunner);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
    }

    [TestMethod]
    public async Task TheAnswerOfThePlayerWhoWasAskedCountsEvenIfTheLockMovedOnTheWay()
    {
        await Request(gunner);
        packetSender.Sent.Clear();
        // the pilot leaves the chair and the lock goes to somebody else while the shot is being paid for
        ownership.RevokeIfOwner(cyclops.Id, pilot);
        ownership.TryToAcquire(cyclops.Id, bystander, SimulationLockType.TRANSIENT);

        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.None, "the player who paid is the one who was asked");
        packetSender.Sent.Clear();

        // ... but the new lock holder cannot answer a shot that was not forwarded to it
        clock += 10_000;
        await Request(gunner);
        packetSender.Sent.Clear();
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty("the shot went to the new lock holder, not to the old one");
    }

    [TestMethod]
    public async Task AFailureOfThePayingPlayerReachesTheGunner()
    {
        await Request(gunner);
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.NoPower));

        SentTo(gunner).Failure.Should().Be(CyclopsGunnerFailure.NoPower);
    }

    [TestMethod]
    public async Task AnAnswerNobodyAskedForOrFromAnotherPlayerOrAfterTheWaitOrForAGunnerWhoLeftIsDropped()
    {
        // nothing was forwarded: an unsolicited grant
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty();

        // the answer of a player who does not simulate the Cyclops
        await Request(gunner);
        packetSender.Sent.Clear();
        await Send(bystander, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        await Send(gunner, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty();

        // an answer for somebody else's shot
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, bystander.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty();

        // too late
        clock += CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS + 1;
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty();

        // the gunner left before the answer came
        clock += 10_000;
        await Request(gunner);
        packetSender.Sent.Clear();
        arbiter.Leave(cyclops.Id, gunner.SessionId);
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnAnswerForSomethingThatIsNoCyclopsOrWithAFailureThatDoesNotExistOrWithTheUpgradesOffIsDropped()
    {
        await Request(gunner);
        packetSender.Sent.Clear();

        await Send(pilot, new CyclopsGunnerFire(CyclopsGunnerFireKind.Result, cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, (CyclopsGunnerFailure)99));
        ownership.TryToAcquire(seamoth.Id, pilot, SimulationLockType.EXCLUSIVE);
        await Send(pilot, CyclopsGunnerFire.Result(seamoth.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        serverOptions.VehicleUpgrades = false;
        await Send(pilot, CyclopsGunnerFire.Result(cyclops.Id, shot, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AClientCannotSendTheServersOwnMessagesOrNothingAtAll()
    {
        await Send(gunner, new CyclopsGunnerFire(CyclopsGunnerFireKind.Forward, cyclops.Id, 1, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        await Send(gunner, new CyclopsGunnerFire(CyclopsGunnerFireKind.Unknown, cyclops.Id, 1, default, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        await Send(gunner, new CyclopsGunnerFire(CyclopsGunnerFireKind.Request, null!, 1, default, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));
        await Send(pilot, new CyclopsGunnerFire(CyclopsGunnerFireKind.Result, null!, 1, gunner.SessionId, 1f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.None));

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public void TheFirePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsGunnerFire packet = CyclopsGunnerFire.Result(new NitroxId(), 17, (SessionId)(ushort)4, 0.625f, CyclopsGunnerAmmo.StasisSphere, CyclopsGunnerFailure.NoPower);

        CyclopsGunnerFire received = (CyclopsGunnerFire)Packet.Deserialize(packet.Serialize());

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
