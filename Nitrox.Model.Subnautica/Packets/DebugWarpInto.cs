using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Sync test harness (Debug builds): server asks the receiving client to put its local player into the given vehicle
///     (Seamoth/Exosuit: pilot seat, Cyclops: inside on foot, or at the helm when <see cref="Pilot" /> is set).
///     Sent by <c>/warpinto</c>.
/// </summary>
[Serializable]
public class DebugWarpInto(NitroxId vehicleId, bool pilot) : Packet
{
    public NitroxId VehicleId { get; } = vehicleId;
    public bool Pilot { get; } = pilot;
}
