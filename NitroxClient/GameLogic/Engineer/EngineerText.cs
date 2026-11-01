using System;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>The words of the engineer console: the names of the routes and what each does with the server's numbers, and the heat. Plain ASCII (the game computes the glyphs of its font before the mod's strings are added).</summary>
public static class EngineerText
{
    public static string RouteName(EngineerRoute route)
    {
        return route switch
        {
            EngineerRoute.Engines => "Engines",
            EngineerRoute.Shields => "Shields",
            EngineerRoute.Systems => "Systems",
            _ => "Balanced"
        };
    }

    /// <summary>What a route does, with the numbers of the server's settings.</summary>
    public static string RouteEffect(EngineerRoute route)
    {
        return route switch
        {
            EngineerRoute.Engines => $"{Percent(VehicleUpgradeSettings.EngineerSpeedBonus)} percent faster, and the overdrive can be used",
            EngineerRoute.Shields => $"The shield uses {Percent(VehicleUpgradeSettings.ShieldSaving)} percent less power",
            EngineerRoute.Systems => $"Sonar sees {Percent(CyclopsEngineerRules.SONAR_FACTOR - 1f)} percent farther; silent running is {Percent(VehicleUpgradeSettings.SilentRunningBonus)} percent quieter and uses {Percent(CyclopsEngineerRules.SILENT_RUNNING_COST_FACTOR - 1f)} percent more power",
            _ => "The game's own numbers"
        };
    }

    public static string HeatBand(int heat)
    {
        return CyclopsEngineerRules.BandOf(heat) switch
        {
            EngineerHeatBand.Hot => "HOT: fires start often",
            EngineerHeatBand.Warm => "warm: fires can start",
            _ => "cool"
        };
    }

    public static string Seconds(float seconds)
    {
        return seconds >= 10f ? $"{Math.Ceiling(seconds):F0} s" : $"{seconds:F1} s";
    }

    private static string Percent(float fraction)
    {
        return Math.Round(fraction * 100f).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
