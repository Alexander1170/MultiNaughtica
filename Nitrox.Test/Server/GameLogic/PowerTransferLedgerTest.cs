using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The server's books of the power transfers between vehicles (the Cyclops power share): the energy a giver took out is delivered at most once, only on the word of the player the offer went to,
///     or given back when it is too late; never both and never twice.
/// </summary>
[TestClass]
public class PowerTransferLedgerTest
{
    private static readonly SessionId Requester = (ushort)1;
    private static readonly SessionId Giver = (ushort)2;
    private static readonly SessionId Other = (ushort)3;

    private double now;
    private PowerTransferLedger ledger = null!;
    private readonly NitroxId cyclopsId = new();
    private readonly NitroxId seamothId = new();

    [TestInitialize]
    public void Init()
    {
        now = 100.0;
        ledger = new PowerTransferLedger(() => now);
    }

    private PowerTransferLedger.Entry OpenForward(float amount = 2f) => ledger.Open(Requester, Giver, cyclopsId, seamothId, amount, PowerTransferMode.Forward);

    [TestMethod]
    public void EveryTransferGetsItsOwnNumberAndIsCounted()
    {
        PowerTransferLedger.Entry first = OpenForward();
        PowerTransferLedger.Entry second = OpenForward();

        second.TransferId.Should().BeGreaterThan(first.TransferId);
        first.TransferId.Should().BeGreaterThan(0, "0 is what a request carries before the server numbered it");
        ledger.OpenCount.Should().Be(2);
    }

    [TestMethod]
    public void TheEntryRemembersWhoAskedWhoWasAskedForWhatAndWhen()
    {
        PowerTransferLedger.Entry entry = OpenForward(3.5f);

        entry.Requester.Should().Be(Requester);
        entry.GiverHolder.Should().Be(Giver);
        entry.GiverId.Should().Be(cyclopsId);
        entry.ReceiverId.Should().Be(seamothId);
        entry.Amount.Should().Be(3.5f);
        entry.Mode.Should().Be(PowerTransferMode.Forward);
        entry.OpenedAt.Should().Be(100.0);
    }

    [TestMethod]
    public void TheGrantOfThePlayerTheOfferWentToIsDeliveredOnce()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);

        PowerTransferLedger.CloseResult result = ledger.Close(entry.TransferId, Giver, 1.5f, out PowerTransferLedger.Entry closed, out float amount);

        result.Should().Be(PowerTransferLedger.CloseResult.Delivered);
        amount.Should().Be(1.5f);
        closed.ReceiverId.Should().Be(seamothId);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public void ASecondGrantForTheSameTransferIsNotDeliveredAgain()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        ledger.Close(entry.TransferId, Giver, 2f, out _, out _);

        PowerTransferLedger.CloseResult second = ledger.Close(entry.TransferId, Giver, 2f, out _, out float amount);

        second.Should().Be(PowerTransferLedger.CloseResult.Unknown, "no free energy: a replayed grant has nothing to deliver");
        amount.Should().Be(0f);
    }

    [TestMethod]
    public void AGrantFromAnotherPlayerIsIgnoredAndTheRealGiverCanStillAnswer()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);

        ledger.Close(entry.TransferId, Other, 2f, out _, out float forged).Should().Be(PowerTransferLedger.CloseResult.Unknown);
        ledger.Close(entry.TransferId, Requester, 2f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Unknown, "not even the requester may answer for the giver");

        forged.Should().Be(0f);
        ledger.OpenCount.Should().Be(1);
        ledger.Close(entry.TransferId, Giver, 2f, out _, out float amount).Should().Be(PowerTransferLedger.CloseResult.Delivered);
        amount.Should().Be(2f);
    }

    [TestMethod]
    public void AGrantForANumberThatWasNeverGivenOutIsIgnored()
    {
        ledger.Close(99, Giver, 5f, out _, out float amount).Should().Be(PowerTransferLedger.CloseResult.Unknown);

        amount.Should().Be(0f);
    }

    [TestMethod]
    public void AGiverCannotGrantMoreThanWasOffered()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);

        ledger.Close(entry.TransferId, Giver, 50f, out _, out float amount);

        amount.Should().Be(2f, "a giver that grants more than it was asked for would make energy");
    }

    [TestMethod]
    [DataRow(-3f)]
    [DataRow(0f)]
    [DataRow(float.NaN)]
    public void ANegativeZeroOrGarbledGrantDeliversNothingButStillEndsTheTransfer(float granted)
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);

        ledger.Close(entry.TransferId, Giver, granted, out _, out float amount).Should().Be(PowerTransferLedger.CloseResult.Delivered);

        amount.Should().Be(0f);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public void AGrantThatComesTooLateIsRefundedOnceToTheGiver()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + 0.5;

        PowerTransferLedger.CloseResult late = ledger.Close(entry.TransferId, Giver, 2f, out PowerTransferLedger.Entry closed, out float amount);
        PowerTransferLedger.CloseResult again = ledger.Close(entry.TransferId, Giver, 2f, out _, out float second);

        late.Should().Be(PowerTransferLedger.CloseResult.Late);
        amount.Should().Be(2f);
        closed.GiverHolder.Should().Be(Giver);
        again.Should().Be(PowerTransferLedger.CloseResult.Unknown);
        second.Should().Be(0f);
    }

    [TestMethod]
    public void ALateGrantOfAnotherPlayerIsIgnored()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + 0.5;

        ledger.Close(entry.TransferId, Other, 2f, out _, out float amount).Should().Be(PowerTransferLedger.CloseResult.Unknown);

        amount.Should().Be(0f);
    }

    [TestMethod]
    public void AGrantJustInsideTheExpiryIsStillDelivered()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS - 0.1;

        ledger.Close(entry.TransferId, Giver, 2f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Delivered);
    }

    [TestMethod]
    public void AnExpiredTransferIsForgottenAfterTheMemoryTime()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + PowerTransferRules.LATE_GRANT_MEMORY_SECONDS + 1;

        ledger.Open(Requester, Giver, cyclopsId, seamothId, 1f, PowerTransferMode.Forward);

        ledger.ExpiredCount.Should().Be(0);
        ledger.Close(entry.TransferId, Giver, 2f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Unknown, "so a made-up late grant cannot refund anything");
    }

    [TestMethod]
    public void WhenTheGiversPlayerLeavesItsTransfersAreDroppedAndReturned()
    {
        PowerTransferLedger.Entry gone = OpenForward(2f);
        PowerTransferLedger.Entry other = ledger.Open(Requester, Other, cyclopsId, seamothId, 1f, PowerTransferMode.Forward);

        IReadOnlyList<PowerTransferLedger.Entry> dropped = ledger.SessionEnded(Giver);

        dropped.Should().ContainSingle().Which.TransferId.Should().Be(gone.TransferId);
        ledger.Close(gone.TransferId, Giver, 2f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Unknown);
        ledger.Close(other.TransferId, Other, 1f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Delivered, "the transfer of another giver is untouched");
    }

    [TestMethod]
    public void TheOpenTransfersOfAPlayerAreCountedAndLeaveWhenTheyAreClosedOrExpire()
    {
        PowerTransferLedger.Entry first = OpenForward();
        OpenForward();
        ledger.Open(Other, Giver, cyclopsId, seamothId, 1f, PowerTransferMode.Forward);

        ledger.OpenCountFor(Requester).Should().Be(2);
        ledger.OpenCountFor(Other).Should().Be(1);

        ledger.Close(first.TransferId, Giver, 1f, out _, out _);
        ledger.OpenCountFor(Requester).Should().Be(1);

        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + 1;
        ledger.OpenCountFor(Requester).Should().Be(0, "an expired transfer is not open any more");
    }

    [TestMethod]
    public async Task WhenTheGiversPlayerLeavesTheRequestersAreToldAtOnce()
    {
        RecordingSender sender = new();
        PowerTransferLedger told = new(sender, () => now);
        PowerTransferLedger.Entry gone = told.Open(Requester, Giver, cyclopsId, seamothId, 2f, PowerTransferMode.Forward);
        told.Open(Requester, Other, cyclopsId, seamothId, 1f, PowerTransferMode.Forward);

        await told.OnEventAsync(new ISessionCleaner.Args(new SessionManager.Session(Giver, new IPEndPoint(IPAddress.Loopback, 1)), 1));

        (Packet packet, SessionId session) = sender.Sent.Should().ContainSingle().Subject;
        session.Should().Be(Requester);
        PowerTransfer answer = packet.Should().BeOfType<PowerTransfer>().Subject;
        answer.Kind.Should().Be(PowerTransferKind.Delivery);
        answer.Outcome.Should().Be(PowerTransferOutcome.NoGiver);
        answer.TransferId.Should().Be(gone.TransferId);
        answer.Amount.Should().Be(0f);
    }

    private sealed class RecordingSender : IPacketSender
    {
        public List<(Packet Packet, SessionId SessionId)> Sent { get; } = [];

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add((packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => throw new AssertFailedException("Not expected");

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => throw new AssertFailedException("Not expected");
    }

    // ---- the return of what a delivery could not use

    /// <summary>A transfer that was granted and delivered to <paramref name="to" />.</summary>
    private PowerTransferLedger.Entry Delivered(float amount, SessionId to)
    {
        PowerTransferLedger.Entry entry = OpenForward(amount);
        ledger.Close(entry.TransferId, Giver, amount, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Delivered);
        ledger.RecordDelivery(entry, to, amount);
        return entry;
    }

    [TestMethod]
    public void ADeliveryMayBeReturnedOnceByThePlayerItWentTo()
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);

        ledger.TryReturn(entry.TransferId, Requester, 4f, out PowerTransferLedger.Entry returned, out float amount).Should().BeTrue();

        amount.Should().Be(4f);
        returned.GiverId.Should().Be(cyclopsId);
        returned.ReceiverId.Should().Be(seamothId);
        returned.Mode.Should().Be(PowerTransferMode.Forward);
        ledger.TryReturn(entry.TransferId, Requester, 1f, out _, out float second).Should().BeFalse("one return per delivery");
        second.Should().Be(0f);
        ledger.DeliveredCount.Should().Be(0);
    }

    [TestMethod]
    public void AReturnOfAnotherPlayerIsIgnoredAndLeavesTheReturnToTheRightOne()
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);

        ledger.TryReturn(entry.TransferId, Other, 4f, out _, out float amount).Should().BeFalse();
        ledger.TryReturn(entry.TransferId, Giver, 4f, out _, out _).Should().BeFalse("the giver's player was not delivered to either");

        amount.Should().Be(0f);
        ledger.TryReturn(entry.TransferId, Requester, 4f, out _, out _).Should().BeTrue();
    }

    [TestMethod]
    public void AReturnIsNeverMoreThanWasDelivered()
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);

        ledger.TryReturn(entry.TransferId, Requester, 50f, out _, out float amount).Should().BeTrue();

        amount.Should().Be(6f, "a client that returns more than it was given would make energy");
    }

    [TestMethod]
    public void AReturnForANumberThatWasNeverDeliveredIsIgnored()
    {
        PowerTransferLedger.Entry open = OpenForward(2f);

        ledger.TryReturn(open.TransferId, Requester, 1f, out _, out _).Should().BeFalse("a transfer that is still open has delivered nothing");
        ledger.TryReturn(0, Requester, 1f, out _, out _).Should().BeFalse();
        ledger.TryReturn(987654, Requester, 1f, out _, out _).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-2f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(PowerTransferRules.MAX_AMOUNT + 1f)]
    public void AGarbledReturnIsIgnoredAndDoesNotUseUpTheDeliverysReturn(float garbled)
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);

        ledger.TryReturn(entry.TransferId, Requester, garbled, out _, out float amount).Should().BeFalse();

        amount.Should().Be(0f);
        ledger.TryReturn(entry.TransferId, Requester, 3f, out _, out float real).Should().BeTrue();
        real.Should().Be(3f);
    }

    [TestMethod]
    public void ADeliveryIsForgottenAfterTheMemoryTime()
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);
        now += PowerTransferRules.LATE_GRANT_MEMORY_SECONDS + 1;

        ledger.TryReturn(entry.TransferId, Requester, 3f, out _, out _).Should().BeFalse();

        ledger.DeliveredCount.Should().Be(0);
    }

    [TestMethod]
    public void ADeliveryIsStillRememberedJustInsideTheMemoryTime()
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);
        now += PowerTransferRules.LATE_GRANT_MEMORY_SECONDS - 1;

        ledger.TryReturn(entry.TransferId, Requester, 3f, out _, out _).Should().BeTrue();
    }

    [TestMethod]
    public void WhenThePlayerWhoWasDeliveredToLeavesItsReturnIsGone()
    {
        PowerTransferLedger.Entry gone = Delivered(6f, Requester);
        PowerTransferLedger.Entry stays = Delivered(6f, Other);

        ledger.SessionEnded(Requester);

        ledger.TryReturn(gone.TransferId, Requester, 3f, out _, out _).Should().BeFalse();
        ledger.TryReturn(stays.TransferId, Other, 3f, out _, out _).Should().BeTrue("the delivery to another player is untouched");
    }

    [TestMethod]
    public void WhenTheGiversPlayerLeavesTheDeliveriesStayBecauseTheRefundGoesToWhoSimulatesTheGiverNow()
    {
        PowerTransferLedger.Entry entry = Delivered(6f, Requester);

        ledger.SessionEnded(Giver);

        ledger.TryReturn(entry.TransferId, Requester, 3f, out _, out _).Should().BeTrue();
    }

    [TestMethod]
    public void WhenTheRequestersPlayerLeavesTheTransferStaysBecauseTheDeliveryGoesToWhoSimulatesTheReceiverNow()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);

        ledger.SessionEnded(Requester).Should().BeEmpty();

        ledger.Close(entry.TransferId, Giver, 2f, out _, out float amount).Should().Be(PowerTransferLedger.CloseResult.Delivered);
        amount.Should().Be(2f);
    }

    [TestMethod]
    public void TheExpiredTransfersOfALeavingGiverAreForgottenToo()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + 1;
        ledger.Open(Requester, Other, cyclopsId, seamothId, 1f, PowerTransferMode.Forward);

        ledger.SessionEnded(Giver);

        ledger.ExpiredCount.Should().Be(0);
        ledger.Close(entry.TransferId, Giver, 2f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Unknown);
    }

    [TestMethod]
    public async Task TheSessionCleanerEventDropsTheTransfersOfTheLeavingSession()
    {
        PowerTransferLedger.Entry entry = OpenForward(2f);
        ISessionCleaner.Args args = new(new SessionManager.Session(Giver, new IPEndPoint(IPAddress.Loopback, 1)), 1);

        await ledger.OnEventAsync(args);

        ledger.OpenCount.Should().Be(0);
        ledger.Close(entry.TransferId, Giver, 2f, out _, out _).Should().Be(PowerTransferLedger.CloseResult.Unknown);
    }

    [TestMethod]
    public async Task TheLedgerIsSafeToUseFromManyThreadsAndNothingIsDeliveredTwice()
    {
        PowerTransferLedger.Entry[] entries = Enumerable.Range(0, 200).Select(index => OpenForward(1f)).ToArray();
        int delivered = 0;

        // Every transfer is answered by four threads at the same time: exactly one of them gets the delivery
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            foreach (PowerTransferLedger.Entry entry in entries)
            {
                if (ledger.Close(entry.TransferId, Giver, 1f, out _, out _) == PowerTransferLedger.CloseResult.Delivered)
                {
                    Interlocked.Increment(ref delivered);
                }
            }
        })));

        delivered.Should().Be(200);
        ledger.OpenCount.Should().Be(0);
    }
}
