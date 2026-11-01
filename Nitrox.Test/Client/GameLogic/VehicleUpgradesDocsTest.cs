using System.Globalization;
using System.Text.RegularExpressions;
using Nitrox.Model.Configuration;
using Nitrox.Model.Serialization;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using static Nitrox.Model.Subnautica.DataStructures.GameLogic.VehicleUpgradeSettingsRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     The player page docs/coop/VEHICLE_UPGRADES.md says what the code does: the names of the items, the default and the range of every setting, the depths. A number that changes in the code and
///     not in the page fails here.
/// </summary>
[TestClass]
public class VehicleUpgradesDocsTest
{
    private static string ReadPage()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, "docs", "coop", "VEHICLE_UPGRADES.md");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.Inconclusive("docs/coop/VEHICLE_UPGRADES.md was not found above the test assembly");
        return "";
    }

    private static string Text(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [TestMethod]
    public void ThePageNamesEveryNewItem()
    {
        string page = ReadPage();

        foreach (CoopItemDefinition item in CoopItems.All.Where(item => !item.Adopted))
        {
            page.Should().Contain($"| {item.DisplayName} |", $"the table of the modules must list {item.DisplayName}");
        }
        page.Should().Contain("Seamoth Solar Charger");
    }

    [TestMethod]
    public void ThePageShowsTheDepthsOfTheSixModules()
    {
        string page = ReadPage();

        foreach ((string vehicle, string vanilla, string tier1, string tier2) in new[] { ("Seamoth", "MK3: 900 m", "MK4: 1200 m", "MK5: 1500 m"), ("Prawn Suit", "MK2: 1700 m", "MK3: 2000 m", "MK4: 2300 m"), ("Cyclops", "MK3: 1700 m", "MK4: 2000 m", "MK5: 2300 m") })
        {
            page.Should().Contain($"| {vehicle} | {vanilla} | {tier1} | {tier2} |");
        }
    }

    [TestMethod]
    public void ThePageHasTheDefaultAndTheRangeOfEverySetting()
    {
        string page = ReadPage();
        SubnauticaServerOptions options = new();

        (string key, string expectedDefault, string range)[] settings =
        [
            (nameof(options.VehicleUpgrades), options.VehicleUpgrades ? "true" : "false", ""),
            (nameof(options.UpgradesUnlockedFromStart), options.UpgradesUnlockedFromStart ? "true" : "false", ""),
            (nameof(options.ExtraDepthPerModule), Text(options.ExtraDepthPerModule), $"{Text(MIN_EXTRA_DEPTH_PER_MODULE)} to {Text(MAX_EXTRA_DEPTH_PER_MODULE)}"),
            (nameof(options.AfterburnerSpeed), Text(options.AfterburnerSpeed), $"{Text(MIN_AFTERBURNER_SPEED)} to {Text(MAX_AFTERBURNER_SPEED)}"),
            (nameof(options.AfterburnerSeconds), Text(options.AfterburnerSeconds), $"{Text(MIN_AFTERBURNER_SECONDS)} to {Text(MAX_AFTERBURNER_SECONDS)}"),
            (nameof(options.AfterburnerCooldownSeconds), Text(options.AfterburnerCooldownSeconds), $"{Text(MIN_AFTERBURNER_COOLDOWN_SECONDS)} to {Text(MAX_AFTERBURNER_COOLDOWN_SECONDS)}"),
            (nameof(options.AfterburnerEnergy), Text(options.AfterburnerEnergy), $"{Text(MIN_AFTERBURNER_ENERGY)} to {Text(MAX_AFTERBURNER_ENERGY)}"),
            (nameof(options.SolarChargeSeamoth), Text(options.SolarChargeSeamoth), $"{Text(MIN_SOLAR_CHARGE)} to {Text(MAX_SOLAR_CHARGE)}"),
            (nameof(options.SolarChargeCyclops), Text(options.SolarChargeCyclops), $"{Text(MIN_SOLAR_CHARGE)} to {Text(MAX_SOLAR_CHARGE)}"),
            (nameof(options.PowerShareRange), Text(options.PowerShareRange), $"{Text(MIN_POWER_SHARE_RANGE)} to {Text(MAX_POWER_SHARE_RANGE)}"),
            (nameof(options.PowerShareRate), Text(options.PowerShareRate), $"{Text(MIN_POWER_SHARE_RATE)} to {Text(MAX_POWER_SHARE_RATE)}"),
            (nameof(options.EmergencyPowerThreshold), Text(options.EmergencyPowerThreshold), $"{Text(MIN_EMERGENCY_POWER_THRESHOLD)} to {Text(MAX_EMERGENCY_POWER_THRESHOLD)}"),
            (nameof(options.EmergencyPowerReserve), Text(options.EmergencyPowerReserve), $"{Text(MIN_EMERGENCY_POWER_RESERVE)} to {Text(MAX_EMERGENCY_POWER_RESERVE)}"),
            (nameof(options.EmergencyPowerRate), Text(options.EmergencyPowerRate), $"{Text(MIN_EMERGENCY_POWER_RATE)} to {Text(MAX_EMERGENCY_POWER_RATE)}"),
            (nameof(options.TowSpeedFactor), Text(options.TowSpeedFactor), $"{Text(MIN_TOW_SPEED_FACTOR)} to {Text(MAX_TOW_SPEED_FACTOR)}"),
            (nameof(options.TowMaxDistance), Text(options.TowMaxDistance), $"{Text(MIN_TOW_MAX_DISTANCE)} to {Text(MAX_TOW_MAX_DISTANCE)}"),
            (nameof(options.TerrainSonarRange), Text(options.TerrainSonarRange), $"{Text(MIN_TERRAIN_SONAR_RANGE)} to {Text(MAX_TERRAIN_SONAR_RANGE)}"),
            (nameof(options.ClearanceWarningDistance), Text(options.ClearanceWarningDistance), $"{Text(MIN_CLEARANCE_WARNING_DISTANCE)} to {Text(MAX_CLEARANCE_WARNING_DISTANCE)}"),
            (nameof(options.GunnerShotEnergy), Text(options.GunnerShotEnergy), $"{Text(MIN_GUNNER_SHOT_ENERGY)} to {Text(MAX_GUNNER_SHOT_ENERGY)}"),
            (nameof(options.GunnerCooldownSeconds), Text(options.GunnerCooldownSeconds), $"{Text(MIN_GUNNER_COOLDOWN_SECONDS)} to {Text(MAX_GUNNER_COOLDOWN_SECONDS)}"),
            (nameof(options.GunnerRange), Text(options.GunnerRange), $"{Text(MIN_GUNNER_RANGE)} to {Text(MAX_GUNNER_RANGE)}"),
            (nameof(options.EngineerSpeedBonus), Text(options.EngineerSpeedBonus), $"{Text(MIN_ENGINEER_SPEED_BONUS)} to {Text(MAX_ENGINEER_SPEED_BONUS)}"),
            (nameof(options.OverdriveSeconds), Text(options.OverdriveSeconds), $"{Text(MIN_OVERDRIVE_SECONDS)} to {Text(MAX_OVERDRIVE_SECONDS)}"),
            (nameof(options.OverdriveCooldownSeconds), Text(options.OverdriveCooldownSeconds), $"{Text(MIN_OVERDRIVE_COOLDOWN_SECONDS)} to {Text(MAX_OVERDRIVE_COOLDOWN_SECONDS)}"),
            (nameof(options.ShieldSaving), Text(options.ShieldSaving), $"{Text(MIN_SHIELD_SAVING)} to {Text(MAX_SHIELD_SAVING)}"),
            (nameof(options.VentCooldownSeconds), Text(options.VentCooldownSeconds), $"{Text(MIN_VENT_COOLDOWN_SECONDS)} to {Text(MAX_VENT_COOLDOWN_SECONDS)}"),
            (nameof(options.SilentRunningBonus), Text(options.SilentRunningBonus), $"{Text(MIN_SILENT_RUNNING_BONUS)} to {Text(MAX_SILENT_RUNNING_BONUS)}"),
            (nameof(options.DroneSpeedFactor), Text(options.DroneSpeedFactor), $"{Text(MIN_DRONE_SPEED_FACTOR)} to {Text(MAX_DRONE_SPEED_FACTOR)}"),
            (nameof(options.DroneRange), Text(options.DroneRange), $"{Text(MIN_DRONE_RANGE)} to {Text(MAX_DRONE_RANGE)}"),
            (nameof(options.DroneStaticStart), Text(options.DroneStaticStart), $"{Text(MIN_DRONE_STATIC_START)} to {Text(MAX_DRONE_STATIC_START)}"),
            (nameof(options.DroneHealth), Text(options.DroneHealth), $"{Text(MIN_DRONE_HEALTH)} to {Text(MAX_DRONE_HEALTH)}"),
            (nameof(options.ZapEnergy), Text(options.ZapEnergy), $"{Text(MIN_ZAP_ENERGY)} to {Text(MAX_ZAP_ENERGY)}"),
            (nameof(options.ZapCooldownSeconds), Text(options.ZapCooldownSeconds), $"{Text(MIN_ZAP_COOLDOWN_SECONDS)} to {Text(MAX_ZAP_COOLDOWN_SECONDS)}"),
            (nameof(options.ZapRadius), Text(options.ZapRadius), $"{Text(MIN_ZAP_RADIUS)} to {Text(MAX_ZAP_RADIUS)}"),
            (nameof(options.ExtraModuleSlots), options.ExtraModuleSlots.ToString(CultureInfo.InvariantCulture), $"{ExtraModuleSlotRules.MIN_EXTRA} to {ExtraModuleSlotRules.MAX_EXTRA}")
        ];

        foreach ((string key, string expectedDefault, string range) in settings)
        {
            Match row = Regex.Match(page, $@"^\| `{key}` \|(?<default>[^|]*)\|(?<range>[^|]*)\|", RegexOptions.Multiline);
            row.Success.Should().BeTrue($"the settings table must have a row for {key}");
            row.Groups["default"].Value.Trim().Should().Be(expectedDefault, $"the default of {key}");
            row.Groups["range"].Value.Trim().Should().Be(range, $"the range of {key}");
        }
    }

    [TestMethod]
    public void TheOptionDescriptionsNameTheRangesOfTheNumbers()
    {
        // The text the host reads in server.cfg: "Allowed 0 to 3000" must be the limits of the rules
        foreach ((string property, float min, float max) in new[]
                 {
                     (nameof(SubnauticaServerOptions.ExtraDepthPerModule), MIN_EXTRA_DEPTH_PER_MODULE, MAX_EXTRA_DEPTH_PER_MODULE),
                     (nameof(SubnauticaServerOptions.AfterburnerSpeed), MIN_AFTERBURNER_SPEED, MAX_AFTERBURNER_SPEED),
                     (nameof(SubnauticaServerOptions.AfterburnerSeconds), MIN_AFTERBURNER_SECONDS, MAX_AFTERBURNER_SECONDS),
                     (nameof(SubnauticaServerOptions.AfterburnerCooldownSeconds), MIN_AFTERBURNER_COOLDOWN_SECONDS, MAX_AFTERBURNER_COOLDOWN_SECONDS),
                     (nameof(SubnauticaServerOptions.AfterburnerEnergy), MIN_AFTERBURNER_ENERGY, MAX_AFTERBURNER_ENERGY),
                     (nameof(SubnauticaServerOptions.SolarChargeSeamoth), MIN_SOLAR_CHARGE, MAX_SOLAR_CHARGE),
                     (nameof(SubnauticaServerOptions.SolarChargeCyclops), MIN_SOLAR_CHARGE, MAX_SOLAR_CHARGE),
                     (nameof(SubnauticaServerOptions.PowerShareRange), MIN_POWER_SHARE_RANGE, MAX_POWER_SHARE_RANGE),
                     (nameof(SubnauticaServerOptions.PowerShareRate), MIN_POWER_SHARE_RATE, MAX_POWER_SHARE_RATE),
                     (nameof(SubnauticaServerOptions.EmergencyPowerThreshold), MIN_EMERGENCY_POWER_THRESHOLD, MAX_EMERGENCY_POWER_THRESHOLD),
                     (nameof(SubnauticaServerOptions.EmergencyPowerReserve), MIN_EMERGENCY_POWER_RESERVE, MAX_EMERGENCY_POWER_RESERVE),
                     (nameof(SubnauticaServerOptions.EmergencyPowerRate), MIN_EMERGENCY_POWER_RATE, MAX_EMERGENCY_POWER_RATE),
                     (nameof(SubnauticaServerOptions.TowSpeedFactor), MIN_TOW_SPEED_FACTOR, MAX_TOW_SPEED_FACTOR),
                     (nameof(SubnauticaServerOptions.TowMaxDistance), MIN_TOW_MAX_DISTANCE, MAX_TOW_MAX_DISTANCE),
                     (nameof(SubnauticaServerOptions.TerrainSonarRange), MIN_TERRAIN_SONAR_RANGE, MAX_TERRAIN_SONAR_RANGE),
                     (nameof(SubnauticaServerOptions.ClearanceWarningDistance), MIN_CLEARANCE_WARNING_DISTANCE, MAX_CLEARANCE_WARNING_DISTANCE),
                     (nameof(SubnauticaServerOptions.GunnerShotEnergy), MIN_GUNNER_SHOT_ENERGY, MAX_GUNNER_SHOT_ENERGY),
                     (nameof(SubnauticaServerOptions.GunnerCooldownSeconds), MIN_GUNNER_COOLDOWN_SECONDS, MAX_GUNNER_COOLDOWN_SECONDS),
                     (nameof(SubnauticaServerOptions.GunnerRange), MIN_GUNNER_RANGE, MAX_GUNNER_RANGE),
                     (nameof(SubnauticaServerOptions.EngineerSpeedBonus), MIN_ENGINEER_SPEED_BONUS, MAX_ENGINEER_SPEED_BONUS),
                     (nameof(SubnauticaServerOptions.OverdriveSeconds), MIN_OVERDRIVE_SECONDS, MAX_OVERDRIVE_SECONDS),
                     (nameof(SubnauticaServerOptions.OverdriveCooldownSeconds), MIN_OVERDRIVE_COOLDOWN_SECONDS, MAX_OVERDRIVE_COOLDOWN_SECONDS),
                     (nameof(SubnauticaServerOptions.ShieldSaving), MIN_SHIELD_SAVING, MAX_SHIELD_SAVING),
                     (nameof(SubnauticaServerOptions.VentCooldownSeconds), MIN_VENT_COOLDOWN_SECONDS, MAX_VENT_COOLDOWN_SECONDS),
                     (nameof(SubnauticaServerOptions.SilentRunningBonus), MIN_SILENT_RUNNING_BONUS, MAX_SILENT_RUNNING_BONUS),
                     (nameof(SubnauticaServerOptions.DroneSpeedFactor), MIN_DRONE_SPEED_FACTOR, MAX_DRONE_SPEED_FACTOR),
                     (nameof(SubnauticaServerOptions.DroneRange), MIN_DRONE_RANGE, MAX_DRONE_RANGE),
                     (nameof(SubnauticaServerOptions.DroneStaticStart), MIN_DRONE_STATIC_START, MAX_DRONE_STATIC_START),
                     (nameof(SubnauticaServerOptions.DroneHealth), MIN_DRONE_HEALTH, MAX_DRONE_HEALTH),
                     (nameof(SubnauticaServerOptions.ZapEnergy), MIN_ZAP_ENERGY, MAX_ZAP_ENERGY),
                     (nameof(SubnauticaServerOptions.ZapCooldownSeconds), MIN_ZAP_COOLDOWN_SECONDS, MAX_ZAP_COOLDOWN_SECONDS),
                     (nameof(SubnauticaServerOptions.ZapRadius), MIN_ZAP_RADIUS, MAX_ZAP_RADIUS),
                     (nameof(SubnauticaServerOptions.ExtraModuleSlots), ExtraModuleSlotRules.MIN_EXTRA, ExtraModuleSlotRules.MAX_EXTRA)
                 })
        {
            string description = typeof(SubnauticaServerOptions).GetProperty(property)!.GetCustomAttributes(typeof(PropertyDescriptionAttribute), false).Cast<PropertyDescriptionAttribute>().Single().Description;

            description.Should().Contain($"Allowed {Text(min)} to {Text(max)}", $"the description of {property}");
        }
    }

    [TestMethod]
    public void ThePageDescribesTheTwoButtonsOfTheLightingPanelAndNoKeyForTheCyclopsHook()
    {
        string page = ReadPage();

        page.Should().Contain("lighting panel");
        page.Should().Contain("**Power share** button");
        page.Should().Contain("**off by default**");
        page.Should().Contain("**Anybody aboard can press it**");
        page.Should().Contain("another player takes the Cyclops' helm", "the tow ends by design when the helm changes hands");
        page.Should().NotContain("key **G**");
        page.Should().NotContain("press G");
        page.Should().NotContain("TowKeyBindingAction");
    }
}
