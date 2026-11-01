using System.Collections.Generic;
using System.Linq;
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
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The buttons of the engineer console on the server (feature group 3, station 3): only the seated engineer can press, the route is the server's and told to everybody, the overdrive and the vent go to the player
///     who simulates the Cyclops and start their cooldown, on the server's clock, only when that player says it was carried out, and the answer goes back to the engineer alone.
/// </summary>
[TestClass]
public class CyclopsEngineerActionProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsEngineerArbiter arbiter = null!;
    private CyclopsEngineerActionProcessor processor = null!;
    private Player pilot = null!;
    private Player engineer = null!;
    private Player bystander = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity seamoth = null!;
    private long clock;
    private int counter;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        clock = 100_000;
        arbiter = new CyclopsEngineerArbiter(packetSender) { Clock = () => clock };
        pilot = CreatePlayer(1);
        engineer = CreatePlayer(2);
        bystander = CreatePlayer(3);
        cyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE);
        arbiter.TrySit(cyclops.Id, engineer.SessionId, engineer.Name, out _);
        processor = new CyclopsEngineerActionProcessor(arbiter, ownership, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsEngineerActionProcessor>.Instance) { Clock = () => clock };
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsEngineerAction packet) => processor.Process(new AuthProcessorContext(from, packetSender), packet);

    private Task Press(Player from, CyclopsEngineerActionKind action, int value = 0) => Send(from, CyclopsEngineerAction.Request(action, cyclops.Id, ++counter, value));

    private Task Answer(Player from, CyclopsEngineerActionKind action, CyclopsEngineerFailure failure = CyclopsEngineerFailure.None, int heat = 3) => Send(from, CyclopsEngineerAction.Result(action, cyclops.Id, counter, engineer.SessionId, failure, heat));

    private CyclopsEngineerAction SentTo(Player player) => (CyclopsEngineerAction)packetSender.Sent.Last(sent => sent.SessionId == player.SessionId).Packet;

    private CyclopsEngineerState LastState() => (CyclopsEngineerState)packetSender.SentToAll[^1];

    private async Task SetRoute(EngineerRoute route)
    {
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)route);
        clock += 1000;
        packetSender.Sent.Clear();
        packetSender.SentToAll.Clear();
    }

    // ---- the route

    [TestMethod]
    public async Task TheSeatedEngineerSendsThePowerToARouteAndEverybodyIsToldOnce()
    {
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Shields);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Shields);
        packetSender.SentToAll.Should().ContainSingle();
        LastState().Route.Should().Be((int)EngineerRoute.Shields);
        LastState().CyclopsId.Should().Be(cyclops.Id);
        LastState().OverdriveSeconds.Should().Be(0f);
        packetSender.Sent.Should().ContainSingle("the engineer is answered").Which.SessionId.Should().Be(engineer.SessionId);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.None);
        SentTo(engineer).Value.Should().Be((int)EngineerRoute.Shields);

        clock += 1000;
        packetSender.SentToAll.Clear();
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Shields);
        packetSender.SentToAll.Should().BeEmpty("nothing changed");
        packetSender.Sent.Should().ContainSingle("but the engineer is answered");
    }

    [TestMethod]
    public async Task APlayerWhoDoesNotSitAtTheConsoleCannotSetTheRouteEvenIfTheyPilotTheCyclops()
    {
        await Press(bystander, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Engines);
        await Press(pilot, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Engines);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Balanced);
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.Sent.Select(sent => ((CyclopsEngineerAction)sent.Packet).Failure).Should().OnlyContain(failure => failure == CyclopsEngineerFailure.NotSeated);
        packetSender.Sent.Select(sent => sent.SessionId).Should().BeEquivalentTo([bystander.SessionId, pilot.SessionId], "each is told alone");
    }

    [TestMethod]
    public async Task ARouteThatDoesNotExistIsRefused()
    {
        await Press(engineer, CyclopsEngineerActionKind.Route, 9);
        await Press(engineer, CyclopsEngineerActionKind.Route, -1);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Balanced);
        packetSender.Sent.Select(sent => ((CyclopsEngineerAction)sent.Packet).Failure).Should().OnlyContain(failure => failure == CyclopsEngineerFailure.NotAvailable);
    }

    [TestMethod]
    public async Task TwoPressesWithinAQuarterOfASecondAreOnePress()
    {
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Shields);
        clock += 100;
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Systems);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Shields);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.TooSoon);

        clock += 300;
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Systems);
        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Systems);
    }

    [TestMethod]
    public async Task ARequestForSomethingThatIsNoLiveCyclopsOrWithTheUpgradesOffOrForAnActionThatDoesNotExistIsRefused()
    {
        await Send(engineer, CyclopsEngineerAction.Request(CyclopsEngineerActionKind.Route, seamoth.Id, 1, 1));
        await Send(engineer, CyclopsEngineerAction.Request((CyclopsEngineerActionKind)9, cyclops.Id, 2));
        await Send(engineer, CyclopsEngineerAction.Request(CyclopsEngineerActionKind.Unknown, cyclops.Id, 3));
        cyclops.Metadata = new CyclopsMetadata(false, false, false, false, 0, 100, true);
        await Press(engineer, CyclopsEngineerActionKind.Route, 1);
        cyclops.Metadata = null;
        serverOptions.VehicleUpgrades = false;
        await Press(engineer, CyclopsEngineerActionKind.Route, 1);

        packetSender.Sent.Should().HaveCount(5);
        packetSender.Sent.Select(sent => ((CyclopsEngineerAction)sent.Packet).Failure).Should().OnlyContain(failure => failure == CyclopsEngineerFailure.NotAvailable);
        packetSender.SentToAll.Should().BeEmpty();
        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Balanced);
    }

    // ---- the overdrive

    [TestMethod]
    public async Task AnOverdriveNeedsTheEnginesRouteWhichTheServerKeeps()
    {
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.WrongRoute);
    }

    [TestMethod]
    public async Task AnOverdriveOnTheEnginesRouteGoesToThePlayerWhoSimulatesTheCyclopsWithTheEngineerFromTheConnection()
    {
        await SetRoute(EngineerRoute.Engines);

        // the packet names somebody else as the engineer: the server does not believe it
        await Send(engineer, new CyclopsEngineerAction(CyclopsEngineerActionStep.Request, CyclopsEngineerActionKind.Overdrive, cyclops.Id, 41, bystander.SessionId, CyclopsEngineerFailure.None, 0));

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
        CyclopsEngineerAction forwarded = (CyclopsEngineerAction)packetSender.Sent[0].Packet;
        forwarded.Step.Should().Be(CyclopsEngineerActionStep.Forward);
        forwarded.Action.Should().Be(CyclopsEngineerActionKind.Overdrive);
        forwarded.Engineer.Should().Be(engineer.SessionId);
        forwarded.Counter.Should().Be(41);
        forwarded.CyclopsId.Should().Be(cyclops.Id);
        packetSender.SentToAll.Should().BeEmpty("nothing starts before the simulator says it was carried out");
    }

    [TestMethod]
    public async Task TheCooldownStartsWhenTheSimulatorSaysItWasCarriedOutAndEverybodyIsToldTheSecondsThatAreLeft()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Clear();

        await Answer(pilot, CyclopsEngineerActionKind.Overdrive, heat: 5);

        CyclopsEngineerState state = LastState();
        state.Route.Should().Be((int)EngineerRoute.Engines);
        state.OverdriveSeconds.Should().Be(12f);
        state.OverdriveReadySeconds.Should().Be(57f, "the cooldown of 45 s starts when the 12 s end");
        state.VentReadySeconds.Should().Be(0f);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        CyclopsEngineerAction answer = SentTo(engineer);
        answer.Step.Should().Be(CyclopsEngineerActionStep.Result);
        answer.Failure.Should().Be(CyclopsEngineerFailure.None);
        answer.Value.Should().Be(5);
    }

    [TestMethod]
    public async Task TheSecondsLeftFallWithTheServersClockAndTheNextOverdriveWaitsForThem()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        await Answer(pilot, CyclopsEngineerActionKind.Overdrive);
        clock += 20_000;
        packetSender.Sent.Clear();
        packetSender.SentToAll.Clear();

        CyclopsEngineerState late = arbiter.StateOf(cyclops.Id);
        late.OverdriveSeconds.Should().Be(0f, "the 12 s are over");
        late.OverdriveReadySeconds.Should().BeApproximately(37f, 0.01f);

        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.Cooldown);
        packetSender.Sent.Should().ContainSingle("not forwarded");

        clock += 38_000;
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
    }

    [TestMethod]
    public async Task ASecondOverdriveWhileOneIsOutIsTurnedDownAndNeverReachesTheSimulator()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        clock += 300;
        packetSender.Sent.Clear();

        await Press(engineer, CyclopsEngineerActionKind.Overdrive);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.TooSoon);
    }

    [TestMethod]
    public async Task AFailureOfTheSimulatorReachesTheEngineerAndStartsNothing()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Clear();

        await Answer(pilot, CyclopsEngineerActionKind.Overdrive, CyclopsEngineerFailure.NotFlank);

        packetSender.SentToAll.Should().BeEmpty();
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.NotFlank);
        arbiter.StateOf(cyclops.Id).OverdriveReadySeconds.Should().Be(0f, "a refusal burns no cooldown");

        clock += 1000;
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "the engineer can try again, the pending press is over");
    }

    [TestMethod]
    public async Task AnAnswerThatTakesLongerThanTheWaitStillStartsTheCooldownBecauseTheSimulatorHasPaid()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Clear();
        clock += CyclopsEngineerRules.PENDING_FORWARD_MILLISECONDS + 500;

        await Answer(pilot, CyclopsEngineerActionKind.Overdrive, heat: 4);

        LastState().OverdriveReadySeconds.Should().BeGreaterThan(40f, "the heat was changed and the energy paid on the simulator, so the cooldown runs");
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.None);
    }

    [TestMethod]
    public async Task TheRouteMovingAwayWhileTheOverdriveIsOutBurnsNoCooldownAndTheEngineerIsTold()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        int overdriveCounter = counter;
        clock += 500;
        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Shields);
        packetSender.Sent.Clear();
        packetSender.SentToAll.Clear();

        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Overdrive, cyclops.Id, overdriveCounter, engineer.SessionId, CyclopsEngineerFailure.None, 3));

        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.WrongRoute);
        packetSender.SentToAll.Should().BeEmpty("nothing started");
        arbiter.StateOf(cyclops.Id).OverdriveReadySeconds.Should().Be(0f);
        arbiter.StateOf(cyclops.Id).OverdriveSeconds.Should().Be(0f);
    }

    [TestMethod]
    public async Task ARouteClickInsideTheFloorOfAForwardedPressIsAccepted()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
        clock += 100;
        packetSender.Sent.Clear();
        packetSender.SentToAll.Clear();

        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Shields);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Shields, "a route and a press have floors of their own");
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.None);
    }

    [TestMethod]
    public async Task AClientThatGaveUpOnAStalledSimulatorCannotPressAgainWhileTheFirstPressMayStillRun()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Clear();
        clock += (long)(CyclopsEngineerRules.REQUEST_TIMEOUT_SECONDS * 1000f) + 100;

        await Press(engineer, CyclopsEngineerActionKind.Vent);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.TooSoon, "the first vent is still out");

        clock += CyclopsEngineerRules.PENDING_FORWARD_MILLISECONDS - (long)(CyclopsEngineerRules.REQUEST_TIMEOUT_SECONDS * 1000f);
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "after the hold-back a stalled simulator no longer blocks the engineer");
    }

    [TestMethod]
    public async Task AVentThatArrivesWhileTheCooldownOfAnotherRunsStartsNothingAndIsToldCooldown()
    {
        // two presses reached the simulator (a stall): the second answer must not run the cooldown again
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        await Answer(pilot, CyclopsEngineerActionKind.Vent);
        float readyAfterFirst = arbiter.StateOf(cyclops.Id).VentReadySeconds;
        clock += 5000;
        packetSender.Sent.Clear();
        packetSender.SentToAll.Clear();

        // a vent that the server never forwarded cannot be answered, so the arbiter is asked directly for a second commit inside the cooldown
        arbiter.CommitVent(cyclops.Id, 30f).Should().Be(CyclopsEngineerFailure.Cooldown);

        arbiter.StateOf(cyclops.Id).VentReadySeconds.Should().BeApproximately(readyAfterFirst - 5f, 0.01f, "the cooldown of the first vent keeps running");
    }

    [TestMethod]
    public async Task ARouteClickRightAfterAPressIsNoRepeatOfIt()
    {
        // the floors are of one kind each: a press that went, and one that was held back for the pending one, push the route's floor out by nothing
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        clock += 300;
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.TooSoon);
        clock += 100;
        packetSender.Sent.Clear();
        packetSender.SentToAll.Clear();

        await Press(engineer, CyclopsEngineerActionKind.Route, (int)EngineerRoute.Systems);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Systems);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.None);
    }

    [TestMethod]
    public async Task AfterTheSimulatorTurnedAPressDownTheSamePressWaitsASecond()
    {
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        await Answer(pilot, CyclopsEngineerActionKind.Overdrive, CyclopsEngineerFailure.EngineOff);
        packetSender.Sent.Clear();

        clock += 500;
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.TooSoon, "a press that comes to nothing is not forwarded again at once");

        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "the vent is another button");

        clock += 600;
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "a second after the refusal the engineer can try again");
    }

    // ---- the vent

    [TestMethod]
    public async Task AVentGoesToTheSimulatorAndItsCooldownIsTheSettingsAndIndependentOfTheOverdrive()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
        ((CyclopsEngineerAction)packetSender.Sent[0].Packet).Action.Should().Be(CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Clear();

        await Answer(pilot, CyclopsEngineerActionKind.Vent, heat: 1);

        CyclopsEngineerState state = LastState();
        state.VentReadySeconds.Should().Be(30f);
        state.OverdriveReadySeconds.Should().Be(0f);
        SentTo(engineer).Value.Should().Be(1);

        clock += 1000;
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.Cooldown);
    }

    [TestMethod]
    public async Task TheHeatInAnAnswerIsKeptInsideTheGamesRange()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Clear();

        await Answer(pilot, CyclopsEngineerActionKind.Vent, heat: 99);

        SentTo(engineer).Value.Should().Be(10);
    }

    [TestMethod]
    public async Task TheServerClampsTheRawOptionsLikeTheClientsDo()
    {
        serverOptions.OverdriveSeconds = 9999f;
        serverOptions.OverdriveCooldownSeconds = float.NaN;
        serverOptions.VentCooldownSeconds = -5f;
        await SetRoute(EngineerRoute.Engines);
        await Press(engineer, CyclopsEngineerActionKind.Overdrive);
        await Answer(pilot, CyclopsEngineerActionKind.Overdrive);
        CyclopsEngineerState overdrive = LastState();
        clock += 1000;
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        await Answer(pilot, CyclopsEngineerActionKind.Vent);

        overdrive.OverdriveSeconds.Should().Be(VehicleUpgradeSettingsRules.MAX_OVERDRIVE_SECONDS);
        overdrive.OverdriveReadySeconds.Should().Be(VehicleUpgradeSettingsRules.MAX_OVERDRIVE_SECONDS + VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_COOLDOWN_SECONDS);
        LastState().VentReadySeconds.Should().Be(VehicleUpgradeSettingsRules.MIN_VENT_COOLDOWN_SECONDS);
    }

    // ---- the simulator

    [TestMethod]
    public async Task WithNobodySimulatingTheCyclopsTheEngineerIsToldAndNothingIsLeftWaiting()
    {
        ownership.RevokeIfOwner(cyclops.Id, pilot);

        await Press(engineer, CyclopsEngineerActionKind.Vent);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.NoSimulator);

        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE);
        clock += 300;
        packetSender.Sent.Clear();
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId, "the failed press reserved nothing");
    }

    [TestMethod]
    public async Task ThePlayerWhoSimulatesTheCyclopsAndIsTheEngineerIsAskedLikeAnyoneElse()
    {
        ownership.RevokeIfOwner(cyclops.Id, pilot);
        ownership.TryToAcquire(cyclops.Id, engineer, SimulationLockType.EXCLUSIVE);

        await Press(engineer, CyclopsEngineerActionKind.Vent);

        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(engineer.SessionId);
        ((CyclopsEngineerAction)packetSender.Sent[0].Packet).Step.Should().Be(CyclopsEngineerActionStep.Forward);
    }

    [TestMethod]
    public async Task TheAnswerOfThePlayerWhoWasAskedCountsEvenIfTheLockMovedOnTheWay()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        ownership.RevokeIfOwner(cyclops.Id, pilot);
        ownership.TryToAcquire(cyclops.Id, bystander, SimulationLockType.EXCLUSIVE);
        packetSender.Sent.Clear();

        await Answer(pilot, CyclopsEngineerActionKind.Vent);

        SentTo(engineer).Failure.Should().Be(CyclopsEngineerFailure.None);
    }

    [TestMethod]
    public async Task AnAnswerNobodyAskedForOrFromAnotherPlayerOrAfterTheWaitOrWithAnotherNumberIsDropped()
    {
        await Answer(pilot, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().BeEmpty("no press was forwarded");

        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Clear();
        await Answer(bystander, CyclopsEngineerActionKind.Vent);
        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Vent, cyclops.Id, counter + 5, engineer.SessionId, CyclopsEngineerFailure.None));
        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Vent, cyclops.Id, counter, bystander.SessionId, CyclopsEngineerFailure.None));
        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Overdrive, cyclops.Id, counter, engineer.SessionId, CyclopsEngineerFailure.None));
        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();

        clock += CyclopsEngineerRules.LATE_RESULT_MILLISECONDS + 100;
        await Answer(pilot, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().BeEmpty("too late even for a late answer");
        packetSender.SentToAll.Should().BeEmpty();

        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Clear();
        await Answer(pilot, CyclopsEngineerActionKind.Vent);
        await Answer(pilot, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Should().ContainSingle("each press is answered once");
    }

    [TestMethod]
    public async Task IfTheEngineerHasLeftTheEngineStillStartsTheCooldownButNobodyIsAnswered()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        arbiter.Leave(cyclops.Id, engineer.SessionId);
        packetSender.Sent.Clear();

        await Answer(pilot, CyclopsEngineerActionKind.Vent);

        packetSender.Sent.Should().BeEmpty();
        LastState().VentReadySeconds.Should().Be(30f, "the vent was carried out");
    }

    [TestMethod]
    public async Task AnAnswerForSomethingThatIsNoCyclopsOrWithAFailureThatDoesNotExistOrWithTheUpgradesOffOrAnActionOfTheRouteIsDropped()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Vent, seamoth.Id, counter, engineer.SessionId, CyclopsEngineerFailure.None));
        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Vent, cyclops.Id, counter, engineer.SessionId, (CyclopsEngineerFailure)99));
        await Send(pilot, CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Route, cyclops.Id, counter, engineer.SessionId, CyclopsEngineerFailure.None));
        serverOptions.VehicleUpgrades = false;
        await Answer(pilot, CyclopsEngineerActionKind.Vent);

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AClientCannotSendTheServersOwnMessagesOrNothingAtAll()
    {
        await Send(engineer, new CyclopsEngineerAction(CyclopsEngineerActionStep.Forward, CyclopsEngineerActionKind.Vent, cyclops.Id, 1, engineer.SessionId, CyclopsEngineerFailure.None, 0));
        await Send(engineer, new CyclopsEngineerAction(CyclopsEngineerActionStep.Unknown, CyclopsEngineerActionKind.Vent, cyclops.Id, 1, default, CyclopsEngineerFailure.None, 0));
        await Send(engineer, new CyclopsEngineerAction(CyclopsEngineerActionStep.Request, CyclopsEngineerActionKind.Vent, null!, 1, default, CyclopsEngineerFailure.None, 0));
        await Send(pilot, new CyclopsEngineerAction(CyclopsEngineerActionStep.Result, CyclopsEngineerActionKind.Vent, null!, 1, engineer.SessionId, CyclopsEngineerFailure.None, 0));

        packetSender.Sent.Should().BeEmpty();
        packetSender.SentToAll.Should().BeEmpty();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    // ---- what a joiner is told, and when a Cyclops is gone

    [TestMethod]
    public async Task APlayerWhoJoinsIsToldWhoSitsAndWhatIsSetOnlyOnCyclopsesWithSomethingSet()
    {
        VehicleEntity quiet = AddVehicle("Cyclops");
        arbiter.SetRoute(quiet.Id, EngineerRoute.Balanced);
        await SetRoute(EngineerRoute.Systems);
        SessionId joiner = (ushort)9;

        await arbiter.SendSnapshotAsync(joiner);

        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == joiner);
        packetSender.Sent.Select(sent => sent.Packet).OfType<CyclopsEngineerSeat>().Should().ContainSingle().Which.PlayerName.Should().Be(engineer.Name);
        CyclopsEngineerState told = packetSender.Sent.Select(sent => sent.Packet).OfType<CyclopsEngineerState>().Single();
        told.CyclopsId.Should().Be(cyclops.Id);
        told.Route.Should().Be((int)EngineerRoute.Systems);
    }

    [TestMethod]
    public async Task ACooldownThatIsStillRunningIsToldToAJoinerWithTheSecondsThatAreLeft()
    {
        await Press(engineer, CyclopsEngineerActionKind.Vent);
        await Answer(pilot, CyclopsEngineerActionKind.Vent);
        clock += 10_000;
        packetSender.Sent.Clear();

        await arbiter.SendSnapshotAsync((ushort)9);

        packetSender.Sent.Select(sent => sent.Packet).OfType<CyclopsEngineerState>().Single().VentReadySeconds.Should().BeApproximately(20f, 0.01f);
    }

    [TestMethod]
    public async Task WhatWasSetOnACyclopsIsForgottenWhenItIsGone()
    {
        await SetRoute(EngineerRoute.Engines);

        await arbiter.BroadcastEndedAsync(arbiter.CyclopsGone(cyclops.Id), CyclopsEngineerSeatReason.CyclopsDestroyed);

        arbiter.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Balanced);
        arbiter.Count.Should().Be(0);
        packetSender.SentToAll.OfType<CyclopsEngineerSeat>().Should().ContainSingle().Which.Reason.Should().Be(CyclopsEngineerSeatReason.CyclopsDestroyed);
    }

    // ---- the packets

    [TestMethod]
    public void TheEngineerPacketsSurviveTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsEngineerAction action = CyclopsEngineerAction.Result(CyclopsEngineerActionKind.Overdrive, new NitroxId(), 17, (SessionId)(ushort)4, CyclopsEngineerFailure.NotFlank, 7);
        CyclopsEngineerState state = new(new NitroxId(), 2, 11.5f, 56.5f, 29f);
        CyclopsEngineerStatus status = new(new NitroxId(), 6, true, true, 55.5f);
        CyclopsEngineerSeat seat = new(CyclopsEngineerSeatMessage.Denied, new NitroxId(), (SessionId)(ushort)3, "Alice", CyclopsEngineerSeatReason.Occupied);

        ((CyclopsEngineerAction)Packet.Deserialize(action.Serialize())).Should().BeEquivalentTo(action);
        ((CyclopsEngineerState)Packet.Deserialize(state.Serialize())).Should().BeEquivalentTo(state);
        ((CyclopsEngineerStatus)Packet.Deserialize(status.Serialize())).Should().BeEquivalentTo(status);
        ((CyclopsEngineerSeat)Packet.Deserialize(seat.Serialize())).Should().BeEquivalentTo(seat);
    }

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
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
