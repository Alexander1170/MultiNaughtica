#if DEBUG
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Pictures of a client for the human who reads a live run (a harness script that is not part of this repository, a harness script that is not part of this repository). Debug builds only.
// Registered by HarnessConsoleCommands.LiveBatch.cs, run with "/console <player> liveshot ..." or "/consoleall liveshot ...".
//
//   liveshot now [tag]                   a picture at the end of the next frame
//   liveshot saving [tag]                armed first, then taken at the end of the first frame in which the autosave indicator of this client (AutosaveIndicator) is at full alpha and has
//                                        been showing for 0.4 s, so that "Saving..." is on the picture whatever the script's timing; fails after 25 s when it never shows
// (/console takes a player, the command and TWO words after it, /consoleall the command and two: that is why the time out is fixed and not an argument)
//
// The picture is the whole window as the game renders it (Texture2D.ReadPixels at WaitForEndOfFrame, which is also what the game's own screenshot does; the window of a client behind another one
// still renders, the game runs in the background). It is written as shot-<player>-<tag>.png into the client's log folder, and one line says where and what the indicator looked like:
//   [SyncHarness] liveshot: saved file=<name> bytes=<n> size=<w>x<h> alpha=.. since=.. text=..      or      [SyncHarness] liveshot: failed reason=<text>
// The script copies the file into the run folder. A window that is minimized has no size and is reported as failed, not written as a black file.
public partial class HarnessConsoleCommands
{
    private const float SAVING_SHOT_TIMEOUT_SECONDS = 25f;

    private bool liveShotBusy;

    private void LiveCommand_liveshot(NotificationCenter.Notification n)
    {
        try
        {
            bool saving = LiveArgString(n, 0, "now") == "saving";
            float seconds = saving ? SAVING_SHOT_TIMEOUT_SECONDS : 0f;
            string tag = Regex.Replace(LiveArgString(n, 1, "shot"), "[^A-Za-z0-9_-]", string.Empty);
            if (tag.Length == 0)
            {
                tag = "shot";
            }

            if (liveShotBusy)
            {
                Log.Info("[SyncHarness] liveshot: failed reason=the previous picture is still being taken");
                return;
            }

            StartCoroutine(LiveShotRoutine(saving, seconds, tag));
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] liveshot: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator LiveShotRoutine(bool saving, float seconds, string tag)
    {
        liveShotBusy = true;
        try
        {
            Log.Info($"[SyncHarness] liveshot: armed saving={saving} tag={tag}");
            float deadline = Time.realtimeSinceStartup + seconds;
            while (saving && !LiveShotIndicatorIsClear())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Log.Info("[SyncHarness] liveshot: failed reason=the autosave indicator never showed at full alpha");
                    yield break;
                }

                yield return null;
            }

            yield return new WaitForEndOfFrame();
            AutosaveIndicator indicator = AutosaveIndicator.Instance;
            string seen = indicator
                ? $"alpha={PdaNumber(indicator.Alpha)} since={PdaNumber(indicator.SecondsSinceShown)} text={indicator.Text.Replace(' ', '_')}"
                : "alpha=0.00 since=none text=none";
            int width = Screen.width;
            int height = Screen.height;
            Texture2D texture = null;
            try
            {
                if (width < 64 || height < 64)
                {
                    throw new InvalidOperationException($"the window is {width}x{height}");
                }

                string name = $"shot-{Nitrox.Model.Core.NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName.Replace(' ', '_')}-{tag}.png";
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                byte[] png = texture.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(Log.LogDirectory, name), png);
                Log.Info($"[SyncHarness] liveshot: saved file={name} bytes={png.Length} size={width}x{height} {seen}");
            }
            catch (Exception exception)
            {
                Log.Info($"[SyncHarness] liveshot: failed reason={exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                if (texture)
                {
                    Destroy(texture);
                }
            }
        }
        finally
        {
            liveShotBusy = false;
        }
    }

    private static bool LiveShotIndicatorIsClear()
    {
        AutosaveIndicator indicator = AutosaveIndicator.Instance;
        return indicator && indicator.IsShowing && indicator.Alpha >= 0.99f && indicator.SecondsSinceShown >= 0.4f;
    }
}
#endif
