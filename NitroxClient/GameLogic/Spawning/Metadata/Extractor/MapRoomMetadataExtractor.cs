using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class MapRoomMetadataExtractor : EntityMetadataExtractor<MapRoomFunctionality, MapRoomMetadata>
{
    public override MapRoomMetadata Extract(MapRoomFunctionality mapRoom)
    {
        return new(mapRoom.typeToScan.ToDto());
    }
}
