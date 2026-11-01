using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors;
using Nitrox.Server.Subnautica.Models.Serialization;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The drone bays are in memory only; what a saved world keeps of one is the metadata on the drone's entity (<see cref="ScoutDroneMetadata" />), which only the server writes. The first player who joins gets the bays
///     rebuilt from it, a drone in the dock rides with its Cyclops on the server (so that the cells it is loaded in follow it), and the metadata travels in a packet and in the save.
/// </summary>
[TestClass]
public class CyclopsDroneRestoreTest
{
    private EntityRegistry registry = null!;
    private CyclopsDroneArbiter arbiter = null!;
    private IPacketSender packetSender = null!;

    [TestInitialize]
    public void Init()
    {
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        packetSender = Substitute.For<IPacketSender>();
        arbiter = new CyclopsDroneArbiter(packetSender);
    }

    private VehicleEntity AddCyclops(bool wreck = false)
    {
        VehicleEntity cyclops = new(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "cyclops", true, new NitroxId(), new NitroxTechType("Cyclops"), null);
        if (wreck)
        {
            cyclops.Metadata = new CyclopsMetadata(false, false, false, false, 0, 100, true);
        }
        registry.AddOrUpdate(cyclops);
        return cyclops;
    }

    private WorldEntity AddDrone(EntityMetadata? metadata, string tech = ScoutDroneRules.ITEM_TECH_TYPE_NAME)
    {
        GlobalRootEntity drone = new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "classId", false, new NitroxId(), new NitroxTechType(tech), null, null, []);
        drone.Metadata = metadata;
        registry.AddOrUpdate(drone);
        return drone;
    }

    [TestMethod]
    public void ADockedDroneAndADroneThatIsOutComeBackIntoTheirBays()
    {
        VehicleEntity first = AddCyclops();
        VehicleEntity second = AddCyclops();
        WorldEntity docked = AddDrone(new ScoutDroneMetadata(first.Id, true));
        WorldEntity adrift = AddDrone(new ScoutDroneMetadata(second.Id, false));
        AddDrone(null);

        int rebuilt = arbiter.RestoreFrom(registry);

        rebuilt.Should().Be(2);
        arbiter.TryGetDrone(first.Id, out NitroxId? firstDrone, out bool firstDocked).Should().BeTrue();
        firstDrone.Should().Be(docked.Id);
        firstDocked.Should().BeTrue();
        arbiter.TryGetDrone(second.Id, out NitroxId? secondDrone, out bool secondDocked).Should().BeTrue();
        secondDrone.Should().Be(adrift.Id);
        secondDocked.Should().BeFalse();
        arbiter.TryGetDockedDrone(first.Id, out NitroxId? dockedId).Should().BeTrue();
        dockedId.Should().Be(docked.Id);
        arbiter.TryGetDockedDrone(second.Id, out _).Should().BeFalse("a drone that is out does not ride with the Cyclops");
        arbiter.StateOf(second.Id).Docked.Should().BeFalse();
    }

    [TestMethod]
    public void ADroneWhoseCyclopsIsGoneOrWreckedOrWhichIsNoScoutDroneLosesItsBay()
    {
        VehicleEntity wreck = AddCyclops(wreck: true);
        WorldEntity lost = AddDrone(new ScoutDroneMetadata(new NitroxId(), true));
        WorldEntity atAWreck = AddDrone(new ScoutDroneMetadata(wreck.Id, true));
        WorldEntity hurtAtAWreck = AddDrone(new ScoutDroneMetadata(wreck.Id, true, 20f));
        VehicleEntity cyclops = AddCyclops();
        WorldEntity notADrone = AddDrone(new ScoutDroneMetadata(cyclops.Id, true), tech: "Seamoth");

        int rebuilt = arbiter.RestoreFrom(registry);

        rebuilt.Should().Be(0);
        lost.Metadata.Should().BeNull();
        atAWreck.Metadata.Should().BeNull();
        ScoutDroneMetadata hurt = hurtAtAWreck.Metadata.Should().BeOfType<ScoutDroneMetadata>().Subject;
        hurt.CyclopsId.Should().BeNull("the bay is gone");
        hurt.Health.Should().Be(20f, "the drone is still hurt");
        notADrone.Metadata.Should().BeNull();
        arbiter.TryGetDrone(cyclops.Id, out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TheBaysAreRebuiltOnceAndABayThatARequestBuiltFirstIsNotTouched()
    {
        VehicleEntity cyclops = AddCyclops();
        WorldEntity saved = AddDrone(new ScoutDroneMetadata(cyclops.Id, true));
        NitroxId other = new();
        arbiter.Load(cyclops.Id, other).Should().BeTrue();

        arbiter.RestoreFrom(registry).Should().Be(0, "the bay of that Cyclops exists already");
        arbiter.TryGetDrone(cyclops.Id, out NitroxId? held, out _).Should().BeTrue();
        held.Should().Be(other);

        VehicleEntity later = AddCyclops();
        AddDrone(new ScoutDroneMetadata(later.Id, true));
        arbiter.RestoreFrom(registry).Should().Be(0, "the world is read once");
        arbiter.TryGetDrone(later.Id, out _, out _).Should().BeFalse();
        saved.Metadata.Should().BeOfType<ScoutDroneMetadata>("a drone that lost the race for its Cyclops' bay keeps what it was saved with");
    }

    [TestMethod]
    public async Task ADroneInTheDockRidesWithItsCyclopsAndOneThatIsOutDoesNot()
    {
        VehicleEntity cyclops = AddCyclops();
        WorldEntity drone = AddDrone(null);
        arbiter.Load(cyclops.Id, drone.Id);
        PlayerManager players = NoPlayers();
        WorldEntityManager worldEntityManager = new(packetSender, registry, null!, players, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        SimulationOwnershipData ownership = new();
        Player pilot = CreatePlayer(1);
        ownership.TryToAcquire(cyclops.Id, pilot, SimulationLockType.EXCLUSIVE).Should().BeTrue();
        VehicleMovementsPacketProcessor processor = new(registry, ownership, new SeamothRiderArbiter(packetSender), players, worldEntityManager, arbiter, NullLogger<VehicleMovementsPacketProcessor>.Instance);
        AuthProcessorContext context = new(pilot, packetSender);

        await processor.Process(context, new VehicleMovements([new SimpleMovementData(cyclops.Id, new NitroxVector3(500f, -30f, 200f), NitroxQuaternion.Identity)], 1.0));

        cyclops.Transform.Position.Should().Be(new NitroxVector3(500f, -30f, 200f));
        drone.Transform.Position.Should().Be(new NitroxVector3(500f, -30f, 200f));

        arbiter.SetDocked(cyclops.Id, drone.Id, false).Should().BeTrue();
        await processor.Process(context, new VehicleMovements([new SimpleMovementData(cyclops.Id, new NitroxVector3(10f, 10f, 10f), NitroxQuaternion.Identity)], 2.0));

        cyclops.Transform.Position.Should().Be(new NitroxVector3(10f, 10f, 10f));
        drone.Transform.Position.Should().Be(new NitroxVector3(500f, -30f, 200f), "a drone that is out is placed by its operator's client, not by the Cyclops");
    }

    [TestMethod]
    public async Task AClientReportOfADroneChangesItsHealthAndNeverItsBay()
    {
        VehicleEntity cyclops = AddCyclops();
        WorldEntity inTheBay = AddDrone(new ScoutDroneMetadata(cyclops.Id, true, 100f));
        WorldEntity loose = AddDrone(null);
        PlayerManager players = NoPlayers();
        EntityMetadataUpdateProcessor processor = new(players, registry, new CyclopsGunnerArbiter(packetSender), new CyclopsEngineerArbiter(packetSender), arbiter, new CyclopsNavRegistry(packetSender), NullLogger<EntityMetadataUpdateProcessor>.Instance);
        AuthProcessorContext context = new(CreatePlayer(1), packetSender);

        // the report carries what the reporter's drone has: the health. Whatever it says about the bay is not taken
        await processor.Process(context, new EntityMetadataUpdate(inTheBay.Id, new ScoutDroneMetadata(null, false, 40f)));
        ScoutDroneMetadata kept = inTheBay.Metadata.Should().BeOfType<ScoutDroneMetadata>().Subject;
        kept.CyclopsId.Should().Be(cyclops.Id);
        kept.Docked.Should().BeTrue();
        kept.Health.Should().Be(40f);

        // a health that is no number, below nothing or past what a drone can have is not taken
        await processor.Process(context, new EntityMetadataUpdate(inTheBay.Id, new ScoutDroneMetadata(null, false, float.NaN)));
        inTheBay.Metadata.Should().BeOfType<ScoutDroneMetadata>().Which.Health.Should().Be(40f, "NaN is no health: the old one stays");
        await processor.Process(context, new EntityMetadataUpdate(inTheBay.Id, new ScoutDroneMetadata(null, false, 9999f)));
        inTheBay.Metadata.Should().BeOfType<ScoutDroneMetadata>().Which.Health.Should().Be(VehicleUpgradeSettingsRules.MAX_DRONE_HEALTH);
        await processor.Process(context, new EntityMetadataUpdate(inTheBay.Id, new ScoutDroneMetadata(null, false, 40f)));

        // a drone that is in no bay cannot be put into one by a report
        await processor.Process(context, new EntityMetadataUpdate(loose.Id, new ScoutDroneMetadata(cyclops.Id, true, 70f)));
        ScoutDroneMetadata unbound = loose.Metadata.Should().BeOfType<ScoutDroneMetadata>().Subject;
        unbound.CyclopsId.Should().BeNull();
        unbound.Docked.Should().BeFalse();
        unbound.Health.Should().Be(70f);
        arbiter.RestoreFrom(registry).Should().Be(1, "only the drone that the server put into a bay");
    }

    [TestMethod]
    public void TheMetadataTravelsInAPacketAndIsSavedWithTheWorld()
    {
        Packet.InitSerializer();
        NitroxId cyclopsId = new();
        EntityMetadataUpdate packet = new(new NitroxId(), new ScoutDroneMetadata(cyclopsId, true, 61.5f));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        ScoutDroneMetadata sent = received.NewValue.Should().BeOfType<ScoutDroneMetadata>().Subject;
        sent.CyclopsId.Should().Be(cyclopsId);
        sent.Docked.Should().BeTrue();
        sent.Health.Should().Be(61.5f);

        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();
        serializer.Serialize(stream, new Holder { Metadata = new ScoutDroneMetadata(cyclopsId, false, 12f) });
        stream.Flush();
        Holder loaded = serializer.Deserialize<Holder>(new MemoryStream(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()))));

        ScoutDroneMetadata saved = loaded.Metadata.Should().BeOfType<ScoutDroneMetadata>().Subject;
        saved.CyclopsId.Should().Be(cyclopsId);
        saved.Docked.Should().BeFalse();
        saved.Health.Should().Be(12f);
    }

    private static PlayerManager NoPlayers() => new(null!, null!, Options.Create(new SubnauticaServerOptions()), NullLogger<PlayerManager>.Instance);

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    public class Holder
    {
        public EntityMetadata? Metadata { get; set; }
    }
}
