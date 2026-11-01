namespace NitroxClient.GameLogic;

/// <summary>
/// When the refusal "Another player is interacting with that object" is put on the player's hand (<see cref="MonoBehaviours.Gui.HUD.DenyOwnershipHand" />). The game's hand points at the nearest object
/// with a hand target above the collider that the eye ray hits (<c>GUIHand.activeTarget</c>); an object can have hand targets of its own below it (the Rider Bar on a Seamoth), and those keep their text.
/// </summary>
public static class DenyOwnershipRules
{
    /// <summary>Seconds the refusal stays on the hand after the server said no.</summary>
    public const float SHOW_SECONDS = 2f;

    /// <summary>
    /// Whether the refusal text goes on the hand this frame. <paramref name="scoped" />: only while the hand points at the very object that was refused (<paramref name="activeTarget" /> is the object the
    /// game's hand points at, <paramref name="own" /> the refused one); else wherever the player looks (a tool that grabs something has no hand target to compare with).
    /// </summary>
    public static bool ShowsText<T>(bool scoped, T activeTarget, T own) where T : class
    {
        return !scoped || (activeTarget != null && ReferenceEquals(activeTarget, own));
    }

    /// <summary>Whether the refusal has run its time.</summary>
    public static bool IsOver(float now, float until) => now >= until;
}
