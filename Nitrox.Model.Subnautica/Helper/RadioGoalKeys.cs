using System;

namespace Nitrox.Model.Subnautica.Helper;

/// <summary>
/// The goal that vanilla completes when a radio message is played: <c>StoryGoalManager.ExecutePendingRadioMessage</c> takes the first pending message, adds it to the PDA log and
/// completes "OnPlay" plus the key of the message, but only if the PDA log did not have the message yet. Everything that hangs on a played message hangs on this goal: the signals
/// of the lifepods (<c>OnGoalUnlockTracker</c>), the "no signal" PDA lines, the Sunbeam countdown (<c>OnPlayRadioSunbeam4</c>).
/// </summary>
public static class RadioGoalKeys
{
    public const string PLAYED_PREFIX = "OnPlay";

    /// <returns>The goal that is completed when the radio message with this key is played</returns>
    public static string PlayedGoalOf(string radioKey)
    {
        return $"{PLAYED_PREFIX}{radioKey}";
    }

    /// <returns>True for a key that looks like the goal of a played radio message ("OnPlay" and something after it)</returns>
    public static bool IsPlayedGoal(string goalKey)
    {
        return goalKey != null && goalKey.Length > PLAYED_PREFIX.Length && goalKey.StartsWith(PLAYED_PREFIX, StringComparison.Ordinal);
    }

    /// <returns>The key of the radio message that a played-goal belongs to, or null if the key is not a played-goal</returns>
    public static string RadioKeyOf(string playedGoal)
    {
        return IsPlayedGoal(playedGoal) ? playedGoal.Substring(PLAYED_PREFIX.Length) : null;
    }
}
