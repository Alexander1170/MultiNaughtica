using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class CyclopsLightingMetadataExtractor : EntityMetadataExtractor<CyclopsLightingPanel, CyclopsLightingMetadata>
{
    public override CyclopsLightingMetadata Extract(CyclopsLightingPanel lighting)
    {
        return ExtractChange(lighting, CyclopsLightingMetadata.CHANGED_NOTHING);
    }

    /// <summary>The whole panel as this client has it, naming the part that the press which sends it changed (the server takes the other parts from itself: CyclopsLightingMetadata.Merge).</summary>
    public static CyclopsLightingMetadata ExtractChange(CyclopsLightingPanel lighting, byte changed)
    {
        return new(lighting.floodlightsOn, lighting.lightingOn, PowerShareSwitch.IsOn(lighting.cyclopsRoot), changed);
    }
}
