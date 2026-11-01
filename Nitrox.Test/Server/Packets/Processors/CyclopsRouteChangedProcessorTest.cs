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
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The route of a Cyclops on the server (feature group 3, station 1): waypoints are appended and removed by anybody, an advance comes only from the player whose client simulates the Cyclops and goes forward by one;
///     the server keeps the books, passes the change on, answers a refusal to the sender alone, tells a player who joins and drops the route of a destroyed Cyclops.
/// </summary>
[TestClass]
public class CyclopsRouteChangedProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private SimulationOwnershipData ownership = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsNavRegistry registry = null!;
    private CyclopsRouteChangedProcessor processor = null!;
    private Player pilot = null!;
    private Player navigator = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity seamoth = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        ownership = new SimulationOwnershipData();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        registry = new CyclopsNavRegistry(packetSender);
        pilot = CreatePlayer(1);
        navigator = CreatePlayer(2);
        cyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE);
        processor = new CyclopsRouteChangedProcessor(registry, ownership, entityRegistry, Options.Create(serverOptions), NullLogger<CyclopsRouteChangedProcessor>.Instance);
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsRouteChange change, VehicleEntity on, NitroxId? waypointId = null, float x = -78f, float y = -10f, float z = 330f, int index = 0)
    {
        return processor.Process(new AuthProcessorContext(from, packetSender), new CyclopsRouteChanged(change, on.Id, waypointId ?? new NitroxId(), new NitroxVector3(x, y, z), index, from.SessionId, from.Name));
    }

    /// <summary>The advance that the player at the helm sends when the Cyclops reaches the waypoint that is next (as that player's copy of the route has it): it names that waypoint.</summary>
    private Task Advance(Player from, int proposed)
    {
        IReadOnlyList<CyclopsNavRegistry.Waypoint> route = registry.RouteOf(cyclops.Id);
        int next = registry.NextOf(cyclops.Id);
        NitroxId reached = next >= 0 && next < route.Count ? route[next].Id : new NitroxId();
        return Send(from, CyclopsRouteChange.Advanced, cyclops, reached, 0f, 0f, 0f, proposed);
    }

    private async Task<NitroxId> Add(float x, float z, Player? from = null)
    {
        NitroxId id = new();
        await Send(from ?? navigator, CyclopsRouteChange.WaypointAdded, cyclops, id, x, -10f, z);
        return id;
    }

    // ---- waypoints

    [TestMethod]
    public async Task AWaypointIsKeptInOrderAndTheOthersAreToldWithTheSenderAsOwner()
    {
        NitroxId first = await Add(0f, 0f);
        packetSender.SentToOthers.Clear();
        NitroxId second = await Add(100f, 0f);

        registry.RouteOf(cyclops.Id).Select(point => point.Id).Should().Equal(first, second);
        packetSender.SentToOthers.Should().ContainSingle();
        CyclopsRouteChanged relayed = (CyclopsRouteChanged)packetSender.SentToOthers[0].Packet;
        packetSender.SentToOthers[0].Excluded.Should().Be(navigator.SessionId);
        relayed.Change.Should().Be(CyclopsRouteChange.WaypointAdded);
        relayed.WaypointId.Should().Be(second);
        relayed.OwnerName.Should().Be(navigator.Name);
        registry.NextOf(cyclops.Id).Should().Be(0);
        relayed.Index.Should().Be(1, "the server's order is the route's order: the clients put the waypoint in that slot");
    }

    [TestMethod]
    public async Task EveryWaypointIsRelayedWithTheSlotTheServerGaveItSoThatTwoNavigatorsAgree()
    {
        // two navigators add one waypoint each; whichever reaches a client first, the slots say where each goes
        NitroxId first = await Add(0f, 0f, navigator);
        NitroxId second = await Add(100f, 0f, pilot);

        List<CyclopsRouteChanged> relayed = packetSender.SentToOthers.Select(sent => (CyclopsRouteChanged)sent.Packet).ToList();
        relayed.Should().HaveCount(2);
        relayed[0].WaypointId.Should().Be(first);
        relayed[0].Index.Should().Be(0);
        relayed[1].WaypointId.Should().Be(second);
        relayed[1].Index.Should().Be(1);
    }

    [TestMethod]
    public async Task AWaypointOnTopOfTheLastOneOrBeyondTheLimitIsRefusedToTheSenderAlone()
    {
        await Add(0f, 0f);
        packetSender.SentToOthers.Clear();
        NitroxId tooClose = await Add(2f, 0f);

        registry.RouteOf(cyclops.Id).Should().HaveCount(1);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(navigator.SessionId);
        CyclopsRouteChanged answer = (CyclopsRouteChanged)packetSender.Sent[0].Packet;
        answer.Change.Should().Be(CyclopsRouteChange.WaypointRemoved, "the sender takes its own copy back");
        answer.WaypointId.Should().Be(tooClose);
        answer.Index.Should().Be(-1, "there is no next waypoint in a refusal for the sender to take over");

        for (int i = 1; i < NavigationRules.MAX_WAYPOINTS; i++)
        {
            await Add(i * 100f, 0f);
        }
        registry.RouteOf(cyclops.Id).Should().HaveCount(NavigationRules.MAX_WAYPOINTS);
        packetSender.Sent.Clear();

        await Add(5000f, 0f);

        registry.RouteOf(cyclops.Id).Should().HaveCount(NavigationRules.MAX_WAYPOINTS, "a route has at most that many waypoints");
        packetSender.Sent.Should().ContainSingle();
    }

    [TestMethod]
    public async Task TheSameWaypointAgainChangesNothingAndIsNotPassedOnTwice()
    {
        NitroxId id = await Add(0f, 0f);
        packetSender.SentToOthers.Clear();

        await Send(navigator, CyclopsRouteChange.WaypointAdded, cyclops, id, 0f, -10f, 0f);

        registry.RouteOf(cyclops.Id).Should().HaveCount(1);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task OnlyACyclopsHasARouteAndOnlyInsideTheWorldAndWithTheUpgradesOn()
    {
        await Send(navigator, CyclopsRouteChange.WaypointAdded, seamoth);
        await Send(navigator, CyclopsRouteChange.WaypointAdded, cyclops, null, 9000f, 0f, 0f);
        serverOptions.VehicleUpgrades = false;
        await Send(navigator, CyclopsRouteChange.WaypointAdded, cyclops);

        registry.RouteOf(cyclops.Id).Should().BeEmpty();
        registry.RouteOf(seamoth.Id).Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(3).And.OnlyContain(sent => sent.SessionId == navigator.SessionId);
    }

    [TestMethod]
    public async Task AnyPlayerCanRemoveAWaypointAndTheNextOneKeepsItsPlace()
    {
        NitroxId a = await Add(0f, 0f);
        NitroxId b = await Add(100f, 0f);
        NitroxId c = await Add(200f, 0f);
        await Advance(pilot, 1);
        registry.NextOf(cyclops.Id).Should().Be(1, "b is next");
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        await Send(navigator, CyclopsRouteChange.WaypointRemoved, cyclops, a);

        registry.RouteOf(cyclops.Id).Select(point => point.Id).Should().Equal(b, c);
        registry.NextOf(cyclops.Id).Should().Be(0, "b is still next, now at index 0");
        CyclopsRouteChanged relayed = packetSender.SentToOthers.Should().ContainSingle().Which.Packet.Should().BeOfType<CyclopsRouteChanged>().Subject;
        relayed.Change.Should().Be(CyclopsRouteChange.WaypointRemoved);
        relayed.Index.Should().Be(0, "the others take over the next waypoint that the server has");
        // ... and so does the remover, who worked it out for itself while an advance may have been on its way
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(navigator.SessionId);
        CyclopsRouteChanged told = (CyclopsRouteChanged)packetSender.Sent[0].Packet;
        told.Change.Should().Be(CyclopsRouteChange.Advanced);
        told.Index.Should().Be(0);
    }

    [TestMethod]
    public async Task RemovingAWaypointThatIsNotThereOrClearingAnEmptyRouteSaysNothing()
    {
        await Send(navigator, CyclopsRouteChange.WaypointRemoved, cyclops, new NitroxId());
        await Send(navigator, CyclopsRouteChange.RouteCleared, cyclops);

        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ClearingTakesTheWholeRouteAndTheOthersAreTold()
    {
        await Add(0f, 0f);
        await Add(100f, 0f);
        packetSender.SentToOthers.Clear();

        await Send(navigator, CyclopsRouteChange.RouteCleared, cyclops);

        registry.RouteOf(cyclops.Id).Should().BeEmpty();
        registry.NextOf(cyclops.Id).Should().Be(0);
        packetSender.SentToOthers.Should().ContainSingle().Which.Packet.Should().BeOfType<CyclopsRouteChanged>().Which.Change.Should().Be(CyclopsRouteChange.RouteCleared);
    }

    // ---- advancing

    [TestMethod]
    public async Task ThePlayerWhoSimulatesTheCyclopsAdvancesTheRouteByOneAndTheOthersAreTold()
    {
        await Add(0f, 0f);
        await Add(100f, 0f);
        packetSender.SentToOthers.Clear();

        NitroxId reached = registry.RouteOf(cyclops.Id)[0].Id;

        await Advance(pilot, 1);

        registry.NextOf(cyclops.Id).Should().Be(1);
        packetSender.SentToOthers.Should().ContainSingle();
        CyclopsRouteChanged told = (CyclopsRouteChanged)packetSender.SentToOthers[0].Packet;
        told.WaypointId.Should().Be(reached, "it names the waypoint that was reached");
        told.Change.Should().Be(CyclopsRouteChange.Advanced);
        told.Index.Should().Be(1);
        packetSender.SentToOthers[0].Excluded.Should().Be(pilot.SessionId);
    }

    [TestMethod]
    public async Task APlayerWhoDoesNotSimulateTheCyclopsCannotAdvanceItAndIsToldWhereTheRouteIs()
    {
        await Add(0f, 0f);
        await Add(100f, 0f);
        packetSender.SentToOthers.Clear();

        await Advance(navigator, 1);

        registry.NextOf(cyclops.Id).Should().Be(0);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(navigator.SessionId);
        ((CyclopsRouteChanged)packetSender.Sent[0].Packet).Index.Should().Be(0);
    }

    [TestMethod]
    public async Task AnAdvanceGoesForwardByOneNeverBackAndNeverPastTheEnd()
    {
        await Add(0f, 0f);
        await Add(100f, 0f);

        await Advance(pilot, 2);
        registry.NextOf(cyclops.Id).Should().Be(0, "two at once is refused");

        await Advance(pilot, 1);
        await Advance(pilot, 2);
        registry.NextOf(cyclops.Id).Should().Be(2, "the last waypoint reached: the route is finished");

        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();
        await Advance(pilot, 3);
        registry.NextOf(cyclops.Id).Should().Be(2, "a finished route does not advance");
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle("the pilot is told where the route is");
    }

    [TestMethod]
    public async Task ARepeatedAdvanceOfTheSameStepSaysNothing()
    {
        await Add(0f, 0f);
        await Add(100f, 0f);
        await Advance(pilot, 1);
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        await Advance(pilot, 1);

        registry.NextOf(cyclops.Id).Should().Be(1);
        packetSender.SentToOthers.Should().BeEmpty("nothing changed, so nobody else is told");
        packetSender.Sent.Should().ContainSingle("the sender is told where the route is, which is what it has").Which.SessionId.Should().Be(pilot.SessionId);
        ((CyclopsRouteChanged)packetSender.Sent[0].Packet).Index.Should().Be(1);
    }

    [TestMethod]
    public async Task AnAdvanceThatNamesAWaypointThatIsNotNextAnyMoreIsRefusedInsteadOfSkippingOne()
    {
        NitroxId a = await Add(0f, 0f);
        NitroxId b = await Add(100f, 0f);
        await Add(200f, 0f);
        // the navigator's removal of a reaches the server before the pilot's advance for a, which was decided on the pilot's copy of the route
        await Send(navigator, CyclopsRouteChange.WaypointRemoved, cyclops, a);
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsRouteChange.Advanced, cyclops, a, 0f, 0f, 0f, 1);

        registry.NextOf(cyclops.Id).Should().Be(0, "b is still next: without the id the advance would have skipped it");
        registry.RouteOf(cyclops.Id)[registry.NextOf(cyclops.Id)].Id.Should().Be(b);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(pilot.SessionId);
        ((CyclopsRouteChanged)packetSender.Sent[0].Packet).Index.Should().Be(0, "the pilot is told where the route is");
    }

    [TestMethod]
    public async Task AnAdvanceForAWaypointOfAClearedRouteDoesNotCountForTheNewRoute()
    {
        NitroxId old = await Add(0f, 0f);
        await Add(100f, 0f);
        await Send(navigator, CyclopsRouteChange.RouteCleared, cyclops);
        await Add(300f, 0f);
        await Add(400f, 0f);
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        await Send(pilot, CyclopsRouteChange.Advanced, cyclops, old, 0f, 0f, 0f, 1);

        registry.NextOf(cyclops.Id).Should().Be(0);
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnAdvanceThatNamesNoWaypointOrANullCyclopsIsRefusedWithoutAnError()
    {
        await Add(0f, 0f);
        await Add(100f, 0f);
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        await processor.Process(new AuthProcessorContext(pilot, packetSender), new CyclopsRouteChanged(CyclopsRouteChange.Advanced, cyclops.Id, null!, NitroxVector3.Zero, 1, pilot.SessionId, pilot.Name));
        await processor.Process(new AuthProcessorContext(pilot, packetSender), new CyclopsRouteChanged(CyclopsRouteChange.Advanced, null!, new NitroxId(), NitroxVector3.Zero, 1, pilot.SessionId, pilot.Name));
        await processor.Process(new AuthProcessorContext(pilot, packetSender), new CyclopsRouteChanged(CyclopsRouteChange.WaypointAdded, cyclops.Id, null!, NitroxVector3.Zero, 0, pilot.SessionId, pilot.Name));
        await processor.Process(new AuthProcessorContext(pilot, packetSender), new CyclopsRouteChanged(CyclopsRouteChange.WaypointRemoved, cyclops.Id, null!, NitroxVector3.Zero, 0, pilot.SessionId, pilot.Name));

        registry.NextOf(cyclops.Id).Should().Be(0);
        registry.RouteOf(cyclops.Id).Should().HaveCount(2);
        packetSender.SentToOthers.Should().BeEmpty();
    }

    // ---- joining and destroying

    [TestMethod]
    public async Task APlayerWhoJoinsIsToldTheRouteInOrderAndWhichWaypointIsNext()
    {
        NitroxId a = await Add(0f, 0f);
        NitroxId b = await Add(100f, 0f);
        await Advance(pilot, 1);
        packetSender.Sent.Clear();
        SessionId joiner = (ushort)9;

        await registry.SendSnapshotAsync(joiner);

        List<CyclopsRouteChanged> told = packetSender.Sent.Where(sent => sent.Packet is CyclopsRouteChanged).Select(sent => (CyclopsRouteChanged)sent.Packet).ToList();
        packetSender.Sent.Should().OnlyContain(sent => sent.SessionId == joiner);
        told.Should().HaveCount(3);
        told[0].Change.Should().Be(CyclopsRouteChange.WaypointAdded);
        told[0].WaypointId.Should().Be(a);
        told[0].Index.Should().Be(0, "each waypoint comes with its slot, so that one that arrives after a live change still lands in its place");
        told[1].WaypointId.Should().Be(b);
        told[1].Index.Should().Be(1);
        told[2].Change.Should().Be(CyclopsRouteChange.Advanced);
        told[2].Index.Should().Be(1);
    }

    [TestMethod]
    public async Task TheRouteOfADestroyedCyclopsIsGone()
    {
        await Add(0f, 0f);

        registry.EntityDestroyed(cyclops.Id);

        registry.RouteOf(cyclops.Id).Should().BeEmpty();
        registry.NextOf(cyclops.Id).Should().Be(0);
    }

    [TestMethod]
    public void ThePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsRouteChanged packet = new(CyclopsRouteChange.Advanced, new NitroxId(), new NitroxId(), new NitroxVector3(1.5f, -20f, 300f), 3, (SessionId)(ushort)3, "Alice");

        CyclopsRouteChanged received = (CyclopsRouteChanged)Packet.Deserialize(packet.Serialize());

        received.Should().BeEquivalentTo(packet);
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
