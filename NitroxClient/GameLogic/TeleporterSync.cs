using System.Collections.Generic;
using Nitrox.Model.Subnautica.Helper;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxClient.GameLogic;

/// <summary>
/// Shares which Precursor teleporter pairs are active (see <see cref="TeleporterGoalKey" />): a pair that one player activates is active for everyone, also for a player who
/// loads the other side later, joins later or restarts.
/// </summary>
public static class TeleporterSync
{
    /// <summary>
    /// True while a pair that another player activated is switched on here (so that it is not reported back)
    /// </summary>
    public static bool Applying { get; private set; }

    /// <summary>
    /// A teleporter pair was activated on this client
    /// </summary>
    public static void ReportActivation(string identifier)
    {
        if (Applying || string.IsNullOrEmpty(identifier) || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted || !StoryGoalManager.main)
        {
            return;
        }

        // The goal is sent to the server only the first time (StoryGoalManager_OnGoalComplete_Patch)
        StoryGoalManager.main.OnGoalComplete(TeleporterGoalKey.Format(identifier));
    }

    /// <summary>
    /// A story goal of another player arrived: switches the pair on if it is one
    /// </summary>
    public static void ApplyRemoteGoal(string goalKey)
    {
        if (!TeleporterGoalKey.TryParse(goalKey, out string identifier) || !TeleporterManager.main || TeleporterManager.GetTeleporterActive(identifier))
        {
            return;
        }

        Applying = true;
        try
        {
            // Adds it to the active set and tells the loaded teleporters of the pair to open
            TeleporterManager.ActivateTeleporter(identifier);
        }
        finally
        {
            Applying = false;
        }
    }

    /// <summary>
    /// Joining: the pairs of the completed goals are active before any teleporter spawns and reads its state
    /// </summary>
    public static void RestoreFromGoals(IEnumerable<string> completedGoals)
    {
        if (!TeleporterManager.main)
        {
            return;
        }

        foreach (string goal in completedGoals)
        {
            if (TeleporterGoalKey.TryParse(goal, out string identifier))
            {
                TeleporterManager.main.activeTeleporters.Add(identifier);
            }
        }
    }
}
