using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The server's books of the buddy oxygen: the air a giver took out of their tank is accounted for exactly once. Only the receiver the step went to may answer, once; the answer cannot make air; a giver cannot
///     hold up more steps than the server allows or take air faster than the rate; the air of a step that nobody answers, or whose receiver left, goes back to the giver.
/// </summary>
[TestClass]
public class BuddyOxygenLedgerTest
{
    private static readonly SessionId GiverA = (ushort)1;
    private static readonly SessionId GiverB = (ushort)2;
    private static readonly SessionId Receiver = (ushort)3;
    private static readonly SessionId Other = (ushort)4;
    private const float RATE = 5f;

    private double now;
    private BuddyOxygenLedger ledger = null!;

    [TestInitialize]
    public void Init()
    {
        now = 100.0;
        ledger = new BuddyOxygenLedger(null, () => now);
    }

    private BuddyOxygenLedger.Entry Open(SessionId giver, float amount = 1.25f, SessionId? receiver = null)
    {
        BuddyOxygenLedger.OpenResult result = ledger.TryOpen(giver, receiver ?? Receiver, "Bob", amount, RATE, out BuddyOxygenLedger.Entry entry);
        result.Should().Be(BuddyOxygenLedger.OpenResult.Opened);
        return entry;
    }

    [TestMethod]
    public void EveryStepGetsItsOwnNumberAndIsCounted()
    {
        BuddyOxygenLedger.Entry first = Open(GiverA);
        now += 0.25;
        BuddyOxygenLedger.Entry second = Open(GiverA);

        second.TransferId.Should().BeGreaterThan(first.TransferId);
        first.TransferId.Should().BeGreaterThan(0, "0 is what a refusal carries");
        ledger.OpenCount.Should().Be(2);
        ledger.OpenCountFor(GiverA).Should().Be(2);
        ledger.OpenCountFor(GiverB).Should().Be(0);
    }

    [TestMethod]
    public void TheReceiverClosesAStepOnceWithWhatItTook()
    {
        BuddyOxygenLedger.Entry step = Open(GiverA, 1.25f);

        ledger.TryClose(step.TransferId, Receiver, 1f, out BuddyOxygenLedger.Entry closed, out float accepted).Should().BeTrue();

        closed.Giver.Should().Be(GiverA);
        closed.Amount.Should().Be(1.25f);
        accepted.Should().Be(1f);
        ledger.OpenCount.Should().Be(0);
        ledger.TryClose(step.TransferId, Receiver, 1f, out _, out _).Should().BeFalse("a step has one answer");
    }

    [TestMethod]
    public void NobodyButTheReceiverCanAnswer()
    {
        BuddyOxygenLedger.Entry step = Open(GiverA);

        ledger.TryClose(step.TransferId, Other, 1f, out _, out _).Should().BeFalse();
        ledger.TryClose(step.TransferId, GiverA, 1f, out _, out _).Should().BeFalse("the giver cannot answer for the receiver either");
        ledger.OpenCount.Should().Be(1, "the step is still waiting for the real answer");
        ledger.TryClose(step.TransferId, Receiver, 1f, out _, out _).Should().BeTrue();
    }

    [TestMethod]
    public void AStepThatWasNeverOpenedCannotBeAnswered()
    {
        ledger.TryClose(12345, Receiver, 1f, out _, out float accepted).Should().BeFalse();
        accepted.Should().Be(0f);
    }

    [TestMethod]
    [DataRow(5f, 1.25f)]
    [DataRow(-2f, 0f)]
    [DataRow(float.NaN, 0f)]
    [DataRow(float.PositiveInfinity, 1.25f)]
    [DataRow(0.5f, 0.5f)]
    public void AnAnswerCannotMakeAir(float answer, float expected)
    {
        BuddyOxygenLedger.Entry step = Open(GiverA, 1.25f);

        ledger.TryClose(step.TransferId, Receiver, answer, out _, out float accepted).Should().BeTrue();

        accepted.Should().Be(expected);
    }

    [TestMethod]
    public void AGiverHoldsUpAtMostEightStepsAndGetsASlotBackWhenOneIsAnswered()
    {
        List<BuddyOxygenLedger.Entry> steps = [];
        for (int i = 0; i < BuddyOxygenRules.MAX_OPEN_PER_GIVER; i++)
        {
            now += 0.25;
            steps.Add(Open(GiverA, 0.5f));
        }

        ledger.TryOpen(GiverA, Receiver, "Bob", 0.5f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Busy);
        ledger.TryOpen(GiverB, Receiver, "Bob", 0.5f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Opened, "another giver has slots of their own");

        ledger.TryClose(steps[0].TransferId, Receiver, 0.5f, out _, out _);
        now += 0.25;
        ledger.TryOpen(GiverA, Receiver, "Bob", 0.5f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Opened);
    }

    [TestMethod]
    public void AGiverWhoTakesAirFasterThanTheRateIsTurnedDown()
    {
        // a bucket of 7.5 for the rate 5: 6 fits, the next 6 does not
        ledger.TryOpen(GiverA, Receiver, "Bob", 6f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Opened);

        ledger.TryOpen(GiverA, Receiver, "Bob", 6f, RATE, out BuddyOxygenLedger.Entry refused).Should().Be(BuddyOxygenLedger.OpenResult.TooFast);

        refused.TransferId.Should().Be(0, "a step that is turned down is not in the books");
        ledger.OpenCount.Should().Be(1);

        now += 2.0;
        ledger.TryOpen(GiverA, Receiver, "Bob", 6f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Opened, "the bucket filled up again");
    }

    [TestMethod]
    public void TwoGiversCanFeedOneReceiverAndEachStepIsAnsweredOnItsOwn()
    {
        BuddyOxygenLedger.Entry fromA = Open(GiverA, 1.25f);
        BuddyOxygenLedger.Entry fromB = Open(GiverB, 1.25f);

        // the receiver's tank had room for 1.25 of the 2.5 that came: the step of the first giver fitted, the step of the second did not
        ledger.TryClose(fromA.TransferId, Receiver, 1.25f, out BuddyOxygenLedger.Entry closedA, out float acceptedA).Should().BeTrue();
        ledger.TryClose(fromB.TransferId, Receiver, 0f, out BuddyOxygenLedger.Entry closedB, out float acceptedB).Should().BeTrue();

        closedA.Giver.Should().Be(GiverA);
        acceptedA.Should().Be(1.25f);
        closedB.Giver.Should().Be(GiverB);
        BuddyOxygenRules.Refund(closedB.Amount, acceptedB).Should().Be(1.25f, "the giver whose step did not fit gets it all back");
        BuddyOxygenRules.Refund(closedA.Amount, acceptedA).Should().Be(0f);
    }

    [TestMethod]
    public void AStepThatNobodyAnswersForHalfAMinuteIsTakenOutOfTheBooksForGivingBack()
    {
        BuddyOxygenLedger.Entry old = Open(GiverA);
        now += BuddyOxygenRules.STALE_SECONDS - 1;
        Open(GiverB);

        ledger.TakeStale().Should().BeEmpty("nothing is old enough yet");

        now += 2;
        IReadOnlyList<BuddyOxygenLedger.Entry> stale = ledger.TakeStale();

        stale.Should().ContainSingle().Which.TransferId.Should().Be(old.TransferId);
        ledger.OpenCount.Should().Be(1);
        ledger.TryClose(old.TransferId, Receiver, 1f, out _, out _).Should().BeFalse("a late answer finds nothing");
    }

    [TestMethod]
    public void WhenTheReceiverLeavesTheirStepsAreReturnedForGivingBack()
    {
        BuddyOxygenLedger.Entry toReceiver = Open(GiverA);
        BuddyOxygenLedger.Entry toOther = Open(GiverB, 1.25f, Other);

        IReadOnlyList<BuddyOxygenLedger.Entry> toRefund = ledger.SessionEnded(Receiver);

        toRefund.Should().ContainSingle().Which.TransferId.Should().Be(toReceiver.TransferId);
        ledger.OpenCount.Should().Be(1);
        ledger.TryClose(toOther.TransferId, Other, 1f, out _, out _).Should().BeTrue("the steps of other receivers are not touched");
    }

    [TestMethod]
    public void WhenTheGiverLeavesTheirStepsAreDroppedAndNothingIsReturned()
    {
        BuddyOxygenLedger.Entry step = Open(GiverA);

        ledger.SessionEnded(GiverA).Should().BeEmpty("there is nobody left to give the air back to");

        ledger.OpenCount.Should().Be(0);
        ledger.TryClose(step.TransferId, Receiver, 1f, out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void AGiverWhoLeftStartsWithAFullBucketWhenTheyComeBack()
    {
        ledger.TryOpen(GiverA, Receiver, "Bob", 7.5f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Opened);
        ledger.TryOpen(GiverA, Receiver, "Bob", 1f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.TooFast);

        ledger.SessionEnded(GiverA);

        ledger.TryOpen(GiverA, Receiver, "Bob", 7.5f, RATE, out _).Should().Be(BuddyOxygenLedger.OpenResult.Opened);
    }

    [TestMethod]
    public async Task TheSessionCleanerEventTellsTheGiversOfAReceiverWhoLeftAndGivesAllTheirAirBack()
    {
        RecordingSender sender = new();
        BuddyOxygenLedger told = new(sender, () => now);
        told.TryOpen(GiverA, Receiver, "Bob", 1.25f, RATE, out BuddyOxygenLedger.Entry gone);
        told.TryOpen(GiverB, Other, "Carl", 1f, RATE, out _);

        await told.OnEventAsync(new ISessionCleaner.Args(new SessionManager.Session(Receiver, new IPEndPoint(IPAddress.Loopback, 1)), 1));

        (Packet packet, SessionId session) = sender.Sent.Should().ContainSingle().Subject;
        session.Should().Be(GiverA);
        BuddyOxygenTransfer settle = packet.Should().BeOfType<BuddyOxygenTransfer>().Subject;
        settle.Kind.Should().Be(BuddyOxygenKind.Settle);
        settle.TransferId.Should().Be(gone.TransferId);
        settle.Amount.Should().Be(0f);
        settle.Refund.Should().Be(1.25f);
        settle.Reason.Should().Be(BuddyOxygenReason.NoReceiver);
        told.OpenCount.Should().Be(1);
    }

    private sealed class RecordingSender : IPacketSender
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
