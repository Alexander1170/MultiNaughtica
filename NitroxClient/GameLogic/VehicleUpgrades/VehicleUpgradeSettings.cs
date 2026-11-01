using System;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The balance of the vehicle upgrade modules as the server told this client when it joined (<see cref="SessionSettings" />, the server's options clamped by
/// <see cref="VehicleUpgradeSettingsRules" />). Static like <see cref="LockerCrafting.Enabled" />: the game's own code asks for these numbers from many places (the depth of a module, the
/// tooltip of a module, the afterburner, the solar charger). Until the server has spoken the defaults hold, which are also what a server that never changed an option sends.
/// </summary>
public static class VehicleUpgradeSettings
{
    /// <summary>The master switch: when false no fabricator offers the new recipes and the installed new modules do nothing.</summary>
    public static bool Enabled { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_VEHICLE_UPGRADES;

    /// <summary>Every new blueprint is known from the start (otherwise each unlocks with the blueprint it builds on).</summary>
    public static bool UnlockedFromStart { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_UPGRADES_UNLOCKED_FROM_START;

    /// <summary>Metres of crush depth per tier of the new depth modules.</summary>
    public static float ExtraDepthPerModule { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_EXTRA_DEPTH_PER_MODULE;

    public static float AfterburnerSpeed { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_SPEED;
    public static float AfterburnerSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_SECONDS;
    public static float AfterburnerCooldownSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_COOLDOWN_SECONDS;
    public static float AfterburnerEnergy { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_ENERGY;
    public static float SolarChargeSeamoth { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_SOLAR_CHARGE_SEAMOTH;
    public static float SolarChargeCyclops { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_SOLAR_CHARGE_CYCLOPS;

    public static float PowerShareRange { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RANGE;
    public static float PowerShareRate { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RATE;
    public static float EmergencyPowerThreshold { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_THRESHOLD;
    public static float EmergencyPowerReserve { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RESERVE;
    public static float EmergencyPowerRate { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RATE;
    public static float TowSpeedFactor { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_TOW_SPEED_FACTOR;
    public static float TowMaxDistance { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_TOW_MAX_DISTANCE;

    /// <summary>Metres of terrain around the Cyclops that the helm hologram and the navigator's map show.</summary>
    public static float TerrainSonarRange { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_TERRAIN_SONAR_RANGE;

    /// <summary>Metres ahead of the hull within which the clearance warning looks for walls.</summary>
    public static float ClearanceWarningDistance { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_CLEARANCE_WARNING_DISTANCE;

    /// <summary>Energy that a full charge of the gunner station takes from the Cyclops.</summary>
    public static float GunnerShotEnergy { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_SHOT_ENERGY;

    /// <summary>Seconds after a shot before the gunner can charge the next.</summary>
    public static float GunnerCooldownSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_COOLDOWN_SECONDS;

    /// <summary>Metres a stasis sphere of the gunner station flies.</summary>
    public static float GunnerRange { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_RANGE;

    /// <summary>Extra speed of a Cyclops whose engineer routes the power to the engines, as a fraction of its normal speed.</summary>
    public static float EngineerSpeedBonus { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_ENGINEER_SPEED_BONUS;

    /// <summary>Seconds that the overdrive lasts.</summary>
    public static float OverdriveSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_SECONDS;

    /// <summary>Seconds after an overdrive before the next can start.</summary>
    public static float OverdriveCooldownSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_COOLDOWN_SECONDS;

    /// <summary>Share of the shield's power use that is saved while the power is routed to the shields.</summary>
    public static float ShieldSaving { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_SHIELD_SAVING;

    /// <summary>Seconds after venting the engine heat before it can be vented again.</summary>
    public static float VentCooldownSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_VENT_COOLDOWN_SECONDS;

    /// <summary>Share of the noise of silent running that is taken away on top of the game's, while the power is routed to the systems.</summary>
    public static float SilentRunningBonus { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_SILENT_RUNNING_BONUS;

    /// <summary>Top speed of the scout drone as a multiple of the Seamoth's.</summary>
    public static float DroneSpeedFactor { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_DRONE_SPEED_FACTOR;

    /// <summary>Metres from the Cyclops at which the scout drone loses its signal.</summary>
    public static float DroneRange { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_DRONE_RANGE;

    /// <summary>Fraction of the drone's range at which the static begins.</summary>
    public static float DroneStaticStart { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_DRONE_STATIC_START;

    /// <summary>Health of a scout drone.</summary>
    public static float DroneHealth { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_DRONE_HEALTH;

    /// <summary>Energy of the drone's battery that one zap takes.</summary>
    public static float ZapEnergy { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_ZAP_ENERGY;

    /// <summary>Seconds between two zaps.</summary>
    public static float ZapCooldownSeconds { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_ZAP_COOLDOWN_SECONDS;

    /// <summary>Metres around the drone that a zap reaches.</summary>
    public static float ZapRadius { get; private set; } = VehicleUpgradeSettingsRules.DEFAULT_ZAP_RADIUS;

    /// <summary>
    /// The extra module slots the server allows (0 to 2). What is in force is <see cref="VehicleSlotLists.Extra" />: this number with the master switch (<see cref="Enabled" />) applied, and 0 outside a session.
    /// </summary>
    public static int ExtraModuleSlots { get; private set; } = ExtraModuleSlotRules.DEFAULT_EXTRA;

    // The buddy oxygen (docs/coop/BUDDY_OXYGEN.md). Not vehicle upgrades: they hold whether or not the vehicle upgrades are on.

    /// <summary>Seconds of air per second that flow from a giver to a teammate; 0 switches the buddy oxygen off.</summary>
    public static float BuddyOxygenRate { get; private set; } = BuddyOxygenRules.DEFAULT_RATE;

    /// <summary>Metres between two divers within which air can flow.</summary>
    public static float BuddyOxygenRange { get; private set; } = BuddyOxygenRules.DEFAULT_RANGE;

    /// <summary>Seconds of air that the giver keeps for themselves.</summary>
    public static float BuddyOxygenFloor { get; private set; } = BuddyOxygenRules.DEFAULT_FLOOR;
    /// <summary>Seconds that a quick ping stays on the HUD (docs/coop/QUICK_PING.md). Not a vehicle upgrade: it holds whether or not the vehicle upgrades are on.</summary>
    public static float QuickPingSeconds { get; private set; } = QuickPingRules.DEFAULT_SECONDS;

    /// <summary>
    /// True from the moment the settings of the server arrived (the join) until the session ends. Everything that depends on the settings (the craft nodes, the unlock rules, the depth tables) waits
    /// for it: until then the numbers here are only the defaults, and acting on them could unlock or show something that the server has switched off.
    /// </summary>
    public static bool Received { get; private set; }

    /// <summary>Raised after the settings changed (the join, the end of the session), for the things that show a number from them: the tooltips of the modules.</summary>
    public static event Action Changed;

    /// <summary>
    /// Takes over what the server said. The values are clamped again, because a server of another build could send anything and every number here ends up in a calculation of the game.
    /// </summary>
    public static void Apply(SessionSettings settings)
    {
        Take(settings);
        Received = true;
        Changed?.Invoke();
    }

    private static void Take(SessionSettings settings)
    {
        Enabled = settings.VehicleUpgrades;
        UnlockedFromStart = settings.UpgradesUnlockedFromStart;
        ExtraDepthPerModule = VehicleUpgradeSettingsRules.Clamp(nameof(ExtraDepthPerModule), settings.ExtraDepthPerModule, VehicleUpgradeSettingsRules.MIN_EXTRA_DEPTH_PER_MODULE, VehicleUpgradeSettingsRules.MAX_EXTRA_DEPTH_PER_MODULE, VehicleUpgradeSettingsRules.DEFAULT_EXTRA_DEPTH_PER_MODULE, null);
        AfterburnerSpeed = VehicleUpgradeSettingsRules.Clamp(nameof(AfterburnerSpeed), settings.AfterburnerSpeed, VehicleUpgradeSettingsRules.MIN_AFTERBURNER_SPEED, VehicleUpgradeSettingsRules.MAX_AFTERBURNER_SPEED, VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_SPEED, null);
        AfterburnerSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(AfterburnerSeconds), settings.AfterburnerSeconds, VehicleUpgradeSettingsRules.MIN_AFTERBURNER_SECONDS, VehicleUpgradeSettingsRules.MAX_AFTERBURNER_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_SECONDS, null);
        AfterburnerCooldownSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(AfterburnerCooldownSeconds), settings.AfterburnerCooldownSeconds, VehicleUpgradeSettingsRules.MIN_AFTERBURNER_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_AFTERBURNER_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_COOLDOWN_SECONDS, null);
        AfterburnerEnergy = VehicleUpgradeSettingsRules.Clamp(nameof(AfterburnerEnergy), settings.AfterburnerEnergy, VehicleUpgradeSettingsRules.MIN_AFTERBURNER_ENERGY, VehicleUpgradeSettingsRules.MAX_AFTERBURNER_ENERGY, VehicleUpgradeSettingsRules.DEFAULT_AFTERBURNER_ENERGY, null);
        SolarChargeSeamoth = VehicleUpgradeSettingsRules.Clamp(nameof(SolarChargeSeamoth), settings.SolarChargeSeamoth, VehicleUpgradeSettingsRules.MIN_SOLAR_CHARGE, VehicleUpgradeSettingsRules.MAX_SOLAR_CHARGE, VehicleUpgradeSettingsRules.DEFAULT_SOLAR_CHARGE_SEAMOTH, null);
        SolarChargeCyclops = VehicleUpgradeSettingsRules.Clamp(nameof(SolarChargeCyclops), settings.SolarChargeCyclops, VehicleUpgradeSettingsRules.MIN_SOLAR_CHARGE, VehicleUpgradeSettingsRules.MAX_SOLAR_CHARGE, VehicleUpgradeSettingsRules.DEFAULT_SOLAR_CHARGE_CYCLOPS, null);
        PowerShareRange = VehicleUpgradeSettingsRules.Clamp(nameof(PowerShareRange), settings.PowerShareRange, VehicleUpgradeSettingsRules.MIN_POWER_SHARE_RANGE, VehicleUpgradeSettingsRules.MAX_POWER_SHARE_RANGE, VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RANGE, null);
        PowerShareRate = VehicleUpgradeSettingsRules.Clamp(nameof(PowerShareRate), settings.PowerShareRate, VehicleUpgradeSettingsRules.MIN_POWER_SHARE_RATE, VehicleUpgradeSettingsRules.MAX_POWER_SHARE_RATE, VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RATE, null);
        EmergencyPowerThreshold = VehicleUpgradeSettingsRules.Clamp(nameof(EmergencyPowerThreshold), settings.EmergencyPowerThreshold, VehicleUpgradeSettingsRules.MIN_EMERGENCY_POWER_THRESHOLD, VehicleUpgradeSettingsRules.MAX_EMERGENCY_POWER_THRESHOLD, VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_THRESHOLD, null);
        EmergencyPowerReserve = VehicleUpgradeSettingsRules.Clamp(nameof(EmergencyPowerReserve), settings.EmergencyPowerReserve, VehicleUpgradeSettingsRules.MIN_EMERGENCY_POWER_RESERVE, VehicleUpgradeSettingsRules.MAX_EMERGENCY_POWER_RESERVE, VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RESERVE, null);
        EmergencyPowerRate = VehicleUpgradeSettingsRules.Clamp(nameof(EmergencyPowerRate), settings.EmergencyPowerRate, VehicleUpgradeSettingsRules.MIN_EMERGENCY_POWER_RATE, VehicleUpgradeSettingsRules.MAX_EMERGENCY_POWER_RATE, VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RATE, null);
        TowSpeedFactor = VehicleUpgradeSettingsRules.Clamp(nameof(TowSpeedFactor), settings.TowSpeedFactor, VehicleUpgradeSettingsRules.MIN_TOW_SPEED_FACTOR, VehicleUpgradeSettingsRules.MAX_TOW_SPEED_FACTOR, VehicleUpgradeSettingsRules.DEFAULT_TOW_SPEED_FACTOR, null);
        TowMaxDistance = VehicleUpgradeSettingsRules.Clamp(nameof(TowMaxDistance), settings.TowMaxDistance, VehicleUpgradeSettingsRules.MIN_TOW_MAX_DISTANCE, VehicleUpgradeSettingsRules.MAX_TOW_MAX_DISTANCE, VehicleUpgradeSettingsRules.DEFAULT_TOW_MAX_DISTANCE, null);
        TerrainSonarRange = VehicleUpgradeSettingsRules.Clamp(nameof(TerrainSonarRange), settings.TerrainSonarRange, VehicleUpgradeSettingsRules.MIN_TERRAIN_SONAR_RANGE, VehicleUpgradeSettingsRules.MAX_TERRAIN_SONAR_RANGE, VehicleUpgradeSettingsRules.DEFAULT_TERRAIN_SONAR_RANGE, null);
        ClearanceWarningDistance = VehicleUpgradeSettingsRules.Clamp(nameof(ClearanceWarningDistance), settings.ClearanceWarningDistance, VehicleUpgradeSettingsRules.MIN_CLEARANCE_WARNING_DISTANCE, VehicleUpgradeSettingsRules.MAX_CLEARANCE_WARNING_DISTANCE, VehicleUpgradeSettingsRules.DEFAULT_CLEARANCE_WARNING_DISTANCE, null);
        GunnerShotEnergy = VehicleUpgradeSettingsRules.Clamp(nameof(GunnerShotEnergy), settings.GunnerShotEnergy, VehicleUpgradeSettingsRules.MIN_GUNNER_SHOT_ENERGY, VehicleUpgradeSettingsRules.MAX_GUNNER_SHOT_ENERGY, VehicleUpgradeSettingsRules.DEFAULT_GUNNER_SHOT_ENERGY, null);
        GunnerCooldownSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(GunnerCooldownSeconds), settings.GunnerCooldownSeconds, VehicleUpgradeSettingsRules.MIN_GUNNER_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_GUNNER_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_GUNNER_COOLDOWN_SECONDS, null);
        GunnerRange = VehicleUpgradeSettingsRules.Clamp(nameof(GunnerRange), settings.GunnerRange, VehicleUpgradeSettingsRules.MIN_GUNNER_RANGE, VehicleUpgradeSettingsRules.MAX_GUNNER_RANGE, VehicleUpgradeSettingsRules.DEFAULT_GUNNER_RANGE, null);
        EngineerSpeedBonus = VehicleUpgradeSettingsRules.Clamp(nameof(EngineerSpeedBonus), settings.EngineerSpeedBonus, VehicleUpgradeSettingsRules.MIN_ENGINEER_SPEED_BONUS, VehicleUpgradeSettingsRules.MAX_ENGINEER_SPEED_BONUS, VehicleUpgradeSettingsRules.DEFAULT_ENGINEER_SPEED_BONUS, null);
        OverdriveSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(OverdriveSeconds), settings.OverdriveSeconds, VehicleUpgradeSettingsRules.MIN_OVERDRIVE_SECONDS, VehicleUpgradeSettingsRules.MAX_OVERDRIVE_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_SECONDS, null);
        OverdriveCooldownSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(OverdriveCooldownSeconds), settings.OverdriveCooldownSeconds, VehicleUpgradeSettingsRules.MIN_OVERDRIVE_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_OVERDRIVE_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_COOLDOWN_SECONDS, null);
        ShieldSaving = VehicleUpgradeSettingsRules.Clamp(nameof(ShieldSaving), settings.ShieldSaving, VehicleUpgradeSettingsRules.MIN_SHIELD_SAVING, VehicleUpgradeSettingsRules.MAX_SHIELD_SAVING, VehicleUpgradeSettingsRules.DEFAULT_SHIELD_SAVING, null);
        VentCooldownSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(VentCooldownSeconds), settings.VentCooldownSeconds, VehicleUpgradeSettingsRules.MIN_VENT_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_VENT_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_VENT_COOLDOWN_SECONDS, null);
        SilentRunningBonus = VehicleUpgradeSettingsRules.Clamp(nameof(SilentRunningBonus), settings.SilentRunningBonus, VehicleUpgradeSettingsRules.MIN_SILENT_RUNNING_BONUS, VehicleUpgradeSettingsRules.MAX_SILENT_RUNNING_BONUS, VehicleUpgradeSettingsRules.DEFAULT_SILENT_RUNNING_BONUS, null);
        DroneSpeedFactor = VehicleUpgradeSettingsRules.Clamp(nameof(DroneSpeedFactor), settings.DroneSpeedFactor, VehicleUpgradeSettingsRules.MIN_DRONE_SPEED_FACTOR, VehicleUpgradeSettingsRules.MAX_DRONE_SPEED_FACTOR, VehicleUpgradeSettingsRules.DEFAULT_DRONE_SPEED_FACTOR, null);
        DroneRange = VehicleUpgradeSettingsRules.Clamp(nameof(DroneRange), settings.DroneRange, VehicleUpgradeSettingsRules.MIN_DRONE_RANGE, VehicleUpgradeSettingsRules.MAX_DRONE_RANGE, VehicleUpgradeSettingsRules.DEFAULT_DRONE_RANGE, null);
        DroneStaticStart = VehicleUpgradeSettingsRules.Clamp(nameof(DroneStaticStart), settings.DroneStaticStart, VehicleUpgradeSettingsRules.MIN_DRONE_STATIC_START, VehicleUpgradeSettingsRules.MAX_DRONE_STATIC_START, VehicleUpgradeSettingsRules.DEFAULT_DRONE_STATIC_START, null);
        DroneHealth = VehicleUpgradeSettingsRules.Clamp(nameof(DroneHealth), settings.DroneHealth, VehicleUpgradeSettingsRules.MIN_DRONE_HEALTH, VehicleUpgradeSettingsRules.MAX_DRONE_HEALTH, VehicleUpgradeSettingsRules.DEFAULT_DRONE_HEALTH, null);
        ZapEnergy = VehicleUpgradeSettingsRules.Clamp(nameof(ZapEnergy), settings.ZapEnergy, VehicleUpgradeSettingsRules.MIN_ZAP_ENERGY, VehicleUpgradeSettingsRules.MAX_ZAP_ENERGY, VehicleUpgradeSettingsRules.DEFAULT_ZAP_ENERGY, null);
        ZapCooldownSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(ZapCooldownSeconds), settings.ZapCooldownSeconds, VehicleUpgradeSettingsRules.MIN_ZAP_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.MAX_ZAP_COOLDOWN_SECONDS, VehicleUpgradeSettingsRules.DEFAULT_ZAP_COOLDOWN_SECONDS, null);
        ZapRadius = VehicleUpgradeSettingsRules.Clamp(nameof(ZapRadius), settings.ZapRadius, VehicleUpgradeSettingsRules.MIN_ZAP_RADIUS, VehicleUpgradeSettingsRules.MAX_ZAP_RADIUS, VehicleUpgradeSettingsRules.DEFAULT_ZAP_RADIUS, null);
        ExtraModuleSlots = VehicleUpgradeSettingsRules.ClampWhole(nameof(ExtraModuleSlots), settings.ExtraModuleSlots, ExtraModuleSlotRules.MIN_EXTRA, ExtraModuleSlotRules.MAX_EXTRA, null);
        BuddyOxygenRate = VehicleUpgradeSettingsRules.Clamp(nameof(BuddyOxygenRate), settings.BuddyOxygenRate, BuddyOxygenRules.MIN_RATE, BuddyOxygenRules.MAX_RATE, BuddyOxygenRules.DEFAULT_RATE, null);
        BuddyOxygenRange = VehicleUpgradeSettingsRules.Clamp(nameof(BuddyOxygenRange), settings.BuddyOxygenRange, BuddyOxygenRules.MIN_RANGE, BuddyOxygenRules.MAX_RANGE, BuddyOxygenRules.DEFAULT_RANGE, null);
        BuddyOxygenFloor = VehicleUpgradeSettingsRules.Clamp(nameof(BuddyOxygenFloor), settings.BuddyOxygenFloor, BuddyOxygenRules.MIN_FLOOR, BuddyOxygenRules.MAX_FLOOR, BuddyOxygenRules.DEFAULT_FLOOR, null);
        QuickPingSeconds = VehicleUpgradeSettingsRules.Clamp(nameof(QuickPingSeconds), settings.QuickPingSeconds, QuickPingRules.MIN_SECONDS, QuickPingRules.MAX_SECONDS, QuickPingRules.DEFAULT_SECONDS, null);
    }

    /// <summary>Back to the defaults and to "not received" (a session ended).</summary>
    public static void Reset()
    {
        Take(new SessionSettings());
        Received = false;
        Changed?.Invoke();
    }
}
