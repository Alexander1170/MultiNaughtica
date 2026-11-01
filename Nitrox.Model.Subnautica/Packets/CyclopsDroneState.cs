using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// What everybody is told about the drone bay of a Cyclops (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 4): which drone it holds (or sent out) and whether the drone is in the dock. Server to clients only:
/// the state is the server's, decided from the requests of <see cref="CyclopsDroneBay" />, and a client that sent this packet would decide the bay of any Cyclops for everybody (the default relay refuses it; see
/// DefaultServerPacketProcessor). A player who joins is told the state of every bay that holds a drone.
/// </summary>
[Serializable]
public class CyclopsDroneState : Packet
{
    public NitroxId CyclopsId { get; }

    /// <summary>The drone the bay holds or sent out, empty when the bay is empty.</summary>
    public Optional<NitroxId> DroneId { get; }

    /// <summary>The drone is in the dock (false while it is out; meaningless for an empty bay).</summary>
    public bool Docked { get; }

    public CyclopsDroneState(NitroxId cyclopsId, Optional<NitroxId> droneId, bool docked)
    {
        CyclopsId = cyclopsId;
        DroneId = droneId;
        Docked = docked;
    }
}
