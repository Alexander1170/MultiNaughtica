#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "crash homes" (bug 5, the sulfur plants that look exploded): what every sulfur plant (CrashHome) near the local player holds, the same facts on both clients, and the means to make
// the checks go quicker. Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's
// "/console <player> <command> [arg] [arg]" and log "[SyncHarness] <name>: key=value ..." lines. Every number is written with the invariant culture.
//
//   crashhomes [radius] [max]   one "[SyncHarness] crashhomes: ..." line (how many homes within radius metres of the local player, default 150, the game time, how many are listed) and then one
//                               "[SyncHarness] crashhome: ..." line per home, nearest first (at most max, default 12):
//                                 id        the NitroxId of the home: ask the server with "/query <id>" (it must know the home, say CrashHomeMetadata with the same number and, as a child, the Crash
//                                           when hasFish=true)
//                                 dist      metres from the local player
//                                 spawnTime the number of the home: -1 a fish lives in it, 0 a home nobody met (the holder makes the fish within a frame or two), a game time = the timer of the next fish
//                                 now       the game time on this client; timerLeft = spawnTime - now when spawnTime is a game time, else "-"
//                                 hasFish   the home's crash is an object that exists; fishId its NitroxId (or none); fishResting the fish's state is Resting; fishFromHome metres between fish and home
//                                 open      the animator of the plant says "attacking" (the plant is open, as it is when no fish rests inside)
//                                 lock      held = this client holds a simulation lock of the home (it is the one that makes the fish and runs the timer), none = another client does
//   crashfish [detonate [inplace]]  the Crashfish within 80 m of the local player, nearest first (summary line: n, orphans = fish that no home points to, a ghost; one "crashfishitem" line per fish with
//                               home = the home whose crash points to it and orphan=true/false); with detonate the nearest one that is at least 8 m away is detonated (Crash.Detonate, what the
//                               fish does when it reaches a target: the radius damage of 5 m, then Kill, which is announced to the server and to the other client). The fish is first moved
//                               to a spot 10 m from its home (away from every other home) because a fish that explodes INSIDE its plant takes the plant with it (live check 2026-10-26: the
//                               plant is a LiveMixin object that the radius damage kills and destroys, the server then removed the home: a real fish attacks from outside); with "inplace" it is
//                               detonated where it rests, which is the way to see that (the line says inplace=True and the facts of the plant's LiveMixin)
//   crashhomelm [radius]        one "[SyncHarness] crashhomelm: ..." line per home within radius (default 40): whether the plant object has a LiveMixin (health, destroyOnDeath)
//   crashtimer <seconds>        on the nearest home within 150 m that has no fish and whose lock this client holds: the next fish comes in this many seconds (the game's own timer is 1200 s, a
//                               constant); the fish is then made by the normal path of the holder, which tells the server and the other client
public partial class HarnessConsoleCommands
{
    private const float CH_DEFAULT_RADIUS = 150f;
    private const int CH_DEFAULT_MAX = 12;
    private const float CH_FISH_RADIUS = 80f;
    private const float CH_MIN_DETONATE_DISTANCE = 8f;

    private static string ChNumber(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string ChId(GameObject target)
    {
        NitroxEntity entity = target ? target.GetComponent<NitroxEntity>() : null;
        return entity && entity.Id != null ? entity.Id.ToString() : "none";
    }

    private static float ChNow() => DayNightCycle.main ? DayNightCycle.main.timePassedAsFloat : -1f;

    private static bool ChHoldsLock(CrashHome home)
    {
        NitroxEntity entity = home.GetComponent<NitroxEntity>();
        return entity && entity.Id != null && Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>().HasAnyLockType(entity.Id);
    }

    private static string ChFacts(CrashHome home, Vector3 here)
    {
        float now = ChNow();
        string timerLeft = home.spawnTime >= 0f ? ChNumber(home.spawnTime - now) : "-";
        bool hasFish = home.crash;
        bool fishResting = hasFish && home.crash.IsResting();
        string fishId = hasFish ? ChId(home.crash.gameObject) : "none";
        string fishFromHome = ChNumber(hasFish ? Vector3.Distance(home.transform.position, home.crash.transform.position) : -1f);
        bool open = home.animator && home.animator.GetBool(AnimatorHashID.attacking);
        string holdsLock = ChHoldsLock(home) ? "held" : "none";
        Vector3 pos = home.transform.position;

        return $"id={ChId(home.gameObject)} dist={ChNumber(Vector3.Distance(here, pos))} spawnTime={ChNumber(home.spawnTime)} now={ChNumber(now)} timerLeft={timerLeft} hasFish={hasFish} fishId={fishId} fishResting={fishResting} fishFromHome={fishFromHome} open={open} lock={holdsLock} pos={ChNumber(pos.x)},{ChNumber(pos.y)},{ChNumber(pos.z)}";
    }

    private static List<CrashHome> ChHomesNear(Vector3 here, float radius)
    {
        List<CrashHome> near = new List<CrashHome>();
        foreach (CrashHome home in UnityEngine.Object.FindObjectsOfType<CrashHome>())
        {
            if (home && (home.transform.position - here).sqrMagnitude <= radius * radius)
            {
                near.Add(home);
            }
        }
        near.Sort((a, b) => (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude));
        return near;
    }

    private void LiveCommand_crashhomes(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, CH_DEFAULT_RADIUS);
            int max = (int)LiveArgFloat(n, 1, CH_DEFAULT_MAX);
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
            List<CrashHome> near = ChHomesNear(here, radius);

            Log.Info($"[SyncHarness] crashhomes: n={near.Count} radius={ChNumber(radius)} now={ChNumber(ChNow())} listed={Math.Min(max, near.Count)}");
            for (int i = 0; i < near.Count && i < max; i++)
            {
                Log.Info($"[SyncHarness] crashhome: {ChFacts(near[i], here)}");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] crashhomes: failed reason={ex.GetType().Name} {ex.Message}");
        }
    }

    private void LiveCommand_crashfish(NotificationCenter.Notification n)
    {
        try
        {
            bool detonate = LiveArgString(n, 0, "") == "detonate";
            bool inPlace = detonate && LiveArgString(n, 1, "") == "inplace";
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;

            List<Crash> fish = new List<Crash>();
            foreach (Crash crash in UnityEngine.Object.FindObjectsOfType<Crash>())
            {
                if (crash && (crash.transform.position - here).sqrMagnitude <= CH_FISH_RADIUS * CH_FISH_RADIUS)
                {
                    fish.Add(crash);
                }
            }
            fish.Sort((a, b) => (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude));

            // The server's fish is not a child of its home (the vanilla one is), so the home of a fish is the one whose crash points to it. A fish that no home points to is an orphan: a ghost
            // (CrashHome.OnDestroy destroys only the fish its home points to), which is what a second Crash entity under one home would leave behind
            List<CrashHome> homes = new List<CrashHome>();
            foreach (CrashHome candidate in UnityEngine.Object.FindObjectsOfType<CrashHome>())
            {
                if (candidate)
                {
                    homes.Add(candidate);
                }
            }

            int orphans = 0;
            List<CrashHome> owners = new List<CrashHome>();
            foreach (Crash crash in fish)
            {
                CrashHome owner = null;
                foreach (CrashHome candidate in homes)
                {
                    if (candidate.crash == crash)
                    {
                        owner = candidate;
                        break;
                    }
                }

                if (!owner)
                {
                    orphans++;
                }
                owners.Add(owner);
            }

            Log.Info($"[SyncHarness] crashfish: n={fish.Count} orphans={orphans} radius={ChNumber(CH_FISH_RADIUS)} detonate={detonate}");
            for (int i = 0; i < fish.Count; i++)
            {
                Crash crash = fish[i];
                string homeId = owners[i] ? ChId(owners[i].gameObject) : "none";
                Log.Info($"[SyncHarness] crashfishitem: id={ChId(crash.gameObject)} dist={ChNumber(Vector3.Distance(here, crash.transform.position))} resting={crash.IsResting()} home={homeId} orphan={!owners[i]}");
            }

            if (!detonate)
            {
                return;
            }

            for (int i = 0; i < fish.Count; i++)
            {
                Crash crash = fish[i];
                float distance = Vector3.Distance(here, crash.transform.position);
                if (distance < CH_MIN_DETONATE_DISTANCE)
                {
                    continue;
                }

                string id = ChId(crash.gameObject);
                CrashHome owner = owners[i];
                string homeLm = owner ? ChLiveMixinFacts(owner) : "no_home";
                string moved = "";
                if (!inPlace && owner)
                {
                    Vector3 spot = ChBlastSpot(owner, homes);
                    crash.transform.position = spot;
                    if (crash.useRigidbody)
                    {
                        crash.useRigidbody.velocity = Vector3.zero;
                    }
                    moved = $" moved={ChNumber(Vector3.Distance(owner.transform.position, spot))}m_from_home";
                }

                crash.Detonate();
                Log.Info($"[SyncHarness] crashfish: detonated id={id} dist={ChNumber(distance)}{moved} inplace={inPlace} homeLiveMixin={homeLm}");
                return;
            }

            Log.Info($"[SyncHarness] crashfish: nothing detonated reason=no_fish_at_least_{ChNumber(CH_MIN_DETONATE_DISTANCE)}_m_away");
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] crashfish: failed reason={ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// A spot 10 m from the home in the direction that is farthest from every other home (the explosion of a fish has a radius of 5 m): the place where a fish that attacks something
    /// goes off, so that the plant does not take part in it.
    /// </summary>
    private static Vector3 ChBlastSpot(CrashHome owner, List<CrashHome> homes)
    {
        Vector3 center = owner.transform.position;
        Vector3 best = center + new Vector3(10f, 0f, 0f);
        float bestClearance = -1f;
        for (int step = 0; step < 8; step++)
        {
            float angle = step * Mathf.PI / 4f;
            Vector3 spot = center + new Vector3(Mathf.Cos(angle) * 10f, 0f, Mathf.Sin(angle) * 10f);
            float clearance = float.MaxValue;
            foreach (CrashHome other in homes)
            {
                if (other && other != owner)
                {
                    clearance = Mathf.Min(clearance, Vector3.Distance(spot, other.transform.position));
                }
            }

            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                best = spot;
            }
        }
        return best;
    }

    private static string ChLiveMixinFacts(CrashHome home)
    {
        LiveMixin liveMixin = home.GetComponent<LiveMixin>();
        if (!liveMixin)
        {
            liveMixin = home.GetComponentInParent<LiveMixin>();
        }

        return liveMixin ? $"{ChNumber(liveMixin.health)}/{ChNumber(liveMixin.maxHealth)}/destroyOnDeath={liveMixin.destroyOnDeath}/on={(liveMixin.gameObject == home.gameObject ? "home" : liveMixin.gameObject.name)}" : "none";
    }

    private void LiveCommand_crashhomelm(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, 40f);
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
            List<CrashHome> near = ChHomesNear(here, radius);
            Log.Info($"[SyncHarness] crashhomelm: n={near.Count} radius={ChNumber(radius)}");
            foreach (CrashHome home in near)
            {
                Log.Info($"[SyncHarness] crashhomelm: id={ChId(home.gameObject)} dist={ChNumber(Vector3.Distance(here, home.transform.position))} liveMixin={ChLiveMixinFacts(home)}");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] crashhomelm: failed reason={ex.GetType().Name} {ex.Message}");
        }
    }

    private void LiveCommand_crashtimer(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 20f);
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
            foreach (CrashHome home in ChHomesNear(here, CH_DEFAULT_RADIUS))
            {
                if (home.crash || !ChHoldsLock(home))
                {
                    continue;
                }

                home.spawnTime = ChNow() + seconds;
                Log.Info($"[SyncHarness] crashtimer: id={ChId(home.gameObject)} spawnTime={ChNumber(home.spawnTime)} now={ChNumber(ChNow())} seconds={ChNumber(seconds)}");
                return;
            }

            Log.Info("[SyncHarness] crashtimer: nothing changed reason=no_empty_home_with_a_lock_within_150_m");
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] crashtimer: failed reason={ex.GetType().Name} {ex.Message}");
        }
    }
}
#endif
