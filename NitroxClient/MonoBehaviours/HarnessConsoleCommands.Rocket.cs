#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nitrox.Model.DataStructures;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NitroxClient.MonoBehaviours;

// Live check "rocket" (a harness script that is not part of this repository, script rocket.ps1): the Neptune rocket built by two players, compared on both clients and the server, a late joiner, a server restart,
// the launch refused while only one player is aboard and the ending played by both. Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs
// (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" (at most two arguments, all lower case) and log one "[SyncHarness] <name>: key=value ..." line
// per fact. Every number the scripts parse is written with the invariant culture.
//
//   rocketprep                  switches the NoCost cheat on (what bobthebuilder does first, so a Survival client builds without ingredients) and the NoSurvival cheat (Survival.UpdateHunger returns at once, so that
//                               the players do not starve to death during a run of half an hour), logs the game mode facts and food / water. Both cheats are client-local and are cleared by every join
//                               (PlayerInitialSyncProcessor: SetGameMode(mode, None)), so the script runs it again after every client start and world restart
//   rocketstate [seconds]       ONE line with everything about the rocket on this client (exists, id, pos, stage, ready, preflight checks and the total the game wants, lift, whether the launch console is ready,
//                               the local player's distance to the cockpit sphere, who the launch gate (Rockets.GetPlayersNotAboard) misses, ...); with seconds it repeats that every 2 s for that long
//   rocketanchor auto|x,y,z|off holds the rocket where it is first seen (or at x,y,z): a platform that is not on terrain would fall into the sea when its first stage is done (SubConstructionComplete makes the body
//                               non-kinematic), on one client but not on the other. Needed on BOTH clients (and again after a client is started again)
//   rocketmvb                   what the Mobile Vehicle Bay's terminal does for the Neptune Launch Platform (ConstructorInput.Craft(RocketBase)), on the nearest bay within 80 m, after logging the two things that
//                               can refuse it (the bay's valid-position map at the platform's spawn point, the recipe)
//   rocketstage                 the click on the build screen's button: RocketConstructor.StartRocketConstruction() (the call that the Nitrox patch hooks), logs the stage before and after
//   rocketfinish                finishes the construction effects that are still running (what the other player's copy does when it only follows a stage), used when a stage takes too long
//   rocketcomplete <list>       completes preflight checks by number or name (RocketPreflightCheckManager.CompletePreflightCheck, what a switch calls): 0 LifeSupport, 1 TimeCapsule, 2 PrimaryComputer,
//                               3 AuxiliaryPowerUnit, 4 CommunicationsArray, 5 Hydraulics
//   rocketlift down|up          Rocket.CallElevator, what the lift's call buttons do
//   rocketboard in|in2|to x,y,z puts the local player into the cockpit sphere (in2: one metre aside) or at x,y,z (outside the rocket)
//   rocketlaunch                the click on the launch console (LaunchRocket.OnHandClick, patched by Nitrox): logs what the gate sees right before, whether the launch started, and the in-game messages
//   rocketwatch [seconds] [skip] logs the markers of the ending on this client (launch started, ending clock, cinematic, ending animation, scene, credits, the main menu that comes back afterwards); with skip it
//                               ends the credits 12 s after they began, the way the skip button does (EndCreditsManager phase End), so the run does not wait for the whole scroll.
//                               The watch does NOT run on this object: the ending loads the scenes EndCreditsSceneCleaner and Cleaner, and their SceneCleaner destroys every root object that has no SceneCleanerPreserve
//                               component, among them the DontDestroyOnLoad object "Nitrox Multiplayer Client" that this class lives on (the first live run, 2026-10-19, lost its watch lines at the scene change).
//                               So the watch runs on RocketWatchHost, a root object of its own with DontDestroyOnLoad and SceneCleanerPreserve (the way NitroxBootstrapper survives), which also logs every scene
//                               that loads or unloads as "[SyncHarness] rocketscene: ..." (rocket.ps1 reads these lines and the scene lines of NitroxDebugManager to find the credits and the return to the menu)
public partial class HarnessConsoleCommands
{
    private Coroutine rkAnchorRoutine;
    private Coroutine rkStateRoutine;
    private Coroutine rkWatchRoutine;                       // only the fallback when RocketWatchHost cannot be created
    private bool rkAnchorFixed;
    private bool rkAnchorHeld;
    private Vector3 rkAnchorPosition;
    private int rkAnchorCorrections;
    private int rkAnchorLogged;
    private float rkAnchorNextLog;

    private sealed class RkWatchSample
    {
        public string Signature = "";
        public string Line = "";
        public bool Launched;
        public bool Credits;
        public bool Error;
    }

    private static string RkF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    // The scenes that are loaded right now, names joined with + (spaces as _), for the rocketwatch / rocketscene lines; never throws
    internal static string RkSceneList()
    {
        try
        {
            int count = SceneManager.sceneCount;
            List<string> names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string name = SceneManager.GetSceneAt(i).name;
                names.Add(string.IsNullOrEmpty(name) ? "unnamed" : name.Replace(' ', '_'));
            }

            return names.Count > 0 ? string.Join("+", names.ToArray()) : "none";
        }
        catch (Exception exception)
        {
            return $"error:{exception.GetType().Name}";
        }
    }

    private static string RkVec(Vector3 value)
    {
        return $"{RkF(value.x)},{RkF(value.y)},{RkF(value.z)}";
    }

    private static bool RkTryParseVec(string text, out Vector3 value)
    {
        value = Vector3.zero;
        string[] parts = text.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        float x;
        float y;
        float z;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    // The centre of the cockpit sphere that decides who is aboard: the same object the launch gate measures against (Rockets.cs: rocketObject.FindChild("AtmosphereVolume"))
    private static GameObject RkCockpit(Rocket rocket)
    {
        GameObject found = rocket.gameObject.FindChild("AtmosphereVolume");
        if (found)
        {
            return found;
        }

        foreach (Transform child in rocket.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "AtmosphereVolume")
            {
                return child.gameObject;
            }
        }

        return null;
    }

    // The texts of the messages that the game shows in the top left corner right now (Log.InGame ends in ErrorMessage.AddMessage); read by reflection, "unavailable" when the list cannot be read
    private static string RkMessages()
    {
        try
        {
            ErrorMessage messageHud = ErrorMessage.main;
            if (!messageHud)
            {
                return "unavailable";
            }

            FieldInfo listField = typeof(ErrorMessage).GetField("messages", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            IList list = listField != null ? listField.GetValue(messageHud) as IList : null;
            if (list == null)
            {
                return "unavailable";
            }

            List<string> texts = new List<string>();
            foreach (object entry in list)
            {
                if (entry == null)
                {
                    continue;
                }

                FieldInfo textField = entry.GetType().GetField("messageText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                string text = textField != null ? textField.GetValue(entry) as string : null;
                if (!string.IsNullOrEmpty(text))
                {
                    texts.Add(text);
                }
            }

            return texts.Count > 0 ? string.Join(" | ", texts) : "none";
        }
        catch (Exception exception)
        {
            return $"error {exception.GetType().Name}";
        }
    }

    // ---- rocketprep / rocketstate

    // food and water of the local player (Survival.food / Survival.water), "n/a" when the player or the component is not there
    private static string RkVitals()
    {
        Survival survival = Player.main ? Player.main.GetComponent<Survival>() : null;
        return survival ? $"food={RkF(survival.food)} water={RkF(survival.water)}" : "food=n/a water=n/a";
    }

    private void LiveCommand_rocketprep(NotificationCenter.Notification n)
    {
        try
        {
            bool before = GameModeUtils.IsCheatActive(GameModeOption.NoCost);
            bool survivalBefore = GameModeUtils.RequiresSurvival();
            GameModeUtils.ActivateCheat(GameModeOption.NoCost);
            // NoSurvival: GameModeUtils.RequiresSurvival() becomes false and Survival.UpdateHunger (every 10 s) returns at its first test, so no hunger or thirst damage; oxygen is another flag (NoOxygen) and is untouched
            GameModeUtils.ActivateCheat(GameModeOption.NoSurvival);
            string line = $"[SyncHarness] rocketprep: nocost before={before} after={GameModeUtils.IsCheatActive(GameModeOption.NoCost)} ingredients={GameModeUtils.RequiresIngredients()} survivalBefore={survivalBefore} survivalAfter={GameModeUtils.RequiresSurvival()} mode={GameModeUtils.currentEffectiveMode} {RkVitals()}";
            Log.Info(line);
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketprep: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_rocketstate(NotificationCenter.Notification n)
    {
        float seconds = LiveArgFloat(n, 0, 0f);
        RkLogState();
        if (seconds > 0f)
        {
            if (rkStateRoutine != null)
            {
                StopCoroutine(rkStateRoutine);
            }

            rkStateRoutine = StartCoroutine(RkStateLoop(seconds));
        }
    }

    private IEnumerator RkStateLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForSecondsRealtime(2f);
            RkLogState();
        }
    }

    private static void RkLogState()
    {
        try
        {
            Log.Info(RkStateText());
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketstate: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string RkStateText()
    {
        Player player = Player.main;
        string playerPosition = player ? RkVec(player.transform.position) : "none";
        StoryGoalCustomEventHandler events = StoryGoalCustomEventHandler.main;
        string gun = events ? events.gunDisabled.ToString() : "none";
        string common = $"gun={gun} launched={LaunchRocket.launchStarted} nocost={GameModeUtils.IsCheatActive(GameModeOption.NoCost)} ingredients={GameModeUtils.RequiresIngredients()} survival={GameModeUtils.RequiresSurvival()} player={playerPosition} {RkVitals()}";
        Rocket rocket = FindObjectOfType<Rocket>();
        if (!rocket)
        {
            return $"[SyncHarness] rocketstate: exists=False {common}";
        }

        string id = rocket.TryGetNitroxId(out NitroxId rocketId) ? rocketId.ToString() : "noid";
        bool kinematic = rocket.rb && rocket.rb.isKinematic;
        bool console = rocket.rocketConstructor && rocket.rocketConstructor.activeSelf;
        string objects = "";
        if (rocket.stageObjects != null)
        {
            foreach (GameObject stageObject in rocket.stageObjects)
            {
                objects += (stageObject && stageObject.activeSelf) ? "1" : "0";
            }
        }

        string checks = "n/a";
        string total = "n/a";
        string count = "n/a";
        string delay = "n/a";
        string launchReady = "n/a";
        RocketPreflightCheckManager preflight = rocket.GetComponent<RocketPreflightCheckManager>();
        if (preflight)
        {
            List<int> done = new List<int>();
            foreach (PreflightCheck check in preflight.preflightChecks)
            {
                done.Add((int)check);
            }

            done.Sort();
            checks = done.Count > 0 ? string.Join(",", done) : "none";
            total = preflight.totalPreflightChecks.ToString();
            count = preflight.preflightChecks.Count.ToString();
            delay = preflight.rocketReadyDelay.ToString();
            launchReady = preflight.ReturnRocketReadyForLaunch(true).ToString();
        }

        LaunchRocket launch = rocket.GetComponentInChildren<LaunchRocket>(true);
        string forced = launch ? launch.forcedRocketReady.ToString() : "n/a";

        GameObject cockpit = RkCockpit(rocket);
        string cockpitText = cockpit ? RkVec(cockpit.transform.position) : "none";
        string distance = "n/a";
        string aboard = "n/a";
        string missing = "n/a";
        if (cockpit && player)
        {
            distance = RkF(Vector3.Distance(player.transform.position, cockpit.transform.position));
            try
            {
                aboard = NitroxClient.GameLogic.Rockets.IsLocalPlayerAboard(rocket.gameObject).ToString();
                List<string> notAboard = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.Rockets>().GetPlayersNotAboard(rocket.gameObject);
                missing = notAboard.Count > 0 ? string.Join(",", notAboard) : "none";
            }
            catch (Exception exception)
            {
                missing = $"error:{exception.GetType().Name}";
            }
        }

        return $"[SyncHarness] rocketstate: exists=True id={id} pos={RkVec(rocket.transform.position)} stage={rocket.currentRocketStage} finished={rocket.IsFinished()} ready={rocket.IsRocketReady()} building={rocket.isBuilding} kinematic={kinematic} console={console} objs={objects} elevator={(int)rocket.elevatorState}@{RkF(rocket.elevatorPosition)} checks={checks} total={total} count={count} delay={delay} launchReady={launchReady} forced={forced} cockpit={cockpitText} dist={distance} aboard={aboard} missing={missing} {common}";
    }

    // ---- rocketanchor

    private void LiveCommand_rocketanchor(NotificationCenter.Notification n)
    {
        string target = LiveArgString(n, 0, "auto");
        if (target == "off")
        {
            if (rkAnchorRoutine != null)
            {
                StopCoroutine(rkAnchorRoutine);
                rkAnchorRoutine = null;
            }

            Log.Info("[SyncHarness] rocketanchor: off");
            return;
        }

        if (target == "auto")
        {
            rkAnchorFixed = false;
            rkAnchorHeld = false;
        }
        else
        {
            Vector3 parsed;
            if (!RkTryParseVec(target, out parsed))
            {
                Log.Warn("[SyncHarness] rocketanchor: needs auto, off or x,y,z (no spaces)");
                return;
            }

            rkAnchorFixed = true;
            rkAnchorHeld = true;
            rkAnchorPosition = parsed;
        }

        if (rkAnchorRoutine == null)
        {
            rkAnchorRoutine = StartCoroutine(RkAnchorLoop());
        }

        string mode = rkAnchorFixed ? $"fixed at {RkVec(rkAnchorPosition)}" : "auto (the place where the rocket is first seen)";
        Log.Info($"[SyncHarness] rocketanchor: on, {mode}");
    }

    private IEnumerator RkAnchorLoop()
    {
        Rocket rocket = null;
        float nextSearch = 0f;
        float seenSince = 0f;
        while (true)
        {
            if (!rocket && Time.realtimeSinceStartup >= nextSearch)
            {
                rocket = FindObjectOfType<Rocket>();
                nextSearch = Time.realtimeSinceStartup + 0.5f;
                seenSince = Time.realtimeSinceStartup;
            }

            // an automatic anchor waits a second after the first sight: a rocket that a bay or a spawner has just made may not have been put at its place yet
            if (rocket && (rkAnchorHeld || Time.realtimeSinceStartup - seenSince >= 1f))
            {
                RkAnchorStep(rocket);
            }

            yield return null;
        }
    }

    private void RkAnchorStep(Rocket rocket)
    {
        try
        {
            if (!rkAnchorHeld)
            {
                rkAnchorPosition = rocket.transform.position;
                rkAnchorHeld = true;
                Log.Info($"[SyncHarness] rocketanchor: holding the rocket at {RkVec(rkAnchorPosition)}");
            }

            Rigidbody body = rocket.rb;
            bool moved = (rocket.transform.position - rkAnchorPosition).sqrMagnitude > 0.01f;
            bool free = body && !body.isKinematic;
            if (moved || free)
            {
                if (free)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }

                if (moved)
                {
                    rocket.transform.position = rkAnchorPosition;
                }

                rkAnchorCorrections++;
            }

            if (rkAnchorCorrections != rkAnchorLogged && Time.realtimeSinceStartup >= rkAnchorNextLog)
            {
                rkAnchorLogged = rkAnchorCorrections;
                rkAnchorNextLog = Time.realtimeSinceStartup + 5f;
                Log.Info($"[SyncHarness] rocketanchor: {rkAnchorCorrections} corrections so far, rocket at {RkVec(rocket.transform.position)}");
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketanchor: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- rocketmvb

    private void LiveCommand_rocketmvb(NotificationCenter.Notification n)
    {
        StartCoroutine(RkMvb());
    }

    private IEnumerator RkMvb()
    {
        Constructor bay = RkMvbPrepare();
        if (!bay)
        {
            yield break;
        }

        // the bay needs a moment after Deploy (its animation moves the spawn points); the harness' buildvehicle does not wait, this does, up to 10 s
        for (int i = 0; i < 40; i++)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (i >= 8 && !bay.IsDeployAnimationInProgress)
            {
                break;
            }
        }

        RkMvbCraft(bay);
    }

    private static Constructor RkMvbPrepare()
    {
        try
        {
            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] rocketmvb: no local player");
                return null;
            }

            if (FindObjectOfType<Rocket>())
            {
                Log.Info("[SyncHarness] rocketmvb: a rocket exists already, nothing is built");
                return null;
            }

            Constructor nearest = null;
            float best = float.MaxValue;
            foreach (Constructor candidate in FindObjectsOfType<Constructor>())
            {
                float distance = Vector3.Distance(candidate.transform.position, player.transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }

            if (!nearest || best > 80f)
            {
                Log.Warn("[SyncHarness] rocketmvb: no Mobile Vehicle Bay within 80 m");
                return null;
            }

            if (!nearest.deployed)
            {
                nearest.Deploy(true);
            }

            Log.Info($"[SyncHarness] rocketmvb: bay {RkF(best)} m away, deployed {nearest.deployed}");
            return nearest;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketmvb: error {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static void RkMvbCraft(Constructor bay)
    {
        try
        {
            ConstructorInput input = bay ? bay.GetComponentInChildren<ConstructorInput>(true) : null;
            if (!input)
            {
                Log.Warn("[SyncHarness] rocketmvb: no ConstructorInput under the bay");
                return;
            }

            Transform spawnPoint = bay.GetItemSpawnPoint(TechType.RocketBase);
            Vector3 spawnPosition = spawnPoint ? spawnPoint.position : bay.transform.position;
            bool validPosition = input.ReturnValidCraftingPosition(spawnPosition);
            bool recipeOk = CrafterLogic.IsCraftRecipeFulfilled(TechType.RocketBase);
            string bayId = bay.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
            string facts = $"[SyncHarness] rocketmvb: bay={bayId} bayAt={RkVec(bay.transform.position)} spawnPoint={RkVec(spawnPosition)} validPosition={validPosition} recipeOk={recipeOk} ingredients={GameModeUtils.RequiresIngredients()} deployed={bay.deployed}";
            Log.Info(facts);
            if (!validPosition || !recipeOk)
            {
                // ConstructorInput.Craft would only play the "invalid location" notification or the missing ingredients message and return
                Log.Info($"[SyncHarness] rocketmvb: refused validPosition={validPosition} recipeOk={recipeOk}");
                return;
            }

            input.Craft(TechType.RocketBase, 25f);
            Log.Info("[SyncHarness] rocketmvb: Craft(RocketBase) called");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketmvb: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- rocketstage / rocketfinish / rocketcomplete / rocketlift

    private void LiveCommand_rocketstage(NotificationCenter.Notification n)
    {
        try
        {
            RkStage();
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketstage: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void RkStage()
    {
        Rocket rocket = FindObjectOfType<Rocket>();
        RocketConstructor constructor = rocket ? rocket.GetComponentInChildren<RocketConstructor>(true) : null;
        if (!constructor)
        {
            Log.Warn("[SyncHarness] rocketstage: no rocket or no build console loaded");
            return;
        }

        int before = rocket.currentRocketStage;
        bool finished = rocket.IsFinished();
        bool ready = rocket.IsRocketReady();
        bool inProgress = constructor.crafterLogic && constructor.crafterLogic.inProgress;
        string tech = "none";
        bool recipeOk = false;
        if (!finished)
        {
            TechType stageTech = rocket.GetCurrentStageTech();
            tech = stageTech.ToString();
            recipeOk = CrafterLogic.IsCraftRecipeFulfilled(stageTech);
        }

        string beforeLine = $"[SyncHarness] rocketstage: before stage={before} finished={finished} ready={ready} inProgress={inProgress} console={constructor.gameObject.activeInHierarchy} tech={tech} recipeOk={recipeOk} ingredients={GameModeUtils.RequiresIngredients()}";
        Log.Info(beforeLine);
        constructor.StartRocketConstruction();
        int after = rocket.currentRocketStage;
        Log.Info($"[SyncHarness] rocketstage: stage {before} -> {after} advanced={after > before}");
    }

    private void LiveCommand_rocketfinish(NotificationCenter.Notification n)
    {
        try
        {
            Rocket rocket = FindObjectOfType<Rocket>();
            if (!rocket)
            {
                Log.Warn("[SyncHarness] rocketfinish: no rocket loaded");
                return;
            }

            int forced = 0;
            foreach (VFXConstructing construction in rocket.GetComponentsInChildren<VFXConstructing>())
            {
                if (construction && !construction.isDone)
                {
                    NitroxClient.Extensions.VFXConstructingExtensions.EndGracefully(construction);
                    forced++;
                }
            }

            Log.Info($"[SyncHarness] rocketfinish: {forced} construction effects finished at once, stage={rocket.currentRocketStage} ready={rocket.IsRocketReady()}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketfinish: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_rocketcomplete(NotificationCenter.Notification n)
    {
        try
        {
            RkComplete(LiveArgString(n, 0, ""));
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketcomplete: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void RkComplete(string list)
    {
        Rocket rocket = FindObjectOfType<Rocket>();
        RocketPreflightCheckManager preflight = rocket ? rocket.GetComponent<RocketPreflightCheckManager>() : null;
        if (!preflight)
        {
            Log.Warn("[SyncHarness] rocketcomplete: no rocket loaded");
            return;
        }

        List<string> asked = new List<string>();
        foreach (string part in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            PreflightCheck check;
            if (!Enum.TryParse(part, true, out check) || !Enum.IsDefined(typeof(PreflightCheck), check))
            {
                Log.Warn($"[SyncHarness] rocketcomplete: '{part}' is not a preflight check (0 to 5 or a name)");
                continue;
            }

            asked.Add(((int)check).ToString());
            if (!preflight.preflightChecks.Contains(check))
            {
                preflight.CompletePreflightCheck(check);
            }
        }

        List<int> done = new List<int>();
        foreach (PreflightCheck check in preflight.preflightChecks)
        {
            done.Add((int)check);
        }

        done.Sort();
        Log.Info($"[SyncHarness] rocketcomplete: asked [{string.Join(",", asked)}] done [{string.Join(",", done)}] total={preflight.totalPreflightChecks} count={preflight.preflightChecks.Count}");
    }

    private void LiveCommand_rocketlift(NotificationCenter.Notification n)
    {
        try
        {
            Rocket rocket = FindObjectOfType<Rocket>();
            if (!rocket)
            {
                Log.Warn("[SyncHarness] rocketlift: no rocket loaded");
                return;
            }

            bool up = LiveArgString(n, 0, "down") == "up";
            int before = (int)rocket.elevatorState;
            rocket.CallElevator(up);
            int after = (int)rocket.elevatorState;
            Log.Info($"[SyncHarness] rocketlift: asked {(up ? "up" : "down")} state {before} -> {after} changed={before != after} stage={rocket.currentRocketStage}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketlift: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- rocketboard

    private void LiveCommand_rocketboard(NotificationCenter.Notification n)
    {
        try
        {
            RkBoard(LiveArgString(n, 0, "in"), LiveArgString(n, 1, ""));
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketboard: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void RkBoard(string mode, string place)
    {
        Rocket rocket = FindObjectOfType<Rocket>();
        GameObject cockpit = rocket ? RkCockpit(rocket) : null;
        Player player = Player.main;
        if (!cockpit || !player)
        {
            Log.Warn("[SyncHarness] rocketboard: no cockpit sphere or no local player");
            return;
        }

        Vector3 center = cockpit.transform.position;
        Vector3 target = center + new Vector3(0f, 0.2f, 0f);
        if (mode == "in2")
        {
            target = center + new Vector3(1f, 0.2f, 0f);
        }
        else if (mode == "to")
        {
            Vector3 parsed;
            if (!RkTryParseVec(place, out parsed))
            {
                Log.Warn("[SyncHarness] rocketboard: 'to' needs x,y,z (no spaces)");
                return;
            }

            target = parsed;
        }

        Vector3 before = player.transform.position;
        player.OnPlayerPositionCheat();
        player.SetPosition(target);
        Log.Info($"[SyncHarness] rocketboard: {mode} from {RkVec(before)} to {RkVec(player.transform.position)} cockpit {RkVec(center)} distance {RkF(Vector3.Distance(player.transform.position, center))}");
    }

    // ---- rocketlaunch

    private void LiveCommand_rocketlaunch(NotificationCenter.Notification n)
    {
        StartCoroutine(RkLaunch());
    }

    private IEnumerator RkLaunch()
    {
        if (!RkLaunchClick())
        {
            yield break;
        }

        yield return null;
        RkLaunchReport("after 1 frame");
        yield return new WaitForSecondsRealtime(0.7f);
        RkLaunchReport("after 0.7 s");
    }

    private static bool RkLaunchClick()
    {
        try
        {
            Rocket rocket = FindObjectOfType<Rocket>();
            LaunchRocket launch = rocket ? rocket.GetComponentInChildren<LaunchRocket>(true) : null;
            if (!launch || !Player.main)
            {
                Log.Warn("[SyncHarness] rocketlaunch: no launch console or no local player");
                return false;
            }

            StoryGoalCustomEventHandler events = StoryGoalCustomEventHandler.main;
            bool ready = launch.IsRocketReady();
            bool gun = events && events.gunDisabled;
            string aboard = "n/a";
            string missing = "n/a";
            try
            {
                aboard = NitroxClient.GameLogic.Rockets.IsLocalPlayerAboard(rocket.gameObject).ToString();
                List<string> notAboard = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.Rockets>().GetPlayersNotAboard(rocket.gameObject);
                missing = notAboard.Count > 0 ? string.Join(",", notAboard) : "none";
            }
            catch (Exception exception)
            {
                missing = $"error:{exception.GetType().Name}";
            }

            float creditsIn = launch.fadeToWhiteDelay + launch.finalOutroSequenceDuration;
            string beforeLine = $"[SyncHarness] rocketlaunch: before ready={ready} gun={gun} forced={launch.forcedRocketReady} launched={LaunchRocket.launchStarted} localAboard={aboard} missing={missing} creditsIn={RkF(creditsIn)} messages=[{RkMessages()}]";
            Log.Info(beforeLine);
            launch.OnHandClick(Player.main.guiHand);
            Log.Info("[SyncHarness] rocketlaunch: clicked");
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketlaunch: error {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static void RkLaunchReport(string when)
    {
        try
        {
            Rocket rocket = FindObjectOfType<Rocket>();
            LaunchRocket launch = rocket ? rocket.GetComponentInChildren<LaunchRocket>(true) : null;
            string clock = launch ? RkF(launch.timePassed) : "n/a";
            string cinematic = Player.main ? Player.main.cinematicModeActive.ToString() : "n/a";
            string line = $"[SyncHarness] rocketlaunch: {when} launched={LaunchRocket.launchStarted} clock={clock} cinematic={cinematic} messages=[{RkMessages()}]";
            Log.Info(line);
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketlaunch: error {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- rocketwatch

    private void LiveCommand_rocketwatch(NotificationCenter.Notification n)
    {
        float seconds = LiveArgFloat(n, 0, 600f);
        bool skipCredits = LiveArgString(n, 1, "") == "skip";
        try
        {
            // the watch runs on an object that survives the scene cleaners of the ending (see the header of this file)
            RocketWatchHost.Begin(seconds, skipCredits);
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketwatch: no watch host ({exception.GetType().Name}: {exception.Message}), watching on the console object, which does not survive the ending");
            if (rkWatchRoutine != null)
            {
                StopCoroutine(rkWatchRoutine);
            }

            rkWatchRoutine = StartCoroutine(RkWatchLoop(seconds, skipCredits));
        }

        Log.Info($"[SyncHarness] rocketwatch: started for {RkF(seconds)} s, skipCredits={skipCredits}");
    }

    // The watch itself; it runs on RocketWatchHost (a coroutine ends with the object that runs it)
    internal static IEnumerator RkWatchLoop(float seconds, bool skipCredits)
    {
        float start = Time.realtimeSinceStartup;
        float lastLog = -100f;
        float lastErrorLog = -100f;
        string lastSignature = "";
        float creditsSince = -1f;
        bool skipped = false;
        while (Time.realtimeSinceStartup - start < seconds)
        {
            float now = Time.realtimeSinceStartup;
            RkWatchSample sample = RkWatchPoll(now - start);
            bool changed = sample.Signature != lastSignature;
            bool due = now - lastLog >= (sample.Launched ? 2f : 10f);
            // a probe that fails (a menu quirk) is reported at most once per 10 s, however often it flips
            bool quiet = sample.Error && now - lastErrorLog < 10f;
            if ((changed || due) && !quiet)
            {
                Log.Info(sample.Line);
                lastLog = now;
                lastSignature = sample.Signature;
                if (sample.Error)
                {
                    lastErrorLog = now;
                }
            }

            if (sample.Credits)
            {
                if (creditsSince < 0f)
                {
                    creditsSince = now;
                }

                if (skipCredits && !skipped && now - creditsSince >= 12f)
                {
                    skipped = RkSkipCredits();
                }
            }

            yield return new WaitForSecondsRealtime(1f);
        }

        Log.Info("[SyncHarness] rocketwatch: done");
    }

    private static RkWatchSample RkWatchPoll(float elapsed)
    {
        RkWatchSample sample = new RkWatchSample();
        try
        {
            Rocket rocket = FindObjectOfType<Rocket>();
            LaunchRocket launch = rocket ? rocket.GetComponentInChildren<LaunchRocket>(true) : FindObjectOfType<LaunchRocket>();
            Player player = Player.main;
            EndCreditsManager credits = FindObjectOfType<EndCreditsManager>();
            Scene scene = SceneManager.GetActiveScene();
            string sceneName = string.IsNullOrEmpty(scene.name) ? "none" : scene.name.Replace(' ', '_');

            bool launched = LaunchRocket.launchStarted;
            string clock = launch ? RkF(launch.timePassed) : "n/a";
            string cinematic = player ? player.cinematicModeActive.ToString() : "n/a";
            string endSequence = launch && launch.endSequenceGo ? launch.endSequenceGo.activeInHierarchy.ToString() : "n/a";
            string animation = launch && launch.primarySceneAnimator ? launch.primarySceneAnimator.GetBool("ending_sequence").ToString() : "n/a";
            bool goalDone = launch && launch.launchRocketGoal != null && Story.StoryGoalManager.main && Story.StoryGoalManager.main.completedGoals.Contains(launch.launchRocketGoal.key);

            string phase = "-";
            if (credits)
            {
                FieldInfo phaseField = typeof(EndCreditsManager).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                phase = phaseField != null ? phaseField.GetValue(credits).ToString() : "unknown";
            }

            string aboard = "n/a";
            string distance = "n/a";
            GameObject cockpit = rocket ? RkCockpit(rocket) : null;
            if (cockpit && player)
            {
                distance = RkF(Vector3.Distance(player.transform.position, cockpit.transform.position));
                aboard = NitroxClient.GameLogic.Rockets.IsLocalPlayerAboard(rocket.gameObject).ToString();
            }

            // the main menu that comes back after the credits (uGUI_MainMenu.main is set by the menu scene)
            bool menu = uGUI_MainMenu.main != null;
            bool creditsLoaded = credits != null;
            // the scene EndCreditsSceneCleaner (the half second before the credits) is not the credits
            bool creditsScene = sceneName.IndexOf("redits", StringComparison.Ordinal) >= 0 && sceneName.IndexOf("Cleaner", StringComparison.Ordinal) < 0;
            sample.Launched = launched;
            sample.Credits = creditsLoaded || creditsScene;
            sample.Signature = $"{launched}|{cinematic}|{endSequence}|{animation}|{goalDone}|{sceneName}|{creditsLoaded}|{phase}|{aboard}|{menu}";
            sample.Line = $"[SyncHarness] rocketwatch: t={RkF(elapsed)} launched={launched} clock={clock} cinematic={cinematic} endSeq={endSequence} anim={animation} goal={goalDone} scene={sceneName} scenes={SceneManager.sceneCount} credits={creditsLoaded} phase={phase} aboard={aboard} dist={distance} menu={menu} sceneList={RkSceneList()}";
        }
        catch (Exception exception)
        {
            sample.Error = true;
            sample.Signature = $"error {exception.GetType().Name}";
            sample.Line = $"[SyncHarness] rocketwatch: t={RkF(elapsed)} probe error {exception.GetType().Name}: {exception.Message}";
        }

        return sample;
    }

    // The skip button of the credits holds a key for a second and sets the phase to End; the next update then loads the scene that the Nitrox patch hooks (EndCreditsManager_OnLateUpdate_Patch)
    private static bool RkSkipCredits()
    {
        try
        {
            EndCreditsManager credits = FindObjectOfType<EndCreditsManager>();
            FieldInfo phaseField = typeof(EndCreditsManager).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (!credits || phaseField == null)
            {
                return false;
            }

            phaseField.SetValue(credits, Enum.ToObject(phaseField.FieldType, 4));
            Log.Info("[SyncHarness] rocketwatch: credits skipped (phase set to End)");
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketwatch: skip error {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }
}

/// <summary>
///     The home of the rocketwatch coroutine (Debug builds, live check "rocket"). The ending of the story loads the scenes EndCreditsSceneCleaner and Cleaner; their SceneCleaner destroys every root object
///     except itself and the ones with a SceneCleanerPreserve component (SceneCleaner.Start: FindObjectsOfType&lt;GameObject&gt;, skips children, SceneCleanerPreserve, SystemsSpawner, RuntimeManager, Language),
///     so a plain DontDestroyOnLoad object like the one that HarnessConsoleCommands sits on is gone after the scene change and the watch with it. This object is a root object with both, the way
///     NitroxBootstrapper survives. It also logs every scene that loads or unloads (<c>[SyncHarness] rocketscene: ...</c>), a second source for the credits and the menu next to the scene lines of
///     NitroxDebugManager.
/// </summary>
public sealed class RocketWatchHost : MonoBehaviour
{
    private static RocketWatchHost instance;
    private Coroutine routine;
    private bool quitting;

    internal static void Begin(float seconds, bool skipCredits)
    {
        if (!instance)
        {
            GameObject host = new GameObject("Rocket watch (harness)");
            DontDestroyOnLoad(host);
            host.AddComponent<SceneCleanerPreserve>();
            instance = host.AddComponent<RocketWatchHost>();
        }

        instance.Restart(seconds, skipCredits);
    }

    private void Restart(float seconds, bool skipCredits)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
        }

        routine = StartCoroutine(HarnessConsoleCommands.RkWatchLoop(seconds, skipCredits));
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
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
            Log.Warn("[SyncHarness] rocketwatch: host destroyed");
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            Log.Info($"[SyncHarness] rocketscene: loaded {SceneName(scene)} mode={mode} scenes={HarnessConsoleCommands.RkSceneList()}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketscene: error {exception.GetType().Name}");
        }
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        try
        {
            Log.Info($"[SyncHarness] rocketscene: unloaded {SceneName(scene)}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] rocketscene: error {exception.GetType().Name}");
        }
    }

    private static string SceneName(Scene scene)
    {
        return string.IsNullOrEmpty(scene.name) ? "unnamed" : scene.name.Replace(' ', '_');
    }
}
#endif
