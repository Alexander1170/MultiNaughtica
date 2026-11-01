using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Serialization;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class BaseFloodMetadataTest
{
    private static readonly float[] grid = [0f, 0.25f, 1f, 0f, 0.75f];

    [TestMethod]
    public void TheWaterOfABaseTravelsInAPacket()
    {
        Packet.InitSerializer();
        EntityMetadataUpdate packet = new(new NitroxId(), new BaseFloodMetadata(grid));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        received.NewValue.Should().BeOfType<BaseFloodMetadata>().Which.FlatValueGrid.Should().Equal(grid);
    }

    [TestMethod]
    public void TheWaterOfABaseIsKeptInTheSave()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();

        serializer.Serialize(stream, new EntityMetadataHolder { Metadata = new BaseFloodMetadata(grid) });
        stream.Flush();
        EntityMetadataHolder loaded = serializer.Deserialize<EntityMetadataHolder>(new MemoryStream(stream.ToArray()));

        loaded.Metadata.Should().BeOfType<BaseFloodMetadata>().Which.FlatValueGrid.Should().Equal(grid);
    }

    public class EntityMetadataHolder
    {
        public EntityMetadata Metadata { get; set; }
    }
}
