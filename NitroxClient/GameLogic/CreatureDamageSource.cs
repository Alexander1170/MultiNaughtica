using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// A few attacks of creatures hurt a vehicle without telling <c>LiveMixin.TakeDamage</c> who dealt the damage (the grip of the Reaper, the Sea Dragon's hold on an exosuit and the
/// explosion of the Crashfish all pass no dealer). The patches of those attacks name the creature for as long as the attack runs, so that the hit can be told apart from the
/// environment's damage and be relayed to the player that simulates the vehicle.
/// </summary>
public static class CreatureDamageSource
{
    /// <summary>
    /// The creature whose attack is dealing damage right now, null otherwise.
    /// </summary>
    public static GameObject Current { get; private set; }

    /// <returns>What <see cref="Exit" /> needs to restore</returns>
    public static GameObject Enter(GameObject creature)
    {
        GameObject previous = Current;
        Current = creature;
        return previous;
    }

    public static void Exit(GameObject previous)
    {
        Current = previous;
    }
}
