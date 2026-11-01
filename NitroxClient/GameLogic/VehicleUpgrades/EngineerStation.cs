namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The Cyclops Engineer Console (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 3): is a Cyclops fitted with it, and may it do anything. The behaviour is in <c>EngineerConsole</c> (the console in the engine
/// room), added to every real Cyclops by <c>SubRoot_Start_Engineer_Patch</c>; this facade is what it and the rest of the station ask.
/// </summary>
public static class EngineerStation
{
    public static TechType Tech => (TechType)CoopItems.CYCLOPS_ENGINEER_CONSOLE;

    /// <summary>Does the Cyclops have the module in its upgrade console? One counts.</summary>
    public static bool HasModule(SubRoot cyclops)
    {
        return cyclops && cyclops.isCyclops && cyclops.upgradeConsole && cyclops.upgradeConsole.modules != null && cyclops.upgradeConsole.modules.GetCount(Tech) > 0;
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
