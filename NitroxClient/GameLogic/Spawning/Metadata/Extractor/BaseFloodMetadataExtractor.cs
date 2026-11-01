using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class BaseFloodMetadataExtractor : EntityMetadataExtractor<BaseFloodSim, BaseFloodMetadata>
{
    public override BaseFloodMetadata Extract(BaseFloodSim entity)
    {
        // Nothing to send before the simulation has cells (OnProtoSerialize would read a grid that does not exist yet)
        if (!entity.IsInitialized())
        {
            return null;
        }

        // The serializer argument isn't actually used by BaseFloodSim's implementation; it only (re)computes flatValueGrid from the current simulation state.
        entity.OnProtoSerialize(null);
        return new(entity.flatValueGrid);
    }
}
