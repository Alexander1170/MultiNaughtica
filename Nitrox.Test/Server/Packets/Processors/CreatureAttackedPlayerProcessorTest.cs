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
public class CreatureAttackedPlayerProcessorTest
{
    private RecordingPacketSender packetSender;
    private SimulationOwnershipData simulationOwnershipData;
    private CreatureAttackedPlayerProcessor processor;
    private Player simulatingPlayer;
    private Player victim;
    private AuthProcessorContext context;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        simulationOwnershipData = new SimulationOwnershipData();
        processor = new CreatureAttackedPlayerProcessor(simulationOwnershipData);
        simulatingPlayer = CreatePlayer(1);
        victim = CreatePlayer(2);
        context = new AuthProcessorContext(simulatingPlayer, packetSender);
    }

    [TestMethod]
    public async Task Process_SenderSimulatesCreature_ForwardsToVictimOnly()
    {
        NitroxId creatureId = new();
        simulationOwnershipData.TryToAcquire(creatureId, simulatingPlayer, SimulationLockType.TRANSIENT);
        CreatureAttackedPlayer hit = CreateHit(victim.SessionId, creatureId, 15f);

        await processor.Process(context, hit);

        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].SessionId.Should().Be(victim.SessionId);
        packetSender.Sent[0].Packet.Should().BeSameAs(hit);
    }

    [TestMethod]
    public async Task Process_SenderDoesNotSimulateCreature_Dropped()
    {
        NitroxId creatureId = new();
        simulationOwnershipData.TryToAcquire(creatureId, victim, SimulationLockType.TRANSIENT);

        await processor.Process(context, CreateHit(victim.SessionId, creatureId, 15f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Process_NobodySimulatesCreature_Dropped()
    {
        await processor.Process(context, CreateHit(victim.SessionId, new NitroxId(), 15f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Process_VictimIsSender_Dropped()
    {
        NitroxId creatureId = new();
        simulationOwnershipData.TryToAcquire(creatureId, simulatingPlayer, SimulationLockType.TRANSIENT);

        await processor.Process(context, CreateHit(simulatingPlayer.SessionId, creatureId, 15f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-5f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    public async Task Process_InvalidDamage_Dropped(float damage)
    {
        NitroxId creatureId = new();
        simulationOwnershipData.TryToAcquire(creatureId, simulatingPlayer, SimulationLockType.TRANSIENT);

        await processor.Process(context, CreateHit(victim.SessionId, creatureId, damage));

        packetSender.Sent.Should().BeEmpty();
    }

    private static CreatureAttackedPlayer CreateHit(SessionId victimSessionId, NitroxId creatureId, float damage) => new(victimSessionId, creatureId, damage, DamageType.Normal, NitroxVector3.Zero);

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

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => throw new AssertFailedException("A hit must only reach one player");

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => throw new AssertFailedException("A hit must only reach one player");
    }
}
