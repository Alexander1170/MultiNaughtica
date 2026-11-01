using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class FireExtinguisherMetadataExtractor : EntityMetadataExtractor<FireExtinguisher, FireExtinguisherMetadata>
{
    public override FireExtinguisherMetadata Extract(FireExtinguisher fireExtinguisher)
    {
        return new(fireExtinguisher.fuel);
    }
}
