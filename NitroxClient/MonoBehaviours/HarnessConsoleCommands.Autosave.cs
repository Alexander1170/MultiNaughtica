#if DEBUG
using System.Collections;
using System.Globalization;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "autosave_icon" (a harness script that is not part of this repository, item a). Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the
// server's "/console <player> autosaveprobe [seconds]". Without seconds ONE line, with seconds one line every 0.25 s for that long (a chat command takes seconds to type, so the 2.5 s of the indicator can only
// be seen by a sampler that is already running). The line "[SyncHarness] autosaveprobe: key=value ...":
//   exists    the indicator object exists (it is made by the first ServerSaving packet)
//   count     the notices this client has received since it was made (AutosaveIndicator.ShownCount), last = the Number of the latest one
//   showing   the indicator is inside its 2.5 s, alpha = what the canvas group has right now, since = seconds since the latest notice
//   text      the text it shows (spaces as _), x / y = the screen position of the ring (top left: small x, y near screenHeight)
public partial class HarnessConsoleCommands
{
    private bool autosaveProbeRunning;

    private void LiveCommand_autosaveprobe(NotificationCenter.Notification n)
    {
        float seconds = LiveArgFloat(n, 0, 0f);
        if (seconds <= 0f)
        {
            LogAutosaveProbe();
            return;
        }

        if (autosaveProbeRunning)
        {
            Log.Info("[SyncHarness] autosaveprobe: failed reason=the previous run is still going");
            return;
        }

        StartCoroutine(AutosaveProbeLoop(Mathf.Clamp(seconds, 1f, 600f)));
    }

    private IEnumerator AutosaveProbeLoop(float seconds)
    {
        autosaveProbeRunning = true;
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            LogAutosaveProbe();
            yield return new WaitForSecondsRealtime(0.25f);
        }

        LogAutosaveProbe();
        Log.Info("[SyncHarness] autosaveprobe: done");
        autosaveProbeRunning = false;
    }

    private static void LogAutosaveProbe()
    {
        AutosaveIndicator indicator = AutosaveIndicator.Instance;
        bool exists = indicator;
        string alpha = exists ? indicator.Alpha.ToString("F2", CultureInfo.InvariantCulture) : "0.00";
        string since = exists && indicator.IsShowing ? indicator.SecondsSinceShown.ToString("F2", CultureInfo.InvariantCulture) : "none";
        Vector2 position = exists ? indicator.ScreenPosition : Vector2.zero;
        string text = exists ? indicator.Text.Replace(' ', '_') : "none";
        Log.Info($"[SyncHarness] autosaveprobe: exists={exists} count={AutosaveIndicator.ShownCount} last={AutosaveIndicator.LastNumber} showing={exists && indicator.IsShowing} alpha={alpha} since={since} text={text} x={position.x.ToString("F0", CultureInfo.InvariantCulture)} y={position.y.ToString("F0", CultureInfo.InvariantCulture)} screenHeight={Screen.height}");
    }
}
#endif
