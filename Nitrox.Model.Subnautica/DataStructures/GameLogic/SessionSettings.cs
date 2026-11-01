using System;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// States that are temporary for one session so we don't need to persist them
/// </summary>
[Serializable]
public class SessionSettings
{
    public bool FastHatch;
    public bool FastGrow;

    /// <summary>
    /// Crafting stations of a base or a Cyclops also use the items in the lockers of that base or Cyclops. Not a cheat but the server's option <c>CraftFromLockers</c>: the server fills it in
    /// for every joining player (it is on unless the server turned it off), so a client that never heard of it plays with it on.
    /// </summary>
    public bool CraftFromLockers = true;

    // The vehicle upgrade modules (docs/coop/VEHICLE_UPGRADES.md): the server's options, clamped by VehicleUpgradeSettingsRules.Apply. The initial values are the defaults of the options.

    /// <summary>The master switch. When off no fabricator shows the new recipes and installed new modules do nothing.</summary>
    public bool VehicleUpgrades = VehicleUpgradeSettingsRules.DEFAULT_VEHICLE_UPGRADES;

    /// <summary>Every new blueprint is known from the start; otherwise each unlocks with the blueprint it builds on.</summary>
    public bool UpgradesUnlockedFromStart = VehicleUpgradeSettingsRules.DEFAULT_UPGRADES_UNLOCKED_FROM_START;

    /// <summary>Metres of crush depth per tier of the new depth modules.</summary>
    public float ExtraDepthPerModule = VehicleUpgradeSettingsRules.DEFAULT_EXTRA_DEPTH_PER_MODULE;

    public float AfterburnerSpeed = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_SPEED;
    public float AfterburnerSeconds = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_SECONDS;
    public float AfterburnerCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_COOLDOWN_SECONDS;
    public float AfterburnerEnergy = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_ENERGY;
    public float SolarChargeSeamoth = VehicleUpgradeSettingsRules.DEFAULT_SOLAR_CHARGE_SEAMOTH;
    public float SolarChargeCyclops = VehicleUpgradeSettingsRules.DEFAULT_SOLAR_CHARGE_CYCLOPS;

    // feature group 2: the Cyclops power share, the tow hooks

    public float PowerShareRange = VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RANGE;
    public float PowerShareRate = VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RATE;
    public float EmergencyPowerThreshold = VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_THRESHOLD;
    public float EmergencyPowerReserve = VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RESERVE;
    public float EmergencyPowerRate = VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RATE;
    public float TowSpeedFactor = VehicleUpgradeSettingsRules.DEFAULT_TOW_SPEED_FACTOR;
    public float TowMaxDistance = VehicleUpgradeSettingsRules.DEFAULT_TOW_MAX_DISTANCE;

    // feature group 3: the Cyclops Terrain Sonar and the navigator console

    public float TerrainSonarRange = VehicleUpgradeSettingsRules.DEFAULT_TERRAIN_SONAR_RANGE;
    public float ClearanceWarningDistance = VehicleUpgradeSettingsRules.DEFAULT_CLEARANCE_WARNING_DISTANCE;

    // feature group 3, station 2: the Cyclops gunner station

    public float GunnerShotEnergy = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_SHOT_ENERGY;
    public float GunnerCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_COOLDOWN_SECONDS;
    public float GunnerRange = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_RANGE;

    // feature group 3, station 3: the Cyclops engineer console

    public float EngineerSpeedBonus = VehicleUpgradeSettingsRules.DEFAULT_ENGINEER_SPEED_BONUS;
    public float OverdriveSeconds = VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_SECONDS;
    public float OverdriveCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_COOLDOWN_SECONDS;
    public float ShieldSaving = VehicleUpgradeSettingsRules.DEFAULT_SHIELD_SAVING;
    public float VentCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_VENT_COOLDOWN_SECONDS;
    public float SilentRunningBonus = VehicleUpgradeSettingsRules.DEFAULT_SILENT_RUNNING_BONUS;

    // feature group 3, station 4: the Cyclops scout drone

    public float DroneSpeedFactor = VehicleUpgradeSettingsRules.DEFAULT_DRONE_SPEED_FACTOR;
    public float DroneRange = VehicleUpgradeSettingsRules.DEFAULT_DRONE_RANGE;
    public float DroneStaticStart = VehicleUpgradeSettingsRules.DEFAULT_DRONE_STATIC_START;
    public float DroneHealth = VehicleUpgradeSettingsRules.DEFAULT_DRONE_HEALTH;
    public float ZapEnergy = VehicleUpgradeSettingsRules.DEFAULT_ZAP_ENERGY;
    public float ZapCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_ZAP_COOLDOWN_SECONDS;
    public float ZapRadius = VehicleUpgradeSettingsRules.DEFAULT_ZAP_RADIUS;

    // feature group 3: extra module slots

    public int ExtraModuleSlots = ExtraModuleSlotRules.DEFAULT_EXTRA;

    // feature group 4: the buddy oxygen (docs/coop/BUDDY_OXYGEN.md), clamped by VehicleUpgradeSettingsRules.Apply

    /// <summary>Seconds of air per second that flow from a giver to a teammate; 0 switches the buddy oxygen off.</summary>
    public float BuddyOxygenRate = BuddyOxygenRules.DEFAULT_RATE;

    /// <summary>Metres between the two divers within which air can flow.</summary>
    public float BuddyOxygenRange = BuddyOxygenRules.DEFAULT_RANGE;

    /// <summary>Seconds of air that the giver keeps for themselves.</summary>
    public float BuddyOxygenFloor = BuddyOxygenRules.DEFAULT_FLOOR;

    // feature group 4: the quick ping (docs/coop/QUICK_PING.md)

    /// <summary>Seconds that a quick ping stays on the HUD (the server's option <c>QuickPingSeconds</c>, clamped).</summary>
    public float QuickPingSeconds = QuickPingRules.DEFAULT_SECONDS;
}
