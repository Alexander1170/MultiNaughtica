using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Serialization;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class ScannerRoomAndExtinguisherMetadataTest
{
    [TestMethod]
    public void WhatAScannerRoomScansForTravelsInAPacket()
    {
        Packet.InitSerializer();
        EntityMetadataUpdate packet = new(new NitroxId(), new MapRoomMetadata(new NitroxTechType("Quartz")));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        received.NewValue.Should().BeOfType<MapRoomMetadata>().Which.TypeToScan.Should().Be(new NitroxTechType("Quartz"));
    }

    [TestMethod]
    public void TheFuelOfAnExtinguisherTravelsInAPacket()
    {
        Packet.InitSerializer();
        EntityMetadataUpdate packet = new(new NitroxId(), new FireExtinguisherMetadata(37.5f));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        received.NewValue.Should().BeOfType<FireExtinguisherMetadata>().Which.Fuel.Should().Be(37.5f);
    }

    [TestMethod]
    public void BothAreKeptInTheSave()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);

        EntityMetadataHolder scanner = RoundTrip(serializer, new MapRoomMetadata(new NitroxTechType("Magnetite")));
        EntityMetadataHolder extinguisher = RoundTrip(serializer, new FireExtinguisherMetadata(12.25f));

        scanner.Metadata.Should().BeOfType<MapRoomMetadata>().Which.TypeToScan.Should().Be(new NitroxTechType("Magnetite"));
        extinguisher.Metadata.Should().BeOfType<FireExtinguisherMetadata>().Which.Fuel.Should().Be(12.25f);
    }

    private static EntityMetadataHolder RoundTrip(ServerJsonSerializer serializer, EntityMetadata metadata)
    {
        using MemoryStream stream = new();
        serializer.Serialize(stream, new EntityMetadataHolder { Metadata = metadata });
        stream.Flush();
        return serializer.Deserialize<EntityMetadataHolder>(new MemoryStream(stream.ToArray()));
    }

    public class EntityMetadataHolder
    {
        public EntityMetadata Metadata { get; set; }
    }
}
