using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// What only the simulating client of a Cyclops knows and the engineer at its console wants to see (docs/coop/VEHICLE_UPGRADES.md, station 3): the engine heat (the game's own 0 to 10, which is only worked out where
/// the engine runs). The simulator sends it to the server when the heat changes (the game steps it every five seconds) and as a heartbeat every five seconds, while somebody sits at the console; the server
/// hands it to that player alone, accepts it from the Cyclops' simulator only and not oftener than <see cref="Nitrox.Model.Subnautica.DataStructures.GameLogic.CyclopsEngineerRules.MIN_STATUS_INTERVAL_MILLISECONDS" />. Not saved.
/// </summary>
[Serializable]
public class CyclopsEngineerStatus : Packet
{
    public NitroxId CyclopsId { get; }

    /// <summary>The engine heat, 0 to 10.</summary>
    public int Heat { get; }

    /// <summary>The game is heating the engine now (flank speed, throttle, engine on): the gauge is rising.</summary>
    public bool Heating { get; }

    /// <summary>The engine is on.</summary>
    public bool EngineOn { get; }

    /// <summary>The Cyclops' power, 0 to 100 (percent of the cells).</summary>
    public float PowerPercent { get; }

    public CyclopsEngineerStatus(NitroxId cyclopsId, int heat, bool heating, bool engineOn, float powerPercent)
    {
        CyclopsId = cyclopsId;
        Heat = heat;
        Heating = heating;
        EngineOn = engineOn;
        PowerPercent = powerPercent;
    }
}
