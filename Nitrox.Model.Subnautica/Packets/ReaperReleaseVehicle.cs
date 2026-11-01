using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// A Reaper Leviathan let go of the vehicle it held (K16). Sent by the client whose timer ran out, the other copies of the Reaper let go too.
/// </summary>
[Serializable]
public class ReaperReleaseVehicle : Packet
{
    public NitroxId ReaperId { get; }

    public ReaperReleaseVehicle(NitroxId reaperId)
    {
        ReaperId = reaperId;
    }
}
