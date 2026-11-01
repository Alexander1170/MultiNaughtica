using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class FireExtinguisherMetadataProcessor : EntityMetadataProcessor<FireExtinguisherMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, FireExtinguisherMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out FireExtinguisher fireExtinguisher))
        {
            Log.Error($"[{nameof(FireExtinguisherMetadataProcessor)}] Could not find FireExtinguisher on {gameObject.name}");
            return;
        }

        fireExtinguisher.fuel = Mathf.Clamp(metadata.Fuel, 0f, fireExtinguisher.maxFuel);
    }
}
