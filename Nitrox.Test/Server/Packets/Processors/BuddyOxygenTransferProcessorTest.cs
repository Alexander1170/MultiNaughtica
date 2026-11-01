using System.Collections.Generic;
using System.Reflection;
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
///     The buddy oxygen on the server (docs/coop/BUDDY_OXYGEN.md): a giver's step goes to the receiver only when the sharing is on, the receiver is another player who is online, the two are close enough and the
///     giver does not take air faster than the rate; the receiver's answer says what fitted and the giver gets the rest back; every refusal gives all of the air back; and nobody can send the server's own steps.
/// </summary>
[TestClass]
public class BuddyOxygenTransferProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private PlayerManager playerManager = null!;
    private BuddyOxygenLedger ledger = null!;
    private BuddyOxygenTransferProcessor processor = null!;
    private Player alice = null!;
    private Player bob = null!;
    private Player carl = null!;
    private double clock;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        serverOptions = new SubnauticaServerOptions();
        playerManager = new PlayerManager(null!, null!, Options.Create(serverOptions), NullLogger<PlayerManager>.Instance);
        clock = 1000.0;
        ledger = new BuddyOxygenLedger(packetSender, () => clock);
        processor = new BuddyOxygenTransferProcessor(ledger, playerManager, Options.Create(serverOptions), NullLogger<BuddyOxygenTransferProcessor>.Instance);
        alice = Connect(1, "Alice", 0f);
        bob = Connect(2, "Bob", 1.5f);
        carl = Connect(3, "Carl", 1f);
    }

    private Player Connect(ushort sessionId, string name, float x)
    {
        Player player = new(sessionId, sessionId, name, false, null, new NitroxVector3(x, -10f, 0f), NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                            new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                            new Dictionary<string, PingInstancePreference>(), [], false, true);
        FieldInfo field = typeof(PlayerManager).GetField("connectedPlayersBySessionId", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ThreadSafeDictionary<SessionId, Player> connected = (ThreadSafeDictionary<SessionId, Player>)field.GetValue(playerManager)!;
        connected[player.SessionId] = player;
        return player;
    }

    private Task Send(Player from, BuddyOxygenTransfer packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    private BuddyOxygenTransfer SentTo(SessionId session) => (BuddyOxygenTransfer)packetSender.Sent.Should().ContainSingle(sent => sent.SessionId == session).Subject.Packet;

    [TestMethod]
    public async Task AStepInRangeGoesToTheReceiverWithTheGiverOfTheConnection()
    {
        // Alice sends the step and the packet claims Carl gave it
        await Send(alice, new BuddyOxygenTransfer(BuddyOxygenKind.Give, 0, carl.SessionId, bob.SessionId, "Carl", 1.25f, 0f, BuddyOxygenReason.None));

        BuddyOxygenTransfer deliver = SentTo(bob.SessionId);
        deliver.Kind.Should().Be(BuddyOxygenKind.Deliver);
        deliver.Giver.Should().Be(alice.SessionId);
        deliver.Receiver.Should().Be(bob.SessionId);
        deliver.Name.Should().Be("Alice");
        deliver.Amount.Should().Be(1.25f);
        deliver.TransferId.Should().BeGreaterThan(0);
        ledger.OpenCount.Should().Be(1);
    }

    [TestMethod]
    public async Task TheReceiversAnswerSettlesTheStepAndWhatFittedIsWhatTheGiverLost()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        long id = SentTo(bob.SessionId).TransferId;
        packetSender.Sent.Clear();

        await Send(bob, BuddyOxygenTransfer.Answer(id, 1.25f, BuddyOxygenReason.None));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Kind.Should().Be(BuddyOxygenKind.Settle);
        settle.Amount.Should().Be(1.25f);
        settle.Refund.Should().Be(0f);
        settle.Reason.Should().Be(BuddyOxygenReason.None);
        settle.Name.Should().Be("Bob");
        settle.Receiver.Should().Be(bob.SessionId);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task WhatDidNotFitGoesBackToTheGiverAndTheReasonIsAFullTank()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        long id = SentTo(bob.SessionId).TransferId;
        packetSender.Sent.Clear();

        await Send(bob, BuddyOxygenTransfer.Answer(id, 0.5f, BuddyOxygenReason.None));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Amount.Should().Be(0.5f);
        settle.Refund.Should().Be(0.75f);
        settle.Reason.Should().Be(BuddyOxygenReason.Full);
        (1.25f - settle.Refund).Should().BeApproximately(settle.Amount, 1e-5f, "what the giver lost is what the receiver got");
    }

    [TestMethod]
    public async Task AReceiverWhoCannotBreatheMoreSendsEverythingBackWithItsReason()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        long id = SentTo(bob.SessionId).TransferId;
        packetSender.Sent.Clear();

        await Send(bob, BuddyOxygenTransfer.Answer(id, 0f, BuddyOxygenReason.CannotBreatheMore));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Amount.Should().Be(0f);
        settle.Refund.Should().Be(1.25f);
        settle.Reason.Should().Be(BuddyOxygenReason.CannotBreatheMore);
    }

    [TestMethod]
    public async Task AReceiverCannotMakeAirByAnsweringWithMoreThanItWasGiven()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        long id = SentTo(bob.SessionId).TransferId;
        packetSender.Sent.Clear();

        await Send(bob, BuddyOxygenTransfer.Answer(id, 100f, BuddyOxygenReason.None));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Amount.Should().Be(1.25f);
        settle.Refund.Should().Be(0f);
    }

    [TestMethod]
    public async Task NobodyButTheReceiverCanAnswerAndAStepIsAnsweredOnce()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        long id = SentTo(bob.SessionId).TransferId;
        packetSender.Sent.Clear();

        await Send(carl, BuddyOxygenTransfer.Answer(id, 1.25f, BuddyOxygenReason.None));
        await Send(alice, BuddyOxygenTransfer.Answer(id, 1.25f, BuddyOxygenReason.None));
        packetSender.Sent.Should().BeEmpty();

        await Send(bob, BuddyOxygenTransfer.Answer(id, 1.25f, BuddyOxygenReason.None));
        packetSender.Sent.Should().ContainSingle();
        await Send(bob, BuddyOxygenTransfer.Answer(id, 1.25f, BuddyOxygenReason.None));
        packetSender.Sent.Should().ContainSingle("a second answer is ignored");
    }

    [TestMethod]
    public async Task TwoGiversCanFeedOneReceiver()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        await Send(carl, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == bob.SessionId);
        ledger.OpenCount.Should().Be(2);
    }

    [TestMethod]
    public async Task ADiverTooFarAwayGetsAllTheAirBack()
    {
        bob.Position = new NitroxVector3(2.5f + BuddyOxygenRules.RANGE_SLACK + 0.5f, -10f, 0f);

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Kind.Should().Be(BuddyOxygenKind.Settle);
        settle.Reason.Should().Be(BuddyOxygenReason.OutOfRange);
        settle.Refund.Should().Be(1.25f);
        settle.Amount.Should().Be(0f);
        settle.TransferId.Should().Be(0);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task TheServerAllowsTwoMetresMoreThanTheRangeForTheMovementOfTheDivers()
    {
        bob.Position = new NitroxVector3(2.5f + BuddyOxygenRules.RANGE_SLACK - 0.1f, -10f, 0f);

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        SentTo(bob.SessionId).Kind.Should().Be(BuddyOxygenKind.Deliver);
    }

    [TestMethod]
    public async Task TheRangeOfTheOptionIsTheOneThatCounts()
    {
        serverOptions.BuddyOxygenRange = 6f;
        bob.Position = new NitroxVector3(7f, -10f, 0f);

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        SentTo(bob.SessionId).Kind.Should().Be(BuddyOxygenKind.Deliver);
    }

    [TestMethod]
    public async Task APlayerWhoIsNotThereIsNoReceiver()
    {
        await Send(alice, BuddyOxygenTransfer.Give((ushort)99, 1.25f));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Reason.Should().Be(BuddyOxygenReason.NoReceiver);
        settle.Refund.Should().Be(1.25f);
    }

    [TestMethod]
    public async Task ADiverCannotGiveAirToThemselves()
    {
        await Send(alice, BuddyOxygenTransfer.Give(alice.SessionId, 1.25f));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Reason.Should().Be(BuddyOxygenReason.NoReceiver);
        settle.Refund.Should().Be(1.25f);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task AReceiverWhoIsOfflineIsNoReceiver()
    {
        bob.IsOnline = false;

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        SentTo(alice.SessionId).Reason.Should().Be(BuddyOxygenReason.NoReceiver);
    }

    [TestMethod]
    public async Task ARateOfZeroSwitchesTheSharingOffAndGivesTheAirBack()
    {
        serverOptions.BuddyOxygenRate = 0f;

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Reason.Should().Be(BuddyOxygenReason.Off);
        settle.Refund.Should().Be(1.25f);
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-1f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(BuddyOxygenRules.MAX_AMOUNT + 1f)]
    public async Task AStepThatCannotBeOneIsDropped(float amount)
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, amount));

        packetSender.Sent.Should().BeEmpty();
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task APlayerWhoIsNotOnlineCannotGive()
    {
        alice.IsOnline = false;

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AGiverWhoTakesAirFasterThanTheRateGetsTheExcessBack()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 6f));
        packetSender.Sent.Clear();

        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 6f));

        BuddyOxygenTransfer settle = SentTo(alice.SessionId);
        settle.Reason.Should().Be(BuddyOxygenReason.TooFast);
        settle.Refund.Should().Be(6f);
    }

    [TestMethod]
    public async Task AGiverWhoHasAsManyStepsWaitingAsTheServerAllowsGetsTheNextOneBack()
    {
        for (int i = 0; i < BuddyOxygenRules.MAX_OPEN_PER_GIVER; i++)
        {
            clock += 0.25;
            await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 0.5f));
        }
        packetSender.Sent.Clear();

        clock += 0.25;
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 0.5f));

        SentTo(alice.SessionId).Reason.Should().Be(BuddyOxygenReason.Busy);
    }

    [TestMethod]
    public async Task AStepThatNobodyAnsweredIsGivenBackWhenTheGiverGivesAgain()
    {
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));
        long stale = SentTo(bob.SessionId).TransferId;
        packetSender.Sent.Clear();

        clock += BuddyOxygenRules.STALE_SECONDS + 1;
        await Send(alice, BuddyOxygenTransfer.Give(bob.SessionId, 1.25f));

        BuddyOxygenTransfer refund = packetSender.Sent.Should().Contain(sent => sent.SessionId == alice.SessionId).Subject.Packet.Should().BeOfType<BuddyOxygenTransfer>().Subject;
        refund.Kind.Should().Be(BuddyOxygenKind.Settle);
        refund.TransferId.Should().Be(stale);
        refund.Refund.Should().Be(1.25f);
        packetSender.Sent.Should().Contain(sent => sent.SessionId == bob.SessionId, "the new step went on to the receiver");
    }

    [TestMethod]
    public async Task TheServersOwnStepsAreDroppedWhenAClientSendsThem()
    {
        await Send(alice, BuddyOxygenTransfer.Deliver(1, alice.SessionId, bob.SessionId, "Alice", 5f));
        await Send(alice, BuddyOxygenTransfer.Settle(1, alice.SessionId, bob.SessionId, "Bob", 0f, 50f, BuddyOxygenReason.None));
        await Send(alice, new BuddyOxygenTransfer(BuddyOxygenKind.Unknown, 0, default, default, "", 1f, 0f, BuddyOxygenReason.None));

        packetSender.Sent.Should().BeEmpty("a client that sent a settle would be making air for itself");
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
