using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class StarshipDoorMetadataProcessor : EntityMetadataProcessor<StarshipDoorMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, StarshipDoorMetadata metadata)
    {
        StarshipDoor starshipDoor = gameObject.GetComponent<StarshipDoor>();
        if (!starshipDoor)
        {
            // Not an exception: PlaceholderGroupWorldEntitySpawner applies the metadata of all the children of a wreck in one coroutine, a throw here would end the spawn of the rest of it
            Log.Warn($"[{nameof(StarshipDoorMetadataProcessor)}] {gameObject.name} has no StarshipDoor, {metadata} not applied");
            return;
        }

        starshipDoor.doorOpen = metadata.DoorOpen;
        starshipDoor.doorLocked = metadata.DoorLocked;
        if (metadata.DoorLocked)
        {
            starshipDoor.LockDoor();
        }
        else
        {
            starshipDoor.UnlockDoor();
        }
    }
}
