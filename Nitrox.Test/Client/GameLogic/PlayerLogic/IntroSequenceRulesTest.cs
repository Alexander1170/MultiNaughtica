using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.PlayerLogic;

/// <summary>
///     What a client does at each step of the two-player intro, and when the watchdog ends an intro that the sequence does not end (bug1-intro-join: the second player of a new world sat at a black
///     screen through the cinematic and after it, and <c>Escape</c> did nothing).
/// </summary>
[TestClass]
public class IntroSequenceRulesTest
{
    private static IntroReadyAction Ready(IntroCinematicMode mode, bool cinematicStarted = false, bool skipsForDebugOrCreative = false, bool waitingReported = false, bool partnerKnown = false,
                                          float secondsInStart = 0f, bool nobodyToPairWith = false, float secondsNobodyToPairWith = 0f)
    {
        return IntroSequenceRules.DecideReady(cinematicStarted, mode, skipsForDebugOrCreative, waitingReported, partnerKnown, secondsInStart, nobodyToPairWith, secondsNobodyToPairWith);
    }

    // The scenarios of the live check

    [TestMethod]
    public void FirstPlayerAlone_ReportsThatHeIsReadyThenWaits()
    {
        Ready(IntroCinematicMode.LOADING).Should().Be(IntroReadyAction.ReportWaiting);
        Ready(IntroCinematicMode.WAITING, waitingReported: true).Should().Be(IntroReadyAction.Wait);
        // for as long as he wishes: he can hold the skip button to play alone
        Ready(IntroCinematicMode.WAITING, waitingReported: true, secondsInStart: 0f).Should().Be(IntroReadyAction.Wait);
    }

    [TestMethod]
    public void SecondPlayerBeforeTheIntro_IsPairedAndPlaysWithHisPartner()
    {
        // The first player waits: nobody is past the intro, so the second one reports and the server pairs them at once
        Ready(IntroCinematicMode.LOADING, nobodyToPairWith: false).Should().Be(IntroReadyAction.ReportWaiting);
        Ready(IntroCinematicMode.START, waitingReported: true, partnerKnown: true).Should().Be(IntroReadyAction.StartWithPartner);
    }

    [TestMethod]
    public void SecondPlayerDuringTheIntro_SkipsItAtOnceAndIsNeverBlack()
    {
        // The first player is playing (or alone in SINGLEPLAYER): the second one is ready, nobody can be his partner
        Ready(IntroCinematicMode.LOADING, nobodyToPairWith: true).Should().Be(IntroReadyAction.SkipAsNewPlayer);
    }

    [TestMethod]
    public void SecondPlayerAfterTheIntro_SkipsIt()
    {
        Ready(IntroCinematicMode.LOADING, nobodyToPairWith: true).Should().Be(IntroReadyAction.SkipAsNewPlayer);
    }

    [TestMethod]
    public void PlayerTheServerSentPastTheIntro_SkipsItAndKeepsTheLifepodAsTheServerDescribedIt()
    {
        Ready(IntroCinematicMode.COMPLETED).Should().Be(IntroReadyAction.SkipAsCompleted);
        Ready(IntroCinematicMode.COMPLETED, nobodyToPairWith: true).Should().Be(IntroReadyAction.SkipAsCompleted);
    }

    // Waiting players

    [TestMethod]
    public void WaitingPlayerWhosePartnerStartsAlone_StopsWaitingAfterAGrace()
    {
        Ready(IntroCinematicMode.WAITING, waitingReported: true, nobodyToPairWith: true, secondsNobodyToPairWith: 0.5f).Should().Be(IntroReadyAction.Wait);
        Ready(IntroCinematicMode.WAITING, waitingReported: true, nobodyToPairWith: true, secondsNobodyToPairWith: IntroSequenceRules.NOBODY_TO_PAIR_WITH_SECONDS - 0.1f).Should().Be(IntroReadyAction.Wait);
        Ready(IntroCinematicMode.WAITING, waitingReported: true, nobodyToPairWith: true, secondsNobodyToPairWith: IntroSequenceRules.NOBODY_TO_PAIR_WITH_SECONDS).Should().Be(IntroReadyAction.SkipAsNewPlayer);
    }

    [TestMethod]
    public void PlayerBeingPaired_IsNotSkippedBecauseThePartnerStartedAMomentBeforeHim()
    {
        // The server sends the "start" of the partner first: for a moment the others are past the intro and this player is still WAITING. Then his own start arrives and he is START
        Ready(IntroCinematicMode.WAITING, waitingReported: true, nobodyToPairWith: true, secondsNobodyToPairWith: 0.05f).Should().Be(IntroReadyAction.Wait);
        Ready(IntroCinematicMode.START, waitingReported: true, partnerKnown: true, nobodyToPairWith: true, secondsNobodyToPairWith: 0.05f).Should().Be(IntroReadyAction.StartWithPartner);
    }

    [TestMethod]
    public void PlayerPairedWithAPartnerWhoIsNotKnown_PlaysAloneAfterAWhile()
    {
        Ready(IntroCinematicMode.START, waitingReported: true, partnerKnown: false, secondsInStart: 1f).Should().Be(IntroReadyAction.Wait);
        Ready(IntroCinematicMode.START, waitingReported: true, partnerKnown: false, secondsInStart: IntroSequenceRules.PARTNER_LOOKUP_SECONDS).Should().Be(IntroReadyAction.StartAlone);
    }

    // The rest of the sequence

    [TestMethod]
    public void StartedCinematic_LetsTheSequenceGoOn()
    {
        foreach (IntroCinematicMode mode in new[] { IntroCinematicMode.START, IntroCinematicMode.PLAYING, IntroCinematicMode.SINGLEPLAYER, IntroCinematicMode.WAITING })
        {
            Ready(mode, cinematicStarted: true, waitingReported: true).Should().Be(IntroReadyAction.Proceed, $"mode {mode}");
        }
    }

    [TestMethod]
    public void DebugAndCreativeRuns_SkipTheIntro()
    {
        Ready(IntroCinematicMode.LOADING, skipsForDebugOrCreative: true).Should().Be(IntroReadyAction.SkipAsNewPlayer);
        Ready(IntroCinematicMode.COMPLETED, skipsForDebugOrCreative: true).Should().Be(IntroReadyAction.SkipAsCompleted);
    }

    // The skip (the hold of the skip button)

    [TestMethod]
    public void SkipThatIsOff_NeverEndsTheIntroWhateverTheHold()
    {
        IntroSequenceRules.IsSkipArmed(IntroSequenceRules.SKIP_OFF).Should().BeFalse();
        IntroSequenceRules.IsSkipRequested(IntroSequenceRules.SKIP_OFF, 0f, 1.5f).Should().BeFalse();
        IntroSequenceRules.IsSkipRequested(IntroSequenceRules.SKIP_OFF, 60f, 1.5f).Should().BeFalse();
    }

    [TestMethod]
    public void ArmedSkip_NeedsTheFullHoldOfTheGame()
    {
        IntroSequenceRules.IsSkipRequested(0f, 0f, 1.5f).Should().BeFalse();
        IntroSequenceRules.IsSkipRequested(30f, 1.49f, 1.5f).Should().BeFalse();
        IntroSequenceRules.IsSkipRequested(30f, 1.5f, 1.5f).Should().BeTrue();
        IntroSequenceRules.IsSkipRequested(30f, 4f, 1.5f).Should().BeTrue();
        // The hold of a player who just skipped the waiting starts one second in the future, so it is negative first: it counts as not held
        IntroSequenceRules.IsSkipRequested(30f, -1f, 1.5f).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(0.01f)]
    [DataRow(12.5f)]
    [DataRow(3600f)]
    [DataRow(-5f)]
    public void CinematicThatStartsOnThisClient_ArmsTheSkipAtOnce_SoASequenceThatStallsBeforeTheGamesOwnArmingCanStillBeSkipped(float now)
    {
        // bug1-intro-join, finding 1: the start used to switch the skip off until the sequence got past TriggerIntroCinematic, and the second player's sequence did not get there
        float skipHintStart = IntroSequenceRules.SkipHintStartAtCinematicStart(now);

        skipHintStart.Should().NotBe(IntroSequenceRules.SKIP_OFF);
        IntroSequenceRules.IsSkipArmed(skipHintStart).Should().BeTrue();
        IntroSequenceRules.IsSkipRequested(skipHintStart, 1.5f, 1.5f).Should().BeTrue("a player who holds the key at the black screen must get out");
        IntroSequenceRules.IsSkipRequested(skipHintStart, 0.2f, 1.5f).Should().BeFalse("a tap does not skip");
    }

    [TestMethod]
    public void ArmedStart_KeepsTheTimelineOfTheSkipHint()
    {
        // The hint fades in and out relative to this value: it is the time of the start, not a made-up one
        IntroSequenceRules.SkipHintStartAtCinematicStart(42.25f).Should().Be(42.25f);
    }

    // The watchdog

    [TestMethod]
    public void WatchdogLooksAtNothingBeforeTheCinematicStarted()
    {
        // A player may wait for his partner for as long as he wants
        IntroSequenceRules.EvaluateWatchdog(true, false, 0f, false, false, 0f).Should().Be(IntroWatchdogVerdict.Keep);
        IntroSequenceRules.EvaluateWatchdog(true, false, 1000f, false, false, 0f).Should().Be(IntroWatchdogVerdict.Keep);
    }

    [TestMethod]
    public void WatchdogLooksAtNothingWhenTheIntroIsNotShowing()
    {
        IntroSequenceRules.EvaluateWatchdog(false, true, 1000f, true, true, 1000f).Should().Be(IntroWatchdogVerdict.Keep);
    }

    [TestMethod]
    public void CinematicThatNeverStarts_IsEndedAfterTheStartTimeout()
    {
        // The sequence died (or waits for a fade that never completes) right after the start: the screen stays black, the cinematic never runs
        IntroSequenceRules.EvaluateWatchdog(true, true, IntroSequenceRules.CINEMATIC_START_TIMEOUT_SECONDS - 1f, false, false, 0f).Should().Be(IntroWatchdogVerdict.Keep);
        IntroSequenceRules.EvaluateWatchdog(true, true, IntroSequenceRules.CINEMATIC_START_TIMEOUT_SECONDS, false, false, 0f).Should().Be(IntroWatchdogVerdict.FinishCinematicNeverStarted);
    }

    [TestMethod]
    public void CinematicThatIsPlaying_IsLeftAlone()
    {
        IntroSequenceRules.EvaluateWatchdog(true, true, 20f, true, true, 0f).Should().Be(IntroWatchdogVerdict.Keep);
        IntroSequenceRules.EvaluateWatchdog(true, true, 70f, true, true, 0f).Should().Be(IntroWatchdogVerdict.Keep);
    }

    [TestMethod]
    public void CinematicThatEndedWithoutTheSequenceEndingTheIntro_IsEndedAfterAGrace()
    {
        // The pod landed and the screen is still black: the sequence should have ended the intro in the frame after the cinematic ended
        IntroSequenceRules.EvaluateWatchdog(true, true, 65f, true, false, 2f).Should().Be(IntroWatchdogVerdict.Keep);
        IntroSequenceRules.EvaluateWatchdog(true, true, 65f, true, false, IntroSequenceRules.CINEMATIC_ENDED_GRACE_SECONDS).Should().Be(IntroWatchdogVerdict.FinishCinematicEndedUnnoticed);
    }

    [TestMethod]
    public void CinematicThatRunsForever_IsEndedAfterTheLimit()
    {
        IntroSequenceRules.EvaluateWatchdog(true, true, IntroSequenceRules.MAX_CINEMATIC_SECONDS - 1f, true, true, 0f).Should().Be(IntroWatchdogVerdict.Keep);
        IntroSequenceRules.EvaluateWatchdog(true, true, IntroSequenceRules.MAX_CINEMATIC_SECONDS, true, true, 0f).Should().Be(IntroWatchdogVerdict.FinishTooLong);
        IntroSequenceRules.EvaluateWatchdog(true, true, IntroSequenceRules.MAX_CINEMATIC_SECONDS, false, false, 0f).Should().Be(IntroWatchdogVerdict.FinishTooLong);
    }

    [TestMethod]
    public void TheNormalIntroOfTheFirstLiveRun_NeverTripsTheWatchdog()
    {
        // 55 s from "playing" to "completed" (the host, 2026-10-23): the controller is active from the start to the end, then the sequence ends the intro in the next frame
        for (float seconds = 0f; seconds < 55f; seconds += 0.5f)
        {
            bool wasActive = seconds >= 0.5f;
            IntroSequenceRules.EvaluateWatchdog(true, true, seconds, wasActive, wasActive, 0f).Should().Be(IntroWatchdogVerdict.Keep, $"at {seconds} s");
        }
    }
}
