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
///     The marks on the map of a Cyclops' navigator console on the server (feature group 3, station 1): a mark can only be on a Cyclops, inside the world, of a known kind and within the limit of the Cyclops;
///     the server keeps the books, passes the change on to the others (with the sender as the owner), answers a refusal to the sender alone, and tells a player who joins and drops the marks of a destroyed Cyclops.
/// </summary>
[TestClass]
public class CyclopsNavChangedProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private EntityRegistry entityRegistry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CyclopsNavRegistry registry = null!;
    private CyclopsDroneArbiter droneArbiter = null!;
    private CyclopsNavChangedProcessor processor = null!;
    private Player navigator = null!;
    private Player crew = null!;
    private VehicleEntity cyclops = null!;
    private VehicleEntity secondCyclops = null!;
    private VehicleEntity seamoth = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        serverOptions = new SubnauticaServerOptions();
        registry = new CyclopsNavRegistry(packetSender);
        navigator = CreatePlayer(1);
        crew = CreatePlayer(2);
        cyclops = AddVehicle("Cyclops");
        secondCyclops = AddVehicle("Cyclops");
        seamoth = AddVehicle("Seamoth");
        droneArbiter = new CyclopsDroneArbiter(packetSender);
        processor = new CyclopsNavChangedProcessor(registry, entityRegistry, droneArbiter, Options.Create(serverOptions), NullLogger<CyclopsNavChangedProcessor>.Instance);
    }

    private VehicleEntity AddVehicle(string tech)
    {
        VehicleEntity entity = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), tech.ToLowerInvariant(), true, new NitroxId(), new NitroxTechType(tech), null);
        entityRegistry.AddOrUpdate(entity);
        return entity;
    }

    private Task Send(Player from, CyclopsNavChange change, VehicleEntity on, NitroxId? markId = null, byte kind = CyclopsNavRules.MARK_POINT, string label = "Mark 1", float x = -78f, float y = -10f, float z = 330f)
    {
        return processor.Process(new AuthProcessorContext(from, packetSender), new CyclopsNavChanged(change, on.Id, markId ?? new NitroxId(), new NitroxVector3(x, y, z), kind, label, from.SessionId, from.Name));
    }

    // ---- placing

    [TestMethod]
    public async Task AMarkOnACyclopsIsKeptAndTheOthersAreToldWithTheSenderAsOwner()
    {
        NitroxId id = new();

        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, id, CyclopsNavRules.MARK_HAZARD, "  Reef\twall  ");

        registry.Count(cyclops.Id).Should().Be(1);
        packetSender.SentToOthers.Should().ContainSingle();
        CyclopsNavChanged relayed = (CyclopsNavChanged)packetSender.SentToOthers[0].Packet;
        packetSender.SentToOthers[0].Excluded.Should().Be(navigator.SessionId, "the sender's own client has the mark already");
        relayed.Change.Should().Be(CyclopsNavChange.MarkAdded);
        relayed.MarkId.Should().Be(id);
        relayed.CyclopsId.Should().Be(cyclops.Id);
        relayed.MarkKind.Should().Be(CyclopsNavRules.MARK_HAZARD);
        relayed.Label.Should().Be("Reef wall", "the label is cleaned");
        relayed.Owner.Should().Be(navigator.SessionId, "the server puts the sender in as the owner");
        relayed.OwnerName.Should().Be(navigator.Name);
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task TheSameMarkAgainChangesNothingAndIsNotPassedOnTwice()
    {
        NitroxId id = new();
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, id);
        packetSender.SentToOthers.Clear();

        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, id);

        registry.Count(cyclops.Id).Should().Be(1);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().BeEmpty("it is not a refusal either");
    }

    [TestMethod]
    public async Task ACyclopsKeepsAtMostTheLimitAndTheNextMarkIsRefusedToTheSenderAlone()
    {
        for (int i = 0; i < CyclopsNavRules.MAX_MARKS; i++)
        {
            await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);
        }
        packetSender.SentToOthers.Clear();
        NitroxId extra = new();

        await Send(crew, CyclopsNavChange.MarkAdded, cyclops, extra);

        registry.Count(cyclops.Id).Should().Be(CyclopsNavRules.MAX_MARKS);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().ContainSingle().Which.SessionId.Should().Be(crew.SessionId);
        CyclopsNavChanged answer = (CyclopsNavChanged)packetSender.Sent[0].Packet;
        answer.Change.Should().Be(CyclopsNavChange.MarkRemoved, "the sender takes its own copy back");
        answer.MarkId.Should().Be(extra);
    }

    // ---- the marks of the Scout Drone

    private void SeatOperator(Player player, VehicleEntity on)
    {
        droneArbiter.Load(on.Id, new NitroxId());
        droneArbiter.TrySit(on.Id, player.SessionId, player.Name, out _);
    }

    [TestMethod]
    [DataRow(CyclopsNavRules.MARK_RESOURCE)]
    [DataRow(CyclopsNavRules.MARK_THREAT)]
    public async Task AMarkOfTheDronesOperatorIsKeptAndTheOthersAreTold(byte kind)
    {
        SeatOperator(navigator, cyclops);
        NitroxId id = new();

        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, id, kind, "Stalker");

        registry.Count(cyclops.Id).Should().Be(1);
        packetSender.SentToOthers.Should().ContainSingle();
        ((CyclopsNavChanged)packetSender.SentToOthers[0].Packet).MarkKind.Should().Be(kind);
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(CyclopsNavRules.MARK_RESOURCE)]
    [DataRow(CyclopsNavRules.MARK_THREAT)]
    public async Task ADroneMarkFromAPlayerWhoDoesNotFlyTheDroneIsRefusedToTheSenderAlone(byte kind)
    {
        SeatOperator(navigator, cyclops);
        NitroxId id = new();

        await Send(crew, CyclopsNavChange.MarkAdded, cyclops, id, kind, "Stalker");

        registry.Count(cyclops.Id).Should().Be(0);
        packetSender.SentToOthers.Should().BeEmpty();
        CyclopsNavChanged answer = (CyclopsNavChanged)packetSender.Sent.Should().ContainSingle().Which.Packet;
        answer.Change.Should().Be(CyclopsNavChange.MarkRemoved);
        answer.MarkId.Should().Be(id);
    }

    [TestMethod]
    public async Task TheOperatorOfAnotherCyclopsDoesNotMarkForThisOne()
    {
        SeatOperator(navigator, secondCyclops);

        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_RESOURCE);

        registry.Count(cyclops.Id).Should().Be(0);
        registry.Count(secondCyclops.Id).Should().Be(0);
    }

    [TestMethod]
    public async Task APlayerWithNoSeatMarksTheNavigatorKindsAsBefore()
    {
        await Send(crew, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_HAZARD);
        await Send(crew, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_TARGET);

        registry.Count(cyclops.Id).Should().Be(2);
    }

    [TestMethod]
    public async Task TheDronesMarksAndTheNavigatorsMarksHaveALimitEachAndDoNotStarveOneAnother()
    {
        SeatOperator(navigator, cyclops);
        for (int i = 0; i < CyclopsNavRules.MAX_MARKS; i++)
        {
            await Send(crew, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_HAZARD);
        }
        // the navigator's map is full; the drone still marks
        for (int i = 0; i < CyclopsNavRules.MAX_DRONE_MARKS; i++)
        {
            await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, kind: i % 2 == 0 ? CyclopsNavRules.MARK_RESOURCE : CyclopsNavRules.MARK_THREAT);
        }
        registry.Count(cyclops.Id).Should().Be(CyclopsNavRules.MAX_MARKS + CyclopsNavRules.MAX_DRONE_MARKS);
        packetSender.SentToOthers.Clear();
        packetSender.Sent.Clear();

        // each class is full now
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_THREAT);
        await Send(crew, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_POINT);

        registry.Count(cyclops.Id).Should().Be(CyclopsNavRules.MAX_MARKS + CyclopsNavRules.MAX_DRONE_MARKS);
        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => ((CyclopsNavChanged)sent.Packet).Change == CyclopsNavChange.MarkRemoved);

        // taking a drone mark away makes room for a drone mark, not for a navigator mark
        CyclopsNavRegistry.Mark oldest = registry.MarksOf(cyclops.Id).First(mark => CyclopsNavRules.IsDroneKind(mark.Kind));
        await Send(crew, CyclopsNavChange.MarkRemoved, cyclops, oldest.Id);
        await Send(crew, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_HAZARD);
        registry.Count(cyclops.Id).Should().Be(CyclopsNavRules.MAX_MARKS + CyclopsNavRules.MAX_DRONE_MARKS - 1);
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, kind: CyclopsNavRules.MARK_RESOURCE);
        registry.Count(cyclops.Id).Should().Be(CyclopsNavRules.MAX_MARKS + CyclopsNavRules.MAX_DRONE_MARKS);
    }

    [TestMethod]
    public async Task TheLimitIsPerCyclops()
    {
        for (int i = 0; i < CyclopsNavRules.MAX_MARKS; i++)
        {
            await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);
        }

        await Send(navigator, CyclopsNavChange.MarkAdded, secondCyclops);

        registry.Count(secondCyclops.Id).Should().Be(1);
    }

    [TestMethod]
    public async Task AMarkIdThatBelongsToAnotherCyclopsIsRefused()
    {
        NitroxId id = new();
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, id);

        await Send(navigator, CyclopsNavChange.MarkAdded, secondCyclops, id);

        registry.Count(secondCyclops.Id).Should().Be(0);
        registry.Count(cyclops.Id).Should().Be(1);
    }

    [TestMethod]
    public async Task OnlyACyclopsCanHaveMarks()
    {
        await Send(navigator, CyclopsNavChange.MarkAdded, seamoth);
        CyclopsNavChanged stranger = new(CyclopsNavChange.MarkAdded, new NitroxId(), new NitroxId(), new NitroxVector3(1f, -5f, 1f), 0, "x", navigator.SessionId, navigator.Name);
        await processor.Process(new AuthProcessorContext(navigator, packetSender), stranger);

        registry.TotalCount.Should().Be(0);
        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == navigator.SessionId);
    }

    [TestMethod]
    [DataRow(float.NaN, -10f, 330f)]
    [DataRow(-78f, float.PositiveInfinity, 330f)]
    [DataRow(9000f, -10f, 330f)]
    [DataRow(-78f, -10f, -9000f)]
    [DataRow(-78f, -4000f, 330f)]
    [DataRow(-78f, 900f, 330f)]
    public async Task AMarkOutsideTheWorldIsRefused(float x, float y, float z)
    {
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, null, CyclopsNavRules.MARK_POINT, "x", x, y, z);

        registry.TotalCount.Should().Be(0);
        packetSender.Sent.Should().ContainSingle();
        packetSender.SentToOthers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnUnknownKindOfMarkIsRefused()
    {
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, null, 77);

        registry.TotalCount.Should().Be(0);
        packetSender.Sent.Should().ContainSingle();
    }

    [TestMethod]
    public async Task WithTheVehicleUpgradesSwitchedOffNoMarkIsAccepted()
    {
        serverOptions.VehicleUpgrades = false;

        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);

        registry.TotalCount.Should().Be(0);
        packetSender.Sent.Should().ContainSingle();
    }

    // ---- taking marks away

    [TestMethod]
    public async Task AnyPlayerCanRemoveAMarkAndTheOthersAreTold()
    {
        NitroxId id = new();
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, id, CyclopsNavRules.MARK_TARGET, "Dock");
        packetSender.SentToOthers.Clear();

        await Send(crew, CyclopsNavChange.MarkRemoved, cyclops, id);

        registry.Count(cyclops.Id).Should().Be(0);
        packetSender.SentToOthers.Should().ContainSingle();
        CyclopsNavChanged told = (CyclopsNavChanged)packetSender.SentToOthers[0].Packet;
        packetSender.SentToOthers[0].Excluded.Should().Be(crew.SessionId);
        told.Change.Should().Be(CyclopsNavChange.MarkRemoved);
        told.MarkId.Should().Be(id);
        told.OwnerName.Should().Be(navigator.Name, "the mark names the player who placed it, not the one who removed it");
    }

    [TestMethod]
    public async Task RemovingAMarkThatIsNotThereSaysNothing()
    {
        await Send(crew, CyclopsNavChange.MarkRemoved, cyclops, new NitroxId());

        packetSender.SentToOthers.Should().BeEmpty();
        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ClearingTakesEveryMarkOfThatCyclopsOnly()
    {
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);
        await Send(navigator, CyclopsNavChange.MarkAdded, secondCyclops);
        packetSender.SentToOthers.Clear();

        await Send(crew, CyclopsNavChange.MarksCleared, cyclops);

        registry.Count(cyclops.Id).Should().Be(0);
        registry.Count(secondCyclops.Id).Should().Be(1);
        packetSender.SentToOthers.Should().ContainSingle().Which.Packet.Should().BeOfType<CyclopsNavChanged>().Which.Change.Should().Be(CyclopsNavChange.MarksCleared);
    }

    [TestMethod]
    public async Task ClearingACyclopsWithoutMarksSaysNothing()
    {
        await Send(crew, CyclopsNavChange.MarksCleared, cyclops);

        packetSender.SentToOthers.Should().BeEmpty();
    }

    // ---- joining and destroying

    [TestMethod]
    public async Task APlayerWhoJoinsIsToldAboutEveryMarkThatIsOn()
    {
        NitroxId first = new();
        NitroxId second = new();
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops, first, CyclopsNavRules.MARK_POINT, "A");
        await Send(crew, CyclopsNavChange.MarkAdded, secondCyclops, second, CyclopsNavRules.MARK_HAZARD, "B");
        packetSender.Sent.Clear();
        SessionId joiner = (ushort)9;

        await registry.SendSnapshotAsync(joiner);

        packetSender.Sent.Should().HaveCount(2).And.OnlyContain(sent => sent.SessionId == joiner);
        List<CyclopsNavChanged> told = new();
        foreach ((Packet packet, SessionId _) in packetSender.Sent)
        {
            told.Add((CyclopsNavChanged)packet);
        }
        told.Should().OnlyContain(packet => packet.Change == CyclopsNavChange.MarkAdded);
        told.Should().Contain(packet => packet.MarkId.Equals(first) && packet.Label == "A" && packet.OwnerName == navigator.Name);
        told.Should().Contain(packet => packet.MarkId.Equals(second) && packet.MarkKind == CyclopsNavRules.MARK_HAZARD && packet.CyclopsId.Equals(secondCyclops.Id));
    }

    [TestMethod]
    public async Task TheMarksOfADestroyedCyclopsAreGoneAndTheOtherCyclopsKeepsItsOwn()
    {
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);
        await Send(navigator, CyclopsNavChange.MarkAdded, secondCyclops);

        int dropped = registry.EntityDestroyed(cyclops.Id);

        dropped.Should().Be(1);
        registry.Count(cyclops.Id).Should().Be(0);
        registry.Count(secondCyclops.Id).Should().Be(1);
        registry.EntityDestroyed(seamoth.Id).Should().Be(0, "a Seamoth has no marks");
    }

    [TestMethod]
    public async Task MarksStayWhenThePlayerWhoPlacedThemLeaves()
    {
        await Send(navigator, CyclopsNavChange.MarkAdded, cyclops);

        // the registry is not a session cleaner: nothing in it depends on a session
        registry.MarksOf(cyclops.Id).Should().ContainSingle().Which.Owner.Should().Be(navigator.SessionId);
        registry.Snapshot().Should().HaveCount(1);
    }

    [TestMethod]
    public void ThePacketSurvivesTheSerializer()
    {
        Packet.InitSerializer();
        CyclopsNavChanged packet = new(CyclopsNavChange.MarkAdded, new NitroxId(), new NitroxId(), new NitroxVector3(1.5f, -20f, 300f), CyclopsNavRules.MARK_TARGET, "Dock", (SessionId)(ushort)3, "Alice");

        CyclopsNavChanged received = (CyclopsNavChanged)Packet.Deserialize(packet.Serialize());

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
