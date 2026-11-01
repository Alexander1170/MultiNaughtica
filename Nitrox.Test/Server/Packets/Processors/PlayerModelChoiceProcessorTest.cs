using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The custom player model on the server (docs/coop/PLAYER_MODELS.md): the sender is the connection's whatever the packet says, an id that is not a model name is the vanilla body, a player gets one choice
///     per two seconds, the latest choice of every session is remembered, and what is accepted goes to everybody but the sender.
/// </summary>
[TestClass]
public class PlayerModelChoiceProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private PlayerModelLedger ledger = null!;
    private PlayerModelChoiceProcessor processor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private double clock;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ledger = new PlayerModelLedger(packetSender);
        alice = CreatePlayer(1, "Alice");
        bob = CreatePlayer(2, "Bob");
        clock = 1000.0;
        processor = new PlayerModelChoiceProcessor(ledger, NullLogger<PlayerModelChoiceProcessor>.Instance) { Clock = () => clock };
    }

    private Task Send(Player from, PlayerModelChoice packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    [TestMethod]
    public async Task AChoiceGoesToEverybodyElseWithTheSessionOfTheConnection()
    {
        await Send(bob, new PlayerModelChoice(bob.SessionId, "player-example", 21741924));

        PlayerModelChoice relayed = (PlayerModelChoice)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet;
        packetSender.SentToOthers[0].Excluded.Should().Be(bob.SessionId, "the chooser changed his own menu");
        relayed.SessionId.Should().Be(bob.SessionId);
        relayed.ModelId.Should().Be("player-example");
        relayed.FileBytes.Should().Be(21741924);
        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AChoiceThatClaimsToBeAnotherPlayersIsStampedWithTheRealSender()
    {
        // Bob says that Alice chose
        await Send(bob, new PlayerModelChoice(alice.SessionId, "player-example", 21741924));

        ((PlayerModelChoice)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet).SessionId.Should().Be(bob.SessionId);
        ledger.TryGet(bob.SessionId, out _).Should().BeTrue();
        ledger.TryGet(alice.SessionId, out _).Should().BeFalse("a client cannot choose for another player");
    }

    [TestMethod]
    [DataRow("..\\..\\evil")]
    [DataRow("a/b")]
    [DataRow("<b>big</b>")]
    [DataRow("an id with spaces")]
    [DataRow("this-id-is-far-too-long-to-be-the-name-of-a-model-file")]
    public async Task AnIdThatIsNotAModelNameIsTheVanillaBody(string hostile)
    {
        await Send(bob, new PlayerModelChoice(bob.SessionId, hostile, 12345));

        PlayerModelChoice relayed = (PlayerModelChoice)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet;
        relayed.ModelId.Should().BeEmpty();
        relayed.FileBytes.Should().Be(0);
        ledger.TryGet(bob.SessionId, out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task ANegativeLengthBecomesNothing()
    {
        await Send(bob, new PlayerModelChoice(bob.SessionId, "player-example", -5));

        ((PlayerModelChoice)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet).FileBytes.Should().Be(0);
    }

    [TestMethod]
    public async Task OneChoiceInTwoSecondsIsAccepted()
    {
        await Send(bob, new PlayerModelChoice(bob.SessionId, "player-example", 21741924));
        clock += PlayerModelRules.SERVER_INTERVAL_SECONDS - 0.5;
        await Send(bob, new PlayerModelChoice(bob.SessionId, "", 0));

        packetSender.SentToOthers.Should().ContainSingle("the second one came too soon");
        ledger.TryGet(bob.SessionId, out PlayerModelLedger.Choice kept).Should().BeTrue();
        kept.ModelId.Should().Be("player-example");

        clock += 1.0;
        await Send(bob, new PlayerModelChoice(bob.SessionId, "", 0));

        packetSender.SentToOthers.Should().HaveCount(2);
        ledger.TryGet(bob.SessionId, out _).Should().BeFalse("the vanilla body forgets the choice");
    }

    [TestMethod]
    public async Task TheLimitIsPerPlayer()
    {
        await Send(bob, new PlayerModelChoice(bob.SessionId, "player-example", 1));
        await Send(alice, new PlayerModelChoice(alice.SessionId, "alice-model", 2));

        packetSender.SentToOthers.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task APlayerWhoIsNotOnlineIsIgnored()
    {
        Player offline = CreatePlayer(3, "Carol");
        offline.IsOnline = false;

        await Send(offline, new PlayerModelChoice(offline.SessionId, "player-example", 1));

        packetSender.SentToOthers.Should().BeEmpty();
        ledger.Count.Should().Be(0);
    }

    [TestMethod]
    public void AnHonestClientIsNeverTurnedDown()
    {
        PlayerModelRules.CLIENT_INTERVAL_SECONDS.Should().BeGreaterThan(PlayerModelRules.SERVER_INTERVAL_SECONDS);
    }

    private static Player CreatePlayer(ushort sessionId, string name)
    {
        return new Player(sessionId, sessionId, name, false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
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
