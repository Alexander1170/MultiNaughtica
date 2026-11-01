#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "story_infection" (a harness script that is not part of this repository, script story_infection.ps1): the infection of the team (Infection_Progress1-5), the self scan (SelfScan1-5), the stage that a
// joining player is brought to (InfectionStage / InfectionInitialSyncProcessor), the reveal and the cure, for two players and two late joiners. Debug builds only. The commands are registered by
// HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" (at most two arguments, all lower case) and log one
// "[SyncHarness] <name>: key=value ..." line per fact (no spaces inside a value). Every number the script parses is written with the invariant culture. None of the commands sends a packet or sets
// story state: the goals of a scan go through the game's own StoryGoal.Trigger -> Schedule -> Execute and the Nitrox patches on them, exactly as for a scan with the scanner tool.
//
//   sfprep                    switches the NoSurvival cheat on (Survival.UpdateHunger returns at its first test, so that the players do not starve during a run of half an hour). Client-local, cleared by every
//                             join (PlayerInitialSyncProcessor: SetGameMode(mode, None)), so the script runs it after every client start. Logs "sfprep: survivalBefore= survivalAfter= mode= food= water="
//   sfstate                   ONE line "sfstate: ready= sync= who= t= amount= revealed= visual= applied= kw= shader= sick= done= log= sched= remotes= enc=" (see SfPayload)
//   sfwatch [seconds]         the same line as "sfwatch: ..." whenever it changes (checked every 0.2 s, the time and the schedule do not count as a change) and at least every 10 s, for that long (default 30)
//   sfscan self               the self scan: PDAScanner.scanTarget.Initialize(Player.main.gameObject) (what PDAScanner.UpdateTarget(8, true) does for an alt-held scanner) and PDAScanner.Scan() every
//                             frame until it returns Infected / NotInfected (1.7 s of Time.deltaTime for a player target), which has already triggered the PDA goal SelfScan1-5 chosen from the amount
//                             of the TARGET's InfectedMixin. Logs "sfscan: begin ..." / "done ..." / "after ..." (or "failed reason=..." / "timeout ...")
//   sfscan remote <name>      the same with the body of a remote player (RemotePlayer.Body: TechTag Player, an InfectedMixin with the raw amount from the stats packets): ST14d, what the game does when the
//                             scanner is aimed at the other player
public partial class HarnessConsoleCommands
{
    private Coroutine sfWatchRoutine;
    private Coroutine sfScanRoutine;

    private static string SfF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    // The goal that PDAScanner.Scan() triggers for a player target, from the infection amount of the target (vanilla IL_02ec-IL_0358: strictly above 0.8, 0.6, 0.4, 0.2, 0, else SelfScan5)
    private static string SfGoalFor(float amount)
    {
        if (amount > 0.8f)
        {
            return "SelfScan4";
        }

        if (amount > 0.6f)
        {
            return "SelfScan3b";
        }

        if (amount > 0.4f)
        {
            return "SelfScan3";
        }

        if (amount > 0.2f)
        {
            return "SelfScan2";
        }

        if (amount > 0f)
        {
            return "SelfScan1";
        }

        return "SelfScan5";
    }

    // The goals that belong to the infection story: the five steps, the self scans and the advice that comes with the first step
    private static bool SfIsKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return key.StartsWith("Infection_Progress", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("SelfScan", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "AdviseSelfScan", StringComparison.OrdinalIgnoreCase);
    }

    // The infection keys among these, sorted, comma separated, "none" when there are none
    private static string SfList(IEnumerable<string> keys)
    {
        List<string> picked = new List<string>();
        if (keys != null)
        {
            foreach (string key in keys)
            {
                if (SfIsKey(key))
                {
                    picked.Add(key);
                }
            }
        }

        if (picked.Count == 0)
        {
            return "none";
        }

        picked.Sort(StringComparer.Ordinal);
        return string.Join(",", picked.ToArray());
    }

    private static string SfLocalName()
    {
        try
        {
            string name = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>().PlayerName;
            return string.IsNullOrEmpty(name) ? "unknown" : name.Replace(' ', '_');
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    // The scheduled infection goals as key@seconds-from-now, "none" when there are none
    private static string SfSchedule()
    {
        Story.StoryGoalScheduler scheduler = Story.StoryGoalScheduler.main;
        if (!scheduler || scheduler.schedule == null)
        {
            return "none";
        }

        float now = DayNightCycle.main ? DayNightCycle.main.timePassedAsFloat : 0f;
        List<string> items = new List<string>();
        foreach (Story.ScheduledGoal goal in scheduler.schedule)
        {
            if (goal != null && SfIsKey(goal.goalKey))
            {
                items.Add($"{goal.goalKey}@{(goal.timeExecute - now).ToString("F0", CultureInfo.InvariantCulture)}");
            }
        }

        return items.Count > 0 ? string.Join(",", items.ToArray()) : "none";
    }

    // Every remote player as name:raw:applied, raw = the amount the stats packets put into the body's InfectedMixin, applied = the amount that was last written into its hands material
    // (the hands of a partner show the RAW amount, ST14e); name:nobody when the player has no body here; "none" when there is no remote player
    private static string SfRemotes()
    {
        List<string> items = new List<string>();
        foreach (NitroxClient.GameLogic.RemotePlayer remote in Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.PlayerManager>().GetAll())
        {
            if (remote == null)
            {
                continue;
            }

            string name = string.IsNullOrEmpty(remote.PlayerName) ? "unknown" : remote.PlayerName.Replace(' ', '_');
            if (!remote.Body || !remote.InfectedMixin)
            {
                items.Add($"{name}:nobody");
                continue;
            }

            items.Add($"{name}:{SfF(remote.InfectedMixin.GetInfectedAmount())}:{SfF(remote.InfectedMixin.GetPrevInfectedAmount())}");
        }

        return items.Count > 0 ? string.Join(",", items.ToArray()) : "none";
    }

    // The first material of the hands renderers of the local player: whether the infection keyword is on and the value of _InfectionAmount (both "n/a" until the mixin has written a changed amount once)
    private static void SfShader(InfectedMixin mixin, out string keyword, out string shader)
    {
        keyword = "n/a";
        shader = "n/a";
        try
        {
            if (mixin.materials != null && mixin.materials.Count > 0 && mixin.materials[0])
            {
                Material material = mixin.materials[0];
                keyword = material.IsKeywordEnabled(mixin.shaderKeyWord).ToString();
                shader = SfF(material.GetFloat(Shader.PropertyToID("_InfectionAmount")));
            }
        }
        catch (Exception)
        {
            keyword = "n/a";
            shader = "n/a";
        }
    }

    // A part of the payload that is computed on its own: when it throws, its value is "error-<exception type>" and the rest of the line is still written
    private static string SfSafe(Func<string> value)
    {
        try
        {
            return value();
        }
        catch (Exception exception)
        {
            return $"error-{exception.GetType().Name}";
        }
    }

    private static string SfEncyclopedia()
    {
        return PDAEncyclopedia.ContainsEntry("Infection").ToString();
    }

    // The payload of sfstate / sfwatch: key=value pairs without spaces inside a value. With forSignature the time and the schedule (which change every second) are left out, so that the watch only writes
    // a line when something that matters changed. Never throws. When a part that the script judges (amount, revealed, visual, applied, sick, done, log) fails, the line ends with "error=<type>" and is
    // incomplete: the script does not read such a line as a state (it is no evidence of anything).
    private static string SfPayload(bool forSignature)
    {
        List<string> parts = new List<string>();
        try
        {
            bool sync = false;
            try
            {
                sync = Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted;
            }
            catch (Exception)
            {
                sync = false;
            }

            Player player = Player.main;
            Story.StoryGoalManager goals = Story.StoryGoalManager.main;
            if (!player || !player.infectedMixin || !goals)
            {
                parts.Add("ready=False");
                parts.Add($"sync={sync}");
                parts.Add($"who={SfLocalName()}");
                parts.Add("reason=no-player");
                return string.Join(" ", parts.ToArray());
            }

            InfectedMixin mixin = player.infectedMixin;
            parts.Add("ready=True");
            parts.Add($"sync={sync}");
            parts.Add($"who={SfLocalName()}");
            if (!forSignature)
            {
                float now = DayNightCycle.main ? DayNightCycle.main.timePassedAsFloat : -1f;
                parts.Add($"t={now.ToString("F1", CultureInfo.InvariantCulture)}");
            }

            parts.Add($"amount={SfF(mixin.GetInfectedAmount())}");
            parts.Add($"revealed={player.infectionRevealed}");
            parts.Add($"visual={SfF(player.GetInfectionAmount())}");
            parts.Add($"applied={SfF(mixin.GetPrevInfectedAmount())}");
            SfShader(mixin, out string keyword, out string shader);
            parts.Add($"kw={keyword}");
            parts.Add($"shader={shader}");
            StoryGoalCustomEventHandler events = StoryGoalCustomEventHandler.main;
            bool sick = events && events.IsInvoking("TriggerSickAnim");
            parts.Add($"sick={sick}");
            parts.Add($"done={SfList(goals.completedGoals)}");
            parts.Add($"log={SfList(PDALog.entries.Keys)}");
            // The three that follow are not what the state judges read: a failure in one of them (SfRemotes walks the remote players, SfSchedule the scheduler) is named inside its own value and
            // never cuts the line short, so that a fault of this command cannot look like a wrong value of the game (a line with "error=" is ignored by the script)
            if (!forSignature)
            {
                parts.Add($"sched={SfSafe(SfSchedule)}");
            }

            parts.Add($"remotes={SfSafe(SfRemotes)}");
            parts.Add($"enc={SfSafe(SfEncyclopedia)}");
        }
        catch (Exception exception)
        {
            parts.Add($"error={exception.GetType().Name}");
        }

        return string.Join(" ", parts.ToArray());
    }

    // ---- sfprep / sfstate / sfwatch

    private void LiveCommand_sfprep(NotificationCenter.Notification n)
    {
        try
        {
            bool survivalBefore = GameModeUtils.RequiresSurvival();
            // NoSurvival: GameModeUtils.RequiresSurvival() becomes false and Survival.UpdateHunger (every 10 s) returns at its first test, so no hunger or thirst damage; oxygen and the story are untouched
            GameModeUtils.ActivateCheat(GameModeOption.NoSurvival);
            Survival survival = Player.main ? Player.main.GetComponent<Survival>() : null;
            string vitals = survival ? $"food={SfF(survival.food)} water={SfF(survival.water)}" : "food=n/a water=n/a";
            Log.Info($"[SyncHarness] sfprep: survivalBefore={survivalBefore} survivalAfter={GameModeUtils.RequiresSurvival()} mode={GameModeUtils.currentEffectiveMode} {vitals}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sfprep: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_sfstate(NotificationCenter.Notification n)
    {
        try
        {
            Log.Info($"[SyncHarness] sfstate: {SfPayload(false)}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sfstate: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_sfwatch(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 30f);
            if (sfWatchRoutine != null)
            {
                StopCoroutine(sfWatchRoutine);
                sfWatchRoutine = null;
            }

            sfWatchRoutine = StartCoroutine(SfWatchLoop(seconds));
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sfwatch: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator SfWatchLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        string lastSignature = "";
        float lastLogged = -100f;
        Log.Info($"[SyncHarness] sfwatch: begin seconds={SfF(seconds)}");
        while (Time.realtimeSinceStartup < end)
        {
            string signature = SfPayload(true);
            if (signature != lastSignature || Time.realtimeSinceStartup - lastLogged >= 10f)
            {
                lastSignature = signature;
                lastLogged = Time.realtimeSinceStartup;
                Log.Info($"[SyncHarness] sfwatch: {SfPayload(false)}");
            }

            yield return new WaitForSecondsRealtime(0.2f);
        }

        Log.Info("[SyncHarness] sfwatch: end");
    }

    // ---- sfscan

    private void LiveCommand_sfscan(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, "self");
            string name = LiveArgString(n, 1, "");
            if (sfScanRoutine != null)
            {
                StopCoroutine(sfScanRoutine);
                sfScanRoutine = null;
            }

            sfScanRoutine = StartCoroutine(SfScanLoop(mode, name));
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sfscan: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // Everything the scan needs, read before it starts (no yield, so every risky call is inside the try/catch): the object to scan (self = the local player, which is what UpdateTarget(8, true) hands to
    // ScanTarget.Initialize; remote = the body of the remote player with that name), the infection amount of that object's InfectedMixin, the goal that follows from it, whether that goal is complete
    // and whether the player carries a scanner
    private sealed class SfScanPlan
    {
        public GameObject Target;
        public string TargetName = "none";
        public string Failure = "unknown";
        public bool HasMixin;
        public float Amount = -1f;
        public string Goal = "unknown";
        public bool GoalDone;
        public bool Scanner;
    }

    private static SfScanPlan SfPrepareScan(string mode, string name)
    {
        SfScanPlan plan = new SfScanPlan();
        try
        {
            if (string.Equals(mode, "remote", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(name))
                {
                    plan.Failure = "remote-needs-a-name";
                    return plan;
                }

                foreach (NitroxClient.GameLogic.RemotePlayer remote in Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.PlayerManager>().GetAll())
                {
                    if (remote != null && string.Equals(remote.PlayerName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!remote.Body)
                        {
                            plan.Failure = "remote-has-no-body";
                            return plan;
                        }

                        plan.Target = remote.Body;
                        plan.TargetName = remote.PlayerName.Replace(' ', '_');
                        break;
                    }
                }

                if (!plan.Target)
                {
                    plan.Failure = "remote-not-found";
                    return plan;
                }
            }
            else
            {
                if (!Player.main)
                {
                    plan.Failure = "no-player";
                    return plan;
                }

                plan.Target = Player.main.gameObject;
                plan.TargetName = SfLocalName();
            }

            InfectedMixin mixin = plan.Target.GetComponent<InfectedMixin>();
            plan.HasMixin = mixin;
            plan.Amount = mixin ? mixin.GetInfectedAmount() : -1f;
            plan.Goal = SfGoalFor(plan.Amount);
            plan.GoalDone = Story.StoryGoalManager.main != null && Story.StoryGoalManager.main.completedGoals.Contains(plan.Goal);
            plan.Scanner = Inventory.main && Inventory.main.container != null && Inventory.main.container.GetCount(TechType.Scanner) > 0;
        }
        catch (Exception exception)
        {
            plan.Target = null;
            plan.Failure = $"error-{exception.GetType().Name}";
        }

        return plan;
    }

    // PDAScanner.scanTarget.Initialize(target): true when the game accepted it as a player target (TechType.Player, which makes ScanTarget.isPlayer true)
    private static bool SfInitTarget(GameObject target)
    {
        try
        {
            PDAScanner.scanTarget.Initialize(target);
            return PDAScanner.scanTarget.techType == TechType.Player;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sfscan: error in ScanTarget.Initialize {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static PDAScanner.Result SfScanOnce()
    {
        try
        {
            return PDAScanner.Scan();
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sfscan: error in PDAScanner.Scan {exception.GetType().Name}: {exception.Message}");
            return PDAScanner.Result.None;
        }
    }

    // What came of the goal 1.5 s after the scan: complete, in the PDA log, still scheduled
    private static string SfGoalOutcome(string goal)
    {
        try
        {
            bool completed = Story.StoryGoalManager.main != null && Story.StoryGoalManager.main.completedGoals.Contains(goal);
            bool inLog = PDALog.entries.ContainsKey(goal);
            bool scheduled = Story.StoryGoalScheduler.main != null && Story.StoryGoalScheduler.main.schedule.Exists(entry => entry.goalKey == goal);
            return $"completed={completed} pda={inLog} scheduled={scheduled}";
        }
        catch (Exception exception)
        {
            return $"error={exception.GetType().Name}";
        }
    }

    private IEnumerator SfScanLoop(string mode, string name)
    {
        SfScanPlan plan = SfPrepareScan(mode, name);
        GameObject target = plan.Target;
        string targetName = plan.TargetName;
        string goal = plan.Goal;
        if (!target)
        {
            Log.Warn($"[SyncHarness] sfscan: failed reason={plan.Failure} target={mode} name={name}");
            yield break;
        }

        if (!SfInitTarget(target))
        {
            Log.Warn($"[SyncHarness] sfscan: failed reason=target-not-accepted tech={PDAScanner.scanTarget.techType} target={mode} name={targetName} amount={SfF(plan.Amount)}");
            yield break;
        }

        Log.Info($"[SyncHarness] sfscan: begin target={mode} name={targetName} amount={SfF(plan.Amount)} goal={goal} goalDone={plan.GoalDone} scanner={plan.Scanner} mixin={plan.HasMixin}");

        float startReal = Time.realtimeSinceStartup;
        int frames = 0;
        int retargets = 0;
        float progress = 0f;
        bool finished = false;
        bool lost = false;
        PDAScanner.Result result = PDAScanner.Result.None;
        // ScanTarget.Initialize stores the ROOT that Targeting.GetRoot found for the candidate (the object with the TechTag), which need not be the candidate itself: the target is "put back" only
        // when something else replaced that root
        GameObject root = PDAScanner.scanTarget.gameObject;
        while (Time.realtimeSinceStartup - startReal < 20f)
        {
            if (!target)
            {
                lost = true;
                break;
            }

            // GUIHand re-targets the scanner every frame while a Scanner is held: put the target back and keep the progress
            if (PDAScanner.scanTarget.gameObject != root)
            {
                retargets++;
                SfInitTarget(target);
                PDAScanner.scanTarget.progress = progress;
                root = PDAScanner.scanTarget.gameObject;
            }

            result = SfScanOnce();
            frames++;
            progress = PDAScanner.scanTarget.progress;
            if (result == PDAScanner.Result.Infected || result == PDAScanner.Result.NotInfected)
            {
                finished = true;
                break;
            }

            if (result == PDAScanner.Result.None)
            {
                break;
            }

            yield return null;
        }

        float seconds = Time.realtimeSinceStartup - startReal;
        if (finished)
        {
            Log.Info($"[SyncHarness] sfscan: done target={mode} name={targetName} result={result} frames={frames} seconds={SfF(seconds)} retargets={retargets}");
        }
        else if (lost)
        {
            Log.Warn($"[SyncHarness] sfscan: failed reason=target-destroyed target={mode} name={targetName} frames={frames} seconds={SfF(seconds)} retargets={retargets}");
            yield break;
        }
        else if (result == PDAScanner.Result.None)
        {
            Log.Warn($"[SyncHarness] sfscan: failed reason=scan-returned-none target={mode} name={targetName} frames={frames} seconds={SfF(seconds)} retargets={retargets}");
            yield break;
        }
        else
        {
            Log.Warn($"[SyncHarness] sfscan: timeout target={mode} name={targetName} result={result} frames={frames} seconds={SfF(seconds)} retargets={retargets} progress={SfF(progress)}");
            yield break;
        }

        yield return new WaitForSecondsRealtime(1.5f);
        Log.Info($"[SyncHarness] sfscan: after target={mode} name={targetName} goal={goal} {SfGoalOutcome(goal)}");
    }
}
#endif
