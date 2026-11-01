using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The server's memory of the custom player models (docs/coop/PLAYER_MODELS.md): the latest choice of every session, the vanilla body is not kept, a joiner is told what the others wear (not what he
///     wears himself), and a session that ends is forgotten.
/// </summary>
[TestClass]
public class PlayerModelLedgerTest
{
    private static readonly SessionId Alice = (ushort)1;
    private static readonly SessionId Bob = (ushort)2;
    private static readonly SessionId Carol = (ushort)3;

    private RecordingPacketSender packetSender = null!;
    private PlayerModelLedger ledger = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ledger = new PlayerModelLedger(packetSender);
    }

    [TestMethod]
    public void TheLatestChoiceOfASessionIsTheOneThatIsKept()
    {
        ledger.Set(Bob, "player-example", 21741924);
        ledger.Set(Bob, "other-model", 1000);

        ledger.TryGet(Bob, out PlayerModelLedger.Choice choice).Should().BeTrue();
        choice.ModelId.Should().Be("other-model");
        choice.FileBytes.Should().Be(1000);
        ledger.Count.Should().Be(1);
    }

    [TestMethod]
    public void TheVanillaBodyIsNotKept()
    {
        ledger.Set(Bob, "player-example", 21741924);

        ledger.Set(Bob, "", 0);

        ledger.TryGet(Bob, out _).Should().BeFalse();
        ledger.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task AJoinerIsToldWhatTheOthersWearAndNotWhatHeWearsHimself()
    {
        ledger.Set(Alice, "alice-model", 111);
        ledger.Set(Bob, "player-example", 21741924);

        await ledger.SendSnapshotAsync(Carol);
        await ledger.SendSnapshotAsync(Alice);

        packetSender.Sent.Should().HaveCount(3);
        PlayerModelChoice[] toCarol = packetSender.Sent.Where(sent => sent.SessionId == Carol).Select(sent => (PlayerModelChoice)sent.Packet).ToArray();
        toCarol.Should().HaveCount(2);
        toCarol[0].SessionId.Should().Be(Alice);
        toCarol[0].ModelId.Should().Be("alice-model");
        toCarol[0].FileBytes.Should().Be(111);
        toCarol[1].SessionId.Should().Be(Bob);
        toCarol[1].ModelId.Should().Be("player-example");
        toCarol[1].FileBytes.Should().Be(21741924);
        packetSender.Sent.Where(sent => sent.SessionId == Alice).Select(sent => (PlayerModelChoice)sent.Packet).Should().ContainSingle().Which.SessionId.Should().Be(Bob);
    }

    [TestMethod]
    public async Task ANobodyWhoWearsAModelIsNotToldAnything()
    {
        ledger.Set(Bob, "", 0);

        await ledger.SendSnapshotAsync(Carol);

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ASessionThatEndsIsForgotten()
    {
        ledger.Set(Bob, "player-example", 21741924);

        await ledger.OnEventAsync(new ISessionCleaner.Args(new SessionManager.Session(Bob, new IPEndPoint(IPAddress.Loopback, 1)), 1));

        ledger.TryGet(Bob, out _).Should().BeFalse();
        await ledger.SendSnapshotAsync(Carol);
        packetSender.Sent.Should().BeEmpty();
    }

    private sealed class RecordingPacketSender : IPacketSender
    {
        public List<(Packet Packet, SessionId SessionId)> Sent { get; } = [];

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add((packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => ValueTask.CompletedTask;

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => ValueTask.CompletedTask;
    }
}
