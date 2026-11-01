using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The order of an energy transfer between two vehicles (the Cyclops power share): taken out first, added second, the rest back; so that energy is never made. And the small pieces around it: the
///     requests in flight, the marks of the on-screen lines, the distance to the hull.
/// </summary>
[TestClass]
public class PowerTransfersTest
{
    private readonly NitroxId cyclopsId = new();
    private readonly NitroxId vehicleId = new();

    private sealed class End(float energy, float capacity, float floor = 0f) : IPowerEndpoint
    {
        public float Energy { get; set; } = energy;
        public int Takes { get; private set; }
        public int Gives { get; private set; }

        public float Take(float offered)
        {
            Takes++;
            float taken = Math.Min(offered, Math.Max(0f, Energy - floor));
            Energy -= taken;
            return taken;
        }

        public float Give(float amount)
        {
            Gives++;
            float stored = Math.Min(amount, capacity - Energy);
            Energy += stored;
            return stored;
        }
    }

    /// <summary>An end that lies about what it did, to check that the arithmetic does not trust it.</summary>
    private sealed class LyingEnd(float takes, float stores) : IPowerEndpoint
    {
        public float Take(float offered) => takes;
        public float Give(float amount) => stores;
    }

    // ---- one client simulates both ends

    [TestMethod]
    public void TheGiverLosesWhatTheReceiverGains()
    {
        End giver = new(100f, 600f);
        End receiver = new(50f, 200f);

        float stored = PowerTransfers.Move(giver, receiver, 2f);

        stored.Should().Be(2f);
        giver.Energy.Should().Be(98f);
        receiver.Energy.Should().Be(52f);
    }

    [TestMethod]
    public void TheGiverIsAskedFirstAndTheReceiverNeverGetsMoreThanTheGiverGave()
    {
        End giver = new(1f, 600f);
        End receiver = new(0f, 200f);

        float stored = PowerTransfers.Move(giver, receiver, 5f);

        stored.Should().Be(1f);
        receiver.Energy.Should().Be(1f);
        giver.Energy.Should().Be(0f);
    }

    [TestMethod]
    public void WhatTheReceiverCannotUseGoesBackToTheGiver()
    {
        End giver = new(100f, 600f);
        End receiver = new(199.5f, 200f);

        float stored = PowerTransfers.Move(giver, receiver, 2f);

        stored.Should().Be(0.5f);
        receiver.Energy.Should().Be(200f);
        giver.Energy.Should().Be(99.5f, "only the 0.5 that was used left the giver");
    }

    [TestMethod]
    public void AGiverThatGivesNothingLeavesTheReceiverAlone()
    {
        End giver = new(10f, 600f, floor: 10f);
        End receiver = new(50f, 200f);

        PowerTransfers.Move(giver, receiver, 2f).Should().Be(0f);

        receiver.Gives.Should().Be(0, "the receiver is not even asked");
        receiver.Energy.Should().Be(50f);
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-2f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(1000f)]
    public void AnAmountThatIsNoValidRequestMovesNothing(float amount)
    {
        End giver = new(100f, 600f);
        End receiver = new(50f, 200f);

        PowerTransfers.Move(giver, receiver, amount).Should().Be(0f);

        giver.Takes.Should().Be(0);
        giver.Energy.Should().Be(100f);
    }

    [TestMethod]
    public void AGiverThatTakesMoreThanAskedIsClampedToTheRequest()
    {
        PowerTransfers.Move(new LyingEnd(takes: 50f, stores: 50f), new LyingEnd(0f, stores: 50f), 2f).Should().Be(2f, "a giver is believed only up to what it was asked for");
    }

    [TestMethod]
    public void NegativeOrNanReportsCountAsNothing()
    {
        PowerTransfers.Move(new LyingEnd(takes: -4f, stores: 1f), new LyingEnd(0f, 1f), 2f).Should().Be(0f);
        PowerTransfers.Move(new LyingEnd(takes: float.NaN, stores: 1f), new LyingEnd(0f, 1f), 2f).Should().Be(0f);
        PowerTransfers.Move(new LyingEnd(takes: 2f, stores: 0f), new LyingEnd(0f, stores: -1f), 2f).Should().Be(0f);
    }

    [TestMethod]
    public void TheSumOfAllTheEnergyNeverGrowsOverAThousandTransfers()
    {
        End giver = new(300f, 600f, floor: 30f);
        End receiver = new(0f, 200f);
        float total = giver.Energy + receiver.Energy;
        Random random = new(7);

        for (int i = 0; i < 1000; i++)
        {
            // Sometimes the vehicle is used up in between (energy leaves it), sometimes the Cyclops is charged by its solar charger
            if (i % 7 == 0)
            {
                float used = Math.Min(receiver.Energy, (float)random.NextDouble() * 3f);
                receiver.Energy -= used;
                total -= used;
            }
            if (i % 11 == 0)
            {
                float charged = Math.Min(600f - giver.Energy, 1.25f);
                giver.Energy += charged;
                total += charged;
            }
            PowerTransfers.Move(giver, receiver, (float)random.NextDouble() * 5f + 0.05f);

            (giver.Energy + receiver.Energy).Should().BeApproximately(total, 0.01f);
            giver.Energy.Should().BeGreaterThanOrEqualTo(29.99f, "the floor of the giver holds");
            receiver.Energy.Should().BeLessThanOrEqualTo(200.001f);
        }
    }

    // ---- two clients

    private PowerTransfer Offer(float amount) => PowerTransfer.Offer(77, cyclopsId, vehicleId, amount, PowerTransferMode.Forward);

    [TestMethod]
    public void TheGrantCarriesTheNumberOfTheTransferAndWhatWasTakenOut()
    {
        End giver = new(100f, 600f);

        PowerTransfer grant = PowerTransfers.AnswerOffer(Offer(2f), giver, 100f);

        grant.Kind.Should().Be(PowerTransferKind.Grant);
        grant.TransferId.Should().Be(77);
        grant.Amount.Should().Be(2f);
        grant.GiverId.Should().Be(cyclopsId);
        grant.ReceiverId.Should().Be(vehicleId);
        grant.Mode.Should().Be(PowerTransferMode.Forward);
        giver.Energy.Should().Be(98f, "the energy is taken out of the giver before the grant is sent");
    }

    [TestMethod]
    public void TheGrantIsNeverMoreThanTheGiversRulesAllowOneRequestToAsk()
    {
        End giver = new(100f, 600f);

        PowerTransfer grant = PowerTransfers.AnswerOffer(Offer(50f), giver, 3f);

        grant.Amount.Should().Be(3f, "a request for 50 when the rate allows 3 is not believed");
        giver.Energy.Should().Be(97f);
    }

    [TestMethod]
    public void AGiverThatIsNotThereStillAnswersWithAGrantOfNothingSoThatTheTransferEnds()
    {
        PowerTransfer grant = PowerTransfers.AnswerOffer(Offer(2f), null, 100f);

        grant.Kind.Should().Be(PowerTransferKind.Grant);
        grant.Amount.Should().Be(0f);
        grant.TransferId.Should().Be(77);
    }

    [TestMethod]
    public void AnOfferOfNoAmountTakesNothingOut()
    {
        End giver = new(100f, 600f);

        PowerTransfers.AnswerOffer(Offer(float.NaN), giver, 100f).Amount.Should().Be(0f);
        PowerTransfers.AnswerOffer(Offer(-4f), giver, 100f).Amount.Should().Be(0f);

        giver.Takes.Should().Be(0);
    }

    [TestMethod]
    public void ADeliveryIsAddedToTheReceiverAndNothingComesBackWhenItAllFits()
    {
        End receiver = new(50f, 200f);
        PowerTransfer delivery = PowerTransfer.Delivery(77, cyclopsId, vehicleId, 2f, PowerTransferMode.Forward, PowerTransferOutcome.Granted);

        PowerTransfer giveBack = PowerTransfers.ApplyDelivery(delivery, receiver, out float stored);

        stored.Should().Be(2f);
        giveBack.Should().BeNull();
        receiver.Energy.Should().Be(52f);
    }

    [TestMethod]
    public void WhatADeliveryCouldNotFillIntoTheReceiverIsReturnedToTheGiverAsAPacket()
    {
        End receiver = new(199f, 200f);
        PowerTransfer delivery = PowerTransfer.Delivery(77, cyclopsId, vehicleId, 2f, PowerTransferMode.Forward, PowerTransferOutcome.Granted);

        PowerTransfer giveBack = PowerTransfers.ApplyDelivery(delivery, receiver, out float stored);

        stored.Should().Be(1f);
        giveBack.Should().NotBeNull();
        giveBack.Kind.Should().Be(PowerTransferKind.Return);
        giveBack.Amount.Should().Be(1f);
        giveBack.GiverId.Should().Be(cyclopsId);
        giveBack.ReceiverId.Should().Be(vehicleId);
    }

    [TestMethod]
    public void ADeliveryThatIsNotAGrantAddsNothing()
    {
        End receiver = new(50f, 200f);

        foreach (PowerTransferOutcome outcome in new[] { PowerTransferOutcome.Nothing, PowerTransferOutcome.NoGiver, PowerTransferOutcome.Invalid, PowerTransferOutcome.Unknown })
        {
            PowerTransfer delivery = PowerTransfer.Delivery(77, cyclopsId, vehicleId, 2f, PowerTransferMode.Forward, outcome);

            PowerTransfers.ApplyDelivery(delivery, receiver, out float stored).Should().BeNull();
            stored.Should().Be(0f);
        }
        receiver.Energy.Should().Be(50f);
    }

    [TestMethod]
    public void ADeliveryForAReceiverThatIsNotHereAddsNothing()
    {
        PowerTransfer delivery = PowerTransfer.Delivery(77, cyclopsId, vehicleId, 2f, PowerTransferMode.Forward, PowerTransferOutcome.Granted);

        PowerTransfers.ApplyDelivery(delivery, null, out float stored).Should().BeNull();

        stored.Should().Be(0f);
    }

    [TestMethod]
    public void ADeliveryOfMoreThanTheCapOrOfNoAmountAddsNothing()
    {
        End receiver = new(50f, 200f);

        PowerTransfers.ApplyDelivery(PowerTransfer.Delivery(77, cyclopsId, vehicleId, PowerTransferRules.MAX_AMOUNT + 1f, PowerTransferMode.Forward, PowerTransferOutcome.Granted), receiver, out _);
        PowerTransfers.ApplyDelivery(PowerTransfer.Delivery(77, cyclopsId, vehicleId, float.NaN, PowerTransferMode.Forward, PowerTransferOutcome.Granted), receiver, out _);

        receiver.Energy.Should().Be(50f);
    }

    [TestMethod]
    public void ARefundPutsTheEnergyBackIntoTheGiver()
    {
        End giver = new(90f, 600f);

        float taken = PowerTransfers.ApplyRefund(PowerTransfer.Refund(cyclopsId, vehicleId, 4f, PowerTransferMode.Forward), giver);

        taken.Should().Be(4f);
        giver.Energy.Should().Be(94f);
    }

    [TestMethod]
    public void ARefundIntoAFullGiverOrNoGiverIsLostNotMultiplied()
    {
        End giver = new(599f, 600f);

        PowerTransfers.ApplyRefund(PowerTransfer.Refund(cyclopsId, vehicleId, 4f, PowerTransferMode.Forward), giver).Should().Be(1f);
        PowerTransfers.ApplyRefund(PowerTransfer.Refund(cyclopsId, vehicleId, 4f, PowerTransferMode.Forward), null).Should().Be(0f);
        giver.Energy.Should().Be(600f);
    }

    // ---- the requests in flight

    [TestMethod]
    public void AnotherRequestForTheSamePairWaitsForTheAnswer()
    {
        PowerTransferTracker tracker = new();

        tracker.TryBegin(cyclopsId, vehicleId, 10.0).Should().BeTrue();
        tracker.TryBegin(cyclopsId, vehicleId, 11.0).Should().BeFalse("the last request has not been answered");
        tracker.IsInFlight(cyclopsId, vehicleId, 11.0).Should().BeTrue();
    }

    [TestMethod]
    public void AnAnswerFreesThePairForTheNextRequest()
    {
        PowerTransferTracker tracker = new();
        tracker.TryBegin(cyclopsId, vehicleId, 10.0);

        tracker.Complete(cyclopsId, vehicleId);

        tracker.TryBegin(cyclopsId, vehicleId, 10.5).Should().BeTrue();
    }

    [TestMethod]
    public void ARequestThatIsNeverAnsweredIsAskedAgainAfterTheTimeout()
    {
        PowerTransferTracker tracker = new();
        tracker.TryBegin(cyclopsId, vehicleId, 10.0);

        tracker.TryBegin(cyclopsId, vehicleId, 10.0 + PowerTransferRules.ANSWER_TIMEOUT_SECONDS - 0.01).Should().BeFalse();
        tracker.TryBegin(cyclopsId, vehicleId, 10.0 + PowerTransferRules.ANSWER_TIMEOUT_SECONDS + 0.01).Should().BeTrue("a lost packet or a paused giver must not stop the share for good");
    }

    [TestMethod]
    public void DifferentPairsAreIndependentAndTheDirectionMatters()
    {
        PowerTransferTracker tracker = new();
        NitroxId otherVehicle = new();

        tracker.TryBegin(cyclopsId, vehicleId, 10.0).Should().BeTrue();

        tracker.TryBegin(cyclopsId, otherVehicle, 10.0).Should().BeTrue();
        tracker.TryBegin(vehicleId, cyclopsId, 10.0).Should().BeTrue("the emergency draw on the same two vehicles is another pair");
    }

    [TestMethod]
    public void TheTrackerIsEmptiedWhenTheSessionEnds()
    {
        PowerTransferTracker tracker = new();
        tracker.TryBegin(cyclopsId, vehicleId, 10.0);

        tracker.Clear();

        tracker.Count.Should().Be(0);
        tracker.TryBegin(cyclopsId, vehicleId, 10.1).Should().BeTrue();
    }

    // ---- the marks of the on-screen lines and of the paused docking charge

    [TestMethod]
    public void AMarkIsOnForItsTimeAndThenGone()
    {
        TimedMarks marks = new();
        marks.Mark(vehicleId, 10.0, 2.5, "Seamoth");

        marks.IsMarked(vehicleId, 10.0).Should().BeTrue();
        marks.TryGet(vehicleId, 12.4, out string detail).Should().BeTrue();
        detail.Should().Be("Seamoth");
        marks.IsMarked(vehicleId, 12.5).Should().BeFalse("at its end time the mark is off");
        marks.Count.Should().Be(0, "an ended mark is removed when it is looked at");
    }

    [TestMethod]
    public void ALaterMarkExtendsTheEarlierOne()
    {
        TimedMarks marks = new();
        marks.Mark(vehicleId, 10.0, 2.5);
        marks.Mark(vehicleId, 11.0, 2.5);

        marks.IsMarked(vehicleId, 13.0).Should().BeTrue();
        marks.IsMarked(vehicleId, 13.6).Should().BeFalse();
    }

    [TestMethod]
    public void AnUnmarkedIdAndANullIdAreNotMarked()
    {
        TimedMarks marks = new();
        marks.Mark(vehicleId, 10.0, 2.5);

        marks.IsMarked(cyclopsId, 10.0).Should().BeFalse();
        marks.IsMarked(null!, 10.0).Should().BeFalse();
        marks.Clear();
        marks.IsMarked(vehicleId, 10.0).Should().BeFalse();
    }

    // ---- the distance to the hull

    [TestMethod]
    public void InsideTheBoxTheDistanceIsZeroAndOutsideItIsTheDistanceToTheNearestFace()
    {
        PowerShareGeometry.DistanceToBox(0f, 0f, 0f, 0f, 0f, 0f, 3f, 4f, 30f).Should().Be(0f);
        PowerShareGeometry.DistanceToBox(2f, -3f, 29f, 0f, 0f, 0f, 3f, 4f, 30f).Should().Be(0f);
        PowerShareGeometry.DistanceToBox(13f, 0f, 0f, 0f, 0f, 0f, 3f, 4f, 30f).Should().Be(10f);
        PowerShareGeometry.DistanceToBox(0f, 0f, -40f, 0f, 0f, 0f, 3f, 4f, 30f).Should().Be(10f);
        PowerShareGeometry.DistanceToBox(6f, 8f, 0f, 0f, 0f, 0f, 3f, 4f, 30f).Should().Be(5f, "a corner: 3 along x, 4 along y");
    }

    [TestMethod]
    public void TheBoxMayBeOffCenter()
    {
        PowerShareGeometry.DistanceToBox(20f, 0f, 0f, 10f, 0f, 0f, 5f, 5f, 5f).Should().Be(5f);
        PowerShareGeometry.DistanceToBox(4f, 0f, 0f, 10f, 0f, 0f, 5f, 5f, 5f).Should().Be(1f);
    }

    [TestMethod]
    public void WithoutAHullTheMiddleIsUsedLessTheRadiusOfAHull()
    {
        PowerShareGeometry.DistanceToHullFromCenter(100f).Should().Be(100f - PowerShareGeometry.FALLBACK_HULL_RADIUS);
        PowerShareGeometry.DistanceToHullFromCenter(5f).Should().Be(0f);
    }

    [TestMethod]
    public void TheGiverIsMoreGenerousThanTheVehicleSoAVehicleThatWasInRangeIsNotRefused()
    {
        PowerShareGeometry.InRange(30f, 30f, 0f).Should().BeTrue();
        PowerShareGeometry.InRange(30.1f, 30f, 0f).Should().BeFalse();
        PowerShareGeometry.InRange(30.1f, 30f, PowerShareGeometry.RANGE_TOLERANCE).Should().BeTrue();
        PowerShareGeometry.InRange(40f, 30f, PowerShareGeometry.RANGE_TOLERANCE).Should().BeFalse();
    }
}
