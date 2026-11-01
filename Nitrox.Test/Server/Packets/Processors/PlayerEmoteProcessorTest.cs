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
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The emotes on the server (docs/coop/PLAYER_MODELS.md): the sender is the connection's whatever the packet says, an id that is not a clip name is dropped, a player starts one emote per second and stops
///     one per fifth of a second, and what is accepted goes to everybody but the sender. Nothing is kept for a player who joins later.
/// </summary>
[TestClass]
public class PlayerEmoteProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private PlayerEmoteProcessor processor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private double clock;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        alice = CreatePlayer(1, "Alice");
        bob = CreatePlayer(2, "Bob");
        clock = 1000.0;
        processor = new PlayerEmoteProcessor(NullLogger<PlayerEmoteProcessor>.Instance) { Clock = () => clock };
    }

    private Task Send(Player from, PlayerEmote packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    [TestMethod]
    public async Task AStartGoesToEverybodyElseWithTheSessionOfTheConnection()
    {
        await Send(bob, new PlayerEmote(bob.SessionId, "Gangnam_Groove", true));

        PlayerEmote relayed = (PlayerEmote)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet;
        packetSender.SentToOthers[0].Excluded.Should().Be(bob.SessionId, "the dancer does not see himself");
        relayed.SessionId.Should().Be(bob.SessionId);
        relayed.EmoteId.Should().Be("Gangnam_Groove");
        relayed.Playing.Should().BeTrue();
        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnEmoteThatClaimsToBeAnotherPlayersIsStampedWithTheRealSender()
    {
        // Bob says that Alice dances
        await Send(bob, new PlayerEmote(alice.SessionId, "Gangnam_Groove", true));

        ((PlayerEmote)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet).SessionId.Should().Be(bob.SessionId);
    }

    [TestMethod]
    [DataRow("..\\..\\evil")]
    [DataRow("a/b")]
    [DataRow("<b>big</b>")]
    [DataRow("an id with spaces")]
    [DataRow("this-id-is-far-too-long-to-be-the-name-of-a-clip-in-any-model")]
    [DataRow("")]
    public async Task AStartWithAnIdThatIsNotAClipNameIsDropped(string hostile)
    {
        await Send(bob, new PlayerEmote(bob.SessionId, hostile, true));

        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AStopGoesToEverybodyElseWithoutAnId()
    {
        await Send(bob, new PlayerEmote(bob.SessionId, "whatever the client puts here", false));

        PlayerEmote relayed = (PlayerEmote)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet;
        relayed.SessionId.Should().Be(bob.SessionId);
        relayed.EmoteId.Should().BeEmpty();
        relayed.Playing.Should().BeFalse();
    }

    [TestMethod]
    public async Task OneStartInASecondIsAccepted()
    {
        await Send(bob, new PlayerEmote(bob.SessionId, "Gangnam_Groove", true));
        clock += PlayerEmoteRules.SERVER_START_INTERVAL_SECONDS - 0.3;
        await Send(bob, new PlayerEmote(bob.SessionId, "ymca_dance", true));

        packetSender.SentToOthers.Should().ContainSingle("the second start came too soon");

        clock += 0.5;
        await Send(bob, new PlayerEmote(bob.SessionId, "ymca_dance", true));

        packetSender.SentToOthers.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task AStopIsNotHeldUpByTheLimitOfTheStarts()
    {
        await Send(bob, new PlayerEmote(bob.SessionId, "Gangnam_Groove", true));
        clock += 0.3;
        await Send(bob, new PlayerEmote(bob.SessionId, "", false));

        packetSender.SentToOthers.Should().HaveCount(2, "a player who started an emote can stop it a moment later");
    }

    [TestMethod]
    public async Task StopsAreLimitedToo()
    {
        await Send(bob, new PlayerEmote(bob.SessionId, "", false));
        clock += PlayerEmoteRules.SERVER_STOP_INTERVAL_SECONDS / 2;
        await Send(bob, new PlayerEmote(bob.SessionId, "", false));

        packetSender.SentToOthers.Should().ContainSingle();
    }

    [TestMethod]
    public async Task TheLimitIsPerPlayer()
    {
        await Send(bob, new PlayerEmote(bob.SessionId, "Gangnam_Groove", true));
        await Send(alice, new PlayerEmote(alice.SessionId, "ymca_dance", true));

        packetSender.SentToOthers.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task APlayerWhoIsNotOnlineIsIgnored()
    {
        Player offline = CreatePlayer(3, "Carol");
        offline.IsOnline = false;

        await Send(offline, new PlayerEmote(offline.SessionId, "Gangnam_Groove", true));

        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public void AnHonestClientIsNeverTurnedDown()
    {
        PlayerEmoteRules.CLIENT_START_INTERVAL_SECONDS.Should().BeGreaterThan(PlayerEmoteRules.SERVER_START_INTERVAL_SECONDS);
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
