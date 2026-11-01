using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.Configuration;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The balance of the vehicle upgrade modules (docs/coop/VEHICLE_UPGRADES.md): the limits of every server option, the defaults, and the one place that turns the server's options into the
/// <see cref="SessionSettings" /> every joining player gets, with out-of-range values clamped and every clamp reported. Pure (no game types), so that the server, the client and the tests use
/// the same numbers.
/// </summary>
/// <remarks>
/// The defaults here and the initial values of the properties in <see cref="SubnauticaServerOptions" /> and the fields in <see cref="SessionSettings" /> are the same numbers (a test compares them);
/// the options class cannot use these constants because it lives in a lower project.
/// </remarks>
public static class VehicleUpgradeSettingsRules
{
    public const bool DEFAULT_VEHICLE_UPGRADES = true;
    public const bool DEFAULT_UPGRADES_UNLOCKED_FROM_START = false;

    /// <summary>Metres of crush depth that one tier of a new depth module adds to the vanilla module of the tier before (the Seamoth MK3 gives 900 in vanilla, MK4 1200 and MK5 1500 with the default).</summary>
    public const float DEFAULT_EXTRA_DEPTH_PER_MODULE = 300f;
    public const float MIN_EXTRA_DEPTH_PER_MODULE = 0f;
    public const float MAX_EXTRA_DEPTH_PER_MODULE = 3000f;

    /// <summary>Multiplier of the Seamoth's speed while the afterburner burns.</summary>
    public const float DEFAULT_AFTERBURNER_SPEED = 2f;
    public const float MIN_AFTERBURNER_SPEED = 1f;
    public const float MAX_AFTERBURNER_SPEED = 5f;

    public const float DEFAULT_AFTERBURNER_SECONDS = 5f;
    public const float MIN_AFTERBURNER_SECONDS = 0.5f;
    public const float MAX_AFTERBURNER_SECONDS = 60f;

    public const float DEFAULT_AFTERBURNER_COOLDOWN_SECONDS = 15f;
    public const float MIN_AFTERBURNER_COOLDOWN_SECONDS = 0f;
    public const float MAX_AFTERBURNER_COOLDOWN_SECONDS = 300f;

    /// <summary>Energy units per use (a power cell holds 200, an ion power cell 1000): 10 is 5 percent of a power cell.</summary>
    public const float DEFAULT_AFTERBURNER_ENERGY = 10f;
    public const float MIN_AFTERBURNER_ENERGY = 0f;
    public const float MAX_AFTERBURNER_ENERGY = 200f;

    /// <summary>
    /// Energy units per second at the surface in full daylight that each Seamoth Solar Charger module adds. The game's own module (SeamothSolarCharge) adds 1 per second and module, so the default
    /// is the vanilla behaviour; the modules add up as they do in the game.
    /// </summary>
    public const float DEFAULT_SOLAR_CHARGE_SEAMOTH = 1f;
    public const float MIN_SOLAR_CHARGE = 0f;
    public const float MAX_SOLAR_CHARGE = 5f;

    /// <summary>
    /// The same for the Cyclops Solar Charger (only one counts). The Cyclops Thermal Reactor Module adds up to 1.5 per second (its multiplier; the temperature curve is in the prefab) and a base's solar
    /// panel up to 1.25; the default is the latter, and never below the Seamoth's module.
    /// </summary>
    public const float DEFAULT_SOLAR_CHARGE_CYCLOPS = 1.25f;

    /// <summary>Metres from a Cyclops with the power share module within which a Seamoth or a Prawn Suit is charged by it.</summary>
    public const float DEFAULT_POWER_SHARE_RANGE = 30f;
    public const float MIN_POWER_SHARE_RANGE = 5f;
    public const float MAX_POWER_SHARE_RANGE = 200f;

    /// <summary>Energy per second a Cyclops gives to each vehicle in range (a power cell holds 200); 0 switches the charging of vehicles off.</summary>
    public const float DEFAULT_POWER_SHARE_RATE = 2f;
    public const float MIN_POWER_SHARE_RATE = 0f;
    public const float MAX_POWER_SHARE_RATE = 20f;

    /// <summary>Percent of its power below which the Cyclops draws on the docked vehicle; 0 switches the emergency power off.</summary>
    public const float DEFAULT_EMERGENCY_POWER_THRESHOLD = 10f;
    public const float MIN_EMERGENCY_POWER_THRESHOLD = 0f;
    public const float MAX_EMERGENCY_POWER_THRESHOLD = 50f;

    /// <summary>Percent of its own power the docked vehicle keeps when the Cyclops draws on it.</summary>
    public const float DEFAULT_EMERGENCY_POWER_RESERVE = 25f;
    public const float MIN_EMERGENCY_POWER_RESERVE = 0f;
    public const float MAX_EMERGENCY_POWER_RESERVE = 90f;

    /// <summary>Energy per second the Cyclops draws from the docked vehicle in an emergency; 0 switches the emergency power off.</summary>
    public const float DEFAULT_EMERGENCY_POWER_RATE = 5f;
    public const float MIN_EMERGENCY_POWER_RATE = 0f;
    public const float MAX_EMERGENCY_POWER_RATE = 50f;

    /// <summary>Factor of the speed of a vehicle that tows another one.</summary>
    public const float DEFAULT_TOW_SPEED_FACTOR = 0.7f;
    public const float MIN_TOW_SPEED_FACTOR = 0.2f;
    public const float MAX_TOW_SPEED_FACTOR = 1f;

    /// <summary>The longest cable of a tow in metres; a hook attaches within half of it.</summary>
    public const float DEFAULT_TOW_MAX_DISTANCE = 25f;
    /// <summary>
    /// Shorter than this a tow could not hold: a hook attaches within half of the longest cable, but the cable is measured from the tower's rear to the towed vehicle's nose, which can be a hull's length (under
    /// 5 m for a Seamoth) further away than the vehicle's middle.
    /// </summary>
    public const float MIN_TOW_MAX_DISTANCE = 12f;
    public const float MAX_TOW_MAX_DISTANCE = 100f;

    // feature group 3, station 1: the Cyclops Terrain Sonar and the navigator console

    /// <summary>
    /// Metres of terrain around the Cyclops that the helm hologram and the map of the navigator console show. The scanner room of the game shows up to 500 m; the default is a day's cruising range in
    /// a cave system rather than the open sea, and a big radius costs more (more of the terrain's chunks are loaded, one at a time).
    /// </summary>
    public const float DEFAULT_TERRAIN_SONAR_RANGE = 200f;
    public const float MIN_TERRAIN_SONAR_RANGE = 50f;
    public const float MAX_TERRAIN_SONAR_RANGE = 500f;

    /// <summary>Metres ahead of the hull within which the navigator's clearance warning looks for walls (a tight passage is reported when it gets closer than this).</summary>
    public const float DEFAULT_CLEARANCE_WARNING_DISTANCE = 20f;
    public const float MIN_CLEARANCE_WARNING_DISTANCE = 5f;
    public const float MAX_CLEARANCE_WARNING_DISTANCE = 60f;

    // feature group 3, station 2: the Cyclops gunner station

    /// <summary>Energy that a FULL charge of the gunner station takes from the Cyclops (a partial charge takes the same fraction); the Stasis Rifle's own full charge costs 5 of a 100 battery.</summary>
    public const float DEFAULT_GUNNER_SHOT_ENERGY = 10f;
    public const float MIN_GUNNER_SHOT_ENERGY = 0f;
    public const float MAX_GUNNER_SHOT_ENERGY = 200f;

    /// <summary>Seconds after a shot before the gunner can charge the next (the sphere of a shot is in the air for about three).</summary>
    public const float DEFAULT_GUNNER_COOLDOWN_SECONDS = 3f;
    public const float MIN_GUNNER_COOLDOWN_SECONDS = 0f;
    public const float MAX_GUNNER_COOLDOWN_SECONDS = 60f;

    /// <summary>Metres a stasis sphere of the gunner flies before it is spent (it slows down on the way: the rifle's 25 m/s for 3 s reach 37.5 m).</summary>
    public const float DEFAULT_GUNNER_RANGE = 60f;
    public const float MIN_GUNNER_RANGE = 20f;
    public const float MAX_GUNNER_RANGE = 120f;

    // feature group 3, station 3: the Cyclops engineer console

    /// <summary>Extra speed of a Cyclops whose engineer routes the power to the engines, as a fraction of its normal speed.</summary>
    public const float DEFAULT_ENGINEER_SPEED_BONUS = 0.15f;
    public const float MIN_ENGINEER_SPEED_BONUS = 0f;
    public const float MAX_ENGINEER_SPEED_BONUS = 0.5f;

    /// <summary>Seconds that the overdrive lasts.</summary>
    public const float DEFAULT_OVERDRIVE_SECONDS = 12f;
    public const float MIN_OVERDRIVE_SECONDS = 3f;
    public const float MAX_OVERDRIVE_SECONDS = 60f;

    /// <summary>Seconds after an overdrive before the next can start.</summary>
    public const float DEFAULT_OVERDRIVE_COOLDOWN_SECONDS = 45f;
    public const float MIN_OVERDRIVE_COOLDOWN_SECONDS = 10f;
    public const float MAX_OVERDRIVE_COOLDOWN_SECONDS = 300f;

    /// <summary>Share of the shield's power use that is saved while the power is routed to the shields.</summary>
    public const float DEFAULT_SHIELD_SAVING = 0.4f;
    public const float MIN_SHIELD_SAVING = 0f;
    public const float MAX_SHIELD_SAVING = 0.8f;

    /// <summary>Seconds after venting the engine heat before it can be vented again.</summary>
    public const float DEFAULT_VENT_COOLDOWN_SECONDS = 30f;
    public const float MIN_VENT_COOLDOWN_SECONDS = 5f;
    public const float MAX_VENT_COOLDOWN_SECONDS = 180f;

    /// <summary>Share of the noise of silent running that is taken away on top of the game's, while the power is routed to the systems.</summary>
    public const float DEFAULT_SILENT_RUNNING_BONUS = 0.5f;
    public const float MIN_SILENT_RUNNING_BONUS = 0f;
    public const float MAX_SILENT_RUNNING_BONUS = 0.9f;

    // feature group 3, station 4: the Cyclops scout drone

    /// <summary>Top speed of the scout drone as a multiple of the Seamoth's.</summary>
    public const float DEFAULT_DRONE_SPEED_FACTOR = 2f;
    public const float MIN_DRONE_SPEED_FACTOR = 1f;
    public const float MAX_DRONE_SPEED_FACTOR = 4f;

    /// <summary>Metres from the Cyclops at which the scout drone loses its signal.</summary>
    public const float DEFAULT_DRONE_RANGE = 1000f;
    public const float MIN_DRONE_RANGE = 200f;
    public const float MAX_DRONE_RANGE = 3000f;

    /// <summary>Fraction of the drone's range at which the static begins.</summary>
    public const float DEFAULT_DRONE_STATIC_START = 0.5f;
    public const float MIN_DRONE_STATIC_START = 0.1f;
    public const float MAX_DRONE_STATIC_START = 0.95f;

    /// <summary>Health of a scout drone.</summary>
    public const float DEFAULT_DRONE_HEALTH = 100f;
    public const float MIN_DRONE_HEALTH = 20f;
    public const float MAX_DRONE_HEALTH = 400f;

    /// <summary>Energy of the drone's battery that one zap takes.</summary>
    public const float DEFAULT_ZAP_ENERGY = 15f;
    public const float MIN_ZAP_ENERGY = 0f;
    public const float MAX_ZAP_ENERGY = 100f;

    /// <summary>Seconds between two zaps.</summary>
    public const float DEFAULT_ZAP_COOLDOWN_SECONDS = 12f;
    public const float MIN_ZAP_COOLDOWN_SECONDS = 2f;
    public const float MAX_ZAP_COOLDOWN_SECONDS = 120f;

    /// <summary>Metres around the drone that a zap reaches.</summary>
    public const float DEFAULT_ZAP_RADIUS = 20f;
    public const float MIN_ZAP_RADIUS = 5f;
    public const float MAX_ZAP_RADIUS = 60f;

    /// <summary>
    /// The settings a joining player is told for the vehicle upgrades: the server's options, with every number that is outside its limits clamped (a value that is not a number becomes the default).
    /// Every clamp adds a plain sentence to <paramref name="messages" />, which the caller logs.
    /// </summary>
    public static void Apply(SessionSettings target, SubnauticaServerOptions options, ICollection<string>? messages = null)
    {
        target.VehicleUpgrades = options.VehicleUpgrades;
        target.UpgradesUnlockedFromStart = options.UpgradesUnlockedFromStart;
        target.ExtraDepthPerModule = Clamp(nameof(options.ExtraDepthPerModule), options.ExtraDepthPerModule, MIN_EXTRA_DEPTH_PER_MODULE, MAX_EXTRA_DEPTH_PER_MODULE, DEFAULT_EXTRA_DEPTH_PER_MODULE, messages);
        target.AfterburnerSpeed = Clamp(nameof(options.AfterburnerSpeed), options.AfterburnerSpeed, MIN_AFTERBURNER_SPEED, MAX_AFTERBURNER_SPEED, DEFAULT_AFTERBURNER_SPEED, messages);
        target.AfterburnerSeconds = Clamp(nameof(options.AfterburnerSeconds), options.AfterburnerSeconds, MIN_AFTERBURNER_SECONDS, MAX_AFTERBURNER_SECONDS, DEFAULT_AFTERBURNER_SECONDS, messages);
        target.AfterburnerCooldownSeconds = Clamp(nameof(options.AfterburnerCooldownSeconds), options.AfterburnerCooldownSeconds, MIN_AFTERBURNER_COOLDOWN_SECONDS, MAX_AFTERBURNER_COOLDOWN_SECONDS, DEFAULT_AFTERBURNER_COOLDOWN_SECONDS, messages);
        target.AfterburnerEnergy = Clamp(nameof(options.AfterburnerEnergy), options.AfterburnerEnergy, MIN_AFTERBURNER_ENERGY, MAX_AFTERBURNER_ENERGY, DEFAULT_AFTERBURNER_ENERGY, messages);
        target.SolarChargeSeamoth = Clamp(nameof(options.SolarChargeSeamoth), options.SolarChargeSeamoth, MIN_SOLAR_CHARGE, MAX_SOLAR_CHARGE, DEFAULT_SOLAR_CHARGE_SEAMOTH, messages);
        target.SolarChargeCyclops = Clamp(nameof(options.SolarChargeCyclops), options.SolarChargeCyclops, MIN_SOLAR_CHARGE, MAX_SOLAR_CHARGE, DEFAULT_SOLAR_CHARGE_CYCLOPS, messages);
        target.PowerShareRange = Clamp(nameof(options.PowerShareRange), options.PowerShareRange, MIN_POWER_SHARE_RANGE, MAX_POWER_SHARE_RANGE, DEFAULT_POWER_SHARE_RANGE, messages);
        target.PowerShareRate = Clamp(nameof(options.PowerShareRate), options.PowerShareRate, MIN_POWER_SHARE_RATE, MAX_POWER_SHARE_RATE, DEFAULT_POWER_SHARE_RATE, messages);
        target.EmergencyPowerThreshold = Clamp(nameof(options.EmergencyPowerThreshold), options.EmergencyPowerThreshold, MIN_EMERGENCY_POWER_THRESHOLD, MAX_EMERGENCY_POWER_THRESHOLD, DEFAULT_EMERGENCY_POWER_THRESHOLD, messages);
        target.EmergencyPowerReserve = Clamp(nameof(options.EmergencyPowerReserve), options.EmergencyPowerReserve, MIN_EMERGENCY_POWER_RESERVE, MAX_EMERGENCY_POWER_RESERVE, DEFAULT_EMERGENCY_POWER_RESERVE, messages);
        target.EmergencyPowerRate = Clamp(nameof(options.EmergencyPowerRate), options.EmergencyPowerRate, MIN_EMERGENCY_POWER_RATE, MAX_EMERGENCY_POWER_RATE, DEFAULT_EMERGENCY_POWER_RATE, messages);
        target.TowSpeedFactor = Clamp(nameof(options.TowSpeedFactor), options.TowSpeedFactor, MIN_TOW_SPEED_FACTOR, MAX_TOW_SPEED_FACTOR, DEFAULT_TOW_SPEED_FACTOR, messages);
        target.TowMaxDistance = Clamp(nameof(options.TowMaxDistance), options.TowMaxDistance, MIN_TOW_MAX_DISTANCE, MAX_TOW_MAX_DISTANCE, DEFAULT_TOW_MAX_DISTANCE, messages);
        target.TerrainSonarRange = Clamp(nameof(options.TerrainSonarRange), options.TerrainSonarRange, MIN_TERRAIN_SONAR_RANGE, MAX_TERRAIN_SONAR_RANGE, DEFAULT_TERRAIN_SONAR_RANGE, messages);
        target.ClearanceWarningDistance = Clamp(nameof(options.ClearanceWarningDistance), options.ClearanceWarningDistance, MIN_CLEARANCE_WARNING_DISTANCE, MAX_CLEARANCE_WARNING_DISTANCE, DEFAULT_CLEARANCE_WARNING_DISTANCE, messages);
        target.GunnerShotEnergy = Clamp(nameof(options.GunnerShotEnergy), options.GunnerShotEnergy, MIN_GUNNER_SHOT_ENERGY, MAX_GUNNER_SHOT_ENERGY, DEFAULT_GUNNER_SHOT_ENERGY, messages);
        target.GunnerCooldownSeconds = Clamp(nameof(options.GunnerCooldownSeconds), options.GunnerCooldownSeconds, MIN_GUNNER_COOLDOWN_SECONDS, MAX_GUNNER_COOLDOWN_SECONDS, DEFAULT_GUNNER_COOLDOWN_SECONDS, messages);
        target.GunnerRange = Clamp(nameof(options.GunnerRange), options.GunnerRange, MIN_GUNNER_RANGE, MAX_GUNNER_RANGE, DEFAULT_GUNNER_RANGE, messages);
        target.EngineerSpeedBonus = Clamp(nameof(options.EngineerSpeedBonus), options.EngineerSpeedBonus, MIN_ENGINEER_SPEED_BONUS, MAX_ENGINEER_SPEED_BONUS, DEFAULT_ENGINEER_SPEED_BONUS, messages);
        target.OverdriveSeconds = Clamp(nameof(options.OverdriveSeconds), options.OverdriveSeconds, MIN_OVERDRIVE_SECONDS, MAX_OVERDRIVE_SECONDS, DEFAULT_OVERDRIVE_SECONDS, messages);
        target.OverdriveCooldownSeconds = Clamp(nameof(options.OverdriveCooldownSeconds), options.OverdriveCooldownSeconds, MIN_OVERDRIVE_COOLDOWN_SECONDS, MAX_OVERDRIVE_COOLDOWN_SECONDS, DEFAULT_OVERDRIVE_COOLDOWN_SECONDS, messages);
        target.ShieldSaving = Clamp(nameof(options.ShieldSaving), options.ShieldSaving, MIN_SHIELD_SAVING, MAX_SHIELD_SAVING, DEFAULT_SHIELD_SAVING, messages);
        target.VentCooldownSeconds = Clamp(nameof(options.VentCooldownSeconds), options.VentCooldownSeconds, MIN_VENT_COOLDOWN_SECONDS, MAX_VENT_COOLDOWN_SECONDS, DEFAULT_VENT_COOLDOWN_SECONDS, messages);
        target.SilentRunningBonus = Clamp(nameof(options.SilentRunningBonus), options.SilentRunningBonus, MIN_SILENT_RUNNING_BONUS, MAX_SILENT_RUNNING_BONUS, DEFAULT_SILENT_RUNNING_BONUS, messages);
        target.DroneSpeedFactor = Clamp(nameof(options.DroneSpeedFactor), options.DroneSpeedFactor, MIN_DRONE_SPEED_FACTOR, MAX_DRONE_SPEED_FACTOR, DEFAULT_DRONE_SPEED_FACTOR, messages);
        target.DroneRange = Clamp(nameof(options.DroneRange), options.DroneRange, MIN_DRONE_RANGE, MAX_DRONE_RANGE, DEFAULT_DRONE_RANGE, messages);
        target.DroneStaticStart = Clamp(nameof(options.DroneStaticStart), options.DroneStaticStart, MIN_DRONE_STATIC_START, MAX_DRONE_STATIC_START, DEFAULT_DRONE_STATIC_START, messages);
        target.DroneHealth = Clamp(nameof(options.DroneHealth), options.DroneHealth, MIN_DRONE_HEALTH, MAX_DRONE_HEALTH, DEFAULT_DRONE_HEALTH, messages);
        target.ZapEnergy = Clamp(nameof(options.ZapEnergy), options.ZapEnergy, MIN_ZAP_ENERGY, MAX_ZAP_ENERGY, DEFAULT_ZAP_ENERGY, messages);
        target.ZapCooldownSeconds = Clamp(nameof(options.ZapCooldownSeconds), options.ZapCooldownSeconds, MIN_ZAP_COOLDOWN_SECONDS, MAX_ZAP_COOLDOWN_SECONDS, DEFAULT_ZAP_COOLDOWN_SECONDS, messages);
        target.ZapRadius = Clamp(nameof(options.ZapRadius), options.ZapRadius, MIN_ZAP_RADIUS, MAX_ZAP_RADIUS, DEFAULT_ZAP_RADIUS, messages);
        target.ExtraModuleSlots = ClampWhole(nameof(options.ExtraModuleSlots), options.ExtraModuleSlots, ExtraModuleSlotRules.MIN_EXTRA, ExtraModuleSlotRules.MAX_EXTRA, messages);
        target.BuddyOxygenRate = Clamp(nameof(options.BuddyOxygenRate), options.BuddyOxygenRate, BuddyOxygenRules.MIN_RATE, BuddyOxygenRules.MAX_RATE, BuddyOxygenRules.DEFAULT_RATE, messages);
        target.BuddyOxygenRange = Clamp(nameof(options.BuddyOxygenRange), options.BuddyOxygenRange, BuddyOxygenRules.MIN_RANGE, BuddyOxygenRules.MAX_RANGE, BuddyOxygenRules.DEFAULT_RANGE, messages);
        target.BuddyOxygenFloor = Clamp(nameof(options.BuddyOxygenFloor), options.BuddyOxygenFloor, BuddyOxygenRules.MIN_FLOOR, BuddyOxygenRules.MAX_FLOOR, BuddyOxygenRules.DEFAULT_FLOOR, messages);
        target.QuickPingSeconds = Clamp(nameof(options.QuickPingSeconds), options.QuickPingSeconds, QuickPingRules.MIN_SECONDS, QuickPingRules.MAX_SECONDS, QuickPingRules.DEFAULT_SECONDS, messages);
    }

    /// <summary>
    /// <paramref name="value" /> inside <paramref name="min" /> and <paramref name="max" />; a whole number outside is clamped to the nearest limit, and the sentence about it goes to <paramref name="messages" />.
    /// </summary>
    public static int ClampWhole(string key, int value, int min, int max, ICollection<string>? messages)
    {
        if (value < min)
        {
            messages?.Add($"Server option {key}={value} is below {min}, using {min}.");
            return min;
        }
        if (value > max)
        {
            messages?.Add($"Server option {key}={value} is above {max}, using {max}.");
            return max;
        }
        return value;
    }

    /// <summary>
    /// <paramref name="value" /> inside <paramref name="min" /> and <paramref name="max" />; a value outside is clamped to the nearest limit, a value that is no number (NaN, infinity) becomes
    /// <paramref name="fallback" />. The sentence about it goes to <paramref name="messages" />.
    /// </summary>
    public static float Clamp(string key, float value, float min, float max, float fallback, ICollection<string>? messages)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            messages?.Add($"Server option {key} is not a number, using the default {Format(fallback)}.");
            return fallback;
        }
        if (value < min)
        {
            messages?.Add($"Server option {key}={Format(value)} is below {Format(min)}, using {Format(min)}.");
            return min;
        }
        if (value > max)
        {
            messages?.Add($"Server option {key}={Format(value)} is above {Format(max)}, using {Format(max)}.");
            return max;
        }
        return value;
    }

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
