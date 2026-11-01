using System;
using System.Collections.Generic;

namespace Nitrox.Model.Subnautica.Helper;

/// <summary>
/// The infection of a player that the story goals imply. Vanilla applies it only at the moment a goal completes
/// (<c>StoryGoalCustomEventHandler.NotifyGoalComplete</c>: Infection_Progress1-4 raise the amount to 0.25, 0.5, 0.75 and 1 of a player that is infected,
/// Infection_Progress5 cures), so a player who is offline then, or who joins later, keeps an infection that does not fit the story: after the cure he can never
/// be cured, because the cure ball is switched off by the shared goal. This is what a joiner is caught up to.
/// </summary>
public static class InfectionStage
{
    public const string CURE_GOAL = "Infection_Progress5";
    public const string SELF_SCAN_REVEAL_GOAL = "SelfScan4";

    private static readonly (string goal, float amount)[] progressSteps =
    [
        ("Infection_Progress1", 0.25f),
        ("Infection_Progress2", 0.5f),
        ("Infection_Progress3", 0.75f),
        ("Infection_Progress4", 1f)
    ];

    /// <param name="savedAmount">The infection that the player had when he left (or the default of a new player).</param>
    /// <param name="isCompleted">Whether a story goal is completed (case does not matter to the game's goal manager either).</param>
    public static float CatchUp(float savedAmount, Func<string, bool> isCompleted)
    {
        // The cure removes the infection of everyone, whatever the stage was
        if (isCompleted(CURE_GOAL))
        {
            return 0f;
        }

        // A player who is not infected (cured earlier by other means) is not made sick again: IncreaseInfectedAmount only acts above 0
        if (savedAmount <= 0f)
        {
            return 0f;
        }

        float amount = savedAmount;
        foreach ((string goal, float stage) in progressSteps)
        {
            if (isCompleted(goal))
            {
                amount = Math.Max(amount, stage);
            }
        }

        // The self scan reveals the infection: TriggerSickAnim sets the amount to 1
        if (isCompleted(SELF_SCAN_REVEAL_GOAL))
        {
            amount = 1f;
        }

        return amount;
    }

    /// <summary>
    /// Whether the reveal of the infection (SelfScan4: sets the amount to 1 and plays the animation) applies to a player: only to one who is infected. In single player the
    /// scan that completes it needs an infected player, with several players the goal reaches players who were cured meanwhile.
    /// </summary>
    public static bool CanReveal(float currentAmount) => currentAmount > 0f;

    /// <summary>
    /// Whether the reveal of the infection (SelfScan4, 14 s later) has happened for the team: the joiner's own hands show the infection only when the player counts as revealed
    /// (<c>Player.GetInfectionAmount</c> is 0 until then), and nothing would ever reveal it to him, the goal being complete already
    /// </summary>
    public static bool IsRevealed(Func<string, bool> isCompleted) => isCompleted(SELF_SCAN_REVEAL_GOAL) && !isCompleted(CURE_GOAL);

    public static float CatchUp(float savedAmount, IReadOnlyCollection<string> completedGoals)
    {
        HashSet<string> goals = new(completedGoals, StringComparer.OrdinalIgnoreCase);
        return CatchUp(savedAmount, goals.Contains);
    }
}
