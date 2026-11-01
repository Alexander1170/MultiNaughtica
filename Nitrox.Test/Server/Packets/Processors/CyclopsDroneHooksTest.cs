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
///     The drone bay is ended from processors of other packets (the Cyclops left, destroyed or turned into a wreck, the drone destroyed, the player dead). These run the processors, so that a deleted hook line fails a test.
/// </summary>
[TestClass]
public class CyclopsDroneHooksTest
{
    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private static PlayerManager NoPlayers() => new(null!, null!, Options.Create(new SubnauticaServerOptions()), NullLogger<PlayerManager>.Instance);

    private static async Task<(CyclopsDroneArbiter Arbiter, NitroxId DroneId)> ArbiterWithAFlyingDrone(IPacketSender packetSender, NitroxId cyclopsId, Player operatorPlayer)
    {
        CyclopsDroneArbiter arbiter = new(packetSender);
        NitroxId droneId = new();
        arbiter.Load(cyclopsId, droneId);
        arbiter.TrySit(cyclopsId, operatorPlayer.SessionId, operatorPlayer.Name, out _);
        arbiter.SetDocked(cyclopsId, droneId, false);
        await Task.CompletedTask;
        return (arbiter, droneId);
    }

    [TestMethod]
    public async Task LeavingTheCyclopsEndsTheOperatorsSeatThroughTheSubRootProcessor()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        (CyclopsDroneArbiter drones, _) = await ArbiterWithAFlyingDrone(scene.PacketSender, scene.SubRoot.Id, scene.Player);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), drones);

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        drones.Count.Should().Be(0);
        await scene.PacketSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsDroneSeat>(seat => seat.Message == CyclopsDroneSeatMessage.Released && seat.Reason == CyclopsDroneSeatReason.LeftCyclops && seat.SessionId == scene.Player.SessionId));
    }

    [TestMethod]
    public async Task MovingWithinTheSameCyclopsKeepsTheOperatorsSeat()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        (CyclopsDroneArbiter drones, _) = await ArbiterWithAFlyingDrone(scene.PacketSender, scene.SubRoot.Id, scene.Player);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), drones);

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Of(scene.SubRoot.Id)));

        drones.Count.Should().Be(1);
        await scene.PacketSender.DidNotReceive().SendPacketToAllAsync(Arg.Any<CyclopsDroneSeat>());
    }

    [TestMethod]
    public async Task TheDeathOfTheOperatorEndsTheSeatThroughTheDeathProcessor()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        Player operatorPlayer = CreatePlayer(2);
        NitroxId cyclopsId = new();
        (CyclopsDroneArbiter drones, _) = await ArbiterWithAFlyingDrone(packetSender, cyclopsId, operatorPlayer);
        PlayerDeathEventProcessor death = new(Options.Create(new SubnauticaServerOptions()), new CyclopsGunnerArbiter(packetSender), new CyclopsEngineerArbiter(packetSender), drones);

        await death.Process(new AuthProcessorContext(operatorPlayer, packetSender), new PlayerDeathEvent(operatorPlayer.SessionId, NitroxVector3.Zero));

        drones.Count.Should().Be(0);
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsDroneSeat>(seat => seat.Reason == CyclopsDroneSeatReason.Died && seat.SessionId == operatorPlayer.SessionId));
    }

    [TestMethod]
    public async Task ACyclopsThatBecomesAWreckLosesItsOperatorAndItsBayThroughTheMetadataProcessor()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        EntityRegistry registry = new(NullLogger<EntityRegistry>.Instance);
        VehicleEntity cyclops = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "cyclops", true, new NitroxId(), new NitroxTechType("Cyclops"), null);
        registry.AddOrUpdate(cyclops);
        Player operatorPlayer = CreatePlayer(2);
        (CyclopsDroneArbiter drones, _) = await ArbiterWithAFlyingDrone(packetSender, cyclops.Id, operatorPlayer);
        EntityMetadataUpdateProcessor processor = new(NoPlayers(), registry, new CyclopsGunnerArbiter(packetSender), new CyclopsEngineerArbiter(packetSender), drones, new CyclopsNavRegistry(packetSender), NullLogger<EntityMetadataUpdateProcessor>.Instance);

        await processor.Process(new AuthProcessorContext(CreatePlayer(1), packetSender), new EntityMetadataUpdate(cyclops.Id, new CyclopsMetadata(false, false, false, false, 0, 100, true)));

        drones.Count.Should().Be(0);
        drones.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse("a wreck forgets its bay");
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsDroneSeat>(seat => seat.Reason == CyclopsDroneSeatReason.CyclopsDestroyed && seat.SessionId == operatorPlayer.SessionId));
    }

    private static (EntityDestroyedPacketProcessor Processor, IPacketSender Sender) DestroyedProcessor(CyclopsDroneArbiter drones, IPacketSender packetSender)
    {
        EntityRegistry registry = new(NullLogger<EntityRegistry>.Instance);
        PlayerManager players = NoPlayers();
        WorldEntityManager worldEntityManager = new(packetSender, registry, null!, null!, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        EntitySimulation simulation = new(packetSender, registry, worldEntityManager, new SimulationOwnershipData(), players, NullLogger<EntitySimulation>.Instance);
        return (new EntityDestroyedPacketProcessor(players, simulation, worldEntityManager, new VehicleTowRegistry(packetSender), new SeamothRiderArbiter(packetSender), new CyclopsNavRegistry(packetSender), new CyclopsGunnerArbiter(packetSender), new CyclopsEngineerArbiter(packetSender), drones), packetSender);
    }

    [TestMethod]
    public async Task ACyclopsThatIsDestroyedLosesItsOperatorAndItsBayThroughTheDestroyedProcessor()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        NitroxId cyclopsId = new();
        Player operatorPlayer = CreatePlayer(2);
        (CyclopsDroneArbiter drones, _) = await ArbiterWithAFlyingDrone(packetSender, cyclopsId, operatorPlayer);
        (EntityDestroyedPacketProcessor processor, _) = DestroyedProcessor(drones, packetSender);

        await processor.Process(new AuthProcessorContext(CreatePlayer(1), packetSender), new EntityDestroyed(cyclopsId));

        drones.Count.Should().Be(0);
        drones.TryGetDrone(cyclopsId, out _, out _).Should().BeFalse();
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsDroneSeat>(seat => seat.Reason == CyclopsDroneSeatReason.CyclopsDestroyed));
    }

    [TestMethod]
    public async Task ADroneThatIsDestroyedEmptiesItsBayEndsTheSeatAndEverybodyIsToldThroughTheDestroyedProcessor()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        NitroxId cyclopsId = new();
        Player operatorPlayer = CreatePlayer(2);
        (CyclopsDroneArbiter drones, NitroxId droneId) = await ArbiterWithAFlyingDrone(packetSender, cyclopsId, operatorPlayer);
        (EntityDestroyedPacketProcessor processor, _) = DestroyedProcessor(drones, packetSender);

        await processor.Process(new AuthProcessorContext(CreatePlayer(1), packetSender), new EntityDestroyed(droneId));

        drones.Count.Should().Be(0);
        drones.TryGetDrone(cyclopsId, out _, out _).Should().BeFalse("the bay is empty");
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsDroneSeat>(seat => seat.Reason == CyclopsDroneSeatReason.DroneDestroyed && seat.SessionId == operatorPlayer.SessionId));
        await packetSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsDroneState>(state => state.CyclopsId.Equals(cyclopsId) && !state.DroneId.HasValue));
    }
}
