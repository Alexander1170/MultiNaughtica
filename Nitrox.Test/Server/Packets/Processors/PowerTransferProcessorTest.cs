using System.Collections.Generic;
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
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The Cyclops power share between two players (feature group 2): the server routes the request of the simulator of the receiver to the simulator of the giver and the grant back, and the energy is
///     conserved. The tests run the real server processor and ledger against two fake clients that follow the same steps as the game's client (<see cref="PowerTransfers" />), over vehicles that hold
///     plain numbers, and check the sum of all the energy after every story: it only ever goes down by what was lost on the way (a lock that moved, a packet that never came), never up.
/// </summary>
[TestClass]
public class PowerTransferProcessorTest
{
    private const float START_CYCLOPS = 100f;
    private const float START_SEAMOTH = 50f;
    private const float SEAMOTH_CAPACITY = 200f;

    private double now;
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry registry = null!;
    private PowerTransferLedger ledger = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private PowerTransferProcessor processor = null!;

    private Player playerA = null!;
    private Player playerB = null!;
    private Player playerC = null!;
    private FakeEnd cyclops = null!;
    private FakeEnd seamoth = null!;
    private VehicleEntity cyclopsEntity = null!;
    private VehicleEntity seamothEntity = null!;
    private readonly Dictionary<SessionId, Player> playersBySession = [];

    private NitroxId CyclopsId => cyclopsEntity.Id;
    private NitroxId SeamothId => seamothEntity.Id;
    private float Total => cyclops.Energy + seamoth.Energy;

    [TestInitialize]
    public void Init()
    {
        now = 10.0;
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        ledger = new PowerTransferLedger(() => now);
        serverOptions = new SubnauticaServerOptions();
        processor = new PowerTransferProcessor(ledger, ownership, registry, Options.Create(serverOptions), NullLogger<PowerTransferProcessor>.Instance);

        playerA = CreatePlayer(1);
        playerB = CreatePlayer(2);
        playerC = CreatePlayer(3);
        foreach (Player player in new[] { playerA, playerB, playerC })
        {
            playersBySession[player.SessionId] = player;
        }

        cyclopsEntity = AddVehicle("Cyclops");
        seamothEntity = AddVehicle("Seamoth");
        cyclops = new FakeEnd(START_CYCLOPS, 600f, floor: 30f);
        seamoth = new FakeEnd(START_SEAMOTH, SEAMOTH_CAPACITY, floor: 0f);
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        registry.AddOrUpdate(entity);
        return entity;
    }

    private void Hold(NitroxId id, Player player, SimulationLockType type = SimulationLockType.TRANSIENT) => ownership.TryToAcquire(id, player, type).Should().BeTrue();

    // ---- the clients

    private FakeEnd EndOf(NitroxId id) => id.Equals(CyclopsId) ? cyclops : seamoth;

    private bool Holds(Player player, NitroxId id) => ownership.GetPlayerForLock(id) == player;

    /// <summary>What the game's client does with a packet of the server (<c>PowerShare.OnOffer</c>, <c>OnDelivery</c> and <c>OnRefund</c>, with the rules of <see cref="PowerTransfers" />).</summary>
    private async Task ClientHandles(Player me, PowerTransfer packet)
    {
        switch (packet.Kind)
        {
            case PowerTransferKind.Offer:
                PowerTransfer grant = PowerTransfers.AnswerOffer(packet, Holds(me, packet.GiverId) ? EndOf(packet.GiverId) : null, 100f);
                await ToServer(me, grant);
                break;
            case PowerTransferKind.Delivery:
                if (Holds(me, packet.ReceiverId))
                {
                    PowerTransfer giveBack = PowerTransfers.ApplyDelivery(packet, EndOf(packet.ReceiverId), out _);
                    if (giveBack != null)
                    {
                        await ToServer(me, giveBack);
                    }
                }
                break;
            case PowerTransferKind.Refund:
                if (Holds(me, packet.GiverId))
                {
                    PowerTransfers.ApplyRefund(packet, EndOf(packet.GiverId));
                }
                break;
        }
    }

    private Task ToServer(Player from, PowerTransfer packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    /// <summary>Delivers the packets the server sent, one by one, until none is left (a packet whose recipient is not connected is dropped, like the real server does).</summary>
    private async Task Pump(Func<PowerTransfer, bool>? drop = null)
    {
        for (int step = 0; step < 100 && packetSender.Sent.Count > 0; step++)
        {
            (Packet packet, SessionId session) = packetSender.Sent[0];
            packetSender.Sent.RemoveAt(0);
            PowerTransfer transfer = (PowerTransfer)packet;
            if ((drop != null && drop(transfer)) || !playersBySession.TryGetValue(session, out Player? recipient) || !recipient.IsOnline)
            {
                continue;
            }
            await ClientHandles(recipient, transfer);
        }
        packetSender.Sent.Should().BeEmpty("the story ended with packets still on the way");
    }

    private async Task Forward(Player requester, float amount) => await ToServer(requester, PowerTransfer.Request(CyclopsId, SeamothId, amount, PowerTransferMode.Forward));

    // ---- the happy path, both modes

    [TestMethod]
    public async Task TheSimulatorOfTheVehicleAsksAndTheSimulatorOfTheCyclopsGives()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        await Pump();

        cyclops.Energy.Should().Be(START_CYCLOPS - 2f);
        seamoth.Energy.Should().Be(START_SEAMOTH + 2f);
        Total.Should().Be(START_CYCLOPS + START_SEAMOTH, "no free energy and none lost");
    }

    [TestMethod]
    public async Task TheRequestReachesTheGiversPlayerAsAnOfferWithTheServersNumber()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);

        packetSender.Sent.Should().ContainSingle();
        (Packet packet, SessionId session) = packetSender.Sent[0];
        session.Should().Be(playerA.SessionId);
        PowerTransfer offer = (PowerTransfer)packet;
        offer.Kind.Should().Be(PowerTransferKind.Offer);
        offer.TransferId.Should().BeGreaterThan(0);
        offer.GiverId.Should().Be(CyclopsId);
        offer.ReceiverId.Should().Be(SeamothId);
        offer.Amount.Should().Be(2f);
        offer.Mode.Should().Be(PowerTransferMode.Forward);
    }

    [TestMethod]
    public async Task TheDockedVehicleGivesEmergencyPowerToTheCyclopsAndTheCyclopsAsks()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        cyclops.Energy = 20f;
        seamoth.Energy = 120f;
        seamoth.Floor = 50f;

        await ToServer(playerA, PowerTransfer.Request(SeamothId, CyclopsId, 5f, PowerTransferMode.Emergency));
        await Pump();

        cyclops.Energy.Should().Be(25f);
        seamoth.Energy.Should().Be(115f);
        Total.Should().Be(140f);
    }

    [TestMethod]
    public async Task ManySecondsOfTheShareAddUpWithoutAnyDrift()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        for (int second = 0; second < 30; second++)
        {
            await Forward(playerB, 2f);
            await Pump();
        }

        cyclops.Energy.Should().BeApproximately(START_CYCLOPS - 60f, 0.001f);
        seamoth.Energy.Should().BeApproximately(START_SEAMOTH + 60f, 0.001f);
        Total.Should().BeApproximately(START_CYCLOPS + START_SEAMOTH, 0.001f);
    }

    // ---- the giver gives what it can

    [TestMethod]
    public async Task AGiverAtItsFloorGivesNothing()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        cyclops.Energy = 30f;

        await Forward(playerB, 2f);
        await Pump();

        cyclops.Energy.Should().Be(30f);
        seamoth.Energy.Should().Be(START_SEAMOTH);
    }

    [TestMethod]
    public async Task AGiverWithLessThanAskedGivesWhatItHasAboveItsFloor()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        cyclops.Energy = 31f;

        await Forward(playerB, 5f);
        await Pump();

        cyclops.Energy.Should().Be(30f);
        seamoth.Energy.Should().Be(START_SEAMOTH + 1f);
        Total.Should().Be(31f + START_SEAMOTH);
    }

    [TestMethod]
    public async Task NothingGrantedSendsTheRequesterAnAnswerWithTheOutcomeNothing()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        cyclops.Energy = 0f;
        List<(PowerTransfer Packet, SessionId Session)> seen = [];

        await Forward(playerB, 2f);
        while (packetSender.Sent.Count > 0)
        {
            (Packet packet, SessionId session) = packetSender.Sent[0];
            packetSender.Sent.RemoveAt(0);
            seen.Add(((PowerTransfer)packet, session));
            await ClientHandles(playersBySession[session], (PowerTransfer)packet);
        }

        PowerTransfer answer = seen.Last().Packet;
        seen.Last().Session.Should().Be(playerB.SessionId);
        answer.Kind.Should().Be(PowerTransferKind.Delivery);
        answer.Outcome.Should().Be(PowerTransferOutcome.Nothing);
        answer.Amount.Should().Be(0f);
    }

    // ---- the receiver is full by the time the energy arrives

    [TestMethod]
    public async Task WhatTheReceiverCannotUseGoesBackToTheGiver()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        seamoth.Energy = 199f;

        await Forward(playerB, 5f);
        await Pump();

        seamoth.Energy.Should().Be(SEAMOTH_CAPACITY);
        cyclops.Energy.Should().Be(START_CYCLOPS - 1f, "only the energy that was used leaves the Cyclops");
        Total.Should().Be(START_CYCLOPS + 199f);
    }

    [TestMethod]
    public async Task AReceiverThatFilledUpOnTheWayReturnsTheWholeAmount()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 5f);
        // The pilot charged the Seamoth to the brim by other means while the request was on its way
        seamoth.Energy = SEAMOTH_CAPACITY;
        await Pump();

        cyclops.Energy.Should().Be(START_CYCLOPS, "taken out first, given back in full");
        seamoth.Energy.Should().Be(SEAMOTH_CAPACITY);
    }

    // ---- locks that move

    [TestMethod]
    public async Task IfTheLockOfTheReceiverMovedBeforeTheGrantTheEnergyGoesToTheNewHolder()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        // Player C boards the Seamoth: B loses the lock while A is still answering
        ownership.TryToAcquire(SeamothId, playerC, SimulationLockType.EXCLUSIVE).Should().BeTrue();
        await Pump();

        seamoth.Energy.Should().Be(START_SEAMOTH + 2f);
        cyclops.Energy.Should().Be(START_CYCLOPS - 2f);
        Total.Should().Be(START_CYCLOPS + START_SEAMOTH);
    }

    [TestMethod]
    public async Task IfNobodySimulatesTheReceiverAnyMoreTheEnergyIsRefundedToTheGiver()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        ownership.RevokeIfOwner(SeamothId, playerB);
        await Pump();

        seamoth.Energy.Should().Be(START_SEAMOTH);
        cyclops.Energy.Should().Be(START_CYCLOPS, "the Cyclops took 2 out and got 2 back");
    }

    [TestMethod]
    public async Task IfTheReceiversPlayerLeavesBeforeTheGrantTheEnergyIsRefundedToTheGiver()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        ownership.RevokeAllForOwner(playerB.SessionId);
        playerB.IsOnline = false;
        await Pump();

        cyclops.Energy.Should().Be(START_CYCLOPS);
        seamoth.Energy.Should().Be(START_SEAMOTH);
    }

    [TestMethod]
    public async Task IfTheGiversLockMovedTheOldHoldersAnswerStillCountsBecauseItTookTheEnergyOut()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        // Pump the offer to A only, then A loses the lock to C; A's grant (it already took the energy out) must still be delivered
        (Packet offer, SessionId session) = packetSender.Sent[0];
        packetSender.Sent.RemoveAt(0);
        await ClientHandles(playerA, (PowerTransfer)offer);
        ownership.TryToAcquire(CyclopsId, playerC, SimulationLockType.EXCLUSIVE).Should().BeTrue();
        await Pump();

        session.Should().Be(playerA.SessionId);
        seamoth.Energy.Should().Be(START_SEAMOTH + 2f);
        Total.Should().Be(START_CYCLOPS + START_SEAMOTH);
    }

    [TestMethod]
    public async Task IfBothEndsAreSimulatedByTheSameClientTheServerDoesNotMoveEnergyForIt()
    {
        Hold(CyclopsId, playerB);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);

        packetSender.Sent.Should().ContainSingle();
        PowerTransfer answer = (PowerTransfer)packetSender.Sent[0].Packet;
        packetSender.Sent[0].SessionId.Should().Be(playerB.SessionId);
        answer.Kind.Should().Be(PowerTransferKind.Delivery);
        answer.Outcome.Should().Be(PowerTransferOutcome.Nothing, "the client moves the energy itself when it simulates both ends");
        ledger.OpenCount.Should().Be(0);
    }

    // ---- packets that get lost

    [TestMethod]
    public async Task ALostOfferLeavesAllTheEnergyWhereItWas()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        await Pump(drop: packet => packet.Kind == PowerTransferKind.Offer);

        Total.Should().Be(START_CYCLOPS + START_SEAMOTH);
        cyclops.Energy.Should().Be(START_CYCLOPS);
    }

    [TestMethod]
    public async Task ALostDeliveryLosesEnergyButNeverCreatesAny()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        await Pump(drop: packet => packet.Kind == PowerTransferKind.Delivery);

        cyclops.Energy.Should().Be(START_CYCLOPS - 2f);
        seamoth.Energy.Should().Be(START_SEAMOTH);
        Total.Should().BeLessThan(START_CYCLOPS + START_SEAMOTH);
    }

    [TestMethod]
    public async Task AGrantThatComesAfterTheExpiryIsRefundedAndNotDelivered()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        (Packet offer, SessionId _) = packetSender.Sent[0];
        packetSender.Sent.RemoveAt(0);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + 1;
        await ClientHandles(playerA, (PowerTransfer)offer);
        List<PowerTransfer> afterGrant = packetSender.Sent.Select(entry => (PowerTransfer)entry.Packet).ToList();
        await Pump();

        afterGrant.Should().ContainSingle().Which.Kind.Should().Be(PowerTransferKind.Refund);
        cyclops.Energy.Should().Be(START_CYCLOPS);
        seamoth.Energy.Should().Be(START_SEAMOTH);
    }

    [TestMethod]
    public async Task AGrantThatIsSentTwiceIsDeliveredOnce()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);
        (Packet offer, SessionId _) = packetSender.Sent[0];
        packetSender.Sent.RemoveAt(0);
        PowerTransfer grant = PowerTransfers.AnswerOffer((PowerTransfer)offer, cyclops, 100f);
        await ToServer(playerA, grant);
        await ToServer(playerA, grant);
        await Pump();

        seamoth.Energy.Should().Be(START_SEAMOTH + 2f);
        cyclops.Energy.Should().Be(START_CYCLOPS - 2f);
    }

    // ---- who may say what

    [TestMethod]
    public async Task APlayerWhoDoesNotSimulateTheReceiverCannotAsk()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerC, 2f);

        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].SessionId.Should().Be(playerC.SessionId);
        ((PowerTransfer)packetSender.Sent[0].Packet).Outcome.Should().Be(PowerTransferOutcome.Invalid);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task ARequestForACyclopsNobodySimulatesIsAnsweredWithNoGiver()
    {
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);

        ((PowerTransfer)packetSender.Sent.Single().Packet).Outcome.Should().Be(PowerTransferOutcome.NoGiver);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task AGiverWhosePlayerLeftIsAnsweredWithNoGiver()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        playerA.IsOnline = false;

        await Forward(playerB, 2f);

        ((PowerTransfer)packetSender.Sent.Single().Packet).Outcome.Should().Be(PowerTransferOutcome.NoGiver);
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-5f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(PowerTransferRules.MAX_AMOUNT + 1f)]
    public async Task AnAmountThatIsNoPositiveNumberUpToTheCapIsInvalid(float amount)
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, amount);

        ((PowerTransfer)packetSender.Sent.Single().Packet).Outcome.Should().Be(PowerTransferOutcome.Invalid);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task TheKindsOfVehicleHaveToFitTheMode()
    {
        VehicleEntity secondCyclops = AddVehicle("Cyclops");
        VehicleEntity prawn = AddVehicle("Exosuit");
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        Hold(secondCyclops.Id, playerB);
        Hold(prawn.Id, playerB);

        // Forward needs a Cyclops that gives and a vehicle that receives; emergency the other way round; a Cyclops never gives to a Cyclops and two vehicles never trade
        await ToServer(playerB, PowerTransfer.Request(CyclopsId, secondCyclops.Id, 2f, PowerTransferMode.Forward));
        await ToServer(playerB, PowerTransfer.Request(SeamothId, prawn.Id, 2f, PowerTransferMode.Forward));
        await ToServer(playerB, PowerTransfer.Request(CyclopsId, SeamothId, 2f, PowerTransferMode.Emergency));
        await ToServer(playerB, PowerTransfer.Request(CyclopsId, SeamothId, 2f, PowerTransferMode.Unknown));

        packetSender.Sent.Should().HaveCount(4);
        packetSender.Sent.Select(entry => ((PowerTransfer)entry.Packet).Outcome).Should().OnlyContain(outcome => outcome == PowerTransferOutcome.Invalid);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    public async Task APrawnSuitIsAVehicleToo()
    {
        VehicleEntity prawn = AddVehicle("Exosuit");
        Hold(CyclopsId, playerA);
        Hold(prawn.Id, playerB);

        await ToServer(playerB, PowerTransfer.Request(CyclopsId, prawn.Id, 2f, PowerTransferMode.Forward));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(playerA.SessionId);
    }

    [TestMethod]
    public async Task AnIdTheServerDoesNotKnowIsInvalid()
    {
        Hold(SeamothId, playerB);

        await ToServer(playerB, PowerTransfer.Request(new NitroxId(), SeamothId, 2f, PowerTransferMode.Forward));

        ((PowerTransfer)packetSender.Sent.Single().Packet).Outcome.Should().Be(PowerTransferOutcome.Invalid);
    }

    [TestMethod]
    public async Task WithTheVehicleUpgradesSwitchedOffNothingIsMoved()
    {
        serverOptions.VehicleUpgrades = false;
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await Forward(playerB, 2f);

        ((PowerTransfer)packetSender.Sent.Single().Packet).Outcome.Should().Be(PowerTransferOutcome.Invalid);
        ledger.OpenCount.Should().Be(0);
    }

    [TestMethod]
    [DataRow(PowerTransferKind.Offer)]
    [DataRow(PowerTransferKind.Delivery)]
    [DataRow(PowerTransferKind.Refund)]
    [DataRow(PowerTransferKind.Unknown)]
    public async Task AClientCannotSendTheServersOwnKinds(PowerTransferKind kind)
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await ToServer(playerC, new PowerTransfer(kind, 1, CyclopsId, SeamothId, 50f, PowerTransferMode.Forward, PowerTransferOutcome.Granted));

        packetSender.Sent.Should().BeEmpty("a forged delivery would hand out energy; it is dropped, not relayed");
    }

    [TestMethod]
    public async Task ALateGrantIsRefundedToWhoSimulatesTheGiverNowNotToTheGranter()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        await Forward(playerB, 4f);
        PowerTransfer offer = (PowerTransfer)packetSender.Sent.Single().Packet;
        packetSender.Sent.Clear();
        // The Cyclops changes hands while the grant is on its way, and the grant comes after the ledger gave up on it
        ownership.RevokeOwnerOfId(CyclopsId);
        Hold(CyclopsId, playerC);
        now += PowerTransferRules.LEDGER_EXPIRY_SECONDS + 0.5;

        await ToServer(playerA, PowerTransfer.Grant(offer.TransferId, offer.GiverId, offer.ReceiverId, 4f, PowerTransferMode.Forward));

        packetSender.Sent.Single().SessionId.Should().Be(playerC.SessionId, "player A no longer simulates the Cyclops and would drop the refund");
        ((PowerTransfer)packetSender.Sent.Single().Packet).Kind.Should().Be(PowerTransferKind.Refund);
    }

    [TestMethod]
    public async Task EnergyForAReceiverThatIsGoneIsRefundedToWhoSimulatesTheGiverNow()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        await Forward(playerB, 4f);
        PowerTransfer offer = (PowerTransfer)packetSender.Sent.Single().Packet;
        packetSender.Sent.Clear();
        ownership.RevokeOwnerOfId(SeamothId);
        ownership.RevokeOwnerOfId(CyclopsId);
        Hold(CyclopsId, playerC);

        await ToServer(playerA, PowerTransfer.Grant(offer.TransferId, offer.GiverId, offer.ReceiverId, 4f, PowerTransferMode.Forward));

        packetSender.Sent.Single().SessionId.Should().Be(playerC.SessionId);
    }

    [TestMethod]
    public async Task APlayerCannotKeepMoreThanTheCapOfRequestsOpen()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        for (int i = 0; i < PowerTransferRules.MAX_OPEN_PER_REQUESTER; i++)
        {
            await Forward(playerB, 2f);
        }
        ledger.OpenCountFor(playerB.SessionId).Should().Be(PowerTransferRules.MAX_OPEN_PER_REQUESTER);
        packetSender.Sent.Clear();

        await Forward(playerB, 2f);

        ledger.OpenCountFor(playerB.SessionId).Should().Be(PowerTransferRules.MAX_OPEN_PER_REQUESTER, "the request over the cap opens nothing");
        packetSender.Sent.Single().SessionId.Should().Be(playerB.SessionId, "nothing is offered to the giver; the requester is told");
        ((PowerTransfer)packetSender.Sent.Single().Packet).Outcome.Should().Be(PowerTransferOutcome.Nothing);
    }

    // ---- the return of what could not be used

    /// <summary>A forward transfer as far as the delivery: B asks, A takes the energy out, the server delivers to B. Clears the packets and returns the number of the delivery.</summary>
    private async Task<long> DeliverToB(float amount = 6f)
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        await Forward(playerB, amount);
        PowerTransfer offer = (PowerTransfer)packetSender.Sent.Single().Packet;
        packetSender.Sent.Clear();

        await ToServer(playerA, PowerTransfer.Grant(offer.TransferId, offer.GiverId, offer.ReceiverId, amount, PowerTransferMode.Forward));

        (Packet packet, SessionId session) = packetSender.Sent.Single();
        session.Should().Be(playerB.SessionId);
        PowerTransfer delivery = (PowerTransfer)packet;
        delivery.Kind.Should().Be(PowerTransferKind.Delivery);
        packetSender.Sent.Clear();
        return delivery.TransferId;
    }

    [TestMethod]
    public async Task ThePlayerWhoWasDeliveredToCanReturnTheRestAndItGoesToTheGiversPlayer()
    {
        long id = await DeliverToB();

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));

        PowerTransfer refund = (PowerTransfer)packetSender.Sent.Single().Packet;
        packetSender.Sent.Single().SessionId.Should().Be(playerA.SessionId);
        refund.Kind.Should().Be(PowerTransferKind.Refund);
        refund.Amount.Should().Be(5f);
        refund.GiverId.Should().Be(CyclopsId);
        refund.ReceiverId.Should().Be(SeamothId);
    }

    [TestMethod]
    public async Task ADeliveryCanBeReturnedOnlyOnce()
    {
        long id = await DeliverToB();

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 2f, PowerTransferMode.Forward));
        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 2f, PowerTransferMode.Forward));

        packetSender.Sent.Should().ContainSingle("the second return of the same delivery is ignored, or the energy would be refunded twice");
    }

    [TestMethod]
    public async Task AReturnWithoutADeliveryIsIgnored()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);

        await ToServer(playerB, PowerTransfer.Return(0, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));
        await ToServer(playerB, PowerTransfer.Return(424242, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));

        packetSender.Sent.Should().BeEmpty("nothing was delivered, so there is nothing to give back");
    }

    [TestMethod]
    public async Task AReturnForATransferThatIsOnlyOfferedIsIgnored()
    {
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        await Forward(playerB, 2f);
        long offered = ((PowerTransfer)packetSender.Sent.Single().Packet).TransferId;
        packetSender.Sent.Clear();

        await ToServer(playerB, PowerTransfer.Return(offered, CyclopsId, SeamothId, 2f, PowerTransferMode.Forward));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AReturnFromAnotherPlayerIsIgnoredAndTheRightPlayerCanStillReturn()
    {
        long id = await DeliverToB();

        await ToServer(playerC, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));
        await ToServer(playerA, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));
        packetSender.Sent.Should().BeEmpty("neither player C nor the giver's player was delivered to");

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));

        packetSender.Sent.Should().ContainSingle();
    }

    [TestMethod]
    public async Task AReturnAboveTheDeliveredAmountIsCutToIt()
    {
        long id = await DeliverToB(6f);

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 90f, PowerTransferMode.Forward));

        ((PowerTransfer)packetSender.Sent.Single().Packet).Amount.Should().Be(6f, "the refund cannot be more than the server delivered");
    }

    [TestMethod]
    public async Task TheVehiclesAndTheModeOfAReturnAreTheDeliverysOwn()
    {
        long id = await DeliverToB();

        // The packet names other vehicles and another mode: the server refunds what it delivered, to the giver of that delivery
        await ToServer(playerB, PowerTransfer.Return(id, SeamothId, CyclopsId, 1f, PowerTransferMode.Emergency));

        PowerTransfer refund = (PowerTransfer)packetSender.Sent.Single().Packet;
        packetSender.Sent.Single().SessionId.Should().Be(playerA.SessionId);
        refund.GiverId.Should().Be(CyclopsId);
        refund.ReceiverId.Should().Be(SeamothId);
        refund.Mode.Should().Be(PowerTransferMode.Forward);
    }

    [TestMethod]
    public async Task AReturnForAGiverNobodySimulatesIsDropped()
    {
        long id = await DeliverToB();
        playerA.IsOnline = false;

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-1f)]
    [DataRow(float.NaN)]
    [DataRow(PowerTransferRules.MAX_AMOUNT + 1f)]
    public async Task AReturnOfNoAmountIsDroppedAndTheDeliveryCanStillBeReturned(float amount)
    {
        long id = await DeliverToB();

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, amount, PowerTransferMode.Forward));
        packetSender.Sent.Should().BeEmpty();

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 2f, PowerTransferMode.Forward));
        packetSender.Sent.Should().ContainSingle();
    }

    [TestMethod]
    public async Task AReturnAfterTheMemoryTimeIsIgnored()
    {
        long id = await DeliverToB();
        now += PowerTransferRules.LATE_GRANT_MEMORY_SECONDS + 1;

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task APlayerWhoLeftCannotReturnAnythingAfterComingBackWithTheSameNumber()
    {
        long id = await DeliverToB();
        ledger.SessionEnded(playerB.SessionId);

        await ToServer(playerB, PowerTransfer.Return(id, CyclopsId, SeamothId, 5f, PowerTransferMode.Forward));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task TheRestOfADeliveryThatTheReceiverCouldNotUseGoesBackToTheGiverAndNoEnergyIsMade()
    {
        // The receiver is nearly full: it stores 2 of the 6 delivered and returns 4, which the server refunds
        Hold(CyclopsId, playerA);
        Hold(SeamothId, playerB);
        seamoth.Energy = SEAMOTH_CAPACITY - 2f;

        await Forward(playerB, 6f);
        await Pump();

        seamoth.Energy.Should().Be(SEAMOTH_CAPACITY);
        cyclops.Energy.Should().Be(START_CYCLOPS - 2f);
        Total.Should().Be(START_CYCLOPS + SEAMOTH_CAPACITY - 2f, "the 4 that did not fit came back");
    }

    // ---- serialisation

    [TestMethod]
    public void EveryKindOfThePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId giver = new();
        NitroxId receiver = new();

        foreach (PowerTransferKind kind in Enum.GetValues(typeof(PowerTransferKind)))
        {
            PowerTransfer sent = new(kind, 123456789012L, giver, receiver, 1.25f, PowerTransferMode.Emergency, PowerTransferOutcome.Granted);

            PowerTransfer received = (PowerTransfer)Packet.Deserialize(sent.Serialize());

            received.Kind.Should().Be(kind);
            received.TransferId.Should().Be(123456789012L);
            received.GiverId.Should().Be(giver);
            received.ReceiverId.Should().Be(receiver);
            received.Amount.Should().Be(1.25f);
            received.Mode.Should().Be(PowerTransferMode.Emergency);
            received.Outcome.Should().Be(PowerTransferOutcome.Granted);
        }
    }

    // ---- helpers

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    /// <summary>A vehicle or a Cyclops as the game's client sees it for the share: a charge, a capacity and a floor it keeps for itself.</summary>
    private sealed class FakeEnd(float energy, float capacity, float floor) : IPowerEndpoint
    {
        public float Energy { get; set; } = energy;
        public float Floor { get; set; } = floor;

        public float Take(float offered)
        {
            float taken = Math.Min(offered, Math.Max(0f, Energy - Floor));
            Energy -= taken;
            return taken;
        }

        public float Give(float amount)
        {
            float stored = Math.Min(amount, capacity - Energy);
            Energy += stored;
            return stored;
        }
    }

    private sealed class RecordingPacketSender : IPacketSender
    {
        public List<(Packet Packet, SessionId SessionId)> Sent { get; } = [];

        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            Sent.Add((packet, sessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => throw new AssertFailedException("A power transfer must only reach the players involved");

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => throw new AssertFailedException("A power transfer must only reach the players involved");
    }
}
