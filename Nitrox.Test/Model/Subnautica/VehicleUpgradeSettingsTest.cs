using System.Globalization;
using BinaryPack;
using Nitrox.Model.Configuration;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using static Nitrox.Model.Subnautica.DataStructures.GameLogic.VehicleUpgradeSettingsRules;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The balance of the vehicle upgrade modules (feature group 1): the defaults of the server options, the clamping, the way they reach a joining player and what the client keeps.
/// </summary>
[TestClass]
public class VehicleUpgradeSettingsTest
{
    // ---- defaults

    [TestMethod]
    public void TheDefaultsOfTheOptionsTheSettingsAndTheRulesAreTheSameNumbers()
    {
        SubnauticaServerOptions options = new();
        SessionSettings settings = new();

        options.VehicleUpgrades.Should().Be(DEFAULT_VEHICLE_UPGRADES).And.Be(settings.VehicleUpgrades);
        options.UpgradesUnlockedFromStart.Should().Be(DEFAULT_UPGRADES_UNLOCKED_FROM_START).And.Be(settings.UpgradesUnlockedFromStart);
        options.ExtraDepthPerModule.Should().Be(DEFAULT_EXTRA_DEPTH_PER_MODULE).And.Be(settings.ExtraDepthPerModule);
        options.AfterburnerSpeed.Should().Be(DEFAULT_AFTERBURNER_SPEED).And.Be(settings.AfterburnerSpeed);
        options.AfterburnerSeconds.Should().Be(DEFAULT_AFTERBURNER_SECONDS).And.Be(settings.AfterburnerSeconds);
        options.AfterburnerCooldownSeconds.Should().Be(DEFAULT_AFTERBURNER_COOLDOWN_SECONDS).And.Be(settings.AfterburnerCooldownSeconds);
        options.AfterburnerEnergy.Should().Be(DEFAULT_AFTERBURNER_ENERGY).And.Be(settings.AfterburnerEnergy);
        options.SolarChargeSeamoth.Should().Be(DEFAULT_SOLAR_CHARGE_SEAMOTH).And.Be(settings.SolarChargeSeamoth);
        options.SolarChargeCyclops.Should().Be(DEFAULT_SOLAR_CHARGE_CYCLOPS).And.Be(settings.SolarChargeCyclops);
        options.PowerShareRange.Should().Be(DEFAULT_POWER_SHARE_RANGE).And.Be(settings.PowerShareRange);
        options.PowerShareRate.Should().Be(DEFAULT_POWER_SHARE_RATE).And.Be(settings.PowerShareRate);
        options.EmergencyPowerThreshold.Should().Be(DEFAULT_EMERGENCY_POWER_THRESHOLD).And.Be(settings.EmergencyPowerThreshold);
        options.EmergencyPowerReserve.Should().Be(DEFAULT_EMERGENCY_POWER_RESERVE).And.Be(settings.EmergencyPowerReserve);
        options.EmergencyPowerRate.Should().Be(DEFAULT_EMERGENCY_POWER_RATE).And.Be(settings.EmergencyPowerRate);
        options.TowSpeedFactor.Should().Be(DEFAULT_TOW_SPEED_FACTOR).And.Be(settings.TowSpeedFactor);
        options.TowMaxDistance.Should().Be(DEFAULT_TOW_MAX_DISTANCE).And.Be(settings.TowMaxDistance);
        options.TerrainSonarRange.Should().Be(DEFAULT_TERRAIN_SONAR_RANGE).And.Be(settings.TerrainSonarRange);
        options.ClearanceWarningDistance.Should().Be(DEFAULT_CLEARANCE_WARNING_DISTANCE).And.Be(settings.ClearanceWarningDistance);
        options.GunnerShotEnergy.Should().Be(DEFAULT_GUNNER_SHOT_ENERGY).And.Be(settings.GunnerShotEnergy);
        options.GunnerCooldownSeconds.Should().Be(DEFAULT_GUNNER_COOLDOWN_SECONDS).And.Be(settings.GunnerCooldownSeconds);
        options.GunnerRange.Should().Be(DEFAULT_GUNNER_RANGE).And.Be(settings.GunnerRange);
        options.EngineerSpeedBonus.Should().Be(DEFAULT_ENGINEER_SPEED_BONUS).And.Be(settings.EngineerSpeedBonus);
        options.OverdriveSeconds.Should().Be(DEFAULT_OVERDRIVE_SECONDS).And.Be(settings.OverdriveSeconds);
        options.OverdriveCooldownSeconds.Should().Be(DEFAULT_OVERDRIVE_COOLDOWN_SECONDS).And.Be(settings.OverdriveCooldownSeconds);
        options.ShieldSaving.Should().Be(DEFAULT_SHIELD_SAVING).And.Be(settings.ShieldSaving);
        options.VentCooldownSeconds.Should().Be(DEFAULT_VENT_COOLDOWN_SECONDS).And.Be(settings.VentCooldownSeconds);
        options.SilentRunningBonus.Should().Be(DEFAULT_SILENT_RUNNING_BONUS).And.Be(settings.SilentRunningBonus);
        options.DroneSpeedFactor.Should().Be(DEFAULT_DRONE_SPEED_FACTOR).And.Be(settings.DroneSpeedFactor);
        options.DroneRange.Should().Be(DEFAULT_DRONE_RANGE).And.Be(settings.DroneRange);
        options.DroneStaticStart.Should().Be(DEFAULT_DRONE_STATIC_START).And.Be(settings.DroneStaticStart);
        options.DroneHealth.Should().Be(DEFAULT_DRONE_HEALTH).And.Be(settings.DroneHealth);
        options.ZapEnergy.Should().Be(DEFAULT_ZAP_ENERGY).And.Be(settings.ZapEnergy);
        options.ZapCooldownSeconds.Should().Be(DEFAULT_ZAP_COOLDOWN_SECONDS).And.Be(settings.ZapCooldownSeconds);
        options.ZapRadius.Should().Be(DEFAULT_ZAP_RADIUS).And.Be(settings.ZapRadius);
        options.ExtraModuleSlots.Should().Be(ExtraModuleSlotRules.DEFAULT_EXTRA).And.Be(settings.ExtraModuleSlots);
    }

    [TestMethod]
    public void TheDefaultsAreTheDocumentedOnes()
    {
        // docs/coop/VEHICLE_UPGRADES.md: the master switch on, nothing unlocked from the start, 300 m per tier, a 5 s burst with a 15 s cooldown for 5 percent of a power cell (200)
        DEFAULT_VEHICLE_UPGRADES.Should().BeTrue();
        DEFAULT_UPGRADES_UNLOCKED_FROM_START.Should().BeFalse();
        DEFAULT_EXTRA_DEPTH_PER_MODULE.Should().Be(300f);
        DEFAULT_AFTERBURNER_SECONDS.Should().Be(5f);
        DEFAULT_AFTERBURNER_COOLDOWN_SECONDS.Should().Be(15f);
        (DEFAULT_AFTERBURNER_ENERGY / 200f).Should().BeApproximately(0.05f, 0.0001f);

        // feature group 2: a Cyclops shares within 30 m at 2 energy per second, the emergency starts below 10 percent and leaves a docked vehicle 25 percent, a tow slows the tower to 70 percent
        DEFAULT_POWER_SHARE_RANGE.Should().Be(30f);
        DEFAULT_POWER_SHARE_RATE.Should().Be(2f);
        DEFAULT_EMERGENCY_POWER_THRESHOLD.Should().Be(10f);
        DEFAULT_EMERGENCY_POWER_RESERVE.Should().Be(25f);
        DEFAULT_TOW_SPEED_FACTOR.Should().Be(0.7f);

        // feature group 3, station 1: the terrain sonar shows 200 m around the Cyclops (the scanner room of the game shows up to 500), the clearance warning looks 20 m ahead
        DEFAULT_TERRAIN_SONAR_RANGE.Should().Be(200f);
        MAX_TERRAIN_SONAR_RANGE.Should().Be(500f);
        DEFAULT_CLEARANCE_WARNING_DISTANCE.Should().Be(20f);

        // feature group 3, station 2: a full charge of the gunner costs 10 of the Cyclops' power (the rifle's is 5 of 100), 3 seconds between shots, a sphere flies 60 m (the rifle's reaches 37.5)
        DEFAULT_GUNNER_SHOT_ENERGY.Should().Be(10f);
        DEFAULT_GUNNER_COOLDOWN_SECONDS.Should().Be(3f);
        DEFAULT_GUNNER_RANGE.Should().Be(60f);
        // feature group 3, station 3: 15 percent faster, a 12 s overdrive with a 45 s cooldown, a shield that uses 40 percent less, a vent every 30 s, silent running that is half as loud again
        DEFAULT_ENGINEER_SPEED_BONUS.Should().Be(0.15f);
        DEFAULT_OVERDRIVE_SECONDS.Should().Be(12f);
        DEFAULT_OVERDRIVE_COOLDOWN_SECONDS.Should().Be(45f);
        DEFAULT_SHIELD_SAVING.Should().Be(0.4f);
        DEFAULT_VENT_COOLDOWN_SECONDS.Should().Be(30f);
        DEFAULT_SILENT_RUNNING_BONUS.Should().Be(0.5f);
        // feature group 3, station 4: twice the Seamoth, a kilometre of signal with static from half of it, 100 health, a zap that costs 15 of 100 every 12 s over 20 m
        DEFAULT_DRONE_SPEED_FACTOR.Should().Be(2f);
        DEFAULT_DRONE_RANGE.Should().Be(1000f);
        DEFAULT_DRONE_STATIC_START.Should().Be(0.5f);
        DEFAULT_DRONE_HEALTH.Should().Be(100f);
        DEFAULT_ZAP_ENERGY.Should().Be(15f);
        DEFAULT_ZAP_COOLDOWN_SECONDS.Should().Be(12f);
        DEFAULT_ZAP_RADIUS.Should().Be(20f);
        MIN_GUNNER_RANGE.Should().BeLessThan(37.5f, "a host can shorten the shots below the rifle's reach");
    }

    [TestMethod]
    public void EveryDefaultIsInsideItsLimits()
    {
        List<string> messages = [];

        Apply(new SessionSettings(), new SubnauticaServerOptions(), messages);

        messages.Should().BeEmpty("the defaults must not be clamped");
    }

    // ---- clamping

    [TestMethod]
    public void AValueInsideTheLimitsIsKept()
    {
        List<string> messages = [];

        Clamp("Key", 12f, 0f, 20f, 5f, messages).Should().Be(12f);
        Clamp("Key", 0f, 0f, 20f, 5f, messages).Should().Be(0f, "the lower limit itself is allowed");
        Clamp("Key", 20f, 0f, 20f, 5f, messages).Should().Be(20f, "the upper limit itself is allowed");

        messages.Should().BeEmpty();
    }

    [TestMethod]
    public void AValueBelowTheLowerLimitIsRaisedToIt()
    {
        List<string> messages = [];

        Clamp("AfterburnerSeconds", -3f, 0.5f, 60f, 5f, messages).Should().Be(0.5f);

        messages.Should().ContainSingle().Which.Should().Be("Server option AfterburnerSeconds=-3 is below 0.5, using 0.5.");
    }

    [TestMethod]
    public void AValueAboveTheUpperLimitIsLoweredToIt()
    {
        List<string> messages = [];

        Clamp("ExtraDepthPerModule", 99999f, 0f, 3000f, 300f, messages).Should().Be(3000f);

        messages.Should().ContainSingle().Which.Should().Be("Server option ExtraDepthPerModule=99999 is above 3000, using 3000.");
    }

    [TestMethod]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(float.NegativeInfinity)]
    public void AValueThatIsNoNumberBecomesTheDefault(float value)
    {
        List<string> messages = [];

        Clamp("AfterburnerSpeed", value, 1f, 5f, 2f, messages).Should().Be(2f);

        messages.Should().ContainSingle().Which.Should().Be("Server option AfterburnerSpeed is not a number, using the default 2.");
    }

    [TestMethod]
    public void TheMessagesAreOptional()
    {
        Clamp("Key", 100f, 0f, 20f, 5f, null).Should().Be(20f);
    }

    [TestMethod]
    public void TheMessagesUseADotAndNoCommaWhateverTheCulture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            List<string> messages = [];

            Clamp("Key", 0.25f, 0.5f, 60f, 5f, messages);

            messages.Single().Should().Be("Server option Key=0.25 is below 0.5, using 0.5.");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    public void EveryNumberOfTheOptionsIsClampedAndReported()
    {
        SubnauticaServerOptions options = new()
        {
            ExtraDepthPerModule = 5000f,
            AfterburnerSpeed = 0.2f,
            AfterburnerSeconds = 1000f,
            AfterburnerCooldownSeconds = -1f,
            AfterburnerEnergy = 500f,
            SolarChargeSeamoth = -2f,
            SolarChargeCyclops = 9f,
            PowerShareRange = 1f,
            PowerShareRate = 99f,
            EmergencyPowerThreshold = 80f,
            EmergencyPowerReserve = -5f,
            EmergencyPowerRate = 500f,
            TowSpeedFactor = 0f,
            TowMaxDistance = 1000f,
            TerrainSonarRange = 5000f,
            ClearanceWarningDistance = 1f,
            GunnerShotEnergy = 9999f,
            GunnerCooldownSeconds = -5f,
            GunnerRange = 1f,
            EngineerSpeedBonus = 5f,
            OverdriveSeconds = 0f,
            OverdriveCooldownSeconds = 1f,
            ShieldSaving = 2f,
            VentCooldownSeconds = 9999f,
            SilentRunningBonus = -1f,
            DroneSpeedFactor = 9f,
            DroneRange = 1f,
            DroneStaticStart = 5f,
            DroneHealth = 0f,
            ZapEnergy = -5f,
            ZapCooldownSeconds = 9999f,
            ZapRadius = 1000f,
        };
        SessionSettings told = new();
        List<string> messages = [];

        Apply(told, options, messages);

        told.ExtraDepthPerModule.Should().Be(MAX_EXTRA_DEPTH_PER_MODULE);
        told.AfterburnerSpeed.Should().Be(MIN_AFTERBURNER_SPEED);
        told.AfterburnerSeconds.Should().Be(MAX_AFTERBURNER_SECONDS);
        told.AfterburnerCooldownSeconds.Should().Be(MIN_AFTERBURNER_COOLDOWN_SECONDS);
        told.AfterburnerEnergy.Should().Be(MAX_AFTERBURNER_ENERGY);
        told.SolarChargeSeamoth.Should().Be(MIN_SOLAR_CHARGE);
        told.SolarChargeCyclops.Should().Be(MAX_SOLAR_CHARGE);
        told.PowerShareRange.Should().Be(MIN_POWER_SHARE_RANGE);
        told.PowerShareRate.Should().Be(MAX_POWER_SHARE_RATE);
        told.EmergencyPowerThreshold.Should().Be(MAX_EMERGENCY_POWER_THRESHOLD);
        told.EmergencyPowerReserve.Should().Be(MIN_EMERGENCY_POWER_RESERVE);
        told.EmergencyPowerRate.Should().Be(MAX_EMERGENCY_POWER_RATE);
        told.TowSpeedFactor.Should().Be(MIN_TOW_SPEED_FACTOR);
        told.TowMaxDistance.Should().Be(MAX_TOW_MAX_DISTANCE);
        told.TerrainSonarRange.Should().Be(MAX_TERRAIN_SONAR_RANGE);
        told.ClearanceWarningDistance.Should().Be(MIN_CLEARANCE_WARNING_DISTANCE);
        told.GunnerShotEnergy.Should().Be(MAX_GUNNER_SHOT_ENERGY);
        told.GunnerCooldownSeconds.Should().Be(MIN_GUNNER_COOLDOWN_SECONDS);
        told.GunnerRange.Should().Be(MIN_GUNNER_RANGE);
        told.EngineerSpeedBonus.Should().Be(MAX_ENGINEER_SPEED_BONUS);
        told.OverdriveSeconds.Should().Be(MIN_OVERDRIVE_SECONDS);
        told.OverdriveCooldownSeconds.Should().Be(MIN_OVERDRIVE_COOLDOWN_SECONDS);
        told.ShieldSaving.Should().Be(MAX_SHIELD_SAVING);
        told.VentCooldownSeconds.Should().Be(MAX_VENT_COOLDOWN_SECONDS);
        told.SilentRunningBonus.Should().Be(MIN_SILENT_RUNNING_BONUS);
        told.DroneSpeedFactor.Should().Be(MAX_DRONE_SPEED_FACTOR);
        told.DroneRange.Should().Be(MIN_DRONE_RANGE);
        told.DroneStaticStart.Should().Be(MAX_DRONE_STATIC_START);
        told.DroneHealth.Should().Be(MIN_DRONE_HEALTH);
        told.ZapEnergy.Should().Be(MIN_ZAP_ENERGY);
        told.ZapCooldownSeconds.Should().Be(MAX_ZAP_COOLDOWN_SECONDS);
        told.ZapRadius.Should().Be(MAX_ZAP_RADIUS);
        messages.Should().HaveCount(32);
        messages.Should().Contain(message => message.Contains("ExtraDepthPerModule")).And.Contain(message => message.Contains("SolarChargeCyclops"));
    }

    [TestMethod]
    public void TheSwitchesAreNotClamped()
    {
        SubnauticaServerOptions options = new() { VehicleUpgrades = false, UpgradesUnlockedFromStart = true };
        SessionSettings told = new();

        Apply(told, options, []);

        told.VehicleUpgrades.Should().BeFalse();
        told.UpgradesUnlockedFromStart.Should().BeTrue();
    }

    // ---- the way to the joining player

    [TestMethod]
    public void TheSettingsATheServerTellsAreTheOptionsClampedAndNothingOfTheServersOwnSessionState()
    {
        SessionSettings session = new() { FastHatch = true, FastGrow = true, ExtraDepthPerModule = 1234f };
        SubnauticaServerOptions options = new() { ExtraDepthPerModule = 500f, VehicleUpgrades = false };
        List<string> messages = [];

        SessionSettings told = JoiningManager.BuildSessionSettings(session, options, messages);

        told.FastHatch.Should().BeTrue();
        told.FastGrow.Should().BeTrue();
        told.ExtraDepthPerModule.Should().Be(500f, "the number of the options, not a number of the server's own session settings");
        told.VehicleUpgrades.Should().BeFalse();
        messages.Should().BeEmpty();
    }

    [TestMethod]
    public void TheSettingsSurviveTheSerializerOfThePackets()
    {
        Packet.InitSerializer();
        SessionSettings told = JoiningManager.BuildSessionSettings(new SessionSettings(), new SubnauticaServerOptions
        {
            VehicleUpgrades = false,
            UpgradesUnlockedFromStart = true,
            ExtraDepthPerModule = 500f,
            AfterburnerSpeed = 3.5f,
            AfterburnerSeconds = 7f,
            AfterburnerCooldownSeconds = 20f,
            AfterburnerEnergy = 12.5f,
            SolarChargeSeamoth = 0.75f,
            SolarChargeCyclops = 1.25f,
            PowerShareRange = 45f,
            PowerShareRate = 3.5f,
            EmergencyPowerThreshold = 15f,
            EmergencyPowerReserve = 40f,
            EmergencyPowerRate = 7.5f,
            TowSpeedFactor = 0.55f,
            TowMaxDistance = 60f,
            TerrainSonarRange = 350f,
            ClearanceWarningDistance = 33f,
            GunnerShotEnergy = 12.5f,
            GunnerCooldownSeconds = 4.5f,
            GunnerRange = 80f,
            EngineerSpeedBonus = 0.3f,
            OverdriveSeconds = 20f,
            OverdriveCooldownSeconds = 90f,
            ShieldSaving = 0.6f,
            VentCooldownSeconds = 15f,
            SilentRunningBonus = 0.75f,
            DroneSpeedFactor = 1.5f,
            DroneRange = 750f,
            DroneStaticStart = 0.4f,
            DroneHealth = 150f,
            ZapEnergy = 25f,
            ZapCooldownSeconds = 8f,
            ZapRadius = 30f,
            ExtraModuleSlots = 1,
            CraftFromLockers = false
        });

        SessionSettings received = BinaryConverter.Deserialize<SessionSettings>(BinaryConverter.Serialize(told));

        received.VehicleUpgrades.Should().BeFalse();
        received.UpgradesUnlockedFromStart.Should().BeTrue();
        received.ExtraDepthPerModule.Should().Be(500f);
        received.AfterburnerSpeed.Should().Be(3.5f);
        received.AfterburnerSeconds.Should().Be(7f);
        received.AfterburnerCooldownSeconds.Should().Be(20f);
        received.AfterburnerEnergy.Should().Be(12.5f);
        received.SolarChargeSeamoth.Should().Be(0.75f);
        received.SolarChargeCyclops.Should().Be(1.25f);
        received.PowerShareRange.Should().Be(45f);
        received.PowerShareRate.Should().Be(3.5f);
        received.EmergencyPowerThreshold.Should().Be(15f);
        received.EmergencyPowerReserve.Should().Be(40f);
        received.EmergencyPowerRate.Should().Be(7.5f);
        received.TowSpeedFactor.Should().Be(0.55f);
        received.TowMaxDistance.Should().Be(60f);
        received.TerrainSonarRange.Should().Be(350f);
        received.ClearanceWarningDistance.Should().Be(33f);
        received.GunnerShotEnergy.Should().Be(12.5f);
        received.GunnerCooldownSeconds.Should().Be(4.5f);
        received.GunnerRange.Should().Be(80f);
        received.EngineerSpeedBonus.Should().Be(0.3f);
        received.OverdriveSeconds.Should().Be(20f);
        received.OverdriveCooldownSeconds.Should().Be(90f);
        received.ShieldSaving.Should().Be(0.6f);
        received.VentCooldownSeconds.Should().Be(15f);
        received.SilentRunningBonus.Should().Be(0.75f);
        received.DroneSpeedFactor.Should().Be(1.5f);
        received.DroneRange.Should().Be(750f);
        received.DroneStaticStart.Should().Be(0.4f);
        received.DroneHealth.Should().Be(150f);
        received.ZapEnergy.Should().Be(25f);
        received.ZapCooldownSeconds.Should().Be(8f);
        received.ZapRadius.Should().Be(30f);
        received.ExtraModuleSlots.Should().Be(1);
        received.CraftFromLockers.Should().BeFalse();
    }

    // ---- what the client keeps

    [TestMethod]
    public void TheClientKeepsWhatTheServerToldAndCanBeResetToTheDefaults()
    {
        try
        {
            SessionSettings told = JoiningManager.BuildSessionSettings(new SessionSettings(), new SubnauticaServerOptions { VehicleUpgrades = false, ExtraDepthPerModule = 500f, AfterburnerSeconds = 8f });
            int changes = 0;
            Action handler = () => changes++;
            VehicleUpgradeSettings.Changed += handler;

            try
            {
                VehicleUpgradeSettings.Apply(told);
            }
            finally
            {
                VehicleUpgradeSettings.Changed -= handler;
            }

            VehicleUpgradeSettings.Enabled.Should().BeFalse();
            VehicleUpgradeSettings.ExtraDepthPerModule.Should().Be(500f);
            VehicleUpgradeSettings.AfterburnerSeconds.Should().Be(8f);
            changes.Should().Be(1);

            VehicleUpgradeSettings.Reset();

            VehicleUpgradeSettings.Enabled.Should().Be(DEFAULT_VEHICLE_UPGRADES);
            VehicleUpgradeSettings.ExtraDepthPerModule.Should().Be(DEFAULT_EXTRA_DEPTH_PER_MODULE);
            VehicleUpgradeSettings.AfterburnerSeconds.Should().Be(DEFAULT_AFTERBURNER_SECONDS);
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
    }

    [TestMethod]
    public void TheSettingsAreNotReceivedUntilTheServerSentThemAndNotAfterTheSessionEnded()
    {
        try
        {
            VehicleUpgradeSettings.Reset();
            VehicleUpgradeSettings.Received.Should().BeFalse("a client that has not joined yet only has the defaults, and nothing that depends on the settings may act on them");

            VehicleUpgradeSettings.Apply(new SessionSettings { VehicleUpgrades = false });

            VehicleUpgradeSettings.Received.Should().BeTrue();
            VehicleUpgradeSettings.Enabled.Should().BeFalse();

            VehicleUpgradeSettings.Reset();

            VehicleUpgradeSettings.Received.Should().BeFalse();
            VehicleUpgradeSettings.Enabled.Should().Be(DEFAULT_VEHICLE_UPGRADES);
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
    }

    [TestMethod]
    public void TheChangedEventAlsoComesWhenTheSessionEndsSoThatTheTextsGoBackToTheDefaults()
    {
        try
        {
            VehicleUpgradeSettings.Apply(new SessionSettings { ExtraDepthPerModule = 500f });
            int changes = 0;
            bool receivedAtTheEnd = true;
            void Handler()
            {
                changes++;
                receivedAtTheEnd = VehicleUpgradeSettings.Received;
            }
            VehicleUpgradeSettings.Changed += Handler;

            VehicleUpgradeSettings.Reset();
            VehicleUpgradeSettings.Changed -= Handler;

            changes.Should().Be(1);
            receivedAtTheEnd.Should().BeFalse("a handler that acts on the settings has to see that they are gone");
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
    }

    [TestMethod]
    public void TheClientClampsAgainWhatAServerOfAnotherBuildSends()
    {
        try
        {
            SessionSettings hostile = new()
            {
                ExtraDepthPerModule = float.PositiveInfinity, AfterburnerSpeed = 1000f, AfterburnerSeconds = float.NaN, SolarChargeSeamoth = -5f,
                PowerShareRange = float.NaN, PowerShareRate = 1000f, EmergencyPowerThreshold = -1f, EmergencyPowerReserve = float.PositiveInfinity, EmergencyPowerRate = float.NegativeInfinity, TowSpeedFactor = 7f, TowMaxDistance = float.NaN,
                TerrainSonarRange = float.NegativeInfinity, ClearanceWarningDistance = 9000f, GunnerShotEnergy = float.NaN, GunnerCooldownSeconds = 1000f, GunnerRange = -3f,
                EngineerSpeedBonus = float.NaN, OverdriveSeconds = 1000f, OverdriveCooldownSeconds = float.PositiveInfinity, ShieldSaving = -1f, VentCooldownSeconds = 0f, SilentRunningBonus = 7f,
                DroneSpeedFactor = float.NaN, DroneRange = float.PositiveInfinity, DroneStaticStart = -1f, DroneHealth = 9999f, ZapEnergy = float.NaN, ZapCooldownSeconds = 0f, ZapRadius = -4f
            };

            VehicleUpgradeSettings.Apply(hostile);

            VehicleUpgradeSettings.ExtraDepthPerModule.Should().Be(DEFAULT_EXTRA_DEPTH_PER_MODULE);
            VehicleUpgradeSettings.AfterburnerSpeed.Should().Be(MAX_AFTERBURNER_SPEED);
            VehicleUpgradeSettings.AfterburnerSeconds.Should().Be(DEFAULT_AFTERBURNER_SECONDS);
            VehicleUpgradeSettings.SolarChargeSeamoth.Should().Be(MIN_SOLAR_CHARGE);
            VehicleUpgradeSettings.PowerShareRange.Should().Be(DEFAULT_POWER_SHARE_RANGE);
            VehicleUpgradeSettings.PowerShareRate.Should().Be(MAX_POWER_SHARE_RATE);
            VehicleUpgradeSettings.EmergencyPowerThreshold.Should().Be(MIN_EMERGENCY_POWER_THRESHOLD);
            VehicleUpgradeSettings.EmergencyPowerReserve.Should().Be(DEFAULT_EMERGENCY_POWER_RESERVE);
            VehicleUpgradeSettings.EmergencyPowerRate.Should().Be(DEFAULT_EMERGENCY_POWER_RATE);
            VehicleUpgradeSettings.TowSpeedFactor.Should().Be(MAX_TOW_SPEED_FACTOR);
            VehicleUpgradeSettings.TowMaxDistance.Should().Be(DEFAULT_TOW_MAX_DISTANCE);
            VehicleUpgradeSettings.TerrainSonarRange.Should().Be(DEFAULT_TERRAIN_SONAR_RANGE);
            VehicleUpgradeSettings.ClearanceWarningDistance.Should().Be(MAX_CLEARANCE_WARNING_DISTANCE);
            VehicleUpgradeSettings.GunnerShotEnergy.Should().Be(DEFAULT_GUNNER_SHOT_ENERGY);
            VehicleUpgradeSettings.GunnerCooldownSeconds.Should().Be(MAX_GUNNER_COOLDOWN_SECONDS);
            VehicleUpgradeSettings.GunnerRange.Should().Be(MIN_GUNNER_RANGE);
            VehicleUpgradeSettings.EngineerSpeedBonus.Should().Be(DEFAULT_ENGINEER_SPEED_BONUS);
            VehicleUpgradeSettings.OverdriveSeconds.Should().Be(MAX_OVERDRIVE_SECONDS);
            VehicleUpgradeSettings.OverdriveCooldownSeconds.Should().Be(DEFAULT_OVERDRIVE_COOLDOWN_SECONDS);
            VehicleUpgradeSettings.ShieldSaving.Should().Be(MIN_SHIELD_SAVING);
            VehicleUpgradeSettings.VentCooldownSeconds.Should().Be(MIN_VENT_COOLDOWN_SECONDS);
            VehicleUpgradeSettings.SilentRunningBonus.Should().Be(MAX_SILENT_RUNNING_BONUS);
            VehicleUpgradeSettings.DroneSpeedFactor.Should().Be(DEFAULT_DRONE_SPEED_FACTOR);
            VehicleUpgradeSettings.DroneRange.Should().Be(DEFAULT_DRONE_RANGE);
            VehicleUpgradeSettings.DroneStaticStart.Should().Be(MIN_DRONE_STATIC_START);
            VehicleUpgradeSettings.DroneHealth.Should().Be(MAX_DRONE_HEALTH);
            VehicleUpgradeSettings.ZapEnergy.Should().Be(DEFAULT_ZAP_ENERGY);
            VehicleUpgradeSettings.ZapCooldownSeconds.Should().Be(MIN_ZAP_COOLDOWN_SECONDS);
            VehicleUpgradeSettings.ZapRadius.Should().Be(MIN_ZAP_RADIUS);
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
    }
}
