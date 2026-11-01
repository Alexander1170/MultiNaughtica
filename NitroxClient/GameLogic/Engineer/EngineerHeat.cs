using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>
/// The engine heat of a Cyclops: the game's own (<c>SubFire.engineOverheatValue</c>, an integer from 0 to 10 that the game steps every five seconds while the Cyclops flanks under throttle and cools otherwise; from 4
/// on it warns and risks a fire in the engine room). The game keeps it in a private field and shows it nowhere; this reads and sets it where the engine runs (the client that simulates the Cyclops), and keeps the
/// last reading that the simulator sent (<see cref="CyclopsEngineerStatus" />) for the engineer at the console, who may be on another computer.
/// </summary>
public static class EngineerHeat
{
    /// <summary>What the simulating client reported.</summary>
    public readonly record struct Reading(int Heat, bool Heating, bool EngineOn, float PowerPercent, float At);

    private static readonly Dictionary<NitroxId, Reading> latest = [];

    public static int Get(SubFire fire)
    {
        return fire ? CyclopsEngineerRules.ClampHeat(fire.engineOverheatValue) : 0;
    }

    public static void Set(SubFire fire, int heat)
    {
        if (fire)
        {
            fire.engineOverheatValue = CyclopsEngineerRules.ClampHeat(heat);
        }
    }

    /// <summary>The game is heating the engine now: flank speed, throttle applied, engine on (the condition of the game's own +1).</summary>
    public static bool IsHeating(SubFire fire)
    {
        return fire && fire.cyclopsMotorMode && fire.subControl
               && fire.cyclopsMotorMode.cyclopsMotorMode == CyclopsMotorMode.CyclopsMotorModes.Flank && fire.subControl.appliedThrottle && fire.cyclopsMotorMode.engineOn;
    }

    public static void OnStatus(CyclopsEngineerStatus packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }
        latest[packet.CyclopsId] = new Reading(CyclopsEngineerRules.ClampHeat(packet.Heat), packet.Heating, packet.EngineOn, packet.PowerPercent, Time.unscaledTime);
    }

    public static bool TryGetLatest(NitroxId cyclopsId, out Reading reading)
    {
        reading = default;
        return cyclopsId != null && latest.TryGetValue(cyclopsId, out reading);
    }

    public static void Forget(NitroxId cyclopsId)
    {
        if (cyclopsId != null)
        {
            latest.Remove(cyclopsId);
        }
    }

    public static void Reset()
    {
        latest.Clear();
    }
}
