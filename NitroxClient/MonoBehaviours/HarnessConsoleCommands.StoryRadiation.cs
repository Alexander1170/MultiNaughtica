#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "story_radiation" (a harness script that is not part of this repository, script story_radiation.ps1): the Aurora's radiation field and the 11 drive core leaks seen by two players: the field on
// both clients after the explosion, the radiation damage a player takes inside it, real welds with the vanilla Welder in both directions, the same leak welded by both, a joiner at a partly
// repaired state and a server restart. Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's
// "/console <player> <command> [arg] [arg]" or "/consoleall <command> [arg] [arg]" (at most two arguments reach the client, all lower case, so values that belong together travel in ONE argument
// separated by commas) and log "[SyncHarness] <name>: key=value ..." lines (no spaces inside a value, numbers with the invariant culture). Nothing throws out of a Unity callback.
//
//   srprep                          cheats for a long Survival run: NoSurvival (no hunger or thirst), NoOxygen, NoEnergy (a welder battery does not drain), a full tank. NEVER NoRadiation or NoDamage: the run
//                                   is about the radiation. The cheats are client-local and every join clears them (PlayerInitialSyncProcessor), so the script runs it after every (re)start
//   srleaks                         the 11 leaks exactly as RadiationLeakEntitySpawner binds them (child i of the "radiationleaks" holder): one line each with the NitroxId (noid = never bound), position, health,
//                                   max, whether it leaks, distance to the centre of the field and to the player; then one summary line (total, registered, bound, distinct ids, leaking)
//   srprobe [seconds]               ONE line with the field (radius, the three fields that hold it, the vanilla constants, fixed, leak counts and health, centre, distance, the tracker the damage reads, the
//                                   player's radiation amount and health, the damage component, suit pieces); with seconds it repeats every 2 s for that long
//   srweld <id8|nearest|rest|restrev|status> [seconds[,cap[,step]]]
//                                   welds with the REAL Welder (the one 'item welder' gave): activeWeldTarget = the leak's LiveMixin, then Welder.OnToolUseAnim = Weld() (the Nitrox transpiler
//                                   Welder_Weld_Patch routes the heal through AddHealthOverride -> LiveMixin.AddHealth -> LiveMixin_AddHealth_Patch -> the metadata broadcast). rest = every leak that still
//                                   leaks, ascending ids, restrev = descending (two players finish disjoint halves). cap = fraction of the maximum at which to stop (0.5 leaves a half repaired leak), step = seconds
//                                   between two weld calls (0.25). Three steps without a gain switch to LiveMixin.AddHealth (path=addhealth) and say so. status = welder, battery, health per weld
//   srwatch <id8|all> <seconds>     per frame: a line for every change of the health of a leak (what this client sees, its own weld or a remote one), and a summary at the end
//   srexpose <seconds>              every 0.5 s: distance to the centre of the field, its radius, the player's radiation amount and health, and td = the distance the game's own tracker holds (infinity
//                                   when the player is beyond the tracker's maxDistance: then the game radiates nothing) and last = the type of the last damage the player took (Radiation for the field,
//                                   Fire, Heat or Normal for anything else); below 35 health the player gets 50 back (a direct health
//                                   assignment, nothing is sent), so that the run survives the field
//   srgo <id8|centre> [dy]          fallback mover: puts the local player at a leak or at the centre of the field (like warpto)
public partial class HarnessConsoleCommands
{
    private Coroutine srProbeRoutine;
    private Coroutine srWeldRoutine;
    private Coroutine srWatchRoutine;
    private Coroutine srExposeRoutine;

    private const float SR_HEAL_BELOW = 35f;
    private const float SR_HEAL_BY = 50f;
    private const float SR_WELD_STEP = 0.25f;

    private sealed class SrTarget
    {
        public int Index;
        public RadiationLeak Leak;
    }

    private sealed class SrWeldState
    {
        public int Calls;
        public int Errors;
        public bool Fallback;
        public float PerWeld = 10f;
        public string Path = "welder";
    }

    private sealed class SrWatchState
    {
        public string Id8 = "";
        public RadiationLeak Leak;
        public float First;
        public float Last;
        public int Changes;
        public bool Monotonic = true;
    }

    private sealed class SrExposeState
    {
        public string Me = "?";
        public int Samples;
        public int Inside;
        public int Healed;
        public float MaxAmount;
        public float StartHp = -1f;
        public float MinHp = float.MaxValue;
        public string FirstInsideAt = "none";
    }

    // ---- small helpers

    private static string SrF(float value)
    {
        if (float.IsNaN(value))
        {
            return "nan";
        }

        if (float.IsInfinity(value))
        {
            return "inf";
        }

        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string SrF3(float value)
    {
        if (float.IsNaN(value))
        {
            return "nan";
        }

        if (float.IsInfinity(value))
        {
            return "inf";
        }

        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string SrF4(float value)
    {
        if (float.IsNaN(value))
        {
            return "nan";
        }

        if (float.IsInfinity(value))
        {
            return "inf";
        }

        return value.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string SrD3(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return "inf";
        }

        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string SrVec(Vector3 value)
    {
        return $"{SrF(value.x)},{SrF(value.y)},{SrF(value.z)}";
    }

    private static void SrAdd(List<string> parts, string key, string value)
    {
        parts.Add($"{key}={value}");
    }

    private static string SrJoin(List<string> parts)
    {
        return string.Join(" ", parts.ToArray());
    }

    private static string SrMe()
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

    private static NitroxClient.GameLogic.TimeManager SrTime()
    {
        try
        {
            return Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.TimeManager>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // the real play time of the world in seconds (the clock the leaks' FixRealTime and the joiner's radius use)
    private static double SrRealTime()
    {
        NitroxClient.GameLogic.TimeManager time = SrTime();
        return time != null ? time.RealTimeElapsed : double.NaN;
    }

    private static string SrIdOf(Component component)
    {
        NitroxId id;
        if (component && component.TryGetNitroxId(out id))
        {
            return id.ToString();
        }

        return "noid";
    }

    private static string SrShort(string text)
    {
        return text.Length >= 8 ? text.Substring(0, 8) : text;
    }

    private static void SrError(string command, Exception exception)
    {
        Log.Warn($"[SyncHarness] {command}: error {exception.GetType().Name}: {exception.Message}");
    }

    // The centre of the field: the object of the PlayerDistanceTracker that RadiatePlayerInRange and DamagePlayerInRadius read the distance from. Both components fetch their tracker with
    // GetComponent<PlayerDistanceTracker>() in Start (read in the IL), so it sits on THEIR object, and PlayerDistanceTracker.ScheduledUpdate measures from its own transform: the transform of the
    // RadiatePlayerInRange (then of the DamagePlayerInRadius) is the centre of the game's own distance, whatever LeakingRadiation.playerDistanceTracker points to.
    private static Vector3 SrFieldCenter()
    {
        LeakingRadiation radiation = LeakingRadiation.main;
        if (!radiation)
        {
            return Vector3.zero;
        }

        if (radiation.radiatePlayerInRange)
        {
            return radiation.radiatePlayerInRange.transform.position;
        }

        if (radiation.damagePlayerInRadius)
        {
            return radiation.damagePlayerInRadius.transform.position;
        }

        if (radiation.playerDistanceTracker)
        {
            return radiation.playerDistanceTracker.transform.position;
        }

        return radiation.transform.position;
    }

    // The distance that RadiatePlayerInRange.Radiate and DamagePlayerInRadius.DoDamage really use: the tracker on the object of the RadiatePlayerInRange. It is refreshed on a schedule and is infinity
    // beyond the tracker's maxDistance; NaN when there is no such tracker.
    private static float SrTrackerDistance(LeakingRadiation radiation, out float maxDistance)
    {
        maxDistance = float.NaN;
        PlayerDistanceTracker tracker = null;
        if (radiation && radiation.radiatePlayerInRange)
        {
            tracker = radiation.radiatePlayerInRange.GetComponent<PlayerDistanceTracker>();
        }

        if (!tracker && radiation)
        {
            tracker = radiation.playerDistanceTracker;
        }

        if (!tracker)
        {
            return float.NaN;
        }

        maxDistance = tracker.maxDistance;
        return tracker.distanceToPlayer;
    }

    // The leaks as RadiationLeakEntitySpawner binds them: child i of the first child of the exploder's "radiationleaks" object. An entry is null when that child has no RadiationLeak.
    // holderState: ok, or registered-only (the holder is not there, the leaks that registered themselves are used), or missing / error:<type>
    private static List<RadiationLeak> SrLeakList(out string holderState)
    {
        List<RadiationLeak> list = new List<RadiationLeak>();
        holderState = "missing";
        try
        {
            CrashedShipExploder exploder = CrashedShipExploder.main;
            if (exploder)
            {
                Transform leaks = exploder.transform.Find("radiationleaks");
                if (leaks && leaks.childCount > 0)
                {
                    Transform holder = leaks.GetChild(0);
                    for (int i = 0; i < holder.childCount; i++)
                    {
                        list.Add(holder.GetChild(i).GetComponent<RadiationLeak>());
                    }

                    holderState = "ok";
                    return list;
                }
            }

            LeakingRadiation radiation = LeakingRadiation.main;
            if (radiation && radiation.leaks != null)
            {
                foreach (RadiationLeak leak in radiation.leaks)
                {
                    list.Add(leak);
                }

                holderState = "registered-only";
            }
        }
        catch (Exception exception)
        {
            holderState = $"error:{exception.GetType().Name}";
        }

        return list;
    }

    // all | nearest (the nearest leak that still leaks) | rest / restrev (every leak that still leaks, ascending / descending NitroxId) | an id or the first characters of one
    private static List<SrTarget> SrResolve(string spec)
    {
        string holderState;
        List<RadiationLeak> all = SrLeakList(out holderState);
        List<SrTarget> result = new List<SrTarget>();
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        float bestDistance = float.MaxValue;
        SrTarget best = null;
        for (int i = 0; i < all.Count; i++)
        {
            RadiationLeak leak = all[i];
            if (!leak || !leak.liveMixin)
            {
                continue;
            }

            SrTarget target = new SrTarget();
            target.Index = i;
            target.Leak = leak;
            if (spec == "all")
            {
                result.Add(target);
            }
            else if (spec == "nearest" || spec == "rest" || spec == "restrev")
            {
                if (leak.liveMixin.IsFullHealth())
                {
                    continue;
                }

                if (spec == "nearest")
                {
                    float distance = Vector3.Distance(here, leak.transform.position);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = target;
                    }
                }
                else
                {
                    result.Add(target);
                }
            }
            else if (SrIdOf(leak).StartsWith(spec, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(target);
                break;
            }
        }

        if (best != null)
        {
            result.Add(best);
        }

        if (spec == "rest" || spec == "restrev")
        {
            result.Sort((a, b) => string.CompareOrdinal(SrIdOf(a.Leak), SrIdOf(b.Leak)));
            if (spec == "restrev")
            {
                result.Reverse();
            }
        }

        return result;
    }

    // the first Welder in the inventory (the tool sits there, inactive, after 'item welder')
    private static Welder SrFindWelder()
    {
        if (!Inventory.main || Inventory.main.container == null)
        {
            return null;
        }

        foreach (InventoryItem item in Inventory.main.container)
        {
            if (item == null || !item.item)
            {
                continue;
            }

            if (item.item.GetTechType() == TechType.Welder)
            {
                Welder welder = item.item.GetComponent<Welder>();
                if (welder)
                {
                    return welder;
                }
            }
        }

        return null;
    }

    // the reach of that tracker as text (a helper of its own so that the iterator of srexpose has no out argument)
    private static string SrTrackerMaxText(LeakingRadiation radiation)
    {
        float maxDistance;
        SrTrackerDistance(radiation, out maxDistance);
        return float.IsNaN(maxDistance) ? "n/a" : SrF(maxDistance);
    }

    // ---- srprep

    private void LiveCommand_srprep(NotificationCenter.Notification n)
    {
        try
        {
            GameModeUtils.ActivateCheat(GameModeOption.NoSurvival);
            GameModeUtils.ActivateCheat(GameModeOption.NoOxygen);
            GameModeUtils.ActivateCheat(GameModeOption.NoEnergy);
            if (Player.main && Player.main.oxygenMgr != null)
            {
                Player.main.oxygenMgr.AddOxygen(1000f);
            }

            bool noDamage = NoDamageConsoleCommand.main && NoDamageConsoleCommand.main.GetNoDamageCheat();
            string mode = GameModeUtils.currentEffectiveMode.ToString().Replace(' ', '_');
            string hp = Player.main && Player.main.liveMixin ? SrF(Player.main.liveMixin.health) : "n/a";
            List<string> parts = new List<string>();
            SrAdd(parts, "player", SrMe());
            SrAdd(parts, "nosurvival", (!GameModeUtils.RequiresSurvival()).ToString());
            SrAdd(parts, "nooxygen", (!GameModeUtils.RequiresOxygen()).ToString());
            SrAdd(parts, "noenergy", (!GameModeUtils.RequiresPower()).ToString());
            SrAdd(parts, "hasRadiation", GameModeUtils.HasRadiation().ToString());
            SrAdd(parts, "noDamage", noDamage.ToString());
            SrAdd(parts, "mode", mode);
            SrAdd(parts, "hp", hp);
            string line = SrJoin(parts);
            Log.Info($"[SyncHarness] srprep: {line}");
        }
        catch (Exception exception)
        {
            SrError("srprep", exception);
        }
    }

    // ---- srleaks

    private void LiveCommand_srleaks(NotificationCenter.Notification n)
    {
        try
        {
            string holderState;
            List<RadiationLeak> leaks = SrLeakList(out holderState);
            string me = SrMe();
            Vector3 center = SrFieldCenter();
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
            int bound = 0;
            HashSet<string> distinct = new HashSet<string>();
            for (int i = 0; i < leaks.Count; i++)
            {
                RadiationLeak leak = leaks[i];
                if (!leak)
                {
                    Log.Info($"[SyncHarness] srleaks: player={me} i={i} id=missing");
                    continue;
                }

                string id = SrIdOf(leak);
                if (id != "noid")
                {
                    bound++;
                    distinct.Add(id);
                }

                LiveMixin mixin = leak.liveMixin;
                Vector3 position = leak.transform.position;
                List<string> parts = new List<string>();
                SrAdd(parts, "player", me);
                SrAdd(parts, "i", i.ToString(CultureInfo.InvariantCulture));
                SrAdd(parts, "id", id);
                SrAdd(parts, "pos", SrVec(position));
                SrAdd(parts, "health", mixin ? SrF(mixin.health) : "n/a");
                SrAdd(parts, "max", mixin ? SrF(mixin.maxHealth) : "n/a");
                SrAdd(parts, "leaking", mixin ? leak.IsLeaking().ToString() : "n/a");
                SrAdd(parts, "model", leak.model ? leak.model.activeSelf.ToString() : "n/a");
                SrAdd(parts, "canres", mixin ? mixin.canResurrect.ToString() : "n/a");
                SrAdd(parts, "weldable", mixin ? mixin.weldable.ToString() : "n/a");
                SrAdd(parts, "cdist", SrF(Vector3.Distance(center, position)));
                SrAdd(parts, "pdist", SrF(Vector3.Distance(here, position)));
                string row = SrJoin(parts);
                Log.Info($"[SyncHarness] srleaks: {row}");
            }

            LeakingRadiation radiation = LeakingRadiation.main;
            int registered = radiation && radiation.leaks != null ? radiation.leaks.Count : -1;
            int leaking = radiation ? radiation.GetNumLeaks() : -1;
            Log.Info($"[SyncHarness] srleaks: player={me} total={leaks.Count} registered={registered} bound={bound} distinct={distinct.Count} leaking={leaking} holder={holderState}");
        }
        catch (Exception exception)
        {
            SrError("srleaks", exception);
        }
    }

    // ---- srprobe

    private void LiveCommand_srprobe(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 0f);
            SrLogProbe();
            if (seconds > 0f)
            {
                if (srProbeRoutine != null)
                {
                    StopCoroutine(srProbeRoutine);
                }

                srProbeRoutine = StartCoroutine(SrProbeLoop(seconds));
            }
        }
        catch (Exception exception)
        {
            SrError("srprobe", exception);
        }
    }

    private IEnumerator SrProbeLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForSecondsRealtime(2f);
            SrLogProbe();
        }
    }

    private static void SrLogProbe()
    {
        try
        {
            Log.Info(SrProbeText());
        }
        catch (Exception exception)
        {
            SrError("srprobe", exception);
        }
    }

    private static string SrProbeText()
    {
        string me = SrMe();
        CrashedShipExploder exploder = CrashedShipExploder.main;
        LeakingRadiation radiation = LeakingRadiation.main;
        Player player = Player.main;
        NitroxClient.GameLogic.TimeManager time = SrTime();
        List<string> parts = new List<string>();
        SrAdd(parts, "player", me);
        SrAdd(parts, "t", SrD3(SrRealTime()));
        SrAdd(parts, "game", DayNightCycle.main ? SrD3(DayNightCycle.main.timePassedAsDouble) : "n/a");
        SrAdd(parts, "dt", time != null ? SrF4(time.DeltaTime) : "n/a");
        SrAdd(parts, "exploded", exploder ? exploder.IsExploded().ToString() : "n/a");
        SrAdd(parts, "explodeAt", time != null ? SrD3(time.AuroraRealExplosionTime) : "n/a");
        if (!radiation)
        {
            SrAdd(parts, "noradiation", "True");
            return $"[SyncHarness] srprobe: {SrJoin(parts)}";
        }

        DamagePlayerInRadius damage = radiation.damagePlayerInRadius;
        RadiatePlayerInRange radiate = radiation.radiatePlayerInRange;
        float trackerMax;
        float trackerDistance = SrTrackerDistance(radiation, out trackerMax);
        SrAdd(parts, "radius", SrF3(radiation.currentRadius));
        SrAdd(parts, "dmgRadius", damage ? SrF3(damage.damageRadius) : "n/a");
        SrAdd(parts, "radRadius", radiate ? SrF3(radiate.radiateRadius) : "n/a");
        SrAdd(parts, "kStart", SrF(radiation.kStartRadius));
        SrAdd(parts, "kGrow", SrF3(radiation.kGrowRate));
        SrAdd(parts, "kDiss", SrF3(radiation.kNaturalDissipation));
        SrAdd(parts, "kMax", SrF(radiation.kMaxRadius));
        SrAdd(parts, "fixed", radiation.radiationFixed.ToString());
        SrAdd(parts, "leaking", radiation.GetNumLeaks().ToString(CultureInfo.InvariantCulture));

        int registered = radiation.leaks != null ? radiation.leaks.Count : 0;
        int full = 0;
        float healthSum = 0f;
        float healthMax = 0f;
        List<string> rows = new List<string>();
        if (radiation.leaks != null)
        {
            foreach (RadiationLeak leak in radiation.leaks)
            {
                if (!leak || !leak.liveMixin)
                {
                    continue;
                }

                if (leak.liveMixin.IsFullHealth())
                {
                    full++;
                }

                healthSum += leak.liveMixin.health;
                healthMax += leak.liveMixin.maxHealth;
                rows.Add($"{SrShort(SrIdOf(leak))}:{SrF(leak.liveMixin.health)}");
            }
        }

        rows.Sort(StringComparer.Ordinal);
        string leakh = rows.Count > 0 ? string.Join(",", rows.ToArray()) : "none";
        SrAdd(parts, "leaks", registered.ToString(CultureInfo.InvariantCulture));
        SrAdd(parts, "fullCount", full.ToString(CultureInfo.InvariantCulture));
        SrAdd(parts, "healthSum", SrF(healthSum));
        SrAdd(parts, "healthMax", SrF(healthMax));
        SrAdd(parts, "leakh", leakh);

        Vector3 center = SrFieldCenter();
        Vector3 position = player ? player.transform.position : Vector3.zero;
        SrAdd(parts, "center", SrVec(center));
        SrAdd(parts, "pos", SrVec(position));
        SrAdd(parts, "dist", player ? SrF(Vector3.Distance(center, position)) : "n/a");
        SrAdd(parts, "trackerDist", float.IsNaN(trackerDistance) ? "n/a" : SrF(trackerDistance));
        SrAdd(parts, "trackerMax", float.IsNaN(trackerMax) ? "n/a" : SrF(trackerMax));
        SrAdd(parts, "amount", player ? SrF3(player.radiationAmount) : "n/a");
        SrAdd(parts, "hp", player && player.liveMixin ? SrF(player.liveMixin.health) : "n/a");
        SrAdd(parts, "hasRadiation", GameModeUtils.HasRadiation().ToString());
        bool noDamage = NoDamageConsoleCommand.main && NoDamageConsoleCommand.main.GetNoDamageCheat();
        SrAdd(parts, "noDamage", noDamage.ToString());
        SrAdd(parts, "dmgAmount", damage ? SrF(damage.damageAmount) : "n/a");
        SrAdd(parts, "dmgInterval", damage ? SrF(damage.updateInterval) : "n/a");
        SrAdd(parts, "dmgType", damage ? damage.damageType.ToString().Replace(' ', '_') : "n/a");
        SrAdd(parts, "dmgEnabled", damage ? damage.enabled.ToString() : "n/a");
        string suit = "n/a";
        if (Inventory.main && Inventory.main.equipment != null)
        {
            Equipment equipment = Inventory.main.equipment;
            suit = $"{equipment.GetCount(TechType.RadiationSuit)}/{equipment.GetCount(TechType.RadiationHelmet)}/{equipment.GetCount(TechType.RadiationGloves)}";
        }

        SrAdd(parts, "suit", suit);
        SrAdd(parts, "clamped", (radiation.currentRadius >= radiation.kMaxRadius - 0.005f).ToString());
        return $"[SyncHarness] srprobe: {SrJoin(parts)}";
    }

    // ---- srweld

    private void LiveCommand_srweld(NotificationCenter.Notification n)
    {
        try
        {
            string spec = LiveArgString(n, 0, "status");
            if (spec == "status")
            {
                SrWeldStatus();
                return;
            }

            float seconds;
            float cap;
            float step;
            SrParseWeldArgs(LiveArgString(n, 1, ""), out seconds, out cap, out step);
            // the targets and the tool are looked up here, inside the try: a coroutine that throws before its first yield ends silently (an [ERR-UNITY] line and no "done" line)
            List<SrTarget> targets = SrResolve(spec);
            Welder welder = SrFindWelder();
            if (srWeldRoutine != null)
            {
                StopCoroutine(srWeldRoutine);
            }

            srWeldRoutine = StartCoroutine(SrWeldLoop(spec, targets, welder, seconds, cap, step));
        }
        catch (Exception exception)
        {
            SrError("srweld", exception);
        }
    }

    // "seconds,cap,step": the time budget (40), the fraction of the maximum at which to stop (1 = until full) and the pause between two weld calls (0.25)
    private static void SrParseWeldArgs(string text, out float seconds, out float cap, out float step)
    {
        seconds = 40f;
        cap = 1f;
        step = SR_WELD_STEP;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string[] pieces = text.Split(',');
        float value;
        if (pieces.Length > 0 && float.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f)
        {
            seconds = value;
        }

        if (pieces.Length > 1 && float.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f && value <= 1f)
        {
            cap = value;
        }

        if (pieces.Length > 2 && float.TryParse(pieces[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0.05f)
        {
            step = value;
        }
    }

    private static void SrWeldStatus()
    {
        Welder welder = SrFindWelder();
        if (!welder)
        {
            Log.Info($"[SyncHarness] srweld: status player={SrMe()} welder=none");
            return;
        }

        EnergyMixin energy = welder.GetComponent<EnergyMixin>();
        string charge = energy ? SrF(energy.charge) : "n/a";
        string capacity = energy ? SrF(energy.capacity) : "n/a";
        string noEnergy = (!GameModeUtils.RequiresPower()).ToString();
        string active = welder.gameObject.activeInHierarchy.ToString();
        Log.Info($"[SyncHarness] srweld: status player={SrMe()} welder=ok energy={charge}/{capacity} perWeld={SrF(welder.healthPerWeld)} cost={SrF(welder.weldEnergyCost)} noenergy={noEnergy} active={active}");
    }

    // One weld call on one leak: the vanilla Welder.Weld() (through OnToolUseAnim) or, after the fallback, LiveMixin.AddHealth. Returns the health the call added (0 when nothing happened).
    // The tool sits inactive in the inventory: its flame effect (fxControl) is switched off for the call and put back, so that Weld() cannot fail on an effect of an inactive object.
    private static float SrWeldStep(Welder welder, RadiationLeak leak, SrWeldState state)
    {
        LiveMixin mixin = leak.liveMixin;
        float before = mixin.health;
        VFXController savedFx = null;
        bool fxSwitchedOff = false;
        try
        {
            if (!state.Fallback && welder)
            {
                if (!welder.gameObject.activeInHierarchy)
                {
                    savedFx = welder.fxControl;
                    welder.fxControl = null;
                    fxSwitchedOff = true;
                }

                GUIHand hand = Player.main ? Player.main.guiHand : null;
                welder.activeWeldTarget = mixin;
                welder.OnToolUseAnim(hand);
            }
            else
            {
                mixin.AddHealth(state.PerWeld);
            }
        }
        catch (Exception exception)
        {
            state.Errors++;
            Log.Warn($"[SyncHarness] srweld: step error {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (welder)
            {
                welder.activeWeldTarget = null;
                if (fxSwitchedOff)
                {
                    welder.fxControl = savedFx;
                }
            }
        }

        state.Calls++;
        return mixin.health - before;
    }

    private IEnumerator SrWeldLoop(string spec, List<SrTarget> targets, Welder welder, float seconds, float cap, float step)
    {
        string me = SrMe();
        if (targets.Count == 0)
        {
            Log.Warn($"[SyncHarness] srweld: begin player={me} failed reason=no-target spec={spec}");
            yield break;
        }

        SrWeldState state = new SrWeldState();
        state.PerWeld = welder ? welder.healthPerWeld : 10f;
        if (state.PerWeld <= 0f)
        {
            state.PerWeld = 10f;
        }

        if (!welder)
        {
            state.Fallback = true;
            state.Path = "addhealth";
        }

        bool many = spec == "rest" || spec == "restrev";
        float started = Time.realtimeSinceStartup;
        float end = started + seconds;
        int welded = 0;
        int fallbacks = 0;
        for (int index = 0; index < targets.Count; index++)
        {
            RadiationLeak leak = targets[index].Leak;
            if (!leak || !leak.liveMixin)
            {
                continue;
            }

            LiveMixin mixin = leak.liveMixin;
            string id8 = SrShort(SrIdOf(leak));
            float max = mixin.maxHealth;
            float before = mixin.health;
            float last = before;
            float gainedHere = 0f;
            int calls = 0;
            int errorsBefore = state.Errors;
            int idle = 0;
            string reason = "time";
            string fixedAt = "none";
            float legStarted = Time.realtimeSinceStartup;
            Log.Info($"[SyncHarness] srweld: begin player={me} id8={id8} idx={targets[index].Index} health={SrF(before)} max={SrF(max)} perWeld={SrF(state.PerWeld)} path={state.Path} cap={SrF(cap)} seconds={SrF(seconds)} step={SrF(step)}");
            if (mixin.IsFullHealth())
            {
                reason = "nothing-to-weld";
            }
            else
            {
                while (true)
                {
                    if (!leak || !mixin)
                    {
                        reason = "gone";
                        break;
                    }

                    if (Time.realtimeSinceStartup >= end)
                    {
                        reason = "time";
                        break;
                    }

                    if (mixin.IsFullHealth())
                    {
                        reason = "full";
                        break;
                    }

                    if (cap < 1f && mixin.health >= cap * max - 0.001f)
                    {
                        reason = "cap";
                        break;
                    }

                    float gain = SrWeldStep(welder, leak, state);
                    calls++;
                    float now = mixin.health;
                    if (now != last)
                    {
                        Log.Info($"[SyncHarness] srweld: tick player={me} id8={id8} h={SrF(now)} t={SrD3(SrRealTime())}");
                        last = now;
                    }

                    if (gain > 0f)
                    {
                        gainedHere += gain;
                        idle = 0;
                        if (mixin.IsFullHealth())
                        {
                            fixedAt = SrD3(SrRealTime());
                        }
                    }
                    else
                    {
                        idle++;
                        if (idle >= 3 && !state.Fallback)
                        {
                            state.Fallback = true;
                            state.Path = "addhealth";
                            fallbacks++;
                            idle = 0;
                            Log.Info($"[SyncHarness] srweld: fallback player={me} id8={id8} why=no-gain-in-3-steps");
                        }
                        else if (idle >= 3)
                        {
                            reason = "no-effect";
                            break;
                        }
                    }

                    if (state.Errors - errorsBefore >= 5)
                    {
                        reason = "error";
                        break;
                    }

                    yield return new WaitForSecondsRealtime(step);
                }
            }

            float after = mixin ? mixin.health : before;
            bool isFull = mixin && mixin.IsFullHealth();
            if (isFull)
            {
                welded++;
            }

            float elapsed = Time.realtimeSinceStartup - legStarted;
            Log.Info($"[SyncHarness] srweld: done player={me} id8={id8} before={SrF(before)} after={SrF(after)} calls={calls} gained={SrF(gainedHere)} errors={state.Errors - errorsBefore} full={isFull} reason={reason} elapsed={SrF(elapsed)} path={state.Path} fixedAt={fixedAt}");
            if (!many)
            {
                break;
            }

            yield return new WaitForSecondsRealtime(0.3f);
        }

        if (many)
        {
            LeakingRadiation radiation = LeakingRadiation.main;
            int leaking = radiation ? radiation.GetNumLeaks() : -1;
            float total = Time.realtimeSinceStartup - started;
            Log.Info($"[SyncHarness] srweld: all-done player={me} leaking={leaking} welded={welded} calls={state.Calls} fallbacks={fallbacks} errors={state.Errors} elapsed={SrF(total)} path={state.Path}");
        }
    }

    // ---- srwatch

    private void LiveCommand_srwatch(NotificationCenter.Notification n)
    {
        try
        {
            string spec = LiveArgString(n, 0, "all");
            float seconds = LiveArgFloat(n, 1, 30f);
            // the leaks to watch are looked up here, inside the try (see srweld)
            List<SrWatchState> watched = new List<SrWatchState>();
            foreach (SrTarget target in SrResolve(spec))
            {
                SrWatchState state = new SrWatchState();
                state.Leak = target.Leak;
                state.Id8 = SrShort(SrIdOf(target.Leak));
                state.First = target.Leak.liveMixin.health;
                state.Last = state.First;
                watched.Add(state);
            }

            if (srWatchRoutine != null)
            {
                StopCoroutine(srWatchRoutine);
            }

            srWatchRoutine = StartCoroutine(SrWatchLoop(spec, watched, seconds));
        }
        catch (Exception exception)
        {
            SrError("srwatch", exception);
        }
    }

    private static void SrWatchTick(string me, SrWatchState watched)
    {
        try
        {
            if (!watched.Leak || !watched.Leak.liveMixin)
            {
                return;
            }

            float health = watched.Leak.liveMixin.health;
            if (health == watched.Last)
            {
                return;
            }

            watched.Changes++;
            if (health < watched.Last - 0.001f)
            {
                watched.Monotonic = false;
            }

            watched.Last = health;
            Log.Info($"[SyncHarness] srwatch: change player={me} id8={watched.Id8} h={SrF(health)} t={SrD3(SrRealTime())}");
        }
        catch (Exception exception)
        {
            SrError("srwatch", exception);
        }
    }

    private IEnumerator SrWatchLoop(string spec, List<SrWatchState> watched, float seconds)
    {
        string me = SrMe();
        Log.Info($"[SyncHarness] srwatch: begin player={me} target={spec} tracked={watched.Count} seconds={SrF(seconds)}");
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            for (int i = 0; i < watched.Count; i++)
            {
                SrWatchTick(me, watched[i]);
            }

            yield return null;
        }

        foreach (SrWatchState state in watched)
        {
            Log.Info($"[SyncHarness] srwatch: done player={me} id8={state.Id8} first={SrF(state.First)} last={SrF(state.Last)} changes={state.Changes} monotonic={state.Monotonic}");
        }
    }

    // ---- srexpose

    private void LiveCommand_srexpose(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 30f);
            if (srExposeRoutine != null)
            {
                StopCoroutine(srExposeRoutine);
            }

            srExposeRoutine = StartCoroutine(SrExposeLoop(seconds));
        }
        catch (Exception exception)
        {
            SrError("srexpose", exception);
        }
    }

    private static void SrExposeSample(SrExposeState state)
    {
        try
        {
            LeakingRadiation radiation = LeakingRadiation.main;
            Player player = Player.main;
            if (!radiation || !player || !player.liveMixin)
            {
                return;
            }

            Vector3 position = player.transform.position;
            float distance = Vector3.Distance(SrFieldCenter(), position);
            float radius = radiation.damagePlayerInRadius ? radiation.damagePlayerInRadius.damageRadius : radiation.currentRadius;
            bool inside = radius > 0f && distance <= radius;
            float amount = player.radiationAmount;
            float health = player.liveMixin.health;
            state.Samples++;
            if (state.StartHp < 0f)
            {
                state.StartHp = health;
            }

            if (health < state.MinHp)
            {
                state.MinHp = health;
            }

            if (amount > state.MaxAmount)
            {
                state.MaxAmount = amount;
            }

            if (inside)
            {
                state.Inside++;
                if (state.FirstInsideAt == "none")
                {
                    state.FirstInsideAt = SrD3(SrRealTime());
                }
            }

            float trackerMax;
            float trackerDistance = SrTrackerDistance(radiation, out trackerMax);
            string td = float.IsNaN(trackerDistance) ? "n/a" : SrF(trackerDistance);
            // the type of the last damage the player's LiveMixin took (LiveMixin.TakeDamage writes it into its damageInfo): Radiation for the field, anything else (Fire, Heat, Normal ...) is another source
            string last = "n/a";
            DamageInfo lastDamage = player.liveMixin.damageInfo;
            if (lastDamage != null)
            {
                last = lastDamage.type.ToString();
            }

            Log.Info($"[SyncHarness] srexpose: s player={state.Me} t={SrD3(SrRealTime())} dist={SrF(distance)} r={SrF3(radius)} inside={inside} amount={SrF3(amount)} hp={SrF(health)} pos={SrVec(position)} td={td} last={last}");
            if (health < SR_HEAL_BELOW)
            {
                float healed = Mathf.Min(player.liveMixin.maxHealth, health + SR_HEAL_BY);
                player.liveMixin.health = healed;
                state.Healed++;
                Log.Info($"[SyncHarness] srexpose: healed player={state.Me} from={SrF(health)} to={SrF(healed)}");
            }
        }
        catch (Exception exception)
        {
            SrError("srexpose", exception);
        }
    }

    private IEnumerator SrExposeLoop(float seconds)
    {
        SrExposeState state = new SrExposeState();
        state.Me = SrMe();
        Log.Info($"[SyncHarness] srexpose: begin player={state.Me} seconds={SrF(seconds)}");
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            SrExposeSample(state);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        LeakingRadiation radiation = LeakingRadiation.main;
        DamagePlayerInRadius damage = radiation ? radiation.damagePlayerInRadius : null;
        bool noDamage = NoDamageConsoleCommand.main && NoDamageConsoleCommand.main.GetNoDamageCheat();
        string minHp = state.MinHp == float.MaxValue ? "n/a" : SrF(state.MinHp);
        string dmgAmount = damage ? SrF(damage.damageAmount) : "n/a";
        string dmgInterval = damage ? SrF(damage.updateInterval) : "n/a";
        string trackerMaxText = SrTrackerMaxText(radiation);
        Log.Info($"[SyncHarness] srexpose: done player={state.Me} samples={state.Samples} inside={state.Inside} maxAmount={SrF3(state.MaxAmount)} startHp={SrF(state.StartHp)} minHp={minHp} firstInsideAt={state.FirstInsideAt} healed={state.Healed} dmgAmount={dmgAmount} dmgInterval={dmgInterval} hasRadiation={GameModeUtils.HasRadiation()} noDamage={noDamage} trackerMax={trackerMaxText}");
    }

    // ---- srgo

    private void LiveCommand_srgo(NotificationCenter.Notification n)
    {
        try
        {
            string target = LiveArgString(n, 0, "centre");
            float dy = LiveArgFloat(n, 1, 1f);
            Vector3 position = Vector3.zero;
            if (target == "centre" || target == "center")
            {
                position = SrFieldCenter();
            }
            else
            {
                List<SrTarget> found = SrResolve(target);
                if (found.Count == 0)
                {
                    Log.Warn($"[SyncHarness] srgo: failed reason=no-target target={target}");
                    return;
                }

                position = found[0].Leak.transform.position;
            }

            position += new Vector3(0f, dy, 0f);
            if (!Player.main)
            {
                Log.Warn("[SyncHarness] srgo: failed reason=no-player");
                return;
            }

            Player.main.OnPlayerPositionCheat();
            Player.main.SetPosition(position);
            Log.Info($"[SyncHarness] srgo: player={SrMe()} target={target} now={SrVec(Player.main.transform.position)}");
        }
        catch (Exception exception)
        {
            SrError("srgo", exception);
        }
    }
}
#endif
