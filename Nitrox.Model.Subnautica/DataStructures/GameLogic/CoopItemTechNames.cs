namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The names under which the server sees the items of the vehicle upgrades. A new item travels as its TechType number (<c>NitroxTechType("20012")</c>, the server never parses names), so the server can tell
/// a module apart by comparing the text. The client's table (<c>CoopItems</c>) holds the numbers; a test pins that the two agree.
/// </summary>
public static class CoopItemTechNames
{
    /// <summary>The Seamoth Rider Bar (<c>CoopItems.SEAMOTH_RIDER_BAR</c>).</summary>
    public const string SEAMOTH_RIDER_BAR = "20012";
}
