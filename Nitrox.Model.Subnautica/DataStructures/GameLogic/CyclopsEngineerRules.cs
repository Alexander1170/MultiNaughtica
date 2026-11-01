using System;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>Where the engineer of a Cyclops sends its power (docs/coop/VEHICLE_UPGRADES.md, station 3). A preset gives its bonus and nothing else changes.</summary>
public enum EngineerRoute : byte
{
    /// <summary>The game's own numbers.</summary>
    Balanced = 0,

    /// <summary>Faster, and the overdrive can be used.</summary>
    Engines = 1,

    /// <summary>The shield uses less power.</summary>
    Shields = 2,

    /// <summary>A longer sonar and a quieter silent running (that costs more power).</summary>
    Systems = 3
}

/// <summary>How dangerous the engine heat is: the game's own steps (it warns and starts fires from 4, and more often from 6).</summary>
public enum EngineerHeatBand : byte
{
    Cool = 0,
    Warm = 1,
    Hot = 2
}

/// <summary>
/// The arithmetic of the engineer console that more than one place needs, as plain numbers so that it can be tested: what a route does to the speed, the shield, the sonar and the noise, and the state machines of
/// the overdrive and the vent. A cooldown is decided by the SERVER on its own clock (the stamps below are seconds of that clock, never compared between computers); the clients are told the seconds that are left. The
/// heat is the game's own (<c>SubFire.engineOverheatValue</c>, 0 to 10, one step every five seconds while the Cyclops flanks), not an invention of this mod.
/// </summary>
public static class CyclopsEngineerRules
{
    /// <summary>The top of the game's engine heat.</summary>
    public const int MAX_HEAT = 10;

    /// <summary>From this heat on (the game's "greater than 3") the engine warns and every step has a one in five chance to start a fire in the engine room.</summary>
    public const int WARM_HEAT = 4;

    /// <summary>From this heat on (the game's "greater than 5") the chance of a fire is one in three at every step.</summary>
    public const int HOT_HEAT = 6;

    /// <summary>Seconds between two steps of the game's engine heat.</summary>
    public const float HEAT_TICK_SECONDS = 5f;

    /// <summary>How much faster the overdrive makes the Cyclops on top of the route's bonus (a fraction: 0.35 is 35 percent).</summary>
    public const float OVERDRIVE_BOOST = 0.35f;

    /// <summary>Heat that starting an overdrive adds at once.</summary>
    public const int OVERDRIVE_START_HEAT = 2;

    /// <summary>Heat that every step of the game's heat adds on top of its own while the overdrive runs.</summary>
    public const int OVERDRIVE_TICK_HEAT = 1;

    /// <summary>Heat that venting takes away at once.</summary>
    public const int VENT_HEAT = 4;

    /// <summary>Energy, in units of the Cyclops' power, that venting costs.</summary>
    public const float VENT_ENERGY = 15f;

    /// <summary>How much farther the sonar sees when the power goes to the systems.</summary>
    public const float SONAR_FACTOR = 1.5f;

    /// <summary>What silent running costs in power when the power goes to the systems (the deeper silence is paid for).</summary>
    public const float SILENT_RUNNING_COST_FACTOR = 1.5f;

    /// <summary>How long the engineer's client waits for the answer to a button before it gives up (the server's hold-back of a repeated press is longer).</summary>
    public const float REQUEST_TIMEOUT_SECONDS = 3f;

    /// <summary>The server lets an action of one Cyclops through at most this often (a client that does not keep its own cooldown must not make the simulator work as often as it likes).</summary>
    public const long MIN_SERVER_INTERVAL_MILLISECONDS = 250;

    /// <summary>
    /// How long a forwarded action holds back the next one of its kind while the server waits for the simulator. Longer than the engineer's own timeout (<see cref="REQUEST_TIMEOUT_SECONDS" />), so that a client which
    /// gave up on a stalled simulator cannot press again while the first press may still be carried out (the simulator would pay for both, and only one cooldown would start).
    /// </summary>
    public const long PENDING_FORWARD_MILLISECONDS = 4000;

    /// <summary>
    /// How late the simulator's answer to a forwarded action is still taken: by then the simulator has paid for the action and changed the heat, so the cooldown has to start even if the engineer's client has given up
    /// (the engineer is told all the same; a client that is no longer waiting drops the answer).
    /// </summary>
    public const long LATE_RESULT_MILLISECONDS = 10000;

    /// <summary>After the simulator turned a press down (or the route moved away while it was out) the same press is not forwarded again for this long.</summary>
    public const long REFUSED_PRESS_FLOOR_MILLISECONDS = 1000;

    /// <summary>The server passes the heat of a Cyclops on to its engineer at most this often.</summary>
    public const long MIN_STATUS_INTERVAL_MILLISECONDS = 400;

    /// <summary>A route from the number that travels in the metadata; a number that is no route is Balanced.</summary>
    public static EngineerRoute RouteFrom(int value)
    {
        return IsKnownRoute(value) ? (EngineerRoute)value : EngineerRoute.Balanced;
    }

    public static bool IsKnownRoute(int value)
    {
        return value is >= (int)EngineerRoute.Balanced and <= (int)EngineerRoute.Systems;
    }

    // ---- what a route does

    /// <summary>
    /// The factor on the Cyclops' acceleration (<c>SubControl.accelScale</c>): the route's bonus while the power goes to the engines, and the overdrive's boost on top while it runs at flank speed.
    /// </summary>
    public static float SpeedFactor(EngineerRoute route, bool overdriveActive, bool flank, float speedBonus)
    {
        if (route != EngineerRoute.Engines)
        {
            return 1f;
        }
        float factor = 1f + SafeFraction(speedBonus);
        return overdriveActive && flank ? factor * (1f + OVERDRIVE_BOOST) : factor;
    }

    /// <summary>The factor on the shield's power use.</summary>
    public static float ShieldCostFactor(EngineerRoute route, float saving)
    {
        return route == EngineerRoute.Shields ? 1f - Math.Min(0.95f, SafeFraction(saving)) : 1f;
    }

    /// <summary>The factor on the range of the sonars (the game's creature sonar and the terrain sonar of the navigator).</summary>
    public static float SonarFactor(EngineerRoute route)
    {
        return route == EngineerRoute.Systems ? SONAR_FACTOR : 1f;
    }

    /// <summary>The factor on the noise that creatures hear: the extra silence of silent running when the power goes to the systems.</summary>
    public static float NoiseFactor(EngineerRoute route, bool silentRunning, float bonus)
    {
        return route == EngineerRoute.Systems && silentRunning ? 1f - Math.Min(0.95f, SafeFraction(bonus)) : 1f;
    }

    /// <summary>The factor on the power that silent running uses.</summary>
    public static float SilentRunningCostFactor(EngineerRoute route)
    {
        return route == EngineerRoute.Systems ? SILENT_RUNNING_COST_FACTOR : 1f;
    }

    private static float SafeFraction(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
    }

    // ---- the overdrive and the vent (stamps of the synced clock, seconds)

    /// <summary>Whether the overdrive runs now: it needs the engines route (a route changed away from it suspends the overdrive: the clock keeps running, so a quick way back to the engines does not give it more time) and the clock before the stamp.</summary>
    public static bool IsOverdriveActive(EngineerRoute route, double now, double until)
    {
        return route == EngineerRoute.Engines && now < until;
    }

    /// <summary>Seconds left until <paramref name="readyAt" /> on the same clock, 0 when it has passed (or is no number).</summary>
    public static float Remaining(double now, double readyAt)
    {
        double left = readyAt - now;
        return double.IsNaN(left) || left <= 0d ? 0f : (float)left;
    }

    /// <summary>What the SERVER says to an overdrive: it needs the engines route (the server keeps the route) and no cooldown. (<see cref="CyclopsEngineerFailure.None" /> when it may be forwarded.)</summary>
    public static CyclopsEngineerFailure ServerCheckOverdrive(EngineerRoute route, double now, double readyAt)
    {
        if (route != EngineerRoute.Engines)
        {
            return CyclopsEngineerFailure.WrongRoute;
        }
        return Remaining(now, readyAt) > 0f ? CyclopsEngineerFailure.Cooldown : CyclopsEngineerFailure.None;
    }

    /// <summary>What the SIMULATOR says to an overdrive, knowing the engine: it needs the engine on, flank speed and power.</summary>
    public static CyclopsEngineerFailure SimulatorCheckOverdrive(bool engineOn, bool flank, bool powered)
    {
        if (!engineOn)
        {
            return CyclopsEngineerFailure.EngineOff;
        }
        if (!flank)
        {
            return CyclopsEngineerFailure.NotFlank;
        }
        return powered ? CyclopsEngineerFailure.None : CyclopsEngineerFailure.NoPower;
    }

    /// <summary>What the SERVER says to a vent: no cooldown.</summary>
    public static CyclopsEngineerFailure ServerCheckVent(double now, double readyAt)
    {
        return Remaining(now, readyAt) > 0f ? CyclopsEngineerFailure.Cooldown : CyclopsEngineerFailure.None;
    }

    /// <summary>What the SIMULATOR says to a vent, knowing the energy and the heat: the Cyclops must be powered and have the energy it costs, and the engine must have some heat to take off.</summary>
    public static CyclopsEngineerFailure SimulatorCheckVent(bool powered, bool hasEnergy, int heat)
    {
        if (!powered || !hasEnergy)
        {
            return CyclopsEngineerFailure.NoPower;
        }
        return heat <= 0 ? CyclopsEngineerFailure.AlreadyCool : CyclopsEngineerFailure.None;
    }

    /// <summary>The two stamps of an overdrive that starts now: when it ends, and when the next one can start (after the cooldown that follows its end).</summary>
    public static (double Until, double ReadyAt) StartOverdrive(double now, float seconds, float cooldown)
    {
        double until = now + Math.Max(0f, seconds);
        return (until, until + Math.Max(0f, cooldown));
    }

    /// <summary>The stamp from which the next vent is possible.</summary>
    public static double VentReadyAt(double now, float cooldown)
    {
        return now + Math.Max(0f, cooldown);
    }

    // ---- the heat

    public static int ClampHeat(int heat)
    {
        return Math.Max(0, Math.Min(MAX_HEAT, heat));
    }

    public static int HeatAfterVent(int heat)
    {
        return ClampHeat(heat - VENT_HEAT);
    }

    public static int HeatAfterOverdriveStart(int heat)
    {
        return ClampHeat(heat + OVERDRIVE_START_HEAT);
    }

    /// <summary>The heat just before the game takes its own step: the overdrive adds its extra while the game is heating (flank, throttle, engine on).</summary>
    public static int HeatBeforeStep(int heat, bool overdriveActive, bool gameIsHeating)
    {
        return overdriveActive && gameIsHeating ? ClampHeat(heat + OVERDRIVE_TICK_HEAT) : ClampHeat(heat);
    }

    public static EngineerHeatBand BandOf(int heat)
    {
        return heat >= HOT_HEAT ? EngineerHeatBand.Hot : heat >= WARM_HEAT ? EngineerHeatBand.Warm : EngineerHeatBand.Cool;
    }

    /// <summary>The game's chance that a step of the heat at this value starts a fire (after the step has added its one).</summary>
    public static float FireChancePerStep(int heat)
    {
        return heat >= HOT_HEAT ? 1f / 3f : heat >= WARM_HEAT ? 1f / 5f : 0f;
    }
}
