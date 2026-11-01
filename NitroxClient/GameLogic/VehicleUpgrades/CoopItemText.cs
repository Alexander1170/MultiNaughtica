using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The names and tooltips of the new items: plain strings with placeholders in <see cref="CoopItems" />, filled with the numbers the server told this client (<see cref="VehicleUpgradeSettings" />).
/// Free of Unity types. The game reads them from its language dictionary (<c>Language.strings</c>) under the item's name and under "Tooltip_" plus its name;
/// <see cref="Apply(IDictionary{string, string})" /> writes both, and is called again after every language load (the game clears the dictionary) and after the settings arrived.
/// </summary>
public static class CoopItemText
{
    /// <summary>A number as the tooltips show it: no trailing zeros, a dot, whatever the culture of the player.</summary>
    public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The numbers of feature group 2 that the tooltips show: the power share and the tow hooks.</summary>
    public readonly struct GroupTwoNumbers
    {
        public GroupTwoNumbers(float shareRange, float shareRate, float threshold, float reserve, float towSpeedFactor, float towMaxDistance)
        {
            ShareRange = shareRange;
            ShareRate = shareRate;
            Threshold = threshold;
            Reserve = reserve;
            TowSpeedFactor = towSpeedFactor;
            TowMaxDistance = towMaxDistance;
        }

        public float ShareRange { get; }
        public float ShareRate { get; }
        public float Threshold { get; }
        public float Reserve { get; }
        public float TowSpeedFactor { get; }
        public float TowMaxDistance { get; }

        public static GroupTwoNumbers Defaults => new(VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RANGE, VehicleUpgradeSettingsRules.DEFAULT_POWER_SHARE_RATE, VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_THRESHOLD,
                                                       VehicleUpgradeSettingsRules.DEFAULT_EMERGENCY_POWER_RESERVE, VehicleUpgradeSettingsRules.DEFAULT_TOW_SPEED_FACTOR, VehicleUpgradeSettingsRules.DEFAULT_TOW_MAX_DISTANCE);

        public static GroupTwoNumbers Current => new(VehicleUpgradeSettings.PowerShareRange, VehicleUpgradeSettings.PowerShareRate, VehicleUpgradeSettings.EmergencyPowerThreshold, VehicleUpgradeSettings.EmergencyPowerReserve,
                                                     VehicleUpgradeSettings.TowSpeedFactor, VehicleUpgradeSettings.TowMaxDistance);
    }

    public static string Tooltip(CoopItemDefinition item, float extraDepthPerModule, float afterburnerSpeed, float afterburnerSeconds, float afterburnerCooldown, float afterburnerEnergy, float solarSeamoth,
                                 float solarCyclops, bool enabled = true)
    {
        return Tooltip(item, extraDepthPerModule, afterburnerSpeed, afterburnerSeconds, afterburnerCooldown, afterburnerEnergy, solarSeamoth, solarCyclops, GroupTwoNumbers.Defaults, enabled);
    }

    /// <summary>The numbers of feature group 3 that the tooltips show: the terrain sonar and the navigator console, the gunner station, the engineer console.</summary>
    public readonly struct GroupThreeNumbers
    {
        public GroupThreeNumbers(float terrainSonarRange, float clearanceWarningDistance, float gunnerShotEnergy = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_SHOT_ENERGY,
                                 float gunnerCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_COOLDOWN_SECONDS, float gunnerRange = VehicleUpgradeSettingsRules.DEFAULT_GUNNER_RANGE,
                                 float engineerSpeedBonus = VehicleUpgradeSettingsRules.DEFAULT_ENGINEER_SPEED_BONUS, float overdriveSeconds = VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_SECONDS,
                                 float overdriveCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_OVERDRIVE_COOLDOWN_SECONDS, float shieldSaving = VehicleUpgradeSettingsRules.DEFAULT_SHIELD_SAVING,
                                 float ventCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_VENT_COOLDOWN_SECONDS, float silentRunningBonus = VehicleUpgradeSettingsRules.DEFAULT_SILENT_RUNNING_BONUS,
                                 float droneSpeedFactor = VehicleUpgradeSettingsRules.DEFAULT_DRONE_SPEED_FACTOR, float droneRange = VehicleUpgradeSettingsRules.DEFAULT_DRONE_RANGE, float droneHealth = VehicleUpgradeSettingsRules.DEFAULT_DRONE_HEALTH,
                                 float zapEnergy = VehicleUpgradeSettingsRules.DEFAULT_ZAP_ENERGY, float zapCooldownSeconds = VehicleUpgradeSettingsRules.DEFAULT_ZAP_COOLDOWN_SECONDS, float zapRadius = VehicleUpgradeSettingsRules.DEFAULT_ZAP_RADIUS)
        {
            TerrainSonarRange = terrainSonarRange;
            ClearanceWarningDistance = clearanceWarningDistance;
            GunnerShotEnergy = gunnerShotEnergy;
            GunnerCooldownSeconds = gunnerCooldownSeconds;
            GunnerRange = gunnerRange;
            EngineerSpeedBonus = engineerSpeedBonus;
            OverdriveSeconds = overdriveSeconds;
            OverdriveCooldownSeconds = overdriveCooldownSeconds;
            ShieldSaving = shieldSaving;
            VentCooldownSeconds = ventCooldownSeconds;
            SilentRunningBonus = silentRunningBonus;
            DroneSpeedFactor = droneSpeedFactor;
            DroneRange = droneRange;
            DroneHealth = droneHealth;
            ZapEnergy = zapEnergy;
            ZapCooldownSeconds = zapCooldownSeconds;
            ZapRadius = zapRadius;
        }

        public float TerrainSonarRange { get; }
        public float ClearanceWarningDistance { get; }
        public float GunnerShotEnergy { get; }
        public float GunnerCooldownSeconds { get; }
        public float GunnerRange { get; }
        public float EngineerSpeedBonus { get; }
        public float OverdriveSeconds { get; }
        public float OverdriveCooldownSeconds { get; }
        public float ShieldSaving { get; }
        public float VentCooldownSeconds { get; }
        public float SilentRunningBonus { get; }
        public float DroneSpeedFactor { get; }
        public float DroneRange { get; }
        public float DroneHealth { get; }
        public float ZapEnergy { get; }
        public float ZapCooldownSeconds { get; }
        public float ZapRadius { get; }

        public static GroupThreeNumbers Defaults => new(VehicleUpgradeSettingsRules.DEFAULT_TERRAIN_SONAR_RANGE, VehicleUpgradeSettingsRules.DEFAULT_CLEARANCE_WARNING_DISTANCE);

        public static GroupThreeNumbers Current => new(VehicleUpgradeSettings.TerrainSonarRange, VehicleUpgradeSettings.ClearanceWarningDistance, VehicleUpgradeSettings.GunnerShotEnergy,
                                                       VehicleUpgradeSettings.GunnerCooldownSeconds, VehicleUpgradeSettings.GunnerRange, VehicleUpgradeSettings.EngineerSpeedBonus, VehicleUpgradeSettings.OverdriveSeconds,
                                                       VehicleUpgradeSettings.OverdriveCooldownSeconds, VehicleUpgradeSettings.ShieldSaving, VehicleUpgradeSettings.VentCooldownSeconds, VehicleUpgradeSettings.SilentRunningBonus,
                                                       VehicleUpgradeSettings.DroneSpeedFactor, VehicleUpgradeSettings.DroneRange, VehicleUpgradeSettings.DroneHealth, VehicleUpgradeSettings.ZapEnergy,
                                                       VehicleUpgradeSettings.ZapCooldownSeconds, VehicleUpgradeSettings.ZapRadius);
    }

    public static string Tooltip(CoopItemDefinition item, float extraDepthPerModule, float afterburnerSpeed, float afterburnerSeconds, float afterburnerCooldown, float afterburnerEnergy, float solarSeamoth,
                                 float solarCyclops, GroupTwoNumbers groupTwo, bool enabled = true)
    {
        return Tooltip(item, extraDepthPerModule, afterburnerSpeed, afterburnerSeconds, afterburnerCooldown, afterburnerEnergy, solarSeamoth, solarCyclops, groupTwo, GroupThreeNumbers.Defaults, enabled);
    }

    public static string Tooltip(CoopItemDefinition item, float extraDepthPerModule, float afterburnerSpeed, float afterburnerSeconds, float afterburnerCooldown, float afterburnerEnergy, float solarSeamoth,
                                 float solarCyclops, GroupTwoNumbers groupTwo, GroupThreeNumbers groupThree, bool enabled = true)
    {
        // With the vehicle upgrades switched off the new modules do nothing and the game's Seamoth Solar Charger works like in the game (1 per second): the text must not promise the server's numbers
        if (!enabled && item.Adopted)
        {
            solarSeamoth = 1f;
        }
        string text = item.Tooltip;
        if (item.Kind == CoopItemKind.DepthModule)
        {
            text = text.Replace("{depth}", Number(DepthModuleRules.DepthFor(item.Vehicle, item.Tier, extraDepthPerModule)));
        }
        text = text.Replace("{speed}", Number(afterburnerSpeed));
        text = text.Replace("{seconds}", Number(afterburnerSeconds));
        text = text.Replace("{cooldown}", Number(afterburnerCooldown));
        text = text.Replace("{energy}", Number(afterburnerEnergy));
        text = text.Replace("{rate}", Number(item.Vehicle == CoopVehicle.Cyclops ? solarCyclops : solarSeamoth));
        text = text.Replace("{shareRange}", Number(groupTwo.ShareRange));
        text = text.Replace("{shareRate}", Number(groupTwo.ShareRate));
        text = text.Replace("{threshold}", Number(groupTwo.Threshold));
        text = text.Replace("{reserve}", Number(groupTwo.Reserve));
        text = text.Replace("{towPercent}", Number(groupTwo.TowSpeedFactor * 100f));
        text = text.Replace("{towDistance}", Number(groupTwo.TowMaxDistance));
        text = text.Replace("{towAttach}", Number(TowRules.AttachRange(groupTwo.TowMaxDistance)));
        text = text.Replace("{sonarRange}", Number(groupThree.TerrainSonarRange));
        text = text.Replace("{clearance}", Number(groupThree.ClearanceWarningDistance));
        text = text.Replace("{gunnerEnergy}", Number(groupThree.GunnerShotEnergy));
        text = text.Replace("{gunnerCooldown}", Number(groupThree.GunnerCooldownSeconds));
        text = text.Replace("{gunnerRange}", Number(groupThree.GunnerRange));
        text = text.Replace("{engineerSpeed}", Number(groupThree.EngineerSpeedBonus * 100f));
        text = text.Replace("{overdriveSeconds}", Number(groupThree.OverdriveSeconds));
        text = text.Replace("{overdriveCooldown}", Number(groupThree.OverdriveCooldownSeconds));
        text = text.Replace("{shieldSaving}", Number(groupThree.ShieldSaving * 100f));
        text = text.Replace("{ventCooldown}", Number(groupThree.VentCooldownSeconds));
        text = text.Replace("{silentBonus}", Number(groupThree.SilentRunningBonus * 100f));
        text = text.Replace("{droneSpeed}", Number(groupThree.DroneSpeedFactor));
        text = text.Replace("{droneRange}", Number(groupThree.DroneRange));
        text = text.Replace("{droneHealth}", Number(groupThree.DroneHealth));
        text = text.Replace("{zapEnergy}", Number(groupThree.ZapEnergy));
        text = text.Replace("{zapCooldown}", Number(groupThree.ZapCooldownSeconds));
        text = text.Replace("{zapRadius}", Number(groupThree.ZapRadius));
        if (!enabled && !item.Adopted)
        {
            text += " Switched off on this server: it does nothing.";
        }
        return text;
    }

    /// <summary>The tooltip with the numbers of the settings this client got from the server.</summary>
    public static string Tooltip(CoopItemDefinition item)
    {
        return Tooltip(item, VehicleUpgradeSettings.ExtraDepthPerModule, VehicleUpgradeSettings.AfterburnerSpeed, VehicleUpgradeSettings.AfterburnerSeconds, VehicleUpgradeSettings.AfterburnerCooldownSeconds,
                       VehicleUpgradeSettings.AfterburnerEnergy, VehicleUpgradeSettings.SolarChargeSeamoth, VehicleUpgradeSettings.SolarChargeCyclops, GroupTwoNumbers.Current, GroupThreeNumbers.Current, VehicleUpgradeSettings.Enabled);
    }

    /// <summary>
    /// Writes the name and the tooltip of every item into the game's language dictionary. An adopted item keeps the name the game gives it (and gets ours only when it has none); its tooltip is
    /// always ours, because it has to say what the server's settings make of it.
    /// </summary>
    public static void Apply(IDictionary<string, string> strings)
    {
        foreach (CoopItemDefinition item in CoopItems.All)
        {
            // The game looks the name of an ingredient in a recipe tooltip up under "<name>.TooltipIngredient" and the name in the PDA's blueprint list under "<name>.BlueprintsTab" and falls back to
            // the plain name when they are missing (and logs the missing key once): both are given, with the plain name
            foreach (string key in new[] { item.Key, item.IngredientKey, item.BlueprintKey })
            {
                if (!item.Adopted || !strings.ContainsKey(key))
                {
                    strings[key] = item.DisplayName;
                }
            }
            strings[item.TooltipKey] = Tooltip(item);
        }
    }
}
