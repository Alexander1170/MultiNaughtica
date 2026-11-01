namespace NitroxClient.GameLogic;

/// <summary>
/// How the "Saving..." indicator behaves over time (item a). Contains no Unity types so that it can be tested without the game.
/// </summary>
/// <remarks>
/// The indicator is shown for <see cref="VISIBLE_SECONDS" /> from the moment the server says that it saves: it fades in quickly, stays, and fades out for the last <see cref="FADE_OUT_SECONDS" />.
/// A second notice while it is still showing (two saves close together, the save command typed during an autosave) starts the show over without letting it flicker: the alpha it has is kept.
/// </remarks>
public static class AutosaveIndicatorRules
{
    /// <summary>From the notice to the end of the fade out (the player sees it for 2.5 s: "2 to 3 seconds").</summary>
    public const float VISIBLE_SECONDS = 2.5f;

    public const float FADE_IN_SECONDS = 0.15f;

    public const float FADE_OUT_SECONDS = 0.6f;

    /// <summary>The value of <c>elapsed</c> of an indicator that shows nothing.</summary>
    public const float IDLE = float.PositiveInfinity;

    /// <param name="elapsed">Seconds since the notice, <see cref="IDLE" /> when none is showing.</param>
    public static float Alpha(float elapsed)
    {
        if (elapsed < 0f || elapsed >= VISIBLE_SECONDS)
        {
            return 0f;
        }

        if (elapsed < FADE_IN_SECONDS)
        {
            return elapsed / FADE_IN_SECONDS;
        }

        float fadeOutStart = VISIBLE_SECONDS - FADE_OUT_SECONDS;
        if (elapsed >= fadeOutStart)
        {
            return (VISIBLE_SECONDS - elapsed) / FADE_OUT_SECONDS;
        }

        return 1f;
    }

    public static bool IsDone(float elapsed)
    {
        return elapsed >= VISIBLE_SECONDS;
    }

    /// <summary>
    /// The <c>elapsed</c> a new notice starts at: 0 for an indicator that shows nothing, otherwise the time of the fade in that is no later than the alpha it has now would be reached at, so that the
    /// show starts over without the indicator getting dimmer first (a fade out in progress goes back up to full, a fade in goes on).
    /// </summary>
    public static float ElapsedForNewNotice(float currentElapsed)
    {
        if (IsDone(currentElapsed) || currentElapsed < 0f)
        {
            return 0f;
        }

        return currentElapsed < FADE_IN_SECONDS ? currentElapsed : FADE_IN_SECONDS;
    }
}
