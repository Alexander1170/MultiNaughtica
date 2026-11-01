using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Serialization;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class SealedDoorMetadataTest
{
    [TestMethod]
    public void TheCutStateOfADoorTravelsInAPacket()
    {
        Packet.InitSerializer();
        EntityMetadataUpdate packet = new(new NitroxId(), new SealedDoorMetadata(true, 175f));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        SealedDoorMetadata metadata = received.NewValue.Should().BeOfType<SealedDoorMetadata>().Subject;
        metadata.Sealed.Should().BeTrue();
        metadata.OpenedAmount.Should().Be(175f);
    }

    [TestMethod]
    public void ADoorThatCoop3SavedOpenLoadsOpen()
    {
        // the text of the two cut doors of the world "Test" in EntityData.json, written by coop.3: the world of the first play sessions must keep loading
        const string saved = """{"Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.SealedDoorMetadata, Nitrox.Model.Subnautica","Sealed":false,"OpenedAmount":450.0}}""";
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);

        EntityMetadataHolder loaded = serializer.Deserialize<EntityMetadataHolder>(new MemoryStream(Encoding.UTF8.GetBytes(saved)));

        SealedDoorMetadata metadata = loaded.Metadata.Should().BeOfType<SealedDoorMetadata>().Subject;
        metadata.Sealed.Should().BeFalse();
        metadata.OpenedAmount.Should().Be(450f);
    }

    [TestMethod]
    public void TheStateIsSavedAsBefore()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();

        serializer.Serialize(stream, new EntityMetadataHolder { Metadata = new SealedDoorMetadata(false, 450f) });
        stream.Flush();
        string text = Encoding.UTF8.GetString(stream.ToArray());

        text.Should().Contain("SealedDoorMetadata, Nitrox.Model.Subnautica");
        text.Should().Contain("\"Sealed\"");
        text.Should().Contain("\"OpenedAmount\"");
    }

    [TestMethod]
    public void TheLogLineNamesTheStateInsteadOfTheType()
    {
        new SealedDoorMetadata(false, 450f).ToString().Should().Contain("Sealed: False").And.Contain("OpenedAmount: 450");
    }

    public class EntityMetadataHolder
    {
        public EntityMetadata Metadata { get; set; }
    }
}
