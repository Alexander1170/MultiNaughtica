using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class PrecursorTeleporterMetadataProcessor : EntityMetadataProcessor<PrecursorTeleporterMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, PrecursorTeleporterMetadata metadata)
    {
        Log.Debug($"Received precursor teleporter metadata change for {gameObject.name} with data of {metadata}");

        PrecursorTeleporter precursorTeleporter = gameObject.GetComponent<PrecursorTeleporter>();
        // A teleporter only ever opens: ToggleDoor again on an open one would fade its portal in a second time, and closing one is not something the game does
        if (precursorTeleporter && metadata.IsOpen && !precursorTeleporter.isOpen)
        {
            precursorTeleporter.ToggleDoor(true);
        }
    }
}
