using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// What a received <c>WeldableWallPanelGenericMetadata</c> (the health of a damaged wall panel that somebody repaired) does to the panel on this client. Plain data, no game types.
/// <para>
/// Vanilla repairs a panel with <c>LiveMixin.AddHealth</c>, which also takes the sparks away (the looping damage effect is destroyed when the health is back above the threshold) and
/// sends <c>OnRepair</c> at full health. A panel that is made in this frame gets its health before <c>LiveMixin.Start</c> and <c>WeldableWallPanelGeneric.Start</c> run, which read it (no
/// sparks, door unlocked): that is the restore of a joiner or a restart. A panel that is in the world already has sparks and an animation that only <c>AddHealth</c> clears, so the
/// health must go up through it, not by a field write.
/// </para>
/// </summary>
public static class WeldablePanelRules
{
    /// <param name="Health">The health to write straight into the field (a panel made in this frame), or the unchanged current one.</param>
    /// <param name="HealBy">The amount to pass to <c>LiveMixin.AddHealth</c> for a panel that is in the world (0 for none).</param>
    public readonly record struct Result(float Health, float HealBy);

    public static Result Apply(bool freshlySpawned, float currentHealth, float incomingHealth, float maxHealth)
    {
        float incoming = float.IsNaN(incomingHealth) || incomingHealth < 0f ? 0f : incomingHealth;
        // Math.Clamp does not exist on net472 (the game's runtime)
        incoming = maxHealth > 0f ? Math.Min(incoming, maxHealth) : incoming;

        if (freshlySpawned)
        {
            return new Result(incoming, 0f);
        }

        // A repaired panel is never damaged again by a late or repeated report
        return incoming > currentHealth ? new Result(currentHealth, incoming - currentHealth) : new Result(currentHealth, 0f);
    }
}
