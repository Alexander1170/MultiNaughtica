using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// A Reaper Leviathan grabbed a Seamoth or a Prawn suit (K16). Sent by the client on which it happened first (the creature's copy touched the vehicle there), the other clients
/// that can see both let their copy of the Reaper grab the same vehicle.
/// </summary>
[Serializable]
public class ReaperGrabVehicle : Packet
{
    public NitroxId ReaperId { get; }
    public NitroxId VehicleId { get; }

    public ReaperGrabVehicle(NitroxId reaperId, NitroxId vehicleId)
    {
        ReaperId = reaperId;
        VehicleId = vehicleId;
    }
}
