using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;
using static Nitrox.Server.Subnautica.Models.Packets.Processors.PlayerParentScene;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The engineer console is ended from processors of other packets (the Cyclops left, destroyed or turned into a wreck). The wiring test only checks that those processors ask for the arbiter; these run the
///     processors, so that a deleted hook line fails a test.
/// </summary>
[TestClass]
public class CyclopsEngineerHooksTest
{
    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private static PlayerManager NoPlayers() => new(null!, null!, Options.Create(new SubnauticaServerOptions()), NullLogger<PlayerManager>.Instance);

    [TestMethod]
    public async Task LeavingTheCyclopsEndsTheEngineersSeatThroughTheSubRootProcessor()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        CyclopsEngineerArbiter engineers = new(scene.PacketSender);
        engineers.TrySit(scene.SubRoot.Id, scene.Player.SessionId, scene.Player.Name, out _);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), engineers, new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        engineers.Count.Should().Be(0);
        await scene.PacketSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsEngineerSeat>(seat => seat.Message == CyclopsEngineerSeatMessage.Released && seat.Reason == CyclopsEngineerSeatReason.LeftCyclops && seat.SessionId == scene.Player.SessionId));
    }

    [TestMethod]
    public async Task MovingWithinTheSameCyclopsKeepsTheEngineersSeat()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        CyclopsEngineerArbiter engineers = new(scene.PacketSender);
        engineers.TrySit(scene.SubRoot.Id, scene.Player.SessionId, scene.Player.Name, out _);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), engineers, new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Of(scene.SubRoot.Id)));

        engineers.Count.Should().Be(1);
        await scene.PacketSender.DidNotReceive().SendPacketToAllAsync(Arg.Any<CyclopsEngineerSeat>());
    }

    [TestMethod]
    public async Task ACyclopsThatBecomesAWreckLosesItsEngineerAndWhatWasSetOnItThroughTheMetadataProcessor()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        EntityRegistry registry = new(NullLogger<EntityRegistry>.Instance);
        VehicleEntity cyclops = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "cyclops", true, new NitroxId(), new NitroxTechType("Cyclops"), null);
        registry.AddOrUpdate(cyclops);
        Player engineer = CreatePlayer(2);
        CyclopsEngineerArbiter engineers = new(packetSender);
        engineers.TrySit(cyclops.Id, engineer.SessionId, engineer.Name, out _);
        engineers.SetRoute(cyclops.Id, EngineerRoute.Engines);
        EntityMetadataUpdateProcessor processor = new(NoPlayers(), registry, new CyclopsGunnerArbiter(packetSender), engineers, new CyclopsDroneArbiter(packetSender), new CyclopsNavRegistry(packetSender), NullLogger<EntityMetadataUpdateProcessor>.Instance);

        await processor.Process(new AuthProcessorContext(CreatePlayer(1), packetSender), new EntityMetadataUpdate(cyclops.Id, new CyclopsMetadata(false, false, false, false, 0, 100, true)));

        engineers.Count.Should().Be(0);
        engineers.RouteOf(cyclops.Id).Should().Be(EngineerRoute.Balanced, "a wreck forgets what was set on it");
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsEngineerSeat>(seat => seat.Message == CyclopsEngineerSeatMessage.Released && seat.Reason == CyclopsEngineerSeatReason.CyclopsDestroyed && seat.SessionId == engineer.SessionId));
    }

    [TestMethod]
    public async Task ACyclopsThatIsDestroyedLosesItsEngineerAndWhatWasSetOnItThroughTheDestroyedProcessor()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        EntityRegistry registry = new(NullLogger<EntityRegistry>.Instance);
        PlayerManager players = NoPlayers();
        // the pattern of EntitySpawnedByClientModuleSlotTest: the real world entity manager and simulation; the destroyed id is not in the registry, which is all the hook needs
        WorldEntityManager worldEntityManager = new(packetSender, registry, null!, null!, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        EntitySimulation simulation = new(packetSender, registry, worldEntityManager, new SimulationOwnershipData(), players, NullLogger<EntitySimulation>.Instance);
        NitroxId cyclopsId = new();
        Player engineer = CreatePlayer(2);
        CyclopsEngineerArbiter engineers = new(packetSender);
        engineers.TrySit(cyclopsId, engineer.SessionId, engineer.Name, out _);
        engineers.SetRoute(cyclopsId, EngineerRoute.Shields);
        EntityDestroyedPacketProcessor processor = new(players, simulation, worldEntityManager, new VehicleTowRegistry(packetSender), new SeamothRiderArbiter(packetSender), new CyclopsNavRegistry(packetSender), new CyclopsGunnerArbiter(packetSender), engineers, new CyclopsDroneArbiter(packetSender));

        await processor.Process(new AuthProcessorContext(CreatePlayer(1), packetSender), new EntityDestroyed(cyclopsId));

        engineers.Count.Should().Be(0);
        engineers.RouteOf(cyclopsId).Should().Be(EngineerRoute.Balanced);
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsEngineerSeat>(seat => seat.Message == CyclopsEngineerSeatMessage.Released && seat.Reason == CyclopsEngineerSeatReason.CyclopsDestroyed));
    }
}
