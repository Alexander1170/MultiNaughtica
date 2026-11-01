using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

[TestClass]
public class EntitySpawnedByClientProcessorTest
{
    private static WorldEntity Creature(string techType)
    {
        return new WorldEntity(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType(techType), 0, "classId", false, new NitroxId(), null);
    }

    [TestMethod]
    public void ASecondIntroductionOfASpawnerCreatureIsIgnored()
    {
        EntitySpawnedByClient packet = new(Creature("Skyray"), onlyIfNew: true);

        EntitySpawnedByClientProcessor.IsRepeatedIntroduction(packet, entityAlreadyKnown: true).Should().BeTrue();
    }

    [TestMethod]
    public void TheFirstIntroductionOfASpawnerCreatureCounts()
    {
        EntitySpawnedByClient packet = new(Creature("Skyray"), onlyIfNew: true);

        EntitySpawnedByClientProcessor.IsRepeatedIntroduction(packet, entityAlreadyKnown: false).Should().BeFalse();
    }

    [TestMethod]
    public void ASpawnerCreatureIsSentBackToTheClientThatIntroducedIt()
    {
        // The client destroyed the creature that the game made (the seed of the introduction): it gets the server's entity like everybody else
        EntitySpawnedByClientProcessor.IsSentBackToSender(new EntitySpawnedByClient(Creature("Skyray"), onlyIfNew: true)).Should().BeTrue();
        EntitySpawnedByClientProcessor.IsSentBackToSender(new EntitySpawnedByClient(Creature("Titanium"), requireRespawn: true)).Should().BeFalse();
    }

    [TestMethod]
    public void AnyOtherEntityThatTheServerKnowsIsStillUpdated()
    {
        // A dropped item that the registry knows from the inventory is spawned into the world with the same id: the registry takes the new data
        EntitySpawnedByClient packet = new(Creature("Titanium"), requireRespawn: true);

        EntitySpawnedByClientProcessor.IsRepeatedIntroduction(packet, entityAlreadyKnown: true).Should().BeFalse();
    }

    [TestMethod]
    public void ASpawnerCreatureOfAKindTheServerMovesCountsAsSpawnedByTheServer()
    {
        // Then its lock follows the closest player like the lock of every other fish
        WorldEntity skyray = Creature("Skyray");

        EntitySpawnedByClientProcessor.MarkSpawnerCreature(new EntitySpawnedByClient(skyray, onlyIfNew: true));

        skyray.SpawnedByServer.Should().BeTrue();
    }

    [TestMethod]
    public void ASpawnerCreatureOfAnotherKindStaysWithTheClientThatMadeIt()
    {
        WorldEntity other = Creature("Quartz");

        EntitySpawnedByClientProcessor.MarkSpawnerCreature(new EntitySpawnedByClient(other, onlyIfNew: true));

        other.SpawnedByServer.Should().BeFalse();
    }

    [TestMethod]
    public void EntitiesThatAreNotIntroducedByASpawnerAreLeftAlone()
    {
        WorldEntity skyray = Creature("Skyray");

        EntitySpawnedByClientProcessor.MarkSpawnerCreature(new EntitySpawnedByClient(skyray));

        skyray.SpawnedByServer.Should().BeFalse();
    }

    [TestMethod]
    public void TheMarkTravelsInThePacket()
    {
        Packet.InitSerializer();

        EntitySpawnedByClient received = (EntitySpawnedByClient)Packet.Deserialize(new EntitySpawnedByClient(Creature("Skyray"), true, true, true).Serialize());

        received.OnlyIfNew.Should().BeTrue();
        received.RequireRespawn.Should().BeTrue();
        received.RequireSimulation.Should().BeTrue();
        ((EntitySpawnedByClient)Packet.Deserialize(new EntitySpawnedByClient(Creature("Skyray")).Serialize())).OnlyIfNew.Should().BeFalse();
    }
}
