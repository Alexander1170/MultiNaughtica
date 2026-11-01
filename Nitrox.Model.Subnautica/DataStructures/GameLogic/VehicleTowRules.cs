namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>Which vehicles can tow which (shared by the server and the client, plain strings so that it can be tested).</summary>
public static class VehicleTowRules
{
    /// <summary>A Seamoth or a Cyclops tows a Seamoth or a Prawn Suit. A Prawn Suit tows nothing, and nothing tows a Cyclops.</summary>
    public static bool RolesFit(string towerTech, string towedTech)
    {
        return (towerTech == PowerTransferRules.SEAMOTH_TECH || towerTech == PowerTransferRules.CYCLOPS_TECH) && PowerTransferRules.IsVehicle(towedTech);
    }
}
