using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Spawning.Metadata.Processor;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using CoroutineHost = UWE.CoroutineHost;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class uGUI_SceneIntro_IntroSequence_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo targetMethod = AccessTools.EnumeratorMoveNext(Reflect.Method((uGUI_SceneIntro si) => si.IntroSequence()));

    private static RemotePlayer? partner;
    private static bool callbackRun;
    private static bool packetSend;

    // Everything above and below describes ONE run of the intro. The fields are static, so a second run in the same game process (quit to the menu, join again) used to find them as the first
    // run left them: "callbackRun" true made a player who is past the intro play the whole cinematic (and with a partner object that is long destroyed). runOwner is the intro screen the fields
    // belong to; another one means a new run (EnsureRun).
    private static uGUI_SceneIntro? runOwner;
    private static float cinematicStartedAt;
    private static float startModeSince = -1f;
    private static float nobodyToPairWithSince = -1f;
    private static int stepFailures;
    private static bool cinematicWasActive;
    private static float cinematicEndedAt = -1f;

    public static bool IsWaitingForPartner { get; private set; }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
               // Replace LargeWorldStreamer.Main.IsWorldSettled() check with IsWorldSettledAndInitialSyncCompleted()
               .MatchStartForward(
                   new CodeMatch(OpCodes.Ldsfld, Reflect.Field(() => LargeWorldStreamer.main)),
                   new CodeMatch(OpCodes.Callvirt, Reflect.Method((LargeWorldStreamer lws) => lws.IsWorldSettled()))
               )
               .SetAndAdvance(OpCodes.Call, Reflect.Method(() => IsWorldSettledAndInitialSyncCompleted()))
               .RemoveInstruction()
               // Replace GameInput.AnyKeyDown check with AnyKeyDownOrSubstitute()
               .MatchEndForward(
                   new CodeMatch(OpCodes.Call, Reflect.Property(() => GameInput.AnyKeyDown).GetMethod)
               )
               .SetOperandAndAdvance(Reflect.Method(() => AnyKeyDownOrSubstitute()))
               // Insert custom check if cinematic should be started => waiting for other player & enable skip functionality
               .MatchEndForward(
                   new CodeMatch(OpCodes.Ldfld, Reflect.Field((uGUI_SceneIntro si) => si.moveNext)),
                   new CodeMatch(OpCodes.Brfalse))
               .Insert(
                   new CodeInstruction(OpCodes.Ldloc_1),
                   new CodeInstruction(OpCodes.Call, Reflect.Method(() => IsRemoteCinematicReady(default))),
                   new CodeInstruction(OpCodes.And)
               )
               // Run our prepare code when cinematic starts
               .MatchEndForward(
                   new CodeMatch(OpCodes.Ldsfld, Reflect.Field(() => EscapePod.main)),
                   new CodeMatch(OpCodes.Callvirt, Reflect.Method(() => EscapePod.main.TriggerIntroCinematic()))
               )
               .Advance(1)
               .Insert(
                   new CodeInstruction(OpCodes.Call, Reflect.Method(() => StartRemoteCinematic()))
               )
               // Disable cinematic skip when cinematic already started in duo mode
               .MatchStartForward(
                   new CodeMatch(OpCodes.Call, Reflect.Property(() => Time.time).GetMethod)
               )
               .SetOperandAndAdvance(Reflect.Method(() => GetSkipTime()))
               .Advance(1)
               .Insert(
                   new CodeInstruction(OpCodes.Ldloc_1),
                   new CodeInstruction(OpCodes.Ldfld, Reflect.Field((uGUI_SceneIntro si) => si.skipText)),
                   new CodeInstruction(OpCodes.Ldc_R4, 0f),
                   new CodeInstruction(OpCodes.Call, Reflect.Method(() => TMProExtensions.SetAlpha(default, default(float))))
               )
               // Replace intro text
               .MatchEndForward(
                   new CodeMatch(OpCodes.Ldstr, "IntroUWEPresents")
               )
               .SetOperandAndAdvance("Nitrox_IntroUWEPresents")
               // Run our cleanup code when cinematic ends
               .MatchEndForward(
                   new CodeMatch(OpCodes.Ldloc_1),
                   new CodeMatch(OpCodes.Ldc_I4_0),
                   new CodeMatch(OpCodes.Call, Reflect.Method((uGUI_SceneIntro si) => si.Stop(default)))
               )
               .Advance(1)
               .Insert(
                   new CodeInstruction(OpCodes.Call, Reflect.Method(() => EndRemoteCinematic()))
               )
               .InstructionEnumeration();
    }

    public static void EnqueueStartCinematic(uGUI_SceneIntro uGuiSceneIntro)
    {
        IsWaitingForPartner = false;
        // Armed from here on (IntroSequenceRules.SkipHintStartAtCinematicStart), not switched off: the game arms the skip itself only after TriggerIntroCinematic (IL_0236 of the sequence), and a sequence
        // that stalls in the loop that waits for the fade below, or dies before that point, would leave the player at the black screen with the hold of the skip key ignored (OnUpdate and HandleInput
        // both need skipHintStartTime >= 0) until the watchdog ends the intro. A hold that is still going on from "skip waiting" does not count twice: HandleButtonHeld moved its start into the future.
        uGuiSceneIntro.skipHintStartTime = IntroSequenceRules.SkipHintStartAtCinematicStart(Time.time);
        uGuiSceneIntro.moveNext = false;
        uGuiSceneIntro.mainText.FadeOut(0.2f, uGuiSceneIntro.Callback);
        callbackRun = true;
        cinematicStartedAt = Time.unscaledTime;
        Log.Info($"[Intro] The cinematic starts (mode {Resolve<LocalPlayer>().IntroCinematicMode}, partner {(partner != null ? partner.PlayerName : "none")}, time scale {Time.timeScale}, partner body {(partner != null && partner.Body ? "present" : "missing")})");
    }

    /// <summary>
    ///     Ends the intro for this player on the spot, the way the "hold to skip" of the game does, whatever state the sequence is in: the screen of the intro (the black overlay and its text) is taken
    ///     down, the cinematic is ended, the partner stops being drawn as a copy of this player, the lifepod gets the state of the world and the mode is "completed". Every step is guarded: the
    ///     sequence that would end the intro is a coroutine, and an exception in a step must never leave the player at a black screen.
    /// </summary>
    /// <param name="wasNewPlayer">True if the pod and the radio have to be put in the state the world has after the intro (the player did not get it from the server), false for a player who got it</param>
    public static void SkipLocalCinematic(uGUI_SceneIntro uGuiSceneIntro, bool wasNewPlayer)
    {
        LiveMixin? radioLiveMixin = GetRadioLiveMixin();
        float radioHealthBefore = radioLiveMixin ? radioLiveMixin.health : 0f;

        CleanUpPartnerPresentation();

        try
        {
            uGuiSceneIntro.Stop(true);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] uGUI_SceneIntro.Stop threw while the intro was skipped");
        }
        EndSequence(uGuiSceneIntro);
        ClearIntroScreen(uGuiSceneIntro);

        try
        {
            if (radioLiveMixin)
            {
                if (!wasNewPlayer)
                {
                    // EscapePod.DamageRadio() is called by GuiSceneIntro.Stop(true) but is undesired. We revert it here
                    radioLiveMixin.health = radioHealthBefore;
                }

                if (radioLiveMixin.IsFullHealth())
                {
                    Object.Destroy(radioLiveMixin.loopingDamageEffectObj);
                }
            }

            Transform? introFireHolder = EscapePod.main ? EscapePod.main.transform.Find("Intro") : null;
            if (introFireHolder) // Can be null if called very early
            {
                if (introFireHolder.TryGetComponentInChildren(out NitroxEntity? fireEntity))
                {
                    Resolve<Entities>().RemoveEntity(fireEntity.Id);
                }

                FMOD_CustomEmitter? fireEmitter = introFireHolder.GetComponentInChildren<FMOD_CustomEmitter>(true);
                if (fireEmitter)
                {
                    fireEmitter.ReleaseEvent(); // Not releasing it before destroying results in infinite unstoppable pain
                }
                Object.DestroyImmediate(introFireHolder.gameObject); // Like in Fire.Extinguished() but without delay
            }

            // From uGUI_SceneIntro.IntroSequence() after "if (XRSettings.enabled && VROptions.skipIntro)"
            if (UnityObjectExtensions.TryFind("fire_extinguisher_01_tp", out GameObject gameObject1))
            {
                Object.Destroy(gameObject1);
            }
            if (UnityObjectExtensions.TryFind("IntroFireExtinugisherPickup", out GameObject gameObject2))
            {
                Object.Destroy(gameObject2);
            }

            if (wasNewPlayer)
            {
                EscapePodMetadataProcessor.ReapplyAfterIntro();
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Putting the lifepod in the state after the intro failed");
        }

        Resolve<PlayerCinematics>().SetLocalIntroCinematicMode(IntroCinematicMode.COMPLETED);
        Log.Info($"[Intro] The intro is skipped for this player (new player {wasNewPlayer})");
        ResetRunState();
        CoroutineHost.StartCoroutine(EnsurePlayerReleasedLater());
    }

    /// <summary>
    ///     <c>uGUI_SceneIntro.showing</c> is "the coroutine is set", and the input handler of the intro keeps all the input of the player for as long as it is: if <c>Stop</c> did not get as far as
    ///     ending the coroutine, it is ended here.
    /// </summary>
    private static void EndSequence(uGUI_SceneIntro uGuiSceneIntro)
    {
        try
        {
            if (uGuiSceneIntro.showing)
            {
                Log.Warn("[Intro] The sequence of the intro was still set after Stop, ending it");
                uGuiSceneIntro.StopCoroutine(uGuiSceneIntro.coroutine);
                uGuiSceneIntro.coroutine = null;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Ending the sequence of the intro failed");
        }
    }

    private static LiveMixin? GetRadioLiveMixin()
    {
        EscapePod pod = EscapePod.main;
        if (pod && pod.radioSpawner && pod.radioSpawner.spawnedObj && pod.radioSpawner.spawnedObj.TryGetComponent(out Radio radio))
        {
            return radio.liveMixin;
        }

        return null;
    }

    /// <summary>
    ///     Takes the black overlay of the intro down. <c>uGUI_SceneIntro.Stop</c> does that too (fader and text off) but returns early when the intro is not showing any more, and it
    ///     is the fader that stays up when the sequence that should fade it out is gone.
    /// </summary>
    private static void ClearIntroScreen(uGUI_SceneIntro uGuiSceneIntro)
    {
        try
        {
            if (uGuiSceneIntro.fader)
            {
                uGuiSceneIntro.fader.SetState(false);
                if (uGuiSceneIntro.fader.canvasGroup)
                {
                    uGuiSceneIntro.fader.canvasGroup.alpha = 0f;
                }
            }

            if (uGuiSceneIntro.mainText)
            {
                uGuiSceneIntro.mainText.SetState(false);
            }

            if (uGuiSceneIntro.skipText)
            {
                TMProExtensions.SetAlpha(uGuiSceneIntro.skipText, 0f);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Taking down the screen of the intro failed");
        }
    }

    /// <summary>
    ///     Gives the partner his own body back (the intro draws him as a mirrored copy of this player) and forgets him. Safe when he is gone, destroyed or never was.
    /// </summary>
    private static void CleanUpPartnerPresentation()
    {
        RemotePlayer? remote = partner;
        partner = null;
        IntroCinematicUpdater.Partner = null;
        if (remote == null)
        {
            return;
        }

        try
        {
            if (remote.Body)
            {
                IntroCinematicUpdater introCinematicUpdater = remote.Body.GetComponent<IntroCinematicUpdater>();
                if (introCinematicUpdater)
                {
                    Object.DestroyImmediate(introCinematicUpdater);
                }
            }

            if (remote.PlayerModel)
            {
                remote.PlayerModel.transform.localScale = new Vector3(1, 1, 1);
            }

            if (remote.AnimationController)
            {
                remote.AnimationController["cinematics_enabled"] = false;
                remote.AnimationController["escapepod_intro"] = false;
                remote.AnimationController.UpdatePlayerAnimations = true;
            }

            if (remote.ArmsController)
            {
                remote.ArmsController.enabled = true;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Giving the partner his body back failed");
        }
    }

    /// <summary>
    ///     Forgets what the last run of the intro left behind (see <see cref="runOwner" />).
    /// </summary>
    private static void ResetRunState()
    {
        callbackRun = false;
        packetSend = false;
        IsWaitingForPartner = false;
        startModeSince = -1f;
        nobodyToPairWithSince = -1f;
        stepFailures = 0;
        cinematicStartedAt = 0f;
        cinematicWasActive = false;
        cinematicEndedAt = -1f;
        Resolve<PlayerCinematics>().IntroCinematicPartnerId = null;
    }

    /// <summary>
    ///     Called by every step of the sequence. Starts the bookkeeping of a new run the first time a new intro screen is seen, and the watchdog that ends the intro if the sequence does not.
    /// </summary>
    private static void EnsureRun()
    {
        uGUI_SceneIntro? intro = uGUI.main ? uGUI.main.intro : null;
        if (!intro || intro == runOwner)
        {
            return;
        }

        CleanUpPartnerPresentation();
        ResetRunState();
        runOwner = intro;
        CoroutineHost.StartCoroutine(Watchdog(intro));
#if DEBUG
        HarnessConsoleCommands.StartIntroProbe();
#endif
    }

    /// <summary>
    ///     Ends an intro that the sequence does not end (see <see cref="IntroSequenceRules.EvaluateWatchdog" />): the second player of a new world sat at a black screen through the cinematic
    ///     and after it, and <c>Escape</c> did nothing, because the sequence is a coroutine and nothing else ever took the screen down. Runs on real time and does not depend on anything the
    ///     sequence does (its fades and callbacks use the time scale of the game).
    /// </summary>
    private static IEnumerator Watchdog(uGUI_SceneIntro intro)
    {
        while (intro && intro == runOwner)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (!intro || intro != runOwner || !intro.showing)
            {
                yield break;
            }

            IntroWatchdogVerdict verdict = EvaluateWatchdog(intro);
            if (verdict == IntroWatchdogVerdict.Keep)
            {
                continue;
            }

            ForceFinishIntro(intro, verdict);
            yield break;
        }
    }

    /// <summary>
    ///     The controller of the cinematic lets the player go on its own after a skip (<c>EscapePod.StopIntroCinematic</c>); if it did not within a moment, the game's own release does it.
    /// </summary>
    private static IEnumerator EnsurePlayerReleasedLater()
    {
        yield return new WaitForSecondsRealtime(2f);
        EnsurePlayerReleased();
    }

    private static IntroWatchdogVerdict EvaluateWatchdog(uGUI_SceneIntro intro)
    {
        try
        {
            float now = Time.unscaledTime;
            bool active = EscapePod.main && EscapePod.main.IsPlayingIntroCinematic();
            if (active)
            {
                cinematicWasActive = true;
                cinematicEndedAt = -1f;
            }
            else if (cinematicWasActive && cinematicEndedAt < 0f)
            {
                cinematicEndedAt = now;
            }

            return IntroSequenceRules.EvaluateWatchdog(intro.showing, callbackRun, callbackRun ? now - cinematicStartedAt : 0f, cinematicWasActive, active, cinematicEndedAt >= 0f ? now - cinematicEndedAt : 0f);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] The watchdog of the intro failed to look at the sequence");
            return IntroWatchdogVerdict.Keep;
        }
    }

    private static void ForceFinishIntro(uGUI_SceneIntro intro, IntroWatchdogVerdict verdict)
    {
        Log.Warn($"[Intro] The intro does not end by itself ({verdict}: started {Time.unscaledTime - cinematicStartedAt:0.0} s ago, mode {Resolve<LocalPlayer>().IntroCinematicMode}, cinematic seen active {cinematicWasActive}, time scale {Time.timeScale}), ending it now");
        SkipLocalCinematic(intro, true);
    }

    private static void EnsurePlayerReleased()
    {
        try
        {
            PlayerCinematicController? introController = EscapePod.main ? EscapePod.main.introCinematic : null;
            if (introController && introController.cinematicModeActive)
            {
                Log.Warn("[Intro] The cinematic still holds the player after the intro was ended, releasing him");
                introController.EndCinematicMode();
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Releasing the player after the intro failed");
        }
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private static bool IsWorldSettledAndInitialSyncCompleted()
    {
        try
        {
            EnsureRun();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Starting the bookkeeping of the intro failed");
        }

        return LargeWorldStreamer.main.IsWorldSettled() && Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted;
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private static bool AnyKeyDownOrSubstitute()
    {
        return GameInput.AnyKeyDown ||
               !NitroxEnvironment.IsReleaseMode ||
               Resolve<LocalPlayer>().IntroCinematicMode == IntroCinematicMode.COMPLETED;
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private static float GetSkipTime()
    {
        // Return Time.time when the cinematic starts, solo or duo: holding the skip button ends the intro for this player only (SkipLocalCinematic). It used to be switched off in duo, which left a
        // player whose cinematic did not run at a black screen with no way out (the partner is only drawn as a copy of this player, so nothing of the partner's intro depends on this one)
        IntroCinematicMode mode = Resolve<LocalPlayer>().IntroCinematicMode;
        return mode is IntroCinematicMode.SINGLEPLAYER or IntroCinematicMode.PLAYING ? Time.time : -1f;
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private static bool IsRemoteCinematicReady(uGUI_SceneIntro uGuiSceneIntro)
    {
        // This runs inside the coroutine of the game: an exception here would end the coroutine for good, with the black overlay up and the input of the player captured by the intro
        try
        {
            EnsureRun();
            bool ready = StepIntro(uGuiSceneIntro);
            stepFailures = 0;
            return ready;
        }
        catch (Exception exception)
        {
            stepFailures++;
            Log.Error(exception, $"[Intro] A step of the intro failed ({stepFailures}. time in a row)");
            if (stepFailures >= 3)
            {
                // The step keeps failing: end the intro rather than keep the player at the black screen
                SkipLocalCinematic(uGuiSceneIntro, true);
            }

            return false;
        }
    }

    private static bool StepIntro(uGUI_SceneIntro uGuiSceneIntro)
    {
        LocalPlayer localPlayer = Resolve<LocalPlayer>();
        PlayerCinematics playerCinematics = Resolve<PlayerCinematics>();
        PlayerManager playerManager = Resolve<PlayerManager>();
        IntroCinematicMode mode = localPlayer.IntroCinematicMode;
        float now = Time.unscaledTime;

        if (mode == IntroCinematicMode.START)
        {
            if (startModeSince < 0f)
            {
                startModeSince = now;
            }
        }
        else
        {
            startModeSince = -1f;
        }

        // Are the others past the intro with nobody waiting? Only a player who waits needs to know for how long (the partner's "start" arrives a moment before his own)
        bool nobodyToPairWith = IntroCinematicRules.HasNobodyToPairWith(playerManager.GetAll().Select(other => other.PlayerContext.IntroCinematicMode));
        if (mode == IntroCinematicMode.WAITING && nobodyToPairWith)
        {
            if (nobodyToPairWithSince < 0f)
            {
                nobodyToPairWithSince = now;
            }
        }
        else
        {
            nobodyToPairWithSince = -1f;
        }

        SessionId? opPartnerId = playerCinematics.IntroCinematicPartnerId;
        RemotePlayer? newPartner = null;
        bool partnerKnown = opPartnerId.HasValue && playerManager.TryFind(opPartnerId.Value, out newPartner);

        // Skipping intro if creative like in normal SN or in debug configuration
        bool skipsForDebugOrCreative = ShouldSkipIntroForDebug() || GameModeUtils.currentGameMode.HasFlag(GameModeOption.Creative);

        IntroReadyAction action = IntroSequenceRules.DecideReady(callbackRun, mode, skipsForDebugOrCreative, packetSend, partnerKnown, startModeSince >= 0f ? now - startModeSince : 0f,
                                                                 nobodyToPairWith, nobodyToPairWithSince >= 0f ? now - nobodyToPairWithSince : 0f);
        switch (action)
        {
            case IntroReadyAction.Proceed:
                return true;
            case IntroReadyAction.SkipAsCompleted:
                SkipLocalCinematic(uGuiSceneIntro, false);
                return false;
            case IntroReadyAction.SkipAsNewPlayer:
                SkipLocalCinematic(uGuiSceneIntro, true);
                return false;
            case IntroReadyAction.ReportWaiting:
                uGuiSceneIntro.skipHintStartTime = Time.time;
                uGuiSceneIntro.mainText.SetText(Language.main.GetFormat("Nitrox_IntroWaitingPartner", GameInput.FormatButton(GameInput.Button.UIMenu)));
                uGuiSceneIntro.mainText.SetState(true);

                playerCinematics.SetLocalIntroCinematicMode(IntroCinematicMode.WAITING);
                packetSend = true;
                IsWaitingForPartner = true;
                return false;
            case IntroReadyAction.StartWithPartner:
                partner = newPartner;
                StartCinematic(uGuiSceneIntro, playerCinematics);
                return false;
            case IntroReadyAction.StartAlone:
                Log.Warn($"[Intro] The server paired this player with {opPartnerId} but he is not known here after {IntroSequenceRules.PARTNER_LOOKUP_SECONDS:0} s, playing without him");
                partner = null;
                StartCinematic(uGuiSceneIntro, playerCinematics);
                return false;
            default:
                return false;
        }
    }

    private static void StartCinematic(uGUI_SceneIntro uGuiSceneIntro, PlayerCinematics playerCinematics)
    {
        EnqueueStartCinematic(uGuiSceneIntro);
        playerCinematics.SetLocalIntroCinematicMode(IntroCinematicMode.PLAYING);
        playerCinematics.IntroCinematicPartnerId = null;
    }

    private static bool ShouldSkipIntroForDebug() =>
        !NitroxEnvironment.IsReleaseMode &&
        !NitroxEnvironment.CommandLineArgs.Any(x => x.Equals("--noskipintro", StringComparison.OrdinalIgnoreCase));

    private static void StartRemoteCinematic()
    {
        // The coroutine of the game calls this: whatever goes wrong with the copy of the partner must not end it (the cinematic is the player's own, the partner is only drawn next to it)
        try
        {
            Log.Info($"[Intro] The sequence started the cinematic of the lifepod (controller active {(EscapePod.main ? EscapePod.main.IsPlayingIntroCinematic().ToString() : "no pod")})");
            if (IsPartnerValid()) // Is null when RunCinematicSingleplayer() is called
            {
                partner.PlayerModel.transform.localScale = new Vector3(-1, 1, 1);
                partner.ArmsController.enabled = false;
                partner.AnimationController.UpdatePlayerAnimations = false;
                partner.AnimationController["cinematics_enabled"] = true;
                partner.AnimationController["escapepod_intro"] = true;

                IntroCinematicUpdater.Partner = partner;
                partner.Body.AddComponent<IntroCinematicUpdater>();
            }
            else if (partner != null)
            {
                Log.Warn($"[Intro] The partner {partner.PlayerName} has no body here (yet), the cinematic runs without his copy");
                partner = null;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Drawing the partner next to the cinematic failed, the cinematic runs without his copy");
            CleanUpPartnerPresentation();
        }
    }

    private static void EndRemoteCinematic()
    {
        CleanUpPartnerPresentation();

        try
        {
            EscapePodMetadataProcessor.ReapplyAfterIntro();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Intro] Putting the lifepod in the state after the intro failed");
        }

        Resolve<PlayerCinematics>().SetLocalIntroCinematicMode(IntroCinematicMode.COMPLETED);
        Log.Info("[Intro] The intro is over");
        ResetRunState();
    }

    /// <summary>
    ///     True if the partner is known to this client and has a body to draw. The body is a Unity object: a partner from a run before (the player left, the scene was unloaded) is not null but destroyed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(partner))]
    private static bool IsPartnerValid() => partner != null && partner.Body && partner.PlayerModel && partner.AnimationController && partner.ArmsController && Resolve<PlayerManager>().Find(partner.SessionId).HasValue;
}
