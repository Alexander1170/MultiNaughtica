using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.PlayerLogic;

/// <summary>
///     What one step of the two-player intro (<c>uGUI_SceneIntro.IntroSequence</c>, patched by <c>uGUI_SceneIntro_IntroSequence_Patch</c>) does next.
/// </summary>
public enum IntroReadyAction
{
    /// <summary>The cinematic was started already: the sequence goes on.</summary>
    Proceed,

    /// <summary>Nothing to do in this frame.</summary>
    Wait,

    /// <summary>Show "waiting for a partner" and report <see cref="IntroCinematicMode.WAITING" /> to the server.</summary>
    ReportWaiting,

    /// <summary>The server says the intro is over for this player (a player the server knows): skip it and leave the lifepod as the server described it.</summary>
    SkipAsCompleted,

    /// <summary>A new player with nobody to pair with (his partner is mid-intro or past it), or a run that skips the intro: skip it and put the lifepod in the state of the world after the intro.</summary>
    SkipAsNewPlayer,

    /// <summary>The server paired this player with a partner who is known here: both play the cinematic.</summary>
    StartWithPartner,

    /// <summary>The server paired this player but the partner is not known here (he left, or his player never arrived): play the cinematic without him.</summary>
    StartAlone
}

/// <summary>
///     What the watchdog of the intro does when it looks at the sequence.
/// </summary>
public enum IntroWatchdogVerdict
{
    /// <summary>Nothing is wrong (or the intro is not running).</summary>
    Keep,

    /// <summary>The cinematic never started although the sequence says it did (the sequence died or waits for something that never comes).</summary>
    FinishCinematicNeverStarted,

    /// <summary>The cinematic is over for a while and the sequence did not end the intro.</summary>
    FinishCinematicEndedUnnoticed,

    /// <summary>The cinematic runs far longer than it ever does.</summary>
    FinishTooLong
}

/// <summary>
///     The decisions of the client side of the intro: what a player does when he is ready for it, and when the intro is forced to end because the sequence that should end it did not.
///     The second player of a new world sat at a black screen (the black overlay of <c>uGUI_SceneIntro</c>, <c>Escape</c> did nothing) while the first one finished his intro; nothing
///     ended the intro for her because the sequence is a coroutine and the duo mode switched its skip off. Contains no Unity types, so it can be tested without the game.
/// </summary>
public static class IntroSequenceRules
{
    /// <summary>
    ///     How long a player who the server paired waits for his partner to be known on this client before he plays alone. The partner is known from the initial sync on, so this only
    ///     matters when he left in the meantime.
    /// </summary>
    public const float PARTNER_LOOKUP_SECONDS = 5f;

    /// <summary>
    ///     How long the other players must have been past the intro, without one of them waiting, for a player who waits for a partner to stop waiting. The server sends the "start" of
    ///     the partner a moment before the one of this player, so a shorter time would skip a player who is just being paired.
    /// </summary>
    public const float NOBODY_TO_PAIR_WITH_SECONDS = 3f;

    /// <summary>
    ///     The cinematic starts at once after the text of the sequence faded out (0.2 s). A cinematic that has not started after this long never will.
    /// </summary>
    public const float CINEMATIC_START_TIMEOUT_SECONDS = 12f;

    /// <summary>
    ///     The sequence ends the intro in the frame after the cinematic ended (a slow computer may still show a last fade in the meantime). This long after, it did not.
    /// </summary>
    public const float CINEMATIC_ENDED_GRACE_SECONDS = 12f;

    /// <summary>
    ///     The cinematic takes about a minute (55 s from "playing" to "completed" on the first live run, 2026-10-23). A computer that is several times slower would still be done after this.
    /// </summary>
    public const float MAX_CINEMATIC_SECONDS = 150f;

    /// <summary>
    ///     The value of <c>uGUI_SceneIntro.skipHintStartTime</c> while the skip of the intro is off: both the hold test of <c>uGUI_SceneIntro.OnUpdate</c> and the one of its <c>HandleInput</c> only
    ///     run when the value is not negative. The game sets it back to this value when the intro stops (<c>Stop</c>) and arms it (<c>Time.time</c>) late in the sequence.
    /// </summary>
    public const float SKIP_OFF = -1f;

    /// <returns>True if the skip of the intro is armed: a hold of the skip button can end the intro</returns>
    public static bool IsSkipArmed(float skipHintStartTime) => skipHintStartTime >= 0f;

    /// <summary>
    ///     Whether the player ends the intro now: the skip is armed and the button was held for the time the game asks for (<c>uGUI_SceneIntro.skipTimeout</c>).
    /// </summary>
    public static bool IsSkipRequested(float skipHintStartTime, float heldSeconds, float skipTimeoutSeconds) => IsSkipArmed(skipHintStartTime) && heldSeconds >= skipTimeoutSeconds;

    /// <summary>
    ///     What <c>uGUI_SceneIntro.skipHintStartTime</c> is set to when this client starts the cinematic: armed at once, never <see cref="SKIP_OFF" />. The game arms the skip only after it started the
    ///     cinematic of the lifepod, a few steps of its sequence later (after the fade of the text), and a sequence that stalls or dies before that point left the player at the black screen with the hold
    ///     of the skip button ignored (the second player of a new world, 2026-10-23). The value is overwritten by the game when the sequence gets that far.
    /// </summary>
    /// <param name="now"><c>Time.time</c></param>
    public static float SkipHintStartAtCinematicStart(float now) => now < 0f ? 0f : now;

    /// <summary>
    ///     What the sequence does with a player who is ready (it asks once per frame, until the answer is <see cref="IntroReadyAction.Proceed" /> or the intro is skipped).
    /// </summary>
    /// <param name="cinematicStarted">The cinematic was started on this client (the partner was found, or the player chose to play alone)</param>
    /// <param name="localMode">The intro mode of this player, as the server and this client have it</param>
    /// <param name="skipsForDebugOrCreative">A Debug build without <c>--noskipintro</c>, or a creative game: the intro is never played</param>
    /// <param name="waitingReported">This client told the server that it is ready (<see cref="IntroCinematicMode.WAITING" />)</param>
    /// <param name="partnerKnown">The server named a partner and this client has his player</param>
    /// <param name="secondsInStart">How long the mode has been <see cref="IntroCinematicMode.START" /> (0 if it is not)</param>
    /// <param name="nobodyToPairWith">The other players are past the intro and none of them waits (<see cref="IntroCinematicRules.HasNobodyToPairWith" />)</param>
    /// <param name="secondsNobodyToPairWith">For how long it has been that way without a break, while this player waits</param>
    public static IntroReadyAction DecideReady(bool cinematicStarted, IntroCinematicMode localMode, bool skipsForDebugOrCreative, bool waitingReported, bool partnerKnown, float secondsInStart,
                                               bool nobodyToPairWith, float secondsNobodyToPairWith)
    {
        if (cinematicStarted)
        {
            return IntroReadyAction.Proceed;
        }

        if (localMode == IntroCinematicMode.COMPLETED)
        {
            return IntroReadyAction.SkipAsCompleted;
        }

        if (skipsForDebugOrCreative)
        {
            return IntroReadyAction.SkipAsNewPlayer;
        }

        if (!waitingReported)
        {
            // Nobody can be a partner to a player who is ready after the others started: waiting would never end
            return nobodyToPairWith ? IntroReadyAction.SkipAsNewPlayer : IntroReadyAction.ReportWaiting;
        }

        if (localMode == IntroCinematicMode.START)
        {
            if (partnerKnown)
            {
                return IntroReadyAction.StartWithPartner;
            }

            return secondsInStart >= PARTNER_LOOKUP_SECONDS ? IntroReadyAction.StartAlone : IntroReadyAction.Wait;
        }

        if (localMode == IntroCinematicMode.WAITING && nobodyToPairWith && secondsNobodyToPairWith >= NOBODY_TO_PAIR_WITH_SECONDS)
        {
            // The partner started without this player (he chose to play alone) or is past the intro: waiting can only end by this player holding the skip key
            return IntroReadyAction.SkipAsNewPlayer;
        }

        return IntroReadyAction.Wait;
    }

    /// <summary>
    ///     Whether the watchdog has to end an intro that is still showing. It only looks at the cinematic, never at what the sequence waits for: the sequence is a coroutine, it can stop
    ///     (an exception in the code of the partner), wait for a fade that never completes or for a callback that never comes, and all of that looks the same from here.
    /// </summary>
    /// <param name="introShowing">The intro screen is up (<c>uGUI_SceneIntro.showing</c>)</param>
    /// <param name="cinematicStarted">The cinematic was started on this client</param>
    /// <param name="secondsSinceStart">How long ago (real time)</param>
    /// <param name="cinematicWasActive">The cinematic controller was active at some time since the start</param>
    /// <param name="cinematicActive">It is active now</param>
    /// <param name="secondsSinceCinematicEnded">Since when it is not active any more, after it was (0 while it is active or never was)</param>
    public static IntroWatchdogVerdict EvaluateWatchdog(bool introShowing, bool cinematicStarted, float secondsSinceStart, bool cinematicWasActive, bool cinematicActive, float secondsSinceCinematicEnded)
    {
        if (!introShowing || !cinematicStarted)
        {
            return IntroWatchdogVerdict.Keep;
        }

        if (secondsSinceStart >= MAX_CINEMATIC_SECONDS)
        {
            return IntroWatchdogVerdict.FinishTooLong;
        }

        if (!cinematicWasActive)
        {
            return secondsSinceStart >= CINEMATIC_START_TIMEOUT_SECONDS ? IntroWatchdogVerdict.FinishCinematicNeverStarted : IntroWatchdogVerdict.Keep;
        }

        if (!cinematicActive && secondsSinceCinematicEnded >= CINEMATIC_ENDED_GRACE_SECONDS)
        {
            return IntroWatchdogVerdict.FinishCinematicEndedUnnoticed;
        }

        return IntroWatchdogVerdict.Keep;
    }
}
