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
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The engine heat on its way to the engineer (feature group 3, station 3): taken from the player who simulates the Cyclops only, only while somebody sits at the console, not oftener than the floor, with the
///     numbers clamped, and handed to the engineer alone.
/// </summary>
[TestClass]
public class CyclopsEngineerStatusProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsEngineerArbiter arbiter = null!;
    private CyclopsEngineerStatusProcessor processor = null!;
    private Player pilot = null!;
    private Player engineer = null!;
    private Player bystander = null!;
    private NitroxId cyclopsId = null!;
    private long clock;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        serverOptions = new SubnauticaServerOptions();
        arbiter = new CyclopsEngineerArbiter(packetSender);
        pilot = CreatePlayer(1);
        engineer = CreatePlayer(2);
        bystander = CreatePlayer(3);
        cyclopsId = new NitroxId();
        ownership.TryToAcquire(cyclopsId, pilot, SimulationLockType.EXCLUSIVE);
        arbiter.TrySit(cyclopsId, engineer.SessionId, engineer.Name, out _);
        clock = 100_000;
        processor = new CyclopsEngineerStatusProcessor(arbiter, ownership, Options.Create(serverOptions), NullLogger<CyclopsEngineerStatusProcessor>.Instance) { Clock = () => clock };
    }

    private Task Send(Player from, CyclopsEngineerStatus packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    [TestMethod]
    public async Task TheHeatFromThePlayerWhoSimulatesTheCyclopsGoesToTheEngineerAlone()
    {
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 6, true, true, 72.5f));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        CyclopsEngineerStatus told = (CyclopsEngineerStatus)packetSender.Sent[0].Packet;
        told.Heat.Should().Be(6);
        told.Heating.Should().BeTrue();
        told.EngineOn.Should().BeTrue();
        told.PowerPercent.Should().Be(72.5f);
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HeatFromAnyoneElseIsDroppedEvenFromTheEngineer()
    {
        await Send(engineer, new CyclopsEngineerStatus(cyclopsId, 6, true, true, 50f));
        await Send(bystander, new CyclopsEngineerStatus(cyclopsId, 6, true, true, 50f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HeatWithNobodyAtTheConsoleOrWithTheUpgradesOffIsDropped()
    {
        arbiter.Leave(cyclopsId, engineer.SessionId);
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 6, true, true, 50f));
        arbiter.TrySit(cyclopsId, engineer.SessionId, engineer.Name, out _);
        serverOptions.VehicleUpgrades = false;
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 6, true, true, 50f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task TheFloorBetweenTwoStatusesOfACyclopsIsKept()
    {
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 3, true, true, 50f));
        clock += 100;
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 4, true, true, 50f));
        packetSender.Sent.Should().ContainSingle();

        clock += CyclopsEngineerRules.MIN_STATUS_INTERVAL_MILLISECONDS;
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 4, true, true, 50f));
        packetSender.Sent.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task NumbersThatAreNoNumbersOrOutsideTheRangeAreClamped()
    {
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 99, true, true, float.NaN));
        clock += 1000;
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, -4, false, false, 900f));
        clock += 1000;
        await Send(pilot, new CyclopsEngineerStatus(cyclopsId, 5, false, true, float.PositiveInfinity));

        List<CyclopsEngineerStatus> told = packetSender.Sent.Select(sent => (CyclopsEngineerStatus)sent.Packet).ToList();
        told.Select(status => status.Heat).Should().Equal(10, 0, 5);
        told.Select(status => status.PowerPercent).Should().Equal(0f, 100f, 0f);
    }

    [TestMethod]
    public async Task AStatusWithoutACyclopsIsDropped()
    {
        await Send(pilot, new CyclopsEngineerStatus(null!, 3, true, true, 50f));

        packetSender.Sent.Should().BeEmpty();
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
