using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Nitrox.Model.Constants;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Serialization;

namespace Nitrox.Model.Configuration;

/// <summary>
///     Options that are tied to a hosted Subnautica world.
/// </summary>
[SerializableFileName("server.cfg")]
[PropertyDescription("Server settings can be changed here")]
public sealed partial class SubnauticaServerOptions
{
    public const string CONFIG_SECTION_PATH = "GameServer";

    [Range(1, byte.MaxValue)]
    public byte MaxConnections { get; set; } = 100;

    [Range(1, ushort.MaxValue)]
    public ushort ServerPort { get; set; } = SubnauticaServerConstants.DEFAULT_PORT;

    [PropertyDescription("If not empty, users will be asked to enter a password to join the server")]
    [RegularExpression(@"\w+")]
    public string ServerPassword { get; set; } = "";

    public SubnauticaGameMode GameMode { get; set; }

    [PropertyDescription("Measured in milliseconds. Values less than 1 second will disable auto saving.")]
    public int SaveInterval { get; set; } = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;

    [Range(0, int.MaxValue)]
    public int MaxBackups { get; set; } = 10;

    [Range(30001, int.MaxValue)]
    public int InitialSyncTimeout { get; set; } = 300000;

    [PropertyDescription("Possible values:", typeof(Perms))]
    public Perms DefaultPlayerPerm { get; set; } = Perms.DEFAULT;

    [PropertyDescription("If true, players using localhost get admin by default - disable if you're using a proxy server")]
    public bool LocalhostIsAdmin { get; set; } = true;

    public float DefaultOxygenValue { get; set; } = 45;

    public float DefaultMaxOxygenValue { get; set; } = 45;
    public float DefaultHealthValue { get; set; } = 80;
    public float DefaultHungerValue { get; set; } = 50.5f;
    public float DefaultThirstValue { get; set; } = 90.5f;

    [PropertyDescription("Recommended to keep at 0.1 which is the default starting value. If set to 0, new players are cured by default.")]
    public float DefaultInfectionValue { get; set; } = 0.1f;

    [PropertyDescription("If set to true, the server will try to open ports on your router via UPnP (off by default in the co-op fork: nothing opens a port by surprise)")]
    public bool PortForward { get; set; }

    [PropertyDescription("Determines whether the server will listen for and reply to LAN discovery requests (off by default in the co-op fork; players add the server by its address)")]
    public bool LanDiscovery { get; set; }

    [PropertyDescription("Set to true to Cache entities for the whole map on next run. \nWARNING! Will make server load take longer on the cache run but players will gain a performance boost when entering new areas.")]
    public bool CreateFullEntityCache { get; set; } = false;

    [Required]
    [RegularExpression(@"\w+")]
    public string Seed { get; set; } = "";

    public bool DisableConsole { get; set; }
    public string? AdminPassword { get; set; } = "";
    public bool KeepInventoryOnDeath { get; set; }

    [PropertyDescription("Places a beacon where players die")]
    public bool MarkDeathPointsWithBeacon { get; set; }
    public bool PvpEnabled { get; set; } = true;

    [PropertyDescription("When true, the crafting stations of a base (fabricator, modification station, vehicle upgrade console) and of a Cyclops also use the items in the lockers of that base or Cyclops, after the player's own inventory. Players learn the setting when they join.")]
    public bool CraftFromLockers { get; set; } = true;

    [PropertyDescription("Master switch of the vehicle upgrade modules of the co-op build (Seamoth depth MK4/MK5, Prawn Suit depth MK3/MK4, Cyclops depth MK4/MK5, Seamoth afterburner, Seamoth and Cyclops solar chargers, Cyclops power share, the tow hooks, Seamoth rider bar, Cyclops terrain sonar and its navigator console). When false no fabricator shows their recipes and installed modules do nothing. Players learn the setting when they join.")]
    public bool VehicleUpgrades { get; set; } = true;

    [PropertyDescription("When true every vehicle upgrade module is known from the start (its blueprint is unlocked when a player joins). When false each one unlocks together with the blueprint it builds on: the depth modules with the previous tier of that vehicle (Seamoth MK3, Prawn Suit MK2, Cyclops MK3), the afterburner and the Seamoth solar charger with the Seamoth, the Cyclops solar charger with the Cyclops.")]
    public bool UpgradesUnlockedFromStart { get; set; } = false;

    [PropertyDescription("Metres of crush depth that each new depth module tier adds to the best vanilla module of its vehicle. With 300: Seamoth MK4 1200 m and MK5 1500 m (vanilla MK3 gives 900), Prawn Suit MK3 2000 m and MK4 2300 m (vanilla MK2 gives 1700), Cyclops MK4 2000 m and MK5 2300 m (vanilla MK3 gives 1700). Allowed 0 to 3000; a number outside is clamped to the nearest limit and a value that is not a number uses the default (the server log says so).")]
    public float ExtraDepthPerModule { get; set; } = 300f;

    [PropertyDescription("How many times faster the Seamoth is while its afterburner burns (2 is double speed). Allowed 1 to 5; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float AfterburnerSpeed { get; set; } = 2f;

    [PropertyDescription("How long one use of the Seamoth afterburner burns, in seconds. Allowed 0.5 to 60; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float AfterburnerSeconds { get; set; } = 5f;

    [PropertyDescription("How many seconds after a burn the Seamoth afterburner cannot be used again (the quick slot shows the cooldown). Allowed 0 to 300; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float AfterburnerCooldownSeconds { get; set; } = 15f;

    [PropertyDescription("Energy one use of the Seamoth afterburner takes from the Seamoth's power cell. A power cell holds 200, so 10 is 5 percent of one. Allowed 0 to 200; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float AfterburnerEnergy { get; set; } = 10f;

    [PropertyDescription("Energy per second that EACH Seamoth Solar Charger module adds to the Seamoth's power cell at the surface in full daylight. 1 is how the game's own module works; several modules add up, like in the game. Like a base's solar panel it gives less in the morning and evening, nothing at night and nothing deeper than about 200 m. Allowed 0 to 5; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float SolarChargeSeamoth { get; set; } = 1f;

    [PropertyDescription("Energy per second the Cyclops Solar Charger adds to the power cells of the Cyclops at the surface in full daylight, with the same falloff (morning and evening, night, depth) as the Seamoth module. Only one Cyclops Solar Charger counts. For comparison: the Cyclops Thermal Reactor Module adds up to 1.5 per second in hot water and a base's solar panel up to 1.25. Allowed 0 to 5; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float SolarChargeCyclops { get; set; } = 1.25f;

    [PropertyDescription("How far from a Cyclops with a Cyclops Power Share module a Seamoth or Prawn Suit can be and still be charged by it, in metres. Allowed 5 to 200; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float PowerShareRange { get; set; } = 30f;

    [PropertyDescription("Energy per second that a Cyclops with a Cyclops Power Share module gives to EACH Seamoth or Prawn Suit in range that is not full (a power cell holds 200). 0 switches the charging of vehicles off. Allowed 0 to 20; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float PowerShareRate { get; set; } = 2f;

    [PropertyDescription("Emergency power: when the power of a Cyclops with a Cyclops Power Share module falls below this percent it draws power from the vehicle docked in it, down to EmergencyPowerReserve. 0 switches the emergency power off. Allowed 0 to 50; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float EmergencyPowerThreshold { get; set; } = 10f;

    [PropertyDescription("Emergency power never draws a docked vehicle below this percent of its own power. Allowed 0 to 90; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float EmergencyPowerReserve { get; set; } = 25f;

    [PropertyDescription("Energy per second that a Cyclops below its emergency threshold draws from the vehicle docked in it. 0 switches the emergency power off. Allowed 0 to 50; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float EmergencyPowerRate { get; set; } = 5f;

    [PropertyDescription("How fast a vehicle moves while it tows another one, as a factor of its normal speed (0.7 is 30 percent slower). Allowed 0.2 to 1; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float TowSpeedFactor { get; set; } = 0.7f;

    [PropertyDescription("The longest cable of a tow hook, in metres: the tow releases when the two vehicles are further apart, and a hook only attaches to a vehicle within half of it. Allowed 12 to 100; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float TowMaxDistance { get; set; } = 25f;

    [PropertyDescription("Metres of terrain around the Cyclops that the map of the navigator console shows when the Cyclops has a Cyclops Terrain Sonar module (the scanner room of the game shows up to 500 m); the round hologram at the helm shows the same but no more than the 130 m that its display holds. A big radius costs more to draw. Allowed 50 to 500; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float TerrainSonarRange { get; set; } = 200f;

    [PropertyDescription("How far from the hull, in metres, the clearance warning of the navigator console looks for walls at the sides, above and below (ahead it looks farther the faster the Cyclops goes): a passage that is tighter than this is reported at the helm and at the console. Allowed 5 to 60; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float ClearanceWarningDistance { get; set; } = 20f;

    [PropertyDescription("Energy, in units of the Cyclops' power, that a full charge of the Cyclops gunner station takes when it fires a stasis sphere (a partial charge takes the same fraction; the Stasis Rifle itself uses 5 of a battery of 100). Allowed 0 to 200; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float GunnerShotEnergy { get; set; } = 10f;

    [PropertyDescription("Seconds after a shot of the Cyclops gunner station before the next one can be charged. Allowed 0 to 60; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float GunnerCooldownSeconds { get; set; } = 3f;

    [PropertyDescription("Metres that a stasis sphere of the Cyclops gunner station flies before it is spent (it slows down on the way; the Stasis Rifle reaches 37.5 m). Allowed 20 to 120; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float GunnerRange { get; set; } = 60f;

    [PropertyDescription("Extra speed of a Cyclops while its engineer console routes the power to the engines, as a fraction of the normal speed (0.15 is 15 percent faster; the overdrive adds to it). Allowed 0 to 0.5; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float EngineerSpeedBonus { get; set; } = 0.15f;

    [PropertyDescription("Seconds that the overdrive of the Cyclops engineer console lasts (a speed boost past the flank speed that heats the engine). Allowed 3 to 60; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float OverdriveSeconds { get; set; } = 12f;

    [PropertyDescription("Seconds after an overdrive of the Cyclops engineer console before the next one can be started. Allowed 10 to 300; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float OverdriveCooldownSeconds { get; set; } = 45f;

    [PropertyDescription("Share of the shield's power use that the Cyclops engineer console saves while its power is routed to the shields (0.4 means the shield uses 40 percent less). Allowed 0 to 0.8; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float ShieldSaving { get; set; } = 0.4f;

    [PropertyDescription("Seconds after the engine heat of a Cyclops was vented at its engineer console before it can be vented again. Allowed 5 to 180; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float VentCooldownSeconds { get; set; } = 30f;

    [PropertyDescription("How much quieter silent running gets while the power of a Cyclops is routed to its systems at the engineer console, as a share of the noise that is left (0.5 halves it again, so creatures notice the Cyclops from half the distance; the silent running then also costs more power). Allowed 0 to 0.9; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float SilentRunningBonus { get; set; } = 0.5f;

    [PropertyDescription("How fast the scout drone of a Cyclops flies, against the Seamoth (2 is twice the top speed of a Seamoth, about 22 m/s). Allowed 1 to 4; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float DroneSpeedFactor { get; set; } = 2f;

    [PropertyDescription("Metres from its Cyclops at which the signal of the scout drone is lost: beyond it the picture is black and the controls only let the drone come back. Allowed 200 to 3000; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float DroneRange { get; set; } = 1000f;

    [PropertyDescription("Share of the scout drone's range at which the static begins to cover the picture (0.5 is from half the range). Allowed 0.1 to 0.95; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float DroneStaticStart { get; set; } = 0.5f;

    [PropertyDescription("Health of a scout drone (the game's camera drone has 400, a Seamoth 200); creatures that bite it take it down. Allowed 20 to 400; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float DroneHealth { get; set; } = 100f;

    [PropertyDescription("Energy that one zap of the scout drone takes from its battery (the battery holds 100). Allowed 0 to 100; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float ZapEnergy { get; set; } = 15f;

    [PropertyDescription("Seconds after a zap of the scout drone before it can zap again. Allowed 2 to 120; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float ZapCooldownSeconds { get; set; } = 12f;

    [PropertyDescription("Metres around the scout drone that its zap reaches (it knocks Lava Larvae off the Cyclops hull and scares small creatures). Allowed 5 to 60; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float ZapRadius { get; set; } = 20f;

    // feature group 3: extra module slots

    [PropertyDescription("How many module slots every vehicle gets on top of the game's own: the Seamoth 4 + 2, the Prawn Suit 4 + 2 (and its two arms) and the Cyclops 6 + 2 upgrade slots. Allowed 0 to 2 (0 is the game as it is); a number outside is clamped to the nearest limit. A module in a slot that a lower number takes away stays in the save and comes back with the slot. Off with VehicleUpgrades=false.")]
    public int ExtraModuleSlots { get; set; } = 2;

    // feature group 4: the buddy oxygen

    [PropertyDescription("Seconds of air per second that flow from a diver to a teammate while the diver holds the share key and looks at the teammate (a diver without a tank holds 45 seconds of air). Allowed 0 to 20 (0 switches the buddy oxygen off); a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float BuddyOxygenRate { get; set; } = 5f;

    [PropertyDescription("Metres between two divers within which air can flow from one to the other (the server allows two metres more for the positions that are a moment old). Allowed 1 to 6; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float BuddyOxygenRange { get; set; } = 2.5f;

    [PropertyDescription("Seconds of air that the diver who gives keeps for themselves: the sharing stops when they are down to it. Allowed 0 to 60; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float BuddyOxygenFloor { get; set; } = 15f;

    // feature group 4: the quick ping

    [PropertyDescription("Seconds that a player's quick ping (the middle mouse button, or the key set in the options) stays on everybody's HUD; a new ping of the same player replaces the old one. Allowed 5 to 120; a number outside is clamped to the nearest limit and a value that is not a number uses the default.")]
    public float QuickPingSeconds { get; set; } = 20f;

    public bool AutoSave { get; set; } = true;

    [PropertyDescription("When true, will reject any build actions detected as desynced")]
    public bool SafeBuilding { get; set; } = true;

    [PropertyDescription("Command to run following a successful world save (e.g. .exe, .bat, or PowerShell script). ")]
    public string PostSaveCommandPath { get; set; } = "";

    [PropertyDescription("Seconds a connected client may stay silent before the server drops it (and frees its player name). Raise it when stepping through server code in a debugger.")]
    [Range(1, 3600)]
    public int DisconnectTimeoutSeconds { get; set; } = 30;

    [OptionsValidator]
    public partial class Validator : IValidateOptions<SubnauticaServerOptions>;
}
