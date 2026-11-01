#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using CoroutineHost = UWE.CoroutineHost;

namespace NitroxClient.MonoBehaviours;

// Live check "introjoin" (bug1-intro-join, the design notes (not part of this repository) 6.1 row 1): the two-player intro on a fresh world with the real intro (Release build, or a Debug build started with --noskipintro in a SURVIVAL world), with
// the second player joining before the intro, during it, and leaving during it. Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the
// server's "/console <player> <command> [arg]" (all lower case) and log "[SyncHarness] <name>: key=value ..." lines. Nothing throws out of a Unity callback.
//
//   introprobe on|off|once     "on" logs one line per second ("[SyncHarness] introprobe: ...") for as long as the intro screen is up and for 15 s after it, and starts by itself in a Debug build when the intro
//                              sequence begins (uGUI_SceneIntro_IntroSequence_Patch.EnsureRun); "off" stops it; "once" (the default) logs one line. A line has: t (client real time), mode (the intro mode of this client),
//                              showing (uGUI_SceneIntro.showing), fader (alpha of the black overlay, 0 = gone), text (alpha of the intro text), controller (the lifepod's intro cinematic is active), playercine
//                              (Player.cinematicModeActive), count (PlayerCinematicController.cinematicModeCount), timescale, pos, pod, poddist (metres, flat), dy (player minus pod, height), inpod
//                              (IntroCinematicRules.IsPoseInEscapePod), podmode (Player.currentEscapePod set), podflag (Player.escapePod.value), underwater, black (fader above 0.9: expected only for the blackout of the
//                              crash, a few seconds, and while the "press any button" text is up)
//   introspawn                 ONE line right after a (re)join: the position of the player against the lifepod and the flags that make him walk or swim. The check: poddist <= 10 and inpod=True
//                              (The Release build logs the same without a command: "[PoseAfterSync] sync-end|sync+2s|sync+6s" from PlayerPositionInitialSyncProcessor, and puts a player who joined in his lifepod
//                              and is not in it 2 s after the sync back at the spawn, with a "[PoseAfterSync] This player joined in his escape pod but is ..." warning)
//   introkill                  stops the coroutine of the intro but leaves it set (uGUI_SceneIntro.showing stays true): the state in which the second player sat at the black screen. The watchdog must end the
//                              intro (a "[Intro] The intro does not end by itself" warning, then the screen is up, mode COMPLETED) within 12 s if the cinematic never started, 12 s after the cinematic ended (about 70 s after the start) if it had, and at the latest 150 s after the start
//   introskip                  runs what holding the skip button does (uGUI_SceneIntro_HandleInput_Patch.HandleButtonHeld) so that a script can skip without a hand on the key
//   introstall                 the state in which the second player sat at the black screen and the skip key did nothing: the cinematic was just started by this client (what the sequence does for a
//                              paired player: uGUI_SceneIntro_IntroSequence_Patch.EnqueueStartCinematic) and the sequence is stopped before the game arms its own skip. Logs skiphintstart and armed. The
//                              check: armed=True, and HOLDING the skip key (Escape) for 1.5 s ends the intro ("[Intro] The skip button was held ... skipping the intro", then introprobe shows showing=False, mode COMPLETED)
public partial class HarnessConsoleCommands
{
    private static bool ijProbeRunning;
    private static bool ijProbeStop;

    private static string IjF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string IjVec(Vector3 value)
    {
        return $"{value.x.ToString("F1", CultureInfo.InvariantCulture)},{value.y.ToString("F1", CultureInfo.InvariantCulture)},{value.z.ToString("F1", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    ///     Starts the once-per-second log of the intro on this client (idempotent). Called by the intro patch when a run begins.
    /// </summary>
    public static void StartIntroProbe()
    {
        if (ijProbeRunning)
        {
            return;
        }

        ijProbeStop = false;
        ijProbeRunning = true;
        CoroutineHost.StartCoroutine(IjProbeRoutine());
    }

    private static IEnumerator IjProbeRoutine()
    {
        float introEndedAt = -1f;
        try
        {
            while (!ijProbeStop)
            {
                IjProbeLine("introprobe");

                bool showing = uGUI.main && uGUI.main.intro && uGUI.main.intro.showing;
                if (showing)
                {
                    introEndedAt = -1f;
                }
                else if (introEndedAt < 0f)
                {
                    introEndedAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - introEndedAt >= 15f)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(1f);
            }
        }
        finally
        {
            ijProbeRunning = false;
        }
    }

    private static void IjProbeLine(string tag)
    {
        try
        {
            NitroxClient.GameLogic.LocalPlayer localPlayer = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>();
            uGUI_SceneIntro intro = uGUI.main ? uGUI.main.intro : null;
            Player player = Player.main;
            EscapePod pod = EscapePod.main;

            string fader = "none";
            bool black = false;
            string text = "none";
            if (intro)
            {
                float faderAlpha = intro.fader && intro.fader.canvasGroup ? intro.fader.canvasGroup.alpha : -1f;
                fader = IjF(faderAlpha);
                black = faderAlpha > 0.9f;
                float textAlpha = intro.mainText && intro.mainText.text ? (intro.mainText.text.enabled ? intro.mainText.text.color.a : 0f) : -1f;
                text = IjF(textAlpha);
            }

            string position = player ? IjVec(player.transform.position) : "none";
            string podPosition = pod ? IjVec(pod.transform.position) : "none";
            string podDistance = "none";
            string dy = "none";
            string inPod = "none";
            if (player && pod)
            {
                Vector3 delta = player.transform.position - pod.transform.position;
                podDistance = IjF(new Vector3(delta.x, 0f, delta.z).magnitude);
                dy = IjF(delta.y);
                inPod = Nitrox.Model.Subnautica.DataStructures.GameLogic.IntroCinematicRules.IsPoseInEscapePod(player.transform.position.ToDto(), pod.transform.position.ToDto()).ToString();
            }

            string showing = intro ? intro.showing.ToString() : "none";
            string controller = pod && pod.introCinematic ? pod.introCinematic.cinematicModeActive.ToString() : "none";
            string playerCinematic = player ? player.cinematicModeActive.ToString() : "none";
            string podMode = player ? (player.currentEscapePod != null).ToString() : "none";
            string podFlag = player ? player.escapePod.value.ToString() : "none";
            string underwater = player ? player.IsUnderwaterForSwimming().ToString() : "none";
            Log.Info($"[SyncHarness] {tag}: t={IjF(Time.unscaledTime)} mode={localPlayer.IntroCinematicMode} showing={showing} fader={fader} text={text} controller={controller} playercine={playerCinematic} count={PlayerCinematicController.cinematicModeCount} timescale={IjF(Time.timeScale)} pos={position} pod={podPosition} poddist={podDistance} dy={dy} inpod={inPod} podmode={podMode} podflag={podFlag} underwater={underwater} black={black}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] {tag}: failed {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_introprobe(NotificationCenter.Notification n)
    {
        string mode = LiveArgString(n, 0, "once");
        if (mode == "on")
        {
            StartIntroProbe();
            Log.Info($"[SyncHarness] introprobe: on running={ijProbeRunning}");
        }
        else if (mode == "off")
        {
            ijProbeStop = true;
            Log.Info("[SyncHarness] introprobe: off");
        }
        else
        {
            IjProbeLine("introprobe");
        }
    }

    private void LiveCommand_introspawn(NotificationCenter.Notification n)
    {
        IjProbeLine("introspawn");
    }

    private void LiveCommand_introkill(NotificationCenter.Notification n)
    {
        try
        {
            uGUI_SceneIntro intro = uGUI.main ? uGUI.main.intro : null;
            if (!intro || !intro.showing)
            {
                Log.Info("[SyncHarness] introkill: failed reason=intro-not-showing");
                return;
            }

            intro.StopCoroutine(intro.coroutine);
            Log.Info($"[SyncHarness] introkill: the coroutine of the intro is stopped, showing={intro.showing}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] introkill: failed {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_introstall(NotificationCenter.Notification n)
    {
        try
        {
            uGUI_SceneIntro intro = uGUI.main ? uGUI.main.intro : null;
            if (!intro || !intro.showing)
            {
                Log.Info("[SyncHarness] introstall: failed reason=intro-not-showing");
                return;
            }

            Type patchType = Type.GetType("NitroxPatcher.Patches.Dynamic.uGUI_SceneIntro_IntroSequence_Patch, NitroxPatcher");
            MethodInfo method = patchType != null ? patchType.GetMethod("EnqueueStartCinematic", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (method == null)
            {
                Log.Info("[SyncHarness] introstall: failed reason=no-EnqueueStartCinematic");
                return;
            }

            method.Invoke(null, new object[] { intro });
            intro.StopCoroutine(intro.coroutine);
            Log.Info($"[SyncHarness] introstall: the sequence is stopped right after the start of the cinematic, skiphintstart={IjF(intro.skipHintStartTime)} armed={intro.skipHintStartTime >= 0f} showing={intro.showing}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] introstall: failed {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_introskip(NotificationCenter.Notification n)
    {
        try
        {
            uGUI_SceneIntro intro = uGUI.main ? uGUI.main.intro : null;
            if (!intro || !intro.showing)
            {
                Log.Info("[SyncHarness] introskip: failed reason=intro-not-showing");
                return;
            }

            Type patchType = Type.GetType("NitroxPatcher.Patches.Dynamic.uGUI_SceneIntro_HandleInput_Patch, NitroxPatcher");
            MethodInfo method = patchType != null ? patchType.GetMethod("HandleButtonHeld", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (method == null)
            {
                Log.Info("[SyncHarness] introskip: failed reason=no-HandleButtonHeld");
                return;
            }

            method.Invoke(null, new object[] { intro });
            Log.Info($"[SyncHarness] introskip: done showing={intro.showing}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] introskip: failed {exception.GetType().Name}: {exception.Message}");
        }
    }
}
#endif
