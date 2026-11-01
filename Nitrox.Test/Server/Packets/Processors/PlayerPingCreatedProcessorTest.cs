using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The quick ping on the server (docs/coop/QUICK_PING.md): the sender is the connection's whatever the packet says, the text is cleaned, a ping that is not a place in the world or that comes too soon is
///     dropped, and what is accepted goes to everybody but the sender.
/// </summary>
[TestClass]
public class PlayerPingCreatedProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private PlayerPingCreatedProcessor processor = null!;
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
        processor = new PlayerPingCreatedProcessor(NullLogger<PlayerPingCreatedProcessor>.Instance) { Clock = () => clock };
    }

    private Task Send(Player from, PlayerPingCreated packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    private static PlayerPingCreated Ping(SessionId claimed, string text = "Reaper Leviathan", float x = -80f, string claimedName = "") => new(claimed, text, new NitroxVector3(x, -20f, 355f), new NitroxId(), claimedName);

    [TestMethod]
    public async Task APingGoesToEverybodyElseWithTheSenderOfTheConnection()
    {
        PlayerPingCreated packet = Ping(bob.SessionId);

        await Send(bob, packet);

        packetSender.SentToOthers.Should().ContainSingle().Which.Excluded.Should().Be(bob.SessionId, "the pinger has its own marker already");
        PlayerPingCreated relayed = (PlayerPingCreated)packetSender.SentToOthers[0].Packet;
        relayed.SessionId.Should().Be(bob.SessionId);
        relayed.SenderName.Should().Be("Bob");
        relayed.Text.Should().Be("Reaper Leviathan");
        relayed.Position.Should().Be(packet.Position);
        relayed.PingId.Should().Be(packet.PingId);
        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task APingThatClaimsToBeAnotherPlayersIsStampedWithTheRealSenderAndName()
    {
        // Bob says that Alice pinged
        await Send(bob, Ping(alice.SessionId, claimedName: "Alice"));

        PlayerPingCreated relayed = (PlayerPingCreated)packetSender.SentToOthers.Should().ContainSingle().Subject.Packet;
        relayed.SessionId.Should().Be(bob.SessionId);
        relayed.SenderName.Should().Be("Bob");
    }

    [TestMethod]
    public async Task TheTextIsCleanedAndAnEmptyOneBecomesLookHere()
    {
        await Send(bob, Ping(bob.SessionId, "<size=99><color=red>Boom</color></size>"));
        ((PlayerPingCreated)packetSender.SentToOthers[0].Packet).Text.Should().NotContain("<").And.NotContain(">");
        packetSender.SentToOthers.Clear();

        clock += 5;
        await Send(bob, Ping(bob.SessionId, ""));
        ((PlayerPingCreated)packetSender.SentToOthers[0].Packet).Text.Should().Be(QuickPingRules.LOOK_HERE);
        packetSender.SentToOthers.Clear();

        clock += 5;
        await Send(bob, Ping(bob.SessionId, new string('x', 500)));
        ((PlayerPingCreated)packetSender.SentToOthers[0].Packet).Text.Length.Should().BeLessThanOrEqualTo(CyclopsNavRules.MAX_LABEL_LENGTH);
    }

    [TestMethod]
    public async Task APingThatIsNotAPlaceInTheWorldIsDropped()
    {
        await Send(bob, Ping(bob.SessionId, x: float.NaN));
        await Send(bob, Ping(bob.SessionId, x: 1e9f));
        await Send(bob, new PlayerPingCreated(bob.SessionId, "x", new NitroxVector3(0f, float.PositiveInfinity, 0f), new NitroxId()));

        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task APingWithoutAnIdIsDropped()
    {
        await Send(bob, new PlayerPingCreated(bob.SessionId, "x", new NitroxVector3(0f, -5f, 0f), null!));

        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task APlayerWhoIsNotOnlineCannotPing()
    {
        bob.IsOnline = false;

        await Send(bob, Ping(bob.SessionId));

        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ASecondPingWithinTheIntervalIsDroppedAndOneAfterItIsNot()
    {
        await Send(bob, Ping(bob.SessionId, "one"));
        clock += QuickPingRules.SERVER_INTERVAL_SECONDS - 0.01;
        await Send(bob, Ping(bob.SessionId, "two"));
        packetSender.SentToOthers.Should().ContainSingle("the second came too soon");

        clock += 0.02;
        await Send(bob, Ping(bob.SessionId, "three"));
        packetSender.SentToOthers.Should().HaveCount(2);
        ((PlayerPingCreated)packetSender.SentToOthers[1].Packet).Text.Should().Be("three");
    }

    [TestMethod]
    public async Task AFloodFromOneClientGetsOnePingThrough()
    {
        for (int i = 0; i < 20; i++)
        {
            await Send(bob, Ping(bob.SessionId, $"Flood {i}"));
        }

        packetSender.SentToOthers.Should().ContainSingle().Which.Packet.Should().BeOfType<PlayerPingCreated>().Which.Text.Should().Be("Flood 0");
    }

    [TestMethod]
    public async Task TheLimitOfOnePlayerDoesNotHoldBackAnother()
    {
        await Send(bob, Ping(bob.SessionId, "from Bob"));
        await Send(alice, Ping(alice.SessionId, "from Alice"));

        packetSender.SentToOthers.Should().HaveCount(2);
        packetSender.SentToOthers[1].Excluded.Should().Be(alice.SessionId);
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
