using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

[TestClass]
public class BasePowerConsumedProcessorTest
{
    private RecordingPacketSender packetSender;
    private SimulationOwnershipData simulationOwnershipData;
    private BasePowerConsumedProcessor processor;
    private Player crafter;
    private Player baseSimulator;
    private AuthProcessorContext context;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        simulationOwnershipData = new SimulationOwnershipData();
        processor = new BasePowerConsumedProcessor(simulationOwnershipData);
        crafter = CreatePlayer(1);
        baseSimulator = CreatePlayer(2);
        context = new AuthProcessorContext(crafter, packetSender);
    }

    [TestMethod]
    public async Task Process_OtherPlayerSimulatesTheBase_ForwardsToThem()
    {
        NitroxId baseId = new();
        simulationOwnershipData.TryToAcquire(baseId, baseSimulator, SimulationLockType.TRANSIENT);
        BasePowerConsumed packet = new(baseId, 5f);

        await processor.Process(context, packet);

        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].SessionId.Should().Be(baseSimulator.SessionId);
        packetSender.Sent[0].Packet.Should().BeSameAs(packet);
    }

    [TestMethod]
    public async Task Process_SenderSimulatesTheBase_Dropped()
    {
        NitroxId baseId = new();
        simulationOwnershipData.TryToAcquire(baseId, crafter, SimulationLockType.TRANSIENT);

        await processor.Process(context, new BasePowerConsumed(baseId, 5f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Process_NobodySimulatesTheBase_Dropped()
    {
        await processor.Process(context, new BasePowerConsumed(new NitroxId(), 5f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-5f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(BasePowerConsumedProcessor.MAX_AMOUNT + 1f)]
    public async Task Process_InvalidAmount_Dropped(float amount)
    {
        NitroxId baseId = new();
        simulationOwnershipData.TryToAcquire(baseId, baseSimulator, SimulationLockType.TRANSIENT);

        await processor.Process(context, new BasePowerConsumed(baseId, amount));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public void Packet_KeepsBaseAndAmountThroughTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId baseId = new();

        BasePowerConsumed received = (BasePowerConsumed)Packet.Deserialize(new BasePowerConsumed(baseId, 5f).Serialize());

        received.BaseId.Should().Be(baseId);
        received.Amount.Should().Be(5f);
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

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add((packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => throw new AssertFailedException("A power report must only reach one player");

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => throw new AssertFailedException("A power report must only reach one player");
    }
}
