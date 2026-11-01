namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The rules of the model that a player wears in his own first-person view (docs/coop/PLAYER_MODELS.md, "Your own model"), as plain values so that they can be tested: when the copy is made, when it is drawn
/// (where the vanilla body would be: the game switches the meshes of the vanilla body on and off itself, and the copy follows what it does) and what happens to its head.
/// </summary>
public static class FirstPersonModelRules
{
    /// <summary>The head bone of the copy is scaled to this while the player looks through its eyes (not zero: a matrix with no volume is a warning in the game's log and a flicker on some cards).</summary>
    public const float HIDDEN_HEAD_SCALE = 0.0001f;

    /// <summary>A body that the game makes smaller than this (a cinematic that shrinks it) is hidden by the game: the copy is hidden too.</summary>
    public const float MIN_BODY_SCALE = 0.05f;

    /// <summary>Seconds between two looks for meshes that the game put on the body since the last look (the equipment that is put on), which must not be drawn next to the copy.</summary>
    public const float SCAN_INTERVAL_SECONDS = 0.5f;

    /// <summary>The copy is worn: the setting is on, a model is chosen and the player is in a session.</summary>
    public static bool Wanted(bool settingOn, string savedId, bool inSession)
    {
        return settingOn && !string.IsNullOrEmpty(savedId) && inSession;
    }

    /// <summary>
    /// Whether the copy is drawn this frame: always while the emote camera is out, else where the vanilla body is drawn (its object is active, the game did not shrink it and at least one of its meshes is switched on).
    /// </summary>
    public static bool Shown(bool emoteView, bool bodyActive, float bodyScale, bool anyVanillaMeshOn)
    {
        if (emoteView)
        {
            return true;
        }
        return bodyActive && bodyScale >= MIN_BODY_SCALE && anyVanillaMeshOn;
    }

    /// <summary>The scale that the head bone of the copy has: the head is hidden when the player looks through its eyes, and is the model's own when the emote camera looks at it.</summary>
    public static float HeadScale(bool headHidden)
    {
        return headHidden ? HIDDEN_HEAD_SCALE : 1f;
    }
}
