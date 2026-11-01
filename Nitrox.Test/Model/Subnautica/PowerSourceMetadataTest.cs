using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Serialization;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class PowerSourceMetadataTest
{
    [TestMethod]
    public void TheBurnProgressOfAReactorTravelsWithItsCharge()
    {
        Packet.InitSerializer();
        NitroxId id = new();
        EntityMetadataUpdate packet = new(id, new PowerSourceMetadata(312.5f, 41.25f));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        PowerSourceMetadata metadata = received.NewValue.Should().BeOfType<PowerSourceMetadata>().Subject;
        metadata.Power.Should().Be(312.5f);
        metadata.ToConsume.Should().Be(41.25f);
    }

    [TestMethod]
    public void TheBurnProgressIsKeptInTheSave()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();

        serializer.Serialize(stream, new EntityMetadataHolder { Metadata = new PowerSourceMetadata(80f, 7.5f) });
        stream.Flush();
        EntityMetadataHolder loaded = serializer.Deserialize<EntityMetadataHolder>(new MemoryStream(stream.ToArray()));

        PowerSourceMetadata metadata = loaded.Metadata.Should().BeOfType<PowerSourceMetadata>().Subject;
        metadata.Power.Should().Be(80f);
        metadata.ToConsume.Should().Be(7.5f);
    }

    [TestMethod]
    public void AGeneratorThatNeverBurnsFuelSendsNoProgress()
    {
        // Solar panels and the like: one value, the default of the second
        new PowerSourceMetadata(25f).ToConsume.Should().Be(0f);
    }

    public class EntityMetadataHolder
    {
        public EntityMetadata Metadata { get; set; }
    }
}
