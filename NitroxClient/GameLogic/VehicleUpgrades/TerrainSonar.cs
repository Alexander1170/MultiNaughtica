namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md): is a Cyclops fitted with it, and may it do anything. The behaviour is in <c>CyclopsTerrainSonar</c> (the hologram at the helm), added to every
/// Cyclops by <c>SubRoot_Start_Patch</c>; this facade is what it and the later parts of the station (the navigator console, the marks and the route) ask.
/// </summary>
public static class TerrainSonar
{
    public static TechType Tech => (TechType)CoopItems.CYCLOPS_TERRAIN_SONAR;

    /// <summary>Does the Cyclops have the module in its upgrade console? One counts.</summary>
    public static bool HasModule(SubRoot cyclops)
    {
        return cyclops && cyclops.isCyclops && cyclops.upgradeConsole && cyclops.upgradeConsole.modules != null && cyclops.upgradeConsole.modules.GetCount(Tech) > 0;
    }

    /// <summary>Does the Cyclops have the game's own Cyclops Sonar Module? Read from the upgrade console, not from <c>SubRoot.sonarUpgrade</c>, which is stale while the Cyclops is far away.</summary>
    public static bool HasVanillaSonar(SubRoot cyclops)
    {
        return cyclops && cyclops.isCyclops && cyclops.upgradeConsole && cyclops.upgradeConsole.modules != null && cyclops.upgradeConsole.modules.GetCount(TechType.CyclopsSonarModule) > 0;
    }

    /// <summary>
    /// The station works on this Cyclops: the vehicle upgrades are on (the server's master switch, known since the join) and the Cyclops has the module. A world that holds the module keeps it when the
    /// switch is off; it just does nothing.
    /// </summary>
    public static bool IsAvailable(SubRoot cyclops)
    {
        return VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && HasModule(cyclops);
    }
}
