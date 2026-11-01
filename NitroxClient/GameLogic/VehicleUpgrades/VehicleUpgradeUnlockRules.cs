using System.Collections.Generic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// Which blueprints of the new items are known, decided from the server's two switches (docs/coop/VEHICLE_UPGRADES.md). Free of Unity types so that it can be tested.
/// <list type="bullet">
/// <item>The master switch off (<see cref="VehicleUpgradeSettings.Enabled" /> false): nothing unlocks and no fabricator shows a node.</item>
/// <item><see cref="VehicleUpgradeSettings.UnlockedFromStart" />: every blueprint is known as soon as the player has joined.</item>
/// <item>Otherwise each blueprint unlocks together with the one it builds on (<see cref="CoopItemDefinition.Prerequisite" />): the game's own <c>KnownTech.compoundTech</c> rule, which unlocks the
/// item on every client that learns the prerequisite, or at once when the prerequisite is already known.</item>
/// </list>
/// </summary>
public static class VehicleUpgradeUnlockRules
{
    public readonly struct Plan
    {
        public Plan(IReadOnlyList<CoopItemDefinition> showNodes, IReadOnlyList<CoopItemDefinition> unlockNow, IReadOnlyList<CoopItemDefinition> unlockWithPrerequisite)
        {
            ShowNodes = showNodes;
            UnlockNow = unlockNow;
            UnlockWithPrerequisite = unlockWithPrerequisite;
        }

        /// <summary>The items whose craft nodes are in the fabricators' trees.</summary>
        public IReadOnlyList<CoopItemDefinition> ShowNodes { get; }

        /// <summary>The items whose blueprint is added to the player's known technologies when the settings arrive.</summary>
        public IReadOnlyList<CoopItemDefinition> UnlockNow { get; }

        /// <summary>The items that get a rule "known as soon as the prerequisite is known".</summary>
        public IReadOnlyList<CoopItemDefinition> UnlockWithPrerequisite { get; }
    }

    public static Plan CreatePlan(bool enabled, bool unlockedFromStart, IReadOnlyList<CoopItemDefinition> items)
    {
        if (!enabled)
        {
            return new Plan([], [], []);
        }
        if (unlockedFromStart)
        {
            return new Plan(items, items, []);
        }
        return new Plan(items, [], items);
    }
}
