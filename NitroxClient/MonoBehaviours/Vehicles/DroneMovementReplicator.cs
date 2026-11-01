using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.MonoBehaviours.Vehicles;

/// <summary>
/// Moves the copy of a scout drone on the clients that do not fly it (the vehicle pipeline: the operator's client sends its position 30 times a second, <see cref="MovementReplicator" /> extrapolates). The drone is
/// not a vehicle, so there is nothing to show of a steering wheel or a driver: the position and the rotation, which the base class applies, are all. It places nothing while the drone sits in its dock (the dock carries it) or while this client flies it.
/// </summary>
public class DroneMovementReplicator : MovementReplicator
{
    private Cyclops.ScoutDrone drone;

    protected override bool IsSuspended
    {
        get
        {
            if (!drone)
            {
                drone = GetComponent<Cyclops.ScoutDrone>();
            }
            // in the dock the hull carries it; while this client flies it the pilot places it (and the lock is on its way: a snapshot of the one who flew it before must not pull it back)
            return drone && (drone.IsDocked || drone.LocalOperator);
        }
    }

    public override void ApplyNewMovementData(MovementData newMovementData)
    {
    }
}
