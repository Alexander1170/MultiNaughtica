using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

/// <summary>
/// What a client reports of a Scout Drone: its health (sent when a hit changes it, by the client that holds the drone's lock). The bay is the server's: whatever a report says about it, the server keeps its own.
/// </summary>
public class ScoutDroneMetadataExtractor : EntityMetadataExtractor<ScoutDrone, ScoutDroneMetadata>
{
    public override ScoutDroneMetadata Extract(ScoutDrone drone)
    {
        LiveMixin liveMixin = drone.Camera ? drone.Camera.liveMixin : null;
        return new ScoutDroneMetadata(null, false, liveMixin ? liveMixin.health : ScoutDroneMetadata.HEALTH_UNKNOWN);
    }
}
