#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nitrox.Model.DataStructures;
using Story;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "story_emperor" (a harness script that is not part of this repository, script story_emperor.ps1): the Sea Emperor eggs, the incubator, the hatch with its exclusive lock, the babies, the cure ball, the cure for
// both players, a late joiner and a server restart. Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's
// "/console <player> <command> [arg] [arg]" or "/consoleall <command> [arg] [arg]" (at most two arguments, the console lower-cases them) and log one "[SyncHarness] <name>: key=value ..." line per fact (no spaces
// inside a value, numbers with the invariant culture). Nothing throws out of a Unity callback; a command that fails says "<name>: failed reason=<token>" or "<name>: error <Type>: <message>".
//
// What the game does (vanilla IL): the eggs are scene objects. Incubator.Start puts one IncubatorEgg at every Incubator.eggSpawns; the player brings two items: a Precursor Ion Crystal (TechType 66) that
// IncubatorActivationTerminal.OnHandClick takes into a cinematic (the cinematic's end powers the incubator) and Hatching Enzymes (TechType 1547) that Incubator.OnHandClick takes (hatches every egg).
// Nitrox hooks both clicks (Incubator_OnHandClick_Patch asks the server for the EXCLUSIVE simulation lock of the platform, the parent of the Incubator, before the vanilla code runs).
//
//   sewhere [a|b|all]          lists the named places of the game's own teleport table (GotoConsoleCommand.main.data.locations) whose name contains one of the words (default: aquarium, prison,
//                              emperor, incubator, contain, quarantine, precursor, lava), or all of them: "name=<token> pos=x,y,z". The scenario takes the position of the best name at run time
//   seprep                     client-local cheats for a deep dive (NoSurvival, NoOxygen, NoAggression: the story state is untouched), resets the latches of sestate. Cleared by every join, so run it again
//   sestate [seconds]          ONE line with everything this client sees (incubator, platform id, egg parent id, eggs, terminal, Emperor, babies, lock, items, infection, cure goal); with seconds it repeats
//                              every 2 s and samples the Emperor and the babies every 0.5 s for the latches maxReg / maxMother / maxReal / maxWithId / interaction (seprep resets them)
//   sebabies                   one line per Sea Emperor baby (real or temporary, id, parent, registered with the Emperor, under the Emperor's attach points, swim target) and a summary
//   segive <TechType> [n]      new items with a NitroxId into the inventory (the path of lockeradd, the server registers an InventoryItemEntity); "gave ... id=" per item
//   sego terminal|incubator|pos x,y,z   puts the player next to the terminal / incubator (looking at it) or at x,y,z
//   sepower                    IncubatorActivationTerminal.OnHandClick (what the click on the ion crystal pillar does), then watches the cinematic; at 45 s it ends it by hand (the controller's own end event)
//   searm hatch, sedelay <ms>, sefire   arm remembers the hatch, delay is remembered too, sefire (usually /consoleall sefire) clicks Incubator.OnHandClick after the delay: the race for the lock
//   sehatch                    the same click at once (arm + fire)
//   seball                     instantiates the real cure ball (the addressable key of DropEnzymes.enzymePrefabReference, read from the egg's baby prefab) next to the player and calls EnzymeBall.OnHandClick
//                              (the cinematic ends in onPlayerCuredGoal.Trigger()); at 60 s it ends the cinematic by hand
//   seinfect <amount>          sets this player's infection (a harness emulation of a player who left infected)
public partial class HarnessConsoleCommands
{
    private const string SE_TEMP_MARKER = "_NitroxTemporary";
    private const string SE_CURE_GOAL = "Infection_Progress5";
    private const float SE_SAMPLE_SECONDS = 0.5f;
    private const int SE_LINE_CAP = 250;

    private static bool seHatchArmed;
    private static float seDelayMs;
    private static int seMaxRegistered;
    private static int seMaxOnMother;
    private static int seMaxReal;
    private static int seMaxWithId;
    private static bool seSawWait;
    private static bool seSawInteraction;
    private Coroutine seStateRoutine;

    private sealed class SeCensus
    {
        public bool HasEmperor;
        public int Attach;
        public bool Wait;
        public int Registered;
        public int Real;
        public int Temp;
        public int WithId;
        public int OnMother;
        public int Swimming;
    }

    // ---- small helpers

    private static string SeF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string SeF3(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string SeVec(Vector3 value)
    {
        return $"{SeF(value.x)},{SeF(value.y)},{SeF(value.z)}";
    }

    private static string SeToken(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "none";
        }
        return text.Replace(' ', '_').Replace('|', '_').Replace('=', '_');
    }

    private static string SeMe()
    {
        try
        {
            string name = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>().PlayerName;
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string SeIdOf(Component component)
    {
        return component && component.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
    }

    private static string SeIdOfObject(GameObject gameObject)
    {
        return gameObject && gameObject.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
    }

    private static void SeFailed(string command, string reason)
    {
        Log.Warn($"[SyncHarness] {command}: failed reason={reason} who={SeMe()}");
    }

    private static void SeWarn(string command, Exception exception)
    {
        Log.Warn($"[SyncHarness] {command}: error {exception.GetType().Name}: {exception.Message}");
    }

    private static bool SeTryParseVec(string text, out Vector3 value)
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

    // A field of an object by name, private or not (the game assembly is publicized, but a name that is wrong then simply reads as null); null when there is none
    private static object SeRead(object target, string name)
    {
        if (target == null)
        {
            return null;
        }
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(target) : null;
    }

    private static bool SeReadBool(object target, string name, bool fallback)
    {
        object value = SeRead(target, name);
        return value is bool flag ? flag : fallback;
    }

    // A method without arguments by name (the controller's OnPlayerCinematicModeEnd(), the ball's OnHandClick()); false when there is none
    private static bool SeCall(object target, string name)
    {
        if (target == null)
        {
            return false;
        }
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (method == null)
        {
            return false;
        }
        method.Invoke(target, null);
        return true;
    }

    private static bool SeGoalDone(string key)
    {
        StoryGoalManager goals = StoryGoalManager.main;
        return goals && !string.IsNullOrEmpty(key) && goals.IsGoalComplete(key);
    }

    private static int SeCount(TechType tech)
    {
        return Inventory.main && Inventory.main.container != null ? Inventory.main.container.GetCount(tech) : -1;
    }

    private static string SeLockText(NitroxId id)
    {
        if (id == null)
        {
            return "none";
        }
        try
        {
            NitroxClient.GameLogic.SimulationOwnership ownership = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
            if (ownership.HasExclusiveLock(id))
            {
                return "exclusive";
            }
            return ownership.HasAnyLockType(id) ? "any" : "none";
        }
        catch (Exception)
        {
            return "n/a";
        }
    }

    private static bool SeIsUnder(Transform child, Transform[] parents)
    {
        if (!child || parents == null)
        {
            return false;
        }
        foreach (Transform parent in parents)
        {
            if (parent && (child == parent || child.IsChildOf(parent)))
            {
                return true;
            }
        }
        return false;
    }

    private static HashSet<SeaEmperorBaby> SeRegisteredSet(SeaEmperor emperor)
    {
        HashSet<SeaEmperorBaby> set = new HashSet<SeaEmperorBaby>();
        if (emperor)
        {
            foreach (SeaEmperorBaby baby in emperor.GetBabies())
            {
                if (baby)
                {
                    set.Add(baby);
                }
            }
        }
        return set;
    }

    // What the Emperor and the babies look like right now. Temporary babies (the animation copies of a client that did not get the lock) are told apart by their name
    private static SeCensus SeTakeCensus()
    {
        SeCensus census = new SeCensus();
        SeaEmperor emperor = SeaEmperor.main;
        census.HasEmperor = emperor != null;
        census.Attach = emperor && emperor.babyAttachPoints != null ? emperor.babyAttachPoints.Length : 0;
        census.Wait = emperor && emperor.waitForBabies;
        HashSet<SeaEmperorBaby> registered = SeRegisteredSet(emperor);
        census.Registered = registered.Count;
        foreach (SeaEmperorBaby baby in FindObjectsOfType<SeaEmperorBaby>())
        {
            if (!baby)
            {
                continue;
            }
            if (baby.name.IndexOf(SE_TEMP_MARKER, StringComparison.Ordinal) >= 0)
            {
                census.Temp++;
                continue;
            }
            census.Real++;
            if (SeIdOf(baby) != "noid")
            {
                census.WithId++;
            }
            if (emperor && SeIsUnder(baby.transform, emperor.babyAttachPoints))
            {
                census.OnMother++;
            }
            if (baby.swimToTarget && baby.swimToTarget.target)
            {
                census.Swimming++;
            }
        }
        return census;
    }

    // The latches: the Emperor starts the mother interaction when as many babies as attach points are registered (SeaEmperor.Update), which clears the set and turns waitForBabies off again.
    // The babies are under the attach points for only a part of the sequence, so a state line every 2 s could miss it; the sampler of sestate runs every 0.5 s.
    // The babies are also destroyed again at the end of their way (SeaEmperorBabyCinematicController.OnCinematicModeEnd -> SwimToTeleporter -> SeaEmperorTeleporterTrigger -> Teleport): the highest number of real
    // babies and of real babies with a NitroxId that this client ever had (maxReal, maxWithId) is what says that the hatch made them, a state line of a later moment may find none
    private static void SeLatch(SeCensus census)
    {
        if (census.Registered > seMaxRegistered)
        {
            seMaxRegistered = census.Registered;
        }
        if (census.OnMother > seMaxOnMother)
        {
            seMaxOnMother = census.OnMother;
        }
        if (census.Real > seMaxReal)
        {
            seMaxReal = census.Real;
        }
        if (census.WithId > seMaxWithId)
        {
            seMaxWithId = census.WithId;
        }
        if (census.Wait)
        {
            seSawWait = true;
        }
        else if (seSawWait)
        {
            seSawInteraction = true;
        }
    }

    // ---- sewhere

    private void LiveCommand_sewhere(NotificationCenter.Notification n)
    {
        try
        {
            GotoConsoleCommand go = GotoConsoleCommand.main;
            if (!go)
            {
                go = FindObjectOfType<GotoConsoleCommand>();
            }
            if (!go)
            {
                SeFailed("sewhere", "no-goto");
                return;
            }
            if (!go.data || go.data.locations == null)
            {
                SeFailed("sewhere", "no-locations");
                return;
            }

            string pattern = LiveArgString(n, 0, "aquarium|prison|emperor|incubator|contain|quarantine|precursor|lava");
            bool all = pattern == "all";
            string[] words = pattern.Split('|');
            Player player = Player.main;
            List<string> lines = new List<string>();
            TeleportPosition[] locations = go.data.locations;
            for (int i = 0; i < locations.Length; i++)
            {
                TeleportPosition location = locations[i];
                if (location == null || string.IsNullOrEmpty(location.name))
                {
                    continue;
                }
                bool match = all;
                for (int w = 0; w < words.Length && !match; w++)
                {
                    match = words[w].Length > 0 && location.name.IndexOf(words[w], StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (match)
                {
                    lines.Add($"[SyncHarness] sewhere: name={SeToken(location.name)} pos={SeVec(location.position)} index={i}");
                }
            }

            Log.Info($"[SyncHarness] sewhere: total={locations.Length} matched={lines.Count} pattern={SeToken(pattern)} who={SeMe()} player={(player ? SeVec(player.transform.position) : "none")}");
            for (int i = 0; i < lines.Count && i < SE_LINE_CAP; i++)
            {
                Log.Info(lines[i]);
            }
        }
        catch (Exception exception)
        {
            SeWarn("sewhere", exception);
        }
    }

    // ---- seprep

    private void LiveCommand_seprep(NotificationCenter.Notification n)
    {
        try
        {
            // client-local flags (GameModeUtils currentCheats); PlayerInitialSyncProcessor clears them on every join, so the script runs this after every client start and world restart
            GameModeUtils.ActivateCheat(GameModeOption.NoSurvival);
            GameModeUtils.ActivateCheat(GameModeOption.NoOxygen);
            GameModeUtils.ActivateCheat(GameModeOption.NoAggression);
            seMaxRegistered = 0;
            seMaxOnMother = 0;
            seMaxReal = 0;
            seMaxWithId = 0;
            seSawWait = false;
            seSawInteraction = false;
            seHatchArmed = false;
            seDelayMs = 0f;
            Player player = Player.main;
            Survival survival = player ? player.GetComponent<Survival>() : null;
            string vitals = survival ? $"food={SeF(survival.food)} water={SeF(survival.water)}" : "food=n/a water=n/a";
            Log.Info($"[SyncHarness] seprep: who={SeMe()} mode={GameModeUtils.currentEffectiveMode} survival={GameModeUtils.RequiresSurvival()} oxygen={GameModeUtils.RequiresOxygen()} aggression={GameModeUtils.IsCheatActive(GameModeOption.NoAggression)} {vitals} pos={(player ? SeVec(player.transform.position) : "none")}");
        }
        catch (Exception exception)
        {
            SeWarn("seprep", exception);
        }
    }

    // ---- sestate

    private void LiveCommand_sestate(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 0f);
            SeLogState();
            if (seconds > 0f)
            {
                if (seStateRoutine != null)
                {
                    StopCoroutine(seStateRoutine);
                }
                seStateRoutine = StartCoroutine(SeStateLoop(seconds));
            }
        }
        catch (Exception exception)
        {
            SeWarn("sestate", exception);
        }
    }

    private IEnumerator SeStateLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        float nextLog = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForSecondsRealtime(SE_SAMPLE_SECONDS);
            SeSampleOnly();
            if (Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 2f;
                SeLogState();
            }
        }
    }

    private static void SeSampleOnly()
    {
        try
        {
            SeLatch(SeTakeCensus());
        }
        catch (Exception)
        {
            // the sampler is best effort: the next state line says what it can
        }
    }

    private static void SeLogState()
    {
        try
        {
            // each segment catches its own exceptions, so that one broken probe does not take the whole line away; the line must start with "exists=" (the script waits for "sestate: exists=")
            List<string> parts = new List<string>();
            parts.Add(SeIncubatorSegment());
            parts.Add(SeTerminalSegment());
            parts.Add(SeEmperorSegment());
            parts.Add(SePlayerSegment());
            Log.Info($"[SyncHarness] sestate: {string.Join(" ", parts.ToArray())}");
        }
        catch (Exception exception)
        {
            SeWarn("sestate", exception);
        }
    }

    private static string SeIncubatorSegment()
    {
        try
        {
            Incubator incubator = FindObjectOfType<Incubator>();
            Player player = Player.main;
            if (!incubator)
            {
                return "exists=False platform=none eggParent=none idMatch=n/a ipos=none idist=n/a powered=n/a hatched=n/a eggs=0 eggSpawns=0 eggsActive=0 lock=none hgoal=none hgoalDone=n/a";
            }

            // the server only knows the platform (the parent of the Incubator): that is the id of the lock request (Incubator_OnHandClick_Patch)
            GameObject platform = incubator.transform.parent ? incubator.transform.parent.gameObject : null;
            NitroxId platformId = null;
            string platformText = "noid";
            if (platform && platform.TryGetNitroxId(out NitroxId foundId))
            {
                platformId = foundId;
                platformText = foundId.ToString();
            }

            // IncubatorEgg_HatchNow_Patch asks for the lock of the NEAREST NitroxEntity above the egg: it has to be the platform, else nobody is "simulating" and no real baby is broadcast
            IList eggs = SeRead(incubator, "eggs") as IList;
            int eggCount = eggs != null ? eggs.Count : 0;
            string eggParentText = "none";
            int active = 0;
            if (eggs != null)
            {
                for (int i = 0; i < eggs.Count; i++)
                {
                    IncubatorEgg egg = eggs[i] as IncubatorEgg;
                    if (!egg)
                    {
                        continue;
                    }
                    if (i == 0)
                    {
                        NitroxEntity parentEntity = egg.GetComponentInParent<NitroxEntity>();
                        eggParentText = parentEntity && parentEntity.Id != null ? parentEntity.Id.ToString() : "noid";
                    }
                    if (egg.animationController && SeReadBool(egg.animationController, "animationActive", false))
                    {
                        active++;
                    }
                }
            }

            string idMatch = platformText != "noid" && eggParentText != "none" ? (eggParentText == platformText).ToString() : "n/a";
            string goalKey = incubator.onUseGoal != null ? incubator.onUseGoal.key : "none";
            string goalDone = incubator.onUseGoal != null ? SeGoalDone(incubator.onUseGoal.key).ToString() : "n/a";
            string distance = player ? SeF(Vector3.Distance(player.transform.position, incubator.transform.position)) : "n/a";
            int spawns = incubator.eggSpawns != null ? incubator.eggSpawns.Length : 0;
            return $"exists=True platform={platformText} eggParent={eggParentText} idMatch={idMatch} ipos={SeVec(incubator.transform.position)} idist={distance} powered={incubator.powered} hatched={incubator.hatched} eggs={eggCount} eggSpawns={spawns} eggsActive={active} lock={SeLockText(platformId)} hgoal={SeToken(goalKey)} hgoalDone={goalDone}";
        }
        catch (Exception exception)
        {
            return $"exists=err:{exception.GetType().Name}";
        }
    }

    private static string SeTerminalSegment()
    {
        try
        {
            IncubatorActivationTerminal terminal = FindObjectOfType<IncubatorActivationTerminal>();
            Player player = Player.main;
            if (!terminal)
            {
                return "term=False tpos=none tdist=n/a tgoal=none tgoalDone=n/a";
            }
            Transform anchor = terminal.cinematicController && terminal.cinematicController.animatedTransform ? terminal.cinematicController.animatedTransform : terminal.transform;
            string goalKey = terminal.onUseGoal != null ? terminal.onUseGoal.key : "none";
            string goalDone = terminal.onUseGoal != null ? SeGoalDone(terminal.onUseGoal.key).ToString() : "n/a";
            string distance = player ? SeF(Vector3.Distance(player.transform.position, anchor.position)) : "n/a";
            return $"term=True tpos={SeVec(anchor.position)} tdist={distance} tgoal={SeToken(goalKey)} tgoalDone={goalDone}";
        }
        catch (Exception exception)
        {
            return $"term=err:{exception.GetType().Name}";
        }
    }

    private static string SeEmperorSegment()
    {
        try
        {
            SeaEmperor emperor = SeaEmperor.main;
            SeCensus census = SeTakeCensus();
            SeLatch(census);
            int balls = FindObjectsOfType<EnzymeBall>().Length;
            int juveniles = FindObjectsOfType<SeaEmperorJuvenile>().Length;
            string emperorPosition = emperor ? SeVec(emperor.transform.position) : "none";
            return $"emperor={census.HasEmperor} epos={emperorPosition} attach={census.Attach} wait={census.Wait} regd={census.Registered} real={census.Real} temp={census.Temp} withId={census.WithId} onMother={census.OnMother} swimming={census.Swimming} balls={balls} juv={juveniles} maxReg={seMaxRegistered} maxMother={seMaxOnMother} maxReal={seMaxReal} maxWithId={seMaxWithId} interaction={seSawInteraction}";
        }
        catch (Exception exception)
        {
            return $"emperor=err:{exception.GetType().Name}";
        }
    }

    private static string SePlayerSegment()
    {
        try
        {
            Player player = Player.main;
            if (!player)
            {
                return $"who={SeMe()} inf=n/a cured={SeGoalDone(SE_CURE_GOAL)} cine=n/a alive=n/a survival={GameModeUtils.RequiresSurvival()} oxygen={GameModeUtils.RequiresOxygen()} enzymes={SeCount(TechType.HatchingEnzymes)} crystals={SeCount(TechType.PrecursorIonCrystal)} pos=none t={SeF3(Time.realtimeSinceStartup)}";
            }
            string infection = player.infectedMixin ? SeF(player.infectedMixin.GetInfectedAmount()) : "n/a";
            bool alive = player.liveMixin && player.liveMixin.health > 0f;
            return $"who={SeMe()} inf={infection} cured={SeGoalDone(SE_CURE_GOAL)} cine={player.cinematicModeActive} alive={alive} survival={GameModeUtils.RequiresSurvival()} oxygen={GameModeUtils.RequiresOxygen()} enzymes={SeCount(TechType.HatchingEnzymes)} crystals={SeCount(TechType.PrecursorIonCrystal)} pos={SeVec(player.transform.position)} t={SeF3(Time.realtimeSinceStartup)}";
        }
        catch (Exception exception)
        {
            return $"who=err:{exception.GetType().Name}";
        }
    }

    // ---- sebabies

    private void LiveCommand_sebabies(NotificationCenter.Notification n)
    {
        try
        {
            SeaEmperor emperor = SeaEmperor.main;
            HashSet<SeaEmperorBaby> registered = SeRegisteredSet(emperor);
            int real = 0;
            int temp = 0;
            int onMother = 0;
            int swimming = 0;
            int total = 0;
            foreach (SeaEmperorBaby baby in FindObjectsOfType<SeaEmperorBaby>())
            {
                if (!baby)
                {
                    continue;
                }
                total++;
                bool isTemp = baby.name.IndexOf(SE_TEMP_MARKER, StringComparison.Ordinal) >= 0;
                bool under = emperor && SeIsUnder(baby.transform, emperor.babyAttachPoints);
                string swim = baby.swimToTarget && baby.swimToTarget.target ? SeToken(baby.swimToTarget.target.name) : "none";
                float nearest = -1f;
                if (emperor && emperor.babyAttachPoints != null)
                {
                    foreach (Transform attach in emperor.babyAttachPoints)
                    {
                        if (attach)
                        {
                            float distance = Vector3.Distance(baby.transform.position, attach.position);
                            if (nearest < 0f || distance < nearest)
                            {
                                nearest = distance;
                            }
                        }
                    }
                }
                if (isTemp)
                {
                    temp++;
                }
                else
                {
                    real++;
                    if (under)
                    {
                        onMother++;
                    }
                    if (swim != "none")
                    {
                        swimming++;
                    }
                }
                string parent = baby.transform.parent ? SeToken(baby.transform.parent.name) : "none";
                Log.Info($"[SyncHarness] sebabies: id={SeIdOf(baby)} temp={isTemp} parent={parent} registered={registered.Contains(baby)} onMother={under} swim={swim} pos={SeVec(baby.transform.position)} distMother={(nearest < 0f ? "n/a" : SeF(nearest))}");
            }
            Log.Info($"[SyncHarness] sebabies: summary real={real} temp={temp} registered={registered.Count} onMother={onMother} swimming={swimming} total={total} emperor={emperor != null} who={SeMe()}");
        }
        catch (Exception exception)
        {
            SeWarn("sebabies", exception);
        }
    }

    // ---- segive

    private void LiveCommand_segive(NotificationCenter.Notification n)
    {
        try
        {
            TechType tech;
            if (!Enum.TryParse(LiveArgString(n, 0, ""), true, out tech) || tech == TechType.None)
            {
                SeFailed("segive", "needs-a-TechType");
                return;
            }
            int count = Mathf.Clamp((int)LiveArgFloat(n, 1, 1f), 1, 4);
            StartCoroutine(SeGiveRoutine(tech, count));
        }
        catch (Exception exception)
        {
            SeWarn("segive", exception);
        }
    }

    private IEnumerator SeGiveRoutine(TechType tech, int count)
    {
        // Like lockeradd: a new object with an id goes into the player's inventory, where Nitrox tells the server about it (InventoryItemEntity). No yield inside a try block
        int given = 0;
        string failure = null;
        for (int i = 0; i < count && failure == null; i++)
        {
            TaskResult<GameObject> prefabResult = new();
            yield return CraftData.InstantiateFromPrefabAsync(tech, prefabResult, false);
            failure = SeGiveOne(tech, prefabResult.Get());
            if (failure == null)
            {
                given++;
            }
        }
        if (failure != null)
        {
            SeFailed("segive", failure);
        }
        Log.Info($"[SyncHarness] segive: done who={SeMe()} tech={tech} requested={count} given={given}");
    }

    private static string SeGiveOne(TechType tech, GameObject item)
    {
        try
        {
            if (!item)
            {
                return "no-prefab";
            }
            Pickupable pickupable = item.GetComponent<Pickupable>();
            if (!pickupable)
            {
                UnityEngine.Object.Destroy(item);
                return "no-pickupable";
            }
            pickupable.Initialize();
            NitroxId id = NitroxEntity.GetIdOrGenerateNew(item);
            if (Inventory.main.container.AddItem(pickupable) == null)
            {
                UnityEngine.Object.Destroy(item);
                return "inventory-full";
            }
            Log.Info($"[SyncHarness] segive: gave who={SeMe()} tech={tech} id={id} inventory={SeCount(tech)}");
            return null;
        }
        catch (Exception exception)
        {
            return $"exception-{exception.GetType().Name}";
        }
    }

    // ---- sego

    private void LiveCommand_sego(NotificationCenter.Notification n)
    {
        try
        {
            string target = LiveArgString(n, 0, "");
            Player player = Player.main;
            if (!player)
            {
                SeFailed("sego", "no-player");
                return;
            }

            Vector3 destination;
            Vector3 look = Vector3.zero;
            bool haveLook = false;
            if (target == "terminal")
            {
                IncubatorActivationTerminal terminal = FindObjectOfType<IncubatorActivationTerminal>();
                if (!terminal)
                {
                    SeFailed("sego", "no-terminal");
                    return;
                }
                // the place the cinematic puts the player anyway
                Transform anchor = terminal.cinematicController && terminal.cinematicController.animatedTransform ? terminal.cinematicController.animatedTransform : terminal.transform;
                destination = anchor.position + Vector3.up * 0.2f;
                look = terminal.transform.position;
                haveLook = true;
            }
            else if (target == "incubator")
            {
                Incubator incubator = FindObjectOfType<Incubator>();
                if (!incubator)
                {
                    SeFailed("sego", "no-incubator");
                    return;
                }
                destination = incubator.transform.position + Vector3.up * 1.5f;
                look = incubator.eggSpawns != null && incubator.eggSpawns.Length > 0 && incubator.eggSpawns[0] ? incubator.eggSpawns[0].position : incubator.transform.position;
                haveLook = true;
            }
            else if (target == "pos")
            {
                if (!SeTryParseVec(LiveArgString(n, 1, ""), out destination))
                {
                    SeFailed("sego", "needs-x,y,z");
                    return;
                }
            }
            else
            {
                SeFailed("sego", "needs-terminal-incubator-or-pos");
                return;
            }

            player.OnPlayerPositionCheat();
            player.SetPosition(destination);
            if (haveLook)
            {
                // turn the body towards the place (yaw only): the animators of the eggs and the baby are culled when nothing sees them
                Vector3 direction = look - destination;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.01f)
                {
                    player.transform.rotation = Quaternion.LookRotation(direction.normalized);
                }
            }
            Log.Info($"[SyncHarness] sego: to={target} pos={SeVec(player.transform.position)} look={haveLook} who={SeMe()}");
        }
        catch (Exception exception)
        {
            SeWarn("sego", exception);
        }
    }

    // ---- sepower

    private void LiveCommand_sepower(NotificationCenter.Notification n)
    {
        try
        {
            IncubatorActivationTerminal terminal = FindObjectOfType<IncubatorActivationTerminal>();
            if (!terminal || !terminal.incubator || !Player.main)
            {
                SeFailed("sepower", "no-terminal");
                return;
            }
            StartCoroutine(SePowerRoutine(terminal));
        }
        catch (Exception exception)
        {
            SeWarn("sepower", exception);
        }
    }

    private static string SePowerFacts(IncubatorActivationTerminal terminal)
    {
        bool powered = terminal && terminal.incubator && terminal.incubator.powered;
        bool cine = Player.main && Player.main.cinematicModeActive;
        return $"who={SeMe()} powered={powered} crystals={SeCount(TechType.PrecursorIonCrystal)} cine={cine}";
    }

    private static bool SeClickTerminal(IncubatorActivationTerminal terminal)
    {
        try
        {
            string goalKey = terminal.onUseGoal != null ? terminal.onUseGoal.key : "none";
            Transform anchor = terminal.cinematicController && terminal.cinematicController.animatedTransform ? terminal.cinematicController.animatedTransform : terminal.transform;
            Log.Info($"[SyncHarness] sepower: before {SePowerFacts(terminal)} tdist={SeF(Vector3.Distance(Player.main.transform.position, anchor.position))} goal={SeToken(goalKey)}");
            // the click of the player on the ion crystal pillar: the Nitrox prefix broadcasts IncubatorMetadata(powered) now, the vanilla code takes the crystal and starts the cinematic
            terminal.OnHandClick(Player.main.guiHand);
            Log.Info($"[SyncHarness] sepower: clicked {SePowerFacts(terminal)} t={SeF3(Time.realtimeSinceStartup)}");
            return true;
        }
        catch (Exception exception)
        {
            SeWarn("sepower", exception);
            return false;
        }
    }

    private static bool SePowerDone(IncubatorActivationTerminal terminal)
    {
        return terminal && terminal.incubator && terminal.incubator.powered && !(Player.main && Player.main.cinematicModeActive);
    }

    private IEnumerator SePowerRoutine(IncubatorActivationTerminal terminal)
    {
        // No yield inside a try block: the probes are in helpers
        float start = Time.realtimeSinceStartup;
        bool forced = false;
        if (!SeClickTerminal(terminal))
        {
            yield break;
        }
        float waited = 0f;
        while (waited < 75f)
        {
            yield return new WaitForSecondsRealtime(1f);
            waited = Time.realtimeSinceStartup - start;
            if (SePowerDone(terminal))
            {
                break;
            }
            if (!forced && waited >= 45f && terminal && terminal.cinematicController)
            {
                forced = SeEndCinematic(terminal.cinematicController, "sepower");
            }
        }
        Log.Info($"[SyncHarness] sepower: done {SePowerFacts(terminal)} waited={SeF(waited)} forced={forced}");
    }

    // The animation event that ends a player cinematic calls PlayerCinematicController.OnPlayerCinematicModeEnd(): it ends the cinematic and tells the receiver (the terminal / the ball). A cinematic that does not end by
    // itself (an animator that is culled, a missing event) is ended by calling the same method; the caller reports forced=True
    private static bool SeEndCinematic(PlayerCinematicController controller, string command)
    {
        try
        {
            if (!controller)
            {
                return false;
            }
            bool called = SeCall(controller, "OnPlayerCinematicModeEnd");
            Log.Info($"[SyncHarness] {command}: ended the cinematic by hand called={called} who={SeMe()}");
            return called;
        }
        catch (Exception exception)
        {
            SeWarn(command, exception);
            return false;
        }
    }

    // ---- searm / sedelay / sefire / sehatch

    private void LiveCommand_searm(NotificationCenter.Notification n)
    {
        try
        {
            Incubator incubator = FindObjectOfType<Incubator>();
            seHatchArmed = true;
            GameObject platform = incubator && incubator.transform.parent ? incubator.transform.parent.gameObject : null;
            int enzymes = SeCount(TechType.HatchingEnzymes);
            Log.Info($"[SyncHarness] searm: armed=hatch who={SeMe()} incubator={incubator != null} powered={(incubator ? incubator.powered.ToString() : "n/a")} hatched={(incubator ? incubator.hatched.ToString() : "n/a")} enzymes={enzymes} platform={SeIdOfObject(platform)}");
        }
        catch (Exception exception)
        {
            SeWarn("searm", exception);
        }
    }

    private void LiveCommand_sedelay(NotificationCenter.Notification n)
    {
        seDelayMs = Mathf.Clamp(LiveArgFloat(n, 0, 0f), 0f, 5000f);
        Log.Info($"[SyncHarness] sedelay: delayMs={(int)seDelayMs} who={SeMe()}");
    }

    private void LiveCommand_sefire(NotificationCenter.Notification n)
    {
        try
        {
            if (!seHatchArmed)
            {
                SeFailed("sefire", "nothing-armed");
                return;
            }
            float delay = seDelayMs;
            seHatchArmed = false;
            seDelayMs = 0f;
            if (delay <= 0f)
            {
                // in the frame of the packet: the tightest race the harness can make
                SeHatchNow(delay);
            }
            else
            {
                StartCoroutine(SeFireLater(delay));
            }
        }
        catch (Exception exception)
        {
            SeWarn("sefire", exception);
        }
    }

    private IEnumerator SeFireLater(float delay)
    {
        yield return new WaitForSecondsRealtime(delay / 1000f);
        SeHatchNow(delay);
    }

    private void LiveCommand_sehatch(NotificationCenter.Notification n)
    {
        SeHatchNow(0f);
    }

    // The click of the player on the incubator terminal with Hatching Enzymes in the inventory: Incubator.OnHandClick, which Incubator_OnHandClick_Patch answers with a lock request (the vanilla code runs when the
    // server grants it). A client that is refused gets the DenyOwnershipHand component on the Incubator object for 2 s
    private void SeHatchNow(float delay)
    {
        try
        {
            Incubator incubator = FindObjectOfType<Incubator>();
            Player player = Player.main;
            if (!incubator || !player)
            {
                SeFailed("sehatch", incubator ? "no-player" : "no-incubator");
                return;
            }
            GameObject platform = incubator.transform.parent ? incubator.transform.parent.gameObject : null;
            NitroxId platformId = null;
            string platformText = "noid";
            if (platform && platform.TryGetNitroxId(out NitroxId foundId))
            {
                platformId = foundId;
                platformText = foundId.ToString();
            }
            Log.Info($"[SyncHarness] sehatch: clicked who={SeMe()} delayMs={(int)delay} powered={incubator.powered} hatched={incubator.hatched} enzymes={SeCount(TechType.HatchingEnzymes)} platform={platformText} lock={SeLockText(platformId)} t={SeF3(Time.realtimeSinceStartup)}");
            incubator.OnHandClick(player.guiHand);
            StartCoroutine(SeHatchWatch(incubator, platformId));
        }
        catch (Exception exception)
        {
            SeWarn("sehatch", exception);
        }
    }

    private IEnumerator SeHatchWatch(Incubator incubator, NitroxId platformId)
    {
        // the refusal shows for 2 s only, so it is sampled every 0.1 s for 4 s
        bool deny = false;
        float end = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < end)
        {
            if (incubator && incubator.GetComponent<NitroxClient.MonoBehaviours.Gui.HUD.DenyOwnershipHand>())
            {
                deny = true;
            }
            yield return new WaitForSecondsRealtime(0.1f);
        }
        SeLogHatchResult(incubator, platformId, deny);
    }

    private static void SeLogHatchResult(Incubator incubator, NitroxId platformId, bool deny)
    {
        try
        {
            Log.Info($"[SyncHarness] sehatch: result who={SeMe()} hatched={(incubator ? incubator.hatched.ToString() : "n/a")} enzymes={SeCount(TechType.HatchingEnzymes)} deny={deny} lock={SeLockText(platformId)} t={SeF3(Time.realtimeSinceStartup)}");
        }
        catch (Exception exception)
        {
            SeWarn("sehatch", exception);
        }
    }

    // ---- seball

    private void LiveCommand_seball(NotificationCenter.Notification n)
    {
        try
        {
            if (!Player.main)
            {
                SeFailed("seball", "no-player");
                return;
            }
            StartCoroutine(SeBallRoutine());
        }
        catch (Exception exception)
        {
            SeWarn("seball", exception);
        }
    }

    // The addressable key of the cure ball: the baby prefab of an egg carries DropEnzymes.enzymePrefabReference, the reference that DropEnzymes.SpawnCureBallAsync instantiates (AddressablesUtility.InstantiateAsync with
    // (string)reference.RuntimeKey). Read through reflection (the reference class is in the Addressables assembly)
    private static string SeBallKey(out string source)
    {
        source = "none";
        try
        {
            DropEnzymes drop = null;
            IncubatorEgg egg = null;
            Incubator incubator = FindObjectOfType<Incubator>();
            IList eggs = incubator ? SeRead(incubator, "eggs") as IList : null;
            if (eggs != null && eggs.Count > 0)
            {
                egg = eggs[0] as IncubatorEgg;
                source = "incubator-egg";
            }
            if (!egg)
            {
                egg = FindObjectOfType<IncubatorEgg>();
                source = "scene-egg";
            }
            if (egg && egg.seaEmperorBabyPrefab)
            {
                SeaEmperorBaby babyPrefab = egg.seaEmperorBabyPrefab.GetComponent<SeaEmperorBaby>();
                if (babyPrefab)
                {
                    drop = babyPrefab.dropEnzymes;
                }
            }
            if (!drop)
            {
                drop = FindObjectOfType<DropEnzymes>();
                source = "scene-dropenzymes";
            }
            if (!drop)
            {
                source = "none";
                return null;
            }
            object reference = drop.enzymePrefabReference;
            if (reference == null)
            {
                source = "no-reference";
                return null;
            }
            PropertyInfo property = reference.GetType().GetProperty("RuntimeKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = property != null ? property.GetValue(reference, null) : null;
            string key = value as string;
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (Exception exception)
        {
            SeWarn("seball", exception);
            return null;
        }
    }

    private static string SeBallFacts(EnzymeBall ball)
    {
        bool playerCured = SeReadBool(ball, "playerCured", false);
        bool cine = Player.main && Player.main.cinematicModeActive;
        string infection = Player.main && Player.main.infectedMixin ? SeF(Player.main.infectedMixin.GetInfectedAmount()) : "n/a";
        return $"who={SeMe()} playerCured={playerCured} cine={cine} inf={infection}";
    }

    private static bool SeClickBall(EnzymeBall ball)
    {
        try
        {
            // the click of the player on the ball: EnzymeBall.OnHandClick() (no argument, GUIHand reaches it by SendMessage); it does nothing once the shared cure goal is complete
            bool called = SeCall(ball, "OnHandClick");
            Log.Info($"[SyncHarness] seball: clicked {SeBallFacts(ball)} called={called} t={SeF3(Time.realtimeSinceStartup)}");
            return called;
        }
        catch (Exception exception)
        {
            SeWarn("seball", exception);
            return false;
        }
    }

    // One line when what the script waits for changes (the cinematic, the goal, the infection); returns the new signature
    private static string SeBallTick(EnzymeBall ball, float waited, string lastSignature)
    {
        try
        {
            string goalKey = ball && ball.onPlayerCuredGoal != null ? ball.onPlayerCuredGoal.key : "none";
            bool goalDone = SeGoalDone(goalKey);
            bool cine = Player.main && Player.main.cinematicModeActive;
            string infection = Player.main && Player.main.infectedMixin ? SeF(Player.main.infectedMixin.GetInfectedAmount()) : "n/a";
            string signature = $"cine={cine} goalDone={goalDone} cured={SeGoalDone(SE_CURE_GOAL)} inf={infection}";
            if (signature != lastSignature)
            {
                Log.Info($"[SyncHarness] seball: t={SeF(waited)} {signature} who={SeMe()}");
            }
            return signature;
        }
        catch (Exception exception)
        {
            SeWarn("seball", exception);
            return lastSignature;
        }
    }

    private static bool SeBallFinished(EnzymeBall ball)
    {
        string goalKey = ball && ball.onPlayerCuredGoal != null ? ball.onPlayerCuredGoal.key : "none";
        return SeGoalDone(goalKey) && !(Player.main && Player.main.cinematicModeActive);
    }

    private IEnumerator SeBallRoutine()
    {
        // No yield inside a try block: the probes are in helpers
        string source;
        string key = SeBallKey(out source);
        Log.Info($"[SyncHarness] seball: key={SeToken(key)} source={source} who={SeMe()}");
        if (string.IsNullOrEmpty(key))
        {
            SeFailed("seball", "no-key");
            yield break;
        }
        Player player = Player.main;
        if (!player)
        {
            SeFailed("seball", "no-player");
            yield break;
        }

        Vector3 spawnAt = player.transform.position + player.transform.forward * 1.2f + Vector3.up * 0.3f;
        CoroutineTask<GameObject> task = AddressablesUtility.InstantiateAsync(key, (Transform)null, spawnAt, Quaternion.identity, true);
        yield return task;
        GameObject ballObject = task.GetResult();
        EnzymeBall ball = ballObject ? ballObject.GetComponentInChildren<EnzymeBall>(true) : null;
        if (!ball)
        {
            SeFailed("seball", ballObject ? "no-ball" : "no-spawn");
            yield break;
        }
        // the ball floats on its own; held where it is, so that it does not drift out of the hand's reach
        Rigidbody body = ballObject.GetComponentInChildren<Rigidbody>(true);
        if (body)
        {
            body.isKinematic = true;
        }
        string goalKey = ball.onPlayerCuredGoal != null ? ball.onPlayerCuredGoal.key : "none";
        // EnzymeBall.Start reads playerCured from the goal in its first frame
        yield return null;
        yield return null;
        Log.Info($"[SyncHarness] seball: spawned who={SeMe()} goalKey={SeToken(goalKey)} playerCured={SeReadBool(ball, "playerCured", false)} pos={SeVec(ballObject.transform.position)}");

        float start = Time.realtimeSinceStartup;
        bool forced = false;
        if (!SeClickBall(ball))
        {
            SeFailed("seball", "no-click");
            yield break;
        }

        float waited = 0f;
        string signature = "";
        while (waited < 90f)
        {
            yield return new WaitForSecondsRealtime(1f);
            waited = Time.realtimeSinceStartup - start;
            signature = SeBallTick(ball, waited, signature);
            if (SeBallFinished(ball))
            {
                break;
            }
            if (!forced && waited >= 60f && ball.playerCinematicController && Player.main && Player.main.cinematicModeActive)
            {
                forced = SeEndCinematic(ball.playerCinematicController, "seball");
            }
        }
        // the goal runs the cure on the next frames (Infection_Progress5: RemoveInfection), the stats follow within 3 s
        yield return new WaitForSecondsRealtime(3f);
        bool goalDone = SeGoalDone(goalKey);
        Log.Info($"[SyncHarness] seball: done {SeBallFacts(ball)} goal={SeToken(goalKey)} goalDone={goalDone} cured={SeGoalDone(SE_CURE_GOAL)} waited={SeF(waited)} forced={forced}");
    }

    // ---- seinfect

    private void LiveCommand_seinfect(NotificationCenter.Notification n)
    {
        try
        {
            Player player = Player.main;
            if (!player || !player.infectedMixin)
            {
                SeFailed("seinfect", "no-player");
                return;
            }
            float amount = Mathf.Clamp(LiveArgFloat(n, 0, 1f), 0f, 1f);
            bool changed = player.infectedMixin.SetInfectedAmount(amount);
            Log.Info($"[SyncHarness] seinfect: who={SeMe()} amount={SeF(amount)} changed={changed} now={SeF(player.infectedMixin.GetInfectedAmount())}");
        }
        catch (Exception exception)
        {
            SeWarn("seinfect", exception);
        }
    }
}
#endif
