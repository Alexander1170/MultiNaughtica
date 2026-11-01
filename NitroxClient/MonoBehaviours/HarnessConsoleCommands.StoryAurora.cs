#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "story_aurora" (a harness script that is not part of this repository, script story_aurora.ps1): the Aurora countdown, the four warnings and the explosion seen by two players (and a late joiner, a third player,
// a server restart, a restore and a second explosion). Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the
// server's "/console <player> <command> [arg]" or "/consoleall <command>" and log "[SyncHarness] <name>: key=value ..." lines that the script reads (every number with the invariant culture, no value contains a blank).
//
//   saprep               switches the NoSurvival cheat on (Survival.UpdateHunger returns at once, so that nobody starves during a run of half an hour; client-local, cleared by every join, so the script runs it
//                        again after every client start) and logs the game mode facts
//   saprobe              ONE line with the Aurora state of this client: game time, the warning / countdown times and the three warning thresholds, goals and PDA entries of the five Aurora keys, the
//                        scheduled warning goals, the exploded model and the effects (see SaReadState), the radiation field, the distance of the local player to the exploder (kill zone of the
//                        explosion: 500 m radius damage), the time the server sent (TimeManager)
//   sabases              the AR3 scan: every Base within 520 m of the exploder with its cells and the health of its LiveMixins (STORY.md row 9: the explosion damages base cells on each client separately)
//   sawatch [seconds|off] a per-frame watcher (a coroutine on SaWatchHost, a root object of its own with DontDestroyOnLoad and SceneCleanerPreserve, the recipe of RocketWatchHost) that logs one line at
//                        every change of the Aurora state and one every 10 s, each with the game time and a field "changed=" naming what changed, so that the script can compare the two clients to
//                        the hundredth of a second (both clients run on one PC, one clock)
//
// Everything is read from the game's own fields (the game assemblies are publicized), nothing is patched and nothing is changed: a failing probe says so in its line and never throws out of a Unity callback.
public partial class HarnessConsoleCommands
{
    private const float SA_KILL_RADIUS = 475f;          // RadiusDamage(2000, pos, 500) deals 2000 * (500 - d) / 500: a player with 100 hp dies within 475 m
    private const float SA_BASE_RADIUS = 520f;
    private const float SA_HEARTBEAT_SECONDS = 10f;

    // the order of the fields of a watch sample: SaReadState fills Values in this order, the signature of a sample is the join of the values
    private static readonly string[] SaFieldNames = { "cw", "goals", "pda", "sched", "exploded", "swapped", "swap", "fx0", "fx1", "fxp", "shake", "jumps", "rst", "fixed", "leaking" };
    private const int SA_JUMPS_INDEX = 11;

    private static string[] saKeysCache;

    private sealed class SaState
    {
        public float Tp = -1f;
        public float Radius = -1f;
        public readonly string[] Values = new string[SaFieldNames.Length];
        public bool Error;
        public string ErrorText = "";
    }

    private static string SaF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string SaF1(float value)
    {
        return value.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string SaF0(float value)
    {
        return value.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string SaB(bool value)
    {
        return value ? "1" : "0";
    }

    private static string SaVec(Vector3 value)
    {
        return $"{SaF1(value.x)},{SaF1(value.y)},{SaF1(value.z)}";
    }

    // The five keys of the Aurora in the order of AuroraEventData.GoalNames (Story_AuroraWarning1..4, Story_AuroraExplosion); the order is the bit order of the goals= and pda= fields
    private static string[] SaKeys()
    {
        if (saKeysCache != null)
        {
            return saKeysCache;
        }

        List<string> keys = new List<string>();
        try
        {
            foreach (string key in Nitrox.Model.Subnautica.DataStructures.GameLogic.AuroraEventData.GoalNames)
            {
                keys.Add(key);
            }
        }
        catch (Exception)
        {
            keys.Clear();
        }

        if (keys.Count == 0)
        {
            keys.AddRange(new[] { "Story_AuroraWarning1", "Story_AuroraWarning2", "Story_AuroraWarning3", "Story_AuroraWarning4", "Story_AuroraExplosion" });
        }

        saKeysCache = keys.ToArray();
        return saKeysCache;
    }

    private static string SaMode()
    {
        // a flags enum prints "A, B": no blanks in a value that the script parses
        return GameModeUtils.currentEffectiveMode.ToString().Replace(", ", "+").Replace(' ', '_');
    }

    private static string SaVitals()
    {
        Survival survival = Player.main ? Player.main.GetComponent<Survival>() : null;
        return survival ? $"food={SaF(survival.food)} water={SaF(survival.water)}" : "food=n/a water=n/a";
    }

    private static void SaCountActive(GameObject[] objects, out int active, out int total)
    {
        active = 0;
        total = 0;
        if (objects == null)
        {
            return;
        }

        foreach (GameObject item in objects)
        {
            if (!item)
            {
                continue;
            }

            total++;
            if (item.activeSelf)
            {
                active++;
            }
        }
    }

    // One effect of the exploder's VFXController as two characters: (the effect object exists)(its particle system plays), "n" when it cannot be read
    private static string SaFx(CrashedShipExploder exploder, int index)
    {
        try
        {
            VFXController control = exploder ? exploder.fxControl : null;
            if (!control || control.emitters == null || index < 0 || index >= control.emitters.Length)
            {
                return "n";
            }

            VFXController.VFXEmitter emitter = control.emitters[index];
            if (emitter == null)
            {
                return "n";
            }

            bool exists = emitter.instanceGO;
            bool playing = emitter.fxPS && emitter.fxPS.isPlaying;
            return $"{SaB(exists)}{SaB(playing)}";
        }
        catch (Exception)
        {
            return "n";
        }
    }

    // The camera shake that CrashedShipExploder.ShakePlayerCamera asks for: MainCameraControl.ShakeCamera(4, 8, mode 3, 1.2) leaves shakeAmount 4 and totalShaketime = the first remainingShakeTime 8
    // (the rumble after the explosion uses 0.45 and mode 4), so this fingerprint is the explosion's shake and nothing else. Sets raw to remaining/total/amount
    private static bool SaShake(out string raw)
    {
        raw = "n/a";
        MainCameraControl camera = MainCameraControl.main;
        if (!camera)
        {
            return false;
        }

        raw = $"{SaF(camera.remainingShakeTime)}/{SaF(camera.totalShaketime)}/{SaF(camera.shakeAmount)}";
        return camera.remainingShakeTime > 0f && Mathf.Abs(camera.totalShaketime - 8f) < 0.05f && Mathf.Abs(camera.shakeAmount - 4f) < 0.05f;
    }

    private static string SaGoalBits(bool pda)
    {
        string[] keys = SaKeys();
        char[] bits = new char[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            bool on;
            if (pda)
            {
                on = PDALog.entries != null && PDALog.entries.ContainsKey(keys[i]);
            }
            else
            {
                Story.StoryGoalManager goals = Story.StoryGoalManager.main;
                on = goals && goals.completedGoals != null && goals.completedGoals.Contains(keys[i]);
            }

            bits[i] = on ? '1' : '0';
        }

        return new string(bits);
    }

    // The scheduled entries of the five keys, "key@timeExecute" joined with commas, "none" when there are none
    private static string SaScheduled()
    {
        Story.StoryGoalScheduler scheduler = Story.StoryGoalScheduler.main;
        if (!scheduler || scheduler.schedule == null)
        {
            return "n/a";
        }

        string[] keys = SaKeys();
        List<string> entries = new List<string>();
        foreach (Story.ScheduledGoal goal in scheduler.schedule)
        {
            if (goal != null && Array.IndexOf(keys, goal.goalKey) >= 0)
            {
                entries.Add($"{goal.goalKey}@{SaF1(goal.timeExecute)}");
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return entries.Count > 0 ? string.Join(",", entries.ToArray()) : "none";
    }

    // Everything the watcher compares, once per frame. Never throws: a failure is returned as Error with the exception type and message
    private static SaState SaReadState()
    {
        SaState state = new SaState();
        try
        {
            CrashedShipExploder exploder = CrashedShipExploder.main;
            DayNightCycle clock = DayNightCycle.main;
            LeakingRadiation radiation = LeakingRadiation.main;
            state.Tp = clock ? clock.timePassedAsFloat : -1f;
            state.Radius = radiation ? radiation.currentRadius : -1f;

            state.Values[0] = exploder ? $"{SaF1(exploder.timeToStartCountdown)}/{SaF1(exploder.timeToStartWarning)}" : "none";
            state.Values[1] = SaGoalBits(false);
            state.Values[2] = SaGoalBits(true);
            state.Values[3] = SaScheduled();
            state.Values[4] = SaB(exploder && exploder.IsExploded());

            int enableActive = 0;
            int enableTotal = 0;
            int disableActive = 0;
            int disableTotal = 0;
            if (exploder)
            {
                SaCountActive(exploder.enableOnExplosion, out enableActive, out enableTotal);
                SaCountActive(exploder.disableOnExplosion, out disableActive, out disableTotal);
            }

            bool swapped = enableTotal + disableTotal > 0 && enableActive == enableTotal && disableActive == 0;
            state.Values[5] = SaB(swapped);
            state.Values[6] = $"{enableActive}/{enableTotal}+{disableActive}/{disableTotal}";
            state.Values[7] = SaFx(exploder, 0);
            state.Values[8] = SaFx(exploder, 1);
            // fxp: the player's shock wave plays (VFXExplosionPlayerFX.playing). PlayExplosionFX spawns the instance at the countdown time + 25 s, it starts playing shipDistance / 1000 s later and stops after its duration
            // (vanilla IL), but nothing ever destroys the instance after the explosion (only OnProtoDeserialize does, at a join): "the instance exists" would stay 1 for good and never flip again with a second explosion
            state.Values[9] = SaB(exploder && exploder.playerFXcontrol && exploder.playerFXcontrol.playing);
            state.Values[10] = SaB(SaShake(out _));
            state.Values[SA_JUMPS_INDEX] = "0";
            state.Values[12] = SaB(radiation && radiation.currentRadius > radiation.kStartRadius + 0.05f);
            state.Values[13] = SaB(radiation && radiation.radiationFixed);
            state.Values[14] = radiation ? $"{radiation.GetNumLeaks()}/{radiation.leaks.Count}" : "none";
        }
        catch (Exception exception)
        {
            state.Error = true;
            state.ErrorText = $"{exception.GetType().Name}: {exception.Message}".Replace(' ', '_');
            for (int i = 0; i < state.Values.Length; i++)
            {
                state.Values[i] ??= "err";
            }
        }

        return state;
    }

    // ---- saprep

    private void LiveCommand_saprep(NotificationCenter.Notification n)
    {
        try
        {
            bool survivalBefore = GameModeUtils.RequiresSurvival();
            // NoSurvival: GameModeUtils.RequiresSurvival() becomes false and Survival.UpdateHunger (every 10 s) returns at its first test; oxygen is another flag (NoOxygen) and is untouched
            GameModeUtils.ActivateCheat(GameModeOption.NoSurvival);
            Log.Info($"[SyncHarness] saprep: survivalBefore={survivalBefore} survivalAfter={GameModeUtils.RequiresSurvival()} mode={SaMode()} {SaVitals()}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] saprep: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- saprobe

    private void LiveCommand_saprobe(NotificationCenter.Notification n)
    {
        try
        {
            Log.Info(SaProbeLine());
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] saprobe: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string SaProbeLine()
    {
        CrashedShipExploder exploder = CrashedShipExploder.main;
        LeakingRadiation radiation = LeakingRadiation.main;
        DayNightCycle clock = DayNightCycle.main;
        Player player = Player.main;
        SaState state = SaReadState();
        float tp = clock ? clock.timePassedAsFloat : -1f;
        float tW = exploder ? exploder.timeToStartWarning : 0f;
        float tC = exploder ? exploder.timeToStartCountdown : 0f;

        List<string> parts = new List<string>();
        parts.Add($"t={SaF(tp)}");
        parts.Add($"tW={SaF1(tW)}");
        parts.Add($"tC={SaF1(tC)}");
        // the thresholds of AuroraWarnings.Update: Lerp(tW, tC, 0.2 / 0.5 / 0.8) for warning 1 / 2 / 3, tC itself for warning 4
        parts.Add($"w1={SaF1(Mathf.Lerp(tW, tC, 0.2f))}");
        parts.Add($"w2={SaF1(Mathf.Lerp(tW, tC, 0.5f))}");
        parts.Add($"w3={SaF1(Mathf.Lerp(tW, tC, 0.8f))}");
        parts.Add($"tToC={SaF1(tC - tp)}");
        parts.Add($"exploded={state.Values[4]}");
        parts.Add($"goals={state.Values[1]}");
        parts.Add($"pda={state.Values[2]}");
        parts.Add($"sched={state.Values[3]}");
        parts.Add($"swapped={state.Values[5]}");
        parts.Add($"swap={state.Values[6]}");
        parts.Add($"ext={(exploder && exploder.explodedExterior ? SaB(exploder.explodedExterior.activeSelf) : "n/a")}");
        parts.Add($"fx0={state.Values[7]}");
        parts.Add($"fx1={state.Values[8]}");
        parts.Add($"fxp={state.Values[9]}");
        parts.Add($"shake={state.Values[10]}");
        SaShake(out string shakeRaw);
        parts.Add($"shakeRaw={shakeRaw}");  // remaining/total/amount of the camera shake, "n/a" without a camera
        parts.Add($"radius={(radiation ? SaF(radiation.currentRadius) : "n/a")}");
        parts.Add($"dmgR={(radiation && radiation.damagePlayerInRadius ? SaF(radiation.damagePlayerInRadius.damageRadius) : "n/a")}");
        parts.Add($"radR={(radiation && radiation.radiatePlayerInRange ? SaF(radiation.radiatePlayerInRange.radiateRadius) : "n/a")}");
        parts.Add($"kStart={(radiation ? SaF(radiation.kStartRadius) : "n/a")}");
        parts.Add($"kGrow={(radiation ? SaF(radiation.kGrowRate) : "n/a")}");
        parts.Add($"kMax={(radiation ? SaF(radiation.kMaxRadius) : "n/a")}");
        parts.Add($"kDiss={(radiation ? SaF(radiation.kNaturalDissipation) : "n/a")}");
        parts.Add($"fixed={state.Values[13]}");
        parts.Add($"leaks={(radiation ? radiation.leaks.Count.ToString(CultureInfo.InvariantCulture) : "n/a")}");
        parts.Add($"leaking={(radiation ? radiation.GetNumLeaks().ToString(CultureInfo.InvariantCulture) : "n/a")}");

        string realElapsed = "n/a";
        string realExplosion = "n/a";
        try
        {
            NitroxClient.GameLogic.TimeManager timeManager = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.TimeManager>();
            realElapsed = SaF1((float)timeManager.RealTimeElapsed);
            realExplosion = SaF1(timeManager.AuroraRealExplosionTime);
        }
        catch (Exception)
        {
            // the time manager is only there in a multiplayer session
        }

        parts.Add($"realElapsed={realElapsed}");
        parts.Add($"realExplosion={realExplosion}");

        string auroraText = "none";
        string playerText = "none";
        string distText = "n/a";
        string killText = "n/a";
        string inRadText = "n/a";
        if (exploder)
        {
            auroraText = SaVec(exploder.transform.position);
        }

        if (player)
        {
            playerText = SaVec(player.transform.position);
        }

        if (exploder && player)
        {
            float dist = Vector3.Distance(player.transform.position, exploder.transform.position);
            distText = SaF0(dist);
            killText = SaB(dist < SA_KILL_RADIUS);
            float radius = radiation && radiation.damagePlayerInRadius ? radiation.damagePlayerInRadius.damageRadius : 0f;
            inRadText = SaB(radius > 0f && dist <= radius);
        }

        parts.Add($"aurora={auroraText}");
        parts.Add($"player={playerText}");
        parts.Add($"dist={distText}");
        parts.Add($"kill={killText}");
        parts.Add($"inRad={inRadText}");
        parts.Add($"sync={(Multiplayer.Main ? Multiplayer.Main.InitialSyncCompleted.ToString() : "n/a")}");
        parts.Add($"mode={SaMode()}");
        parts.Add($"survival={GameModeUtils.RequiresSurvival()}");
        if (state.Error)
        {
            parts.Add($"probeError={state.ErrorText}");
        }

        return $"[SyncHarness] saprobe: {string.Join(" ", parts.ToArray())}";
    }

    // ---- sabases

    private void LiveCommand_sabases(NotificationCenter.Notification n)
    {
        try
        {
            CrashedShipExploder exploder = CrashedShipExploder.main;
            Vector3 center = exploder ? exploder.transform.position : Vector3.zero;
            int within = 0;
            int all = 0;
            foreach (Base @base in FindObjectsOfType<Base>())
            {
                if (!@base)
                {
                    continue;
                }

                all++;
                float dist = Vector3.Distance(@base.transform.position, center);
                if (dist > SA_BASE_RADIUS)
                {
                    continue;
                }

                within++;
                string id = @base.TryGetNitroxId(out NitroxId baseId) ? baseId.ToString().Substring(0, 8) : "noid";
                int cells = 0;
                if (@base.cellObjects != null)
                {
                    foreach (Transform cell in @base.cellObjects)
                    {
                        cells += cell ? 1 : 0;
                    }
                }

                int mixins = 0;
                float healthSum = 0f;
                foreach (LiveMixin mixin in @base.GetComponentsInChildren<LiveMixin>(true))
                {
                    if (!mixin)
                    {
                        continue;
                    }

                    mixins++;
                    healthSum += mixin.GetHealthFraction();
                }

                string hp = mixins > 0 ? SaF(healthSum / mixins) : "n/a";
                Log.Info($"[SyncHarness] sabases: base={id} dist={SaF0(dist)} ghost={@base.isGhost} cells={cells} liveMixins={mixins} hp={hp} pos={SaVec(@base.transform.position)}");
            }

            Log.Info($"[SyncHarness] sabases: count={within} within={SaF0(SA_BASE_RADIUS)} total={all} aurora={(exploder ? SaVec(center) : "none")}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sabases: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- sawatch

    private void LiveCommand_sawatch(NotificationCenter.Notification n)
    {
        try
        {
            string first = LiveArgString(n, 0, "");
            if (string.Equals(first, "off", StringComparison.OrdinalIgnoreCase))
            {
                SaWatchHost.Stop();
                Log.Info("[SyncHarness] sawatch: stopped");
                return;
            }

            float seconds = LiveArgFloat(n, 0, 3300f);
            SaWatchHost.Begin(seconds);
            Log.Info($"[SyncHarness] sawatch: started for {SaF1(seconds)} s");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sawatch: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // The watcher: one sample per frame; a line when a field changed (changed= names them), when the game clock jumped (a time skip arrived) and every SA_HEARTBEAT_SECONDS as a sign of life (changed=hb).
    // t, the radius and the heartbeat counter are in every line but are not part of the signature. It runs on SaWatchHost (a coroutine ends with the object that runs it)
    internal static IEnumerator SaWatchLoop(float seconds)
    {
        float start = Time.realtimeSinceStartup;
        float lastLog = -100f;
        float lastErrorLog = -100f;
        string[] previous = null;
        float lastTp = float.NaN;
        int jumps = 0;
        int heartbeat = 0;
        while (Time.realtimeSinceStartup - start < seconds)
        {
            float now = Time.realtimeSinceStartup;
            SaState state = SaReadState();
            if (!float.IsNaN(lastTp) && Mathf.Abs(state.Tp - lastTp) > 5f)
            {
                jumps++;
            }

            lastTp = state.Tp;
            state.Values[SA_JUMPS_INDEX] = jumps.ToString(CultureInfo.InvariantCulture);

            string changed = "";
            if (previous == null)
            {
                changed = "start";
            }
            else
            {
                List<string> names = new List<string>();
                for (int i = 0; i < state.Values.Length; i++)
                {
                    if (!string.Equals(state.Values[i], previous[i], StringComparison.Ordinal))
                    {
                        names.Add(SaFieldNames[i]);
                    }
                }

                changed = names.Count > 0 ? string.Join(",", names.ToArray()) : "";
            }

            bool due = now - lastLog >= SA_HEARTBEAT_SECONDS;
            if (changed == "" && due)
            {
                heartbeat++;
                changed = "hb";
            }

            if (changed != "")
            {
                if (state.Error)
                {
                    // a probe that fails is reported at most once per 10 s, however often it flips
                    if (now - lastErrorLog >= SA_HEARTBEAT_SECONDS)
                    {
                        Log.Info($"[SyncHarness] sawatch: t={SaF(state.Tp)} probe error {state.ErrorText}");
                        lastErrorLog = now;
                        lastLog = now;
                    }
                }
                else
                {
                    Log.Info(SaWatchLine(state, heartbeat, changed));
                    lastLog = now;
                }
            }

            previous = state.Values;
            yield return null;
        }

        Log.Info("[SyncHarness] sawatch: done");
    }

    private static string SaWatchLine(SaState state, int heartbeat, string changed)
    {
        return $"[SyncHarness] sawatch: t={SaF(state.Tp)} cw={state.Values[0]} goals={state.Values[1]} pda={state.Values[2]} sched={state.Values[3]} exploded={state.Values[4]} swapped={state.Values[5]} swap={state.Values[6]} fx0={state.Values[7]} fx1={state.Values[8]} fxp={state.Values[9]} shake={state.Values[10]} jumps={state.Values[SA_JUMPS_INDEX]} radius={SaF(state.Radius)} rst={state.Values[12]} fixed={state.Values[13]} leaking={state.Values[14]} hb={heartbeat} changed={changed}";
    }
}

/// <summary>
///     The home of the sawatch coroutine (Debug builds, live check "story_aurora"): a root object of its own with DontDestroyOnLoad and SceneCleanerPreserve, the recipe of RocketWatchHost (the scene
///     cleaners of the ending destroy every root object without SceneCleanerPreserve). The Aurora scenario never changes scenes, but a watcher that dies with the multiplayer client object would be
///     lost at a disconnect.
/// </summary>
public sealed class SaWatchHost : MonoBehaviour
{
    private static SaWatchHost instance;
    private Coroutine routine;
    private bool quitting;

    internal static void Begin(float seconds)
    {
        if (!instance)
        {
            GameObject host = new GameObject("Aurora watch (harness)");
            DontDestroyOnLoad(host);
            host.AddComponent<SceneCleanerPreserve>();
            instance = host.AddComponent<SaWatchHost>();
        }

        instance.Restart(seconds);
    }

    internal static void Stop()
    {
        if (instance)
        {
            instance.StopWatch();
        }
    }

    private void Restart(float seconds)
    {
        StopWatch();
        routine = StartCoroutine(HarnessConsoleCommands.SaWatchLoop(seconds));
    }

    private void StopWatch()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private void OnApplicationQuit()
    {
        quitting = true;
    }

    private void OnDestroy()
    {
        // only a diagnostic: the host is meant to live until the game quits
        if (!quitting)
        {
            Log.Warn("[SyncHarness] sawatch: host destroyed");
        }
    }
}
#endif
