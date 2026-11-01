using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// What the engineer console has set on a Cyclops (docs/coop/VEHICLE_UPGRADES.md, station 3), for everybody: the route of the power and the time that the overdrive and its cooldown and the vent's cooldown still
/// have to run. The SERVER keeps it (<c>CyclopsEngineerArbiter</c>) and decides the cooldowns on its own clock, so a client never compares a time stamp with another computer's: it gets the seconds that are left
/// and counts them on its own clock. Sent to everybody when it changes and, once for each Cyclops that has something set, to a player who joins. Not saved with the world (a restarted server starts with the
/// power balanced).
/// </summary>
[Serializable]
public class CyclopsEngineerState : Packet
{
    public NitroxId CyclopsId { get; }

    /// <summary>An <c>EngineerRoute</c>.</summary>
    public int Route { get; }

    /// <summary>Seconds that the overdrive still runs (0: not running).</summary>
    public float OverdriveSeconds { get; }

    /// <summary>Seconds until the next overdrive can start (counted from now, so it includes the seconds that the running one still has).</summary>
    public float OverdriveReadySeconds { get; }

    /// <summary>Seconds until the next vent can start.</summary>
    public float VentReadySeconds { get; }

    public CyclopsEngineerState(NitroxId cyclopsId, int route, float overdriveSeconds, float overdriveReadySeconds, float ventReadySeconds)
    {
        CyclopsId = cyclopsId;
        Route = route;
        OverdriveSeconds = overdriveSeconds;
        OverdriveReadySeconds = overdriveReadySeconds;
        VentReadySeconds = ventReadySeconds;
    }
}
