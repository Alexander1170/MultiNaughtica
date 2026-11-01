using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Core;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Services.Core;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using NSubstitute;
using ZLogger;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Crafting from lockers: what the server sends for a claim. The removals go to everybody (the claiming player's copy of the items goes away with the others'), then the answer to the
///     claiming player alone; a refused claim sends no removal; with the option off nothing is touched.
/// </summary>
[TestClass]
public class CraftingClaimRequestProcessorTest
{
    private RecordingPacketSender packetSender = null!;
    private EntityRegistry registry = null!;
    private SubnauticaServerOptions serverOptions = null!;
    private CraftingClaimRequestProcessor processor = null!;
    private Player alice = null!;
    private AuthProcessorContext context = null!;
    private BuildEntity baseEntity = null!;
    private ModuleEntity locker = null!;

    [TestInitialize]
    public void Init()
    {
        packetSender = new RecordingPacketSender();
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        WorldEntityManager worldEntityManager = new(Substitute.For<IPacketSender>(), registry, null!, null!, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        EntitySimulation entitySimulation = new(Substitute.For<IPacketSender>(), registry, worldEntityManager, new SimulationOwnershipData(), null!, NullLogger<EntitySimulation>.Instance);
        serverOptions = new SubnauticaServerOptions();
        processor = new CraftingClaimRequestProcessor(new CraftingClaimArbiter(), registry, worldEntityManager, entitySimulation, Options.Create(serverOptions), NullLogger<CraftingClaimRequestProcessor>.Instance);
        alice = CreatePlayer(1);
        context = new AuthProcessorContext(alice, packetSender);

        baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        registry.AddOrUpdate(baseEntity);
        locker = ModuleEntity.MakeEmpty();
        locker.Id = new NitroxId();
        locker.ParentId = baseEntity.Id;
        registry.AddOrUpdate(locker);
    }

    private InventoryItemEntity AddItem(string techType = "Titanium")
    {
        InventoryItemEntity item = new(new NitroxId(), "classId", new NitroxTechType(techType), null, locker.Id, []);
        registry.AddOrUpdate(item);
        return item;
    }

    [TestMethod]
    public void TheOptionIsOnByDefault()
    {
        new SubnauticaServerOptions().CraftFromLockers.Should().BeTrue();
        new SessionSettings().CraftFromLockers.Should().BeTrue();
    }

    [TestMethod]
    public async Task AGrantedClaimDestroysTheItemsForEverybodyAndThenAnswersTheClaimant()
    {
        InventoryItemEntity a = AddItem();
        InventoryItemEntity b = AddItem("Copper");

        await processor.Process(context, new CraftingClaimRequest(7, baseEntity.Id, [a.Id, b.Id]));

        packetSender.Sent.Should().HaveCount(3);
        packetSender.Sent[0].Packet.Should().BeOfType<EntityDestroyed>().Which.Id.Should().Be(a.Id);
        packetSender.Sent[0].Target.Should().Be(RecordingPacketSender.EVERYBODY);
        packetSender.Sent[1].Packet.Should().BeOfType<EntityDestroyed>().Which.Id.Should().Be(b.Id);
        packetSender.Sent[1].Target.Should().Be(RecordingPacketSender.EVERYBODY);
        CraftingClaimResponse response = packetSender.Sent[2].Packet.Should().BeOfType<CraftingClaimResponse>().Subject;
        response.ClaimId.Should().Be(7);
        response.Outcome.Should().Be(CraftingClaimOutcome.Granted);
        response.UnavailableIds.Should().BeEmpty();
        packetSender.Sent[2].Target.Should().Be(alice.SessionId.ToString());
        registry.TryGetEntityById(a.Id, out Entity? _).Should().BeFalse();
        registry.TryGetEntityById(b.Id, out Entity? _).Should().BeFalse();
    }

    [TestMethod]
    public async Task ARefusedClaimSendsNoRemovalAndListsTheUnavailableItems()
    {
        InventoryItemEntity a = AddItem();
        NitroxId gone = new();

        await processor.Process(context, new CraftingClaimRequest(8, baseEntity.Id, [a.Id, gone]));

        packetSender.Sent.Should().ContainSingle();
        CraftingClaimResponse response = packetSender.Sent[0].Packet.Should().BeOfType<CraftingClaimResponse>().Subject;
        response.ClaimId.Should().Be(8);
        response.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        response.UnavailableIds.Should().Equal(gone);
        registry.TryGetEntityById(a.Id, out Entity? _).Should().BeTrue("a refused claim takes nothing");
    }

    [TestMethod]
    public async Task TheSecondOfTwoClaimsForTheLastItemIsRefused()
    {
        InventoryItemEntity last = AddItem();
        Player bob = CreatePlayer(2);

        await processor.Process(context, new CraftingClaimRequest(1, baseEntity.Id, [last.Id]));
        await processor.Process(new AuthProcessorContext(bob, packetSender), new CraftingClaimRequest(1, baseEntity.Id, [last.Id]));

        List<CraftingClaimResponse> responses = packetSender.Sent.Select(sent => sent.Packet).OfType<CraftingClaimResponse>().ToList();
        responses.Select(response => response.Outcome).Should().Equal(CraftingClaimOutcome.Granted, CraftingClaimOutcome.Taken);
        packetSender.Sent.Select(sent => sent.Packet).OfType<EntityDestroyed>().Should().ContainSingle("the item is destroyed once");
        packetSender.Sent.Last().Target.Should().Be(bob.SessionId.ToString());
    }

    [TestMethod]
    public async Task WithTheOptionOffNothingIsTouchedAndTheAnswerSaysSo()
    {
        serverOptions.CraftFromLockers = false;
        InventoryItemEntity a = AddItem();

        await processor.Process(context, new CraftingClaimRequest(9, baseEntity.Id, [a.Id]));

        packetSender.Sent.Should().ContainSingle();
        packetSender.Sent[0].Packet.Should().BeOfType<CraftingClaimResponse>().Which.Outcome.Should().Be(CraftingClaimOutcome.Disabled);
        registry.TryGetEntityById(a.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public async Task ANonsenseRequestIsInvalid()
    {
        await processor.Process(context, new CraftingClaimRequest(3, baseEntity.Id, []));
        await processor.Process(context, new CraftingClaimRequest(4, baseEntity.Id, null!));

        packetSender.Sent.Should().HaveCount(2, "every request is answered");
        packetSender.Sent.Select(sent => (CraftingClaimResponse)sent.Packet).Select(response => (response.ClaimId, response.Outcome)).Should()
                     .Equal((3, CraftingClaimOutcome.Invalid), (4, CraftingClaimOutcome.Invalid));
    }

    [TestMethod]
    public async Task APlayerOrAPlanterAsTheScopeIsInvalidAndNothingIsDestroyed()
    {
        PlayerEntity bob = new(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, "player", true, new NitroxId(), new NitroxTechType("Player"), null, baseEntity.Id, []);
        registry.AddOrUpdate(bob);
        InventoryItemEntity hers = new(new NitroxId(), "classId", new NitroxTechType("Quartz"), null, bob.Id, []);
        registry.AddOrUpdate(hers);

        await processor.Process(context, new CraftingClaimRequest(5, bob.Id, [hers.Id]));

        packetSender.Sent.Should().ContainSingle().Which.Packet.Should().BeOfType<CraftingClaimResponse>().Which.Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(hers.Id, out Entity? _).Should().BeTrue("the items of a player's inventory are not touched by a claim");
    }

    [TestMethod]
    public async Task TheLoserOfTwoClaimsHearsOfTheRemovalBeforeItsOwnRefusal()
    {
        // Every send takes a moment: without the claims held apart while they are delivered, the second claim is decided (and refused) in the middle of the first one's delivery
        packetSender.DelayMs = 20;
        InventoryItemEntity last = AddItem();
        InventoryItemEntity other = AddItem("Copper");
        Player bob = CreatePlayer(2);

        Task first = processor.Process(context, new CraftingClaimRequest(1, baseEntity.Id, [last.Id, other.Id]));
        Task second = processor.Process(new AuthProcessorContext(bob, packetSender), new CraftingClaimRequest(2, baseEntity.Id, [last.Id]));
        await Task.WhenAll(first, second);

        string order = string.Join(",", packetSender.Sent.Select(sent => sent.Packet switch
        {
            EntityDestroyed => "destroyed",
            CraftingClaimResponse response => $"{response.ClaimId}:{response.Outcome}",
            _ => "other"
        }));
        order.Should().Be("destroyed,destroyed,1:Granted,2:Taken");
    }

    [TestMethod]
    public async Task AnAnswerThatAClientSendsIsNotRelayedToTheOtherPlayers()
    {
        // The answer to a claim is the server's alone. A relayed one would let a modified client grant a claim to another player (a craft without items taken from the lockers).
        // the refusal is logged once with ZLogger, which needs a logger that really is one
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddZLoggerStream(Stream.Null));
        DefaultServerPacketProcessor relay = new(loggerFactory.CreateLogger<DefaultServerPacketProcessor>());

        await relay.Process(context, new CraftingClaimResponse(1, CraftingClaimOutcome.Granted, []));

        packetSender.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public void TheJoiningPlayerIsToldTheOptionOfTheServerAndTheSessionStateOfTheServer()
    {
        SessionSettings session = new() { FastHatch = true, FastGrow = false };
        SubnauticaServerOptions off = new() { CraftFromLockers = false };
        SubnauticaServerOptions on = new() { CraftFromLockers = true };

        SessionSettings told = JoiningManager.BuildSessionSettings(session, off);

        told.CraftFromLockers.Should().BeFalse();
        told.FastHatch.Should().BeTrue();
        told.FastGrow.Should().BeFalse();
        told.Should().NotBeSameAs(session, "the packet gets a copy");
        JoiningManager.BuildSessionSettings(session, on).CraftFromLockers.Should().BeTrue();
        session.CraftFromLockers.Should().BeTrue("the server's own settings are not changed by telling a player");
    }

    [TestMethod]
    public void TheRequestKeepsItsFieldsThroughTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId scope = new();
        NitroxId first = new();
        NitroxId second = new();

        CraftingClaimRequest received = (CraftingClaimRequest)Packet.Deserialize(new CraftingClaimRequest(12, scope, [first, second]).Serialize());

        received.ClaimId.Should().Be(12);
        received.ScopeId.Should().Be(scope);
        received.ItemIds.Should().Equal(first, second);
    }

    [TestMethod]
    public void TheResponseKeepsItsFieldsThroughTheSerializer()
    {
        Packet.InitSerializer();
        NitroxId taken = new();

        CraftingClaimResponse received = (CraftingClaimResponse)Packet.Deserialize(new CraftingClaimResponse(13, CraftingClaimOutcome.Taken, [taken]).Serialize());

        received.ClaimId.Should().Be(13);
        received.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        received.UnavailableIds.Should().Equal(taken);
    }

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private sealed class RecordingPacketSender : IPacketSender
    {
        public const string EVERYBODY = "everybody";

        private readonly List<(Packet Packet, string Target)> sent = [];

        /// <summary>How long a send takes (it yields), to let the claims of two players overlap.</summary>
        public int DelayMs { get; set; }

        public List<(Packet Packet, string Target)> Sent
        {
            get
            {
                lock (sent)
                {
                    return [.. sent];
                }
            }
        }

        public async ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet
        {
            await Record(packet, sessionId.ToString());
        }

        public async ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet
        {
            await Record(packet, EVERYBODY);
        }

        private async Task Record(Packet packet, string target)
        {
            // A real send is out when the call is: the delay comes first, the record afterwards
            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs);
            }
            lock (sent)
            {
                sent.Add((packet, target));
            }
        }

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => throw new AssertFailedException("A claim is answered to the claimant and announced to everybody");
    }
}
