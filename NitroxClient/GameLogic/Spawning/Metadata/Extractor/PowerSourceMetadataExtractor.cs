using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class PowerSourceMetadataExtractor : EntityMetadataExtractor<PowerSource, PowerSourceMetadata>
{
    public override PowerSourceMetadata Extract(PowerSource powerSource)
    {
        // Only generators of bases: other GameObjects with a PowerSource (the lifepod, ...) have their own metadata, and the thermal plant
        // keeps its temperature in its metadata, so its charge is not synced (it refills from the heat within minutes)
        if (!powerSource.GetComponentInParent<Base>() || powerSource.GetComponent<ThermalPlant>())
        {
            return null;
        }

        // The burn progress of the fuel item (bioreactor) or rod (nuclear reactor) that is being used up
        float toConsume = 0f;
        if (powerSource.TryGetComponent(out BaseBioReactor bioReactor))
        {
            toConsume = bioReactor._toConsume;
        }
        else if (powerSource.TryGetComponent(out BaseNuclearReactor nuclearReactor))
        {
            toConsume = nuclearReactor._toConsume;
        }

        return new(powerSource.power, toConsume);
    }
}
