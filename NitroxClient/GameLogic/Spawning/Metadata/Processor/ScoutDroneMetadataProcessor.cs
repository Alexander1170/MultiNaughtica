using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

/// <summary>
/// The health of a Scout Drone arrives with the drone when it spawns and whenever the client that flies it is hit. The bay in the metadata needs nothing done here: the server sends the state of every bay
/// (<see cref="CyclopsDroneState" />) to a client that joins, and the bay of the Cyclops puts the drone into its dock as soon as the drone exists on this client (<c>DroneBay.ApplyServerState</c>), however the drone
/// came to exist.
/// </summary>
public class ScoutDroneMetadataProcessor : EntityMetadataProcessor<ScoutDroneMetadata>
{
    private readonly LiveMixinManager liveMixinManager;

    public ScoutDroneMetadataProcessor(LiveMixinManager liveMixinManager)
    {
        this.liveMixinManager = liveMixinManager;
    }

    public override void ProcessMetadata(GameObject gameObject, ScoutDroneMetadata metadata)
    {
        if (float.IsNaN(metadata.Health) || float.IsInfinity(metadata.Health) || metadata.Health < 0f || !gameObject.TryGetComponent(out ScoutDrone drone))
        {
            return;
        }
        // The health of the mod's drone (the server's DroneHealth, not the prefab's) is in place before the saved one is: the saved one is an absolute number
        drone.ApplyHealth();
        LiveMixin liveMixin = drone.Camera ? drone.Camera.liveMixin : null;
        if (!liveMixin)
        {
            return;
        }
        using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
        {
            liveMixinManager.SyncRemoteHealth(liveMixin, Mathf.Min(metadata.Health, liveMixin.maxHealth));
        }
    }
}
