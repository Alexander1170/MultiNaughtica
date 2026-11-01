#if DEBUG
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sync test harness (Debug builds only): dev console commands for things vanilla offers no command for, run on a client with the server's
///     <c>/console &lt;player&gt; &lt;command&gt;</c>:<br/>
///     - <c>ongoalcomplete &lt;key&gt;</c> calls <c>StoryGoalManager.OnGoalComplete(key)</c> directly, like <c>IntroLifepodDirector</c>, <c>PrecursorGlobalKeyActivator</c> and
///     the radio playback do (the path that bypasses <c>StoryGoal.Execute</c>, ST1e)<br/>
///     - <c>where</c> logs the world position of the Aurora, the escape rocket and the lifepod (scene objects the server does not know as entities), so that scenarios can walk to them<br/>
///     - <c>damagecyclops [amount]</c> hurts the nearest Cyclops like a creature hit would (it then places damage points and fires), <c>cyclopsstate</c> logs health, damage points and
///     fires of every loaded Cyclops (V2: compare the two clients)<br/>
///     - <c>startfire</c> lights a fire in the first room of the nearest Cyclops (SubFire.CreateFire), <c>dousefires [amount]</c> douses every fire of it with the given amount
///     (the local player's extinguisher does the same call every frame)<br/>
///     - <c>watchcyclops</c> logs health and the destroyed flag of every loaded Cyclops twice a second for 60 s (when does each client blow it up?)<br/>
///     - <c>autowalk [seconds] [abrupt]</c> walks the local pawn in a Cyclops back and forth along the deck without key input (a smooth sine by default, "abrupt" = 2.5 m/s and instant reversals every 1.2 s), so one client can walk while the other is driven by keys<br/>
///     - <c>pilotdump</c> logs where the local player, the chairs and every remote player (body, head, pawn) are, to compare the two sides of a piloting player<br/>
///     - <c>remotedump</c> logs for every remote player where the body is (world, local to its parent, rotation), the pawn in a Cyclops, whether the rigidbody is kinematic and what velocity it has, and the velocity and
///     <c>move_speed</c> the animation plays (PL5: a body of a player who does not broadcast must not sit at the origin; PL7: it must not drift or swim in place)<br/>
///     - <c>mutemove [delay,]seconds</c> stops this client's <c>PlayerMovementBroadcaster</c> after the delay for that many seconds, as death, freecam or a stall do in the middle of whatever the player did
///     (typing a command into the console stops a walk first, so a player cannot be made to go quiet while moving by hand)<br/>
///     - <c>resact &lt;action&gt; [name]</c> acts on the nearest loaded entity (within 40 m) whose name or tech type contains the text, with the same calls the hand and the tools make so the Nitrox
///     patches fire: <c>list</c>, <c>state</c>, <c>kill</c> (LiveMixin damage, like the knife), <c>break</c> (BreakableResource.BreakIntoResources), <c>pickup</c> (Inventory.Pickup),
///     <c>drill:N</c> (N hits of Drillable.OnDrill, 5 hp each of the 200 of the closest chunk). <c>trackerhas &lt;id&gt;</c> says whether the scanner room database still lists an id (R8)<br/>
///     - <c>spawnskips</c> logs how many world entities were not spawned because their cell was not awake, by state of the cell (R4: <c>InAwakeAsync</c> should stay at zero)<br/>
///     - <c>repairpod</c> welds the lifepod and its radio to full health through <c>LiveMixin.AddHealth</c>, the call a welding tool makes (ST1d)<br/>
///     - <c>sunbeamrange</c> logs the Sunbeam landing site and radius, the distance of the local player and of every remote player to it, what this client would propose for the range check
///     and the state (completed, scheduled, in the PDA log) of the range check goal and of its two outcome goals (ST1h). <c>where</c> logs the landing site too, so scenarios can walk to it<br/>
///     - <c>introdump</c> logs the intro state of this client (intro mode, the pod it is in, the pods and burning fires around, the pod of every remote player), for the <c>intro-pair</c> scenario (V2: two new players with the intro)<br/>
///     - the story sweep (<c>story-sweep</c> scenario): <c>teleporter &lt;identifier&gt;</c> activates a Precursor teleporter pair, <c>rocketbuild [stages]</c> and <c>rocketchecks</c> build the rocket and complete
///     its preflight checks without ingredients, <c>goalitems</c> lists the goals whose unlock gives items (ST23d)
/// </summary>
public partial class HarnessConsoleCommands : MonoBehaviour
{
    private bool movementMuteActive;
    private PlayerMovementBroadcaster mutedBroadcaster;

    private void Awake()
    {
        DevConsole.RegisterConsoleCommand("ongoalcomplete", OnConsoleCommand_ongoalcomplete, true, false);
        DevConsole.RegisterConsoleCommand("repairpod", OnConsoleCommand_repairpod, false, false);
        DevConsole.RegisterConsoleCommand("sunbeamrange", OnConsoleCommand_sunbeamrange, false, false);
        DevConsole.RegisterConsoleCommand("introdump", OnConsoleCommand_introdump, false, false);
        DevConsole.RegisterConsoleCommand("vehicles", OnConsoleCommand_vehicles, false, false);
        DevConsole.RegisterConsoleCommand("buildvehicle", OnConsoleCommand_buildvehicle, false, false);
        DevConsole.RegisterConsoleCommand("dockvehicle", OnConsoleCommand_dockvehicle, false, false);
        DevConsole.RegisterConsoleCommand("undockvehicle", OnConsoleCommand_undockvehicle, false, false);
        DevConsole.RegisterConsoleCommand("refill", OnConsoleCommand_refill, false, false);
        DevConsole.RegisterConsoleCommand("bases", OnConsoleCommand_bases, false, false);
        DevConsole.RegisterConsoleCommand("groundprobe", OnConsoleCommand_groundprobe, false, false);
        DevConsole.RegisterConsoleCommand("buildplace", OnConsoleCommand_buildplace, false, false);
        DevConsole.RegisterConsoleCommand("buildfinish", OnConsoleCommand_buildfinish, false, false);
        DevConsole.RegisterConsoleCommand("buildmodule", OnConsoleCommand_buildmodule, false, false);
        DevConsole.RegisterConsoleCommand("enterbase", OnConsoleCommand_enterbase, false, false);
        DevConsole.RegisterConsoleCommand("basecolliders", OnConsoleCommand_basecolliders, false, false);
        DevConsole.RegisterConsoleCommand("dropbase", OnConsoleCommand_dropbase, false, false);
        DevConsole.RegisterConsoleCommand("resyncbuild", OnConsoleCommand_resyncbuild, false, false);
        DevConsole.RegisterConsoleCommand("lockeradd", OnConsoleCommand_lockeradd, false, false);
        DevConsole.RegisterConsoleCommand("drainpower", OnConsoleCommand_drainpower, false, false);
        DevConsole.RegisterConsoleCommand("bioreactoradd", OnConsoleCommand_bioreactoradd, false, false);
        DevConsole.RegisterConsoleCommand("reactorburn", OnConsoleCommand_reactorburn, false, false);
        DevConsole.RegisterConsoleCommand("filterpoke", OnConsoleCommand_filterpoke, false, false);
        DevConsole.RegisterConsoleCommand("filterstate", OnConsoleCommand_filterstate, false, false);
        DevConsole.RegisterConsoleCommand("patchcheck", OnConsoleCommand_patchcheck, false, false);
        DevConsole.RegisterConsoleCommand("lockinfo", OnConsoleCommand_lockinfo, false, false);
        DevConsole.RegisterConsoleCommand("beaconlabel", OnConsoleCommand_beaconlabel, false, false);
        DevConsole.RegisterConsoleCommand("beaconlist", OnConsoleCommand_beaconlist, false, false);
        DevConsole.RegisterConsoleCommand("toolinfo", OnConsoleCommand_toolinfo, false, false);
        DevConsole.RegisterConsoleCommand("pickdrop", OnConsoleCommand_pickdrop, false, false);
        DevConsole.RegisterConsoleCommand("fabricate", OnConsoleCommand_fabricate, false, false);
        DevConsole.RegisterConsoleCommand("plant", OnConsoleCommand_plant, false, false);
        DevConsole.RegisterConsoleCommand("pdastate", OnConsoleCommand_pdastate, false, false);
        DevConsole.RegisterConsoleCommand("scanfragment", OnConsoleCommand_scanfragment, false, false);
        DevConsole.RegisterConsoleCommand("where", OnConsoleCommand_where, false, false);
        DevConsole.RegisterConsoleCommand("damagecyclops", OnConsoleCommand_damagecyclops, false, false);
        DevConsole.RegisterConsoleCommand("cyclopsstate", OnConsoleCommand_cyclopsstate, false, false);
        DevConsole.RegisterConsoleCommand("startfire", OnConsoleCommand_startfire, false, false);
        DevConsole.RegisterConsoleCommand("watchcyclops", OnConsoleCommand_watchcyclops, false, false);
        DevConsole.RegisterConsoleCommand("autowalk", OnConsoleCommand_autowalk, false, false);
        DevConsole.RegisterConsoleCommand("pilotdump", OnConsoleCommand_pilotdump, false, false);
        DevConsole.RegisterConsoleCommand("remotedump", OnConsoleCommand_remotedump, false, false);
        DevConsole.RegisterConsoleCommand("mutemove", OnConsoleCommand_mutemove, false, false);
        DevConsole.RegisterConsoleCommand("dousefires", OnConsoleCommand_dousefires, false, false);
        DevConsole.RegisterConsoleCommand("resact", OnConsoleCommand_resact, false, false);
        DevConsole.RegisterConsoleCommand("warpto", OnConsoleCommand_warpto, false, false);
        DevConsole.RegisterConsoleCommand("trackerhas", OnConsoleCommand_trackerhas, false, false);
        DevConsole.RegisterConsoleCommand("spawnskips", OnConsoleCommand_spawnskips, false, false);
        DevConsole.RegisterConsoleCommand("holdsettled", OnConsoleCommand_holdsettled, false, false);
        DevConsole.RegisterConsoleCommand("subreject", OnConsoleCommand_subreject, false, false);
        DevConsole.RegisterConsoleCommand("freezeinfo", OnConsoleCommand_freezeinfo, false, false);
        DevConsole.RegisterConsoleCommand("physprobe", OnConsoleCommand_physprobe, false, false);
        DevConsole.RegisterConsoleCommand("teleporter", OnConsoleCommand_teleporter, false, false);
        DevConsole.RegisterConsoleCommand("rocketbuild", OnConsoleCommand_rocketbuild, false, false);
        DevConsole.RegisterConsoleCommand("rocketchecks", OnConsoleCommand_rocketchecks, false, false);
        DevConsole.RegisterConsoleCommand("goalitems", OnConsoleCommand_goalitems, false, false);
        RegisterLiveBatchCommands();
    }

    private int fixedUpdateCount;

    private void FixedUpdate()
    {
        fixedUpdateCount++;
    }

    /// <summary>
    ///     <c>physprobe [seconds]</c> (PL10): measures for that long (default 10 s) how many frames and how many physics steps this client runs per second, and logs it with the state that
    ///     decides the step: where the player is (Cyclops or not), their mode, whether the game wants high precision physics, <c>Time.fixedDeltaTime</c> and <c>Physics.autoSyncTransforms</c>.
    ///     Run it three times for the Cyclops question (#2832): standing outside, walking inside a moving Cyclops, and piloting it, and compare <c>fps</c> and <c>steps/s</c>.
    /// </summary>
    private void OnConsoleCommand_physprobe(NotificationCenter.Notification n)
    {
        float seconds = 10f;
        if (n?.data != null && n.data.Count > 0 && n.data[0] is string text)
        {
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
        }
        StartCoroutine(PhysicsProbe(Mathf.Clamp(seconds, 1f, 120f)));
    }

    private System.Collections.IEnumerator PhysicsProbe(float seconds)
    {
        int startFrame = Time.frameCount;
        int startSteps = fixedUpdateCount;
        float startTime = Time.realtimeSinceStartup;
        yield return new WaitForSecondsRealtime(seconds);
        float elapsed = Time.realtimeSinceStartup - startTime;

        Player player = Player.main;
        string where = player && player.currentSub ? $"sub {player.currentSub.name} (cyclops {player.currentSub.isCyclops})" : "outside a sub";
        string mode = player ? player.mode.ToString() : "no player";
        string wantsHighPrecision = player ? player.RequiresHighPrecisionPhysics().ToString() : "n/a";
        float fps = (Time.frameCount - startFrame) / elapsed;
        float stepsPerSecond = (fixedUpdateCount - startSteps) / elapsed;
        Log.Info($"[SyncHarness] physprobe: {where}, mode {mode}, requiresHighPrecision {wantsHighPrecision}, fixedDeltaTime {Time.fixedDeltaTime:F3}, autoSyncTransforms {Physics.autoSyncTransforms}, fps {fps:F1}, steps/s {stepsPerSecond:F1} over {elapsed:F1} s");
    }

    /// <summary>
    ///     Until this unscaled time <c>LargeWorldStreamer.IsWorldSettled()</c> answers "not settled" (read by <c>Harness_LargeWorldStreamer_IsWorldSettled_Hold_Patch</c>).
    /// </summary>
    public static float HoldWorldSettledUntil;

    /// <summary>
    ///     <c>holdsettled seconds</c> makes the world look unsettled for that long, which is what the teleport freeze sees when the streamer hangs (PL4): the freeze has to end after
    ///     <see cref="GameLogic.Terrain.TELEPORT_WORLD_LOAD_TIMEOUT" /> although the world is not settled. <c>holdsettled 0</c> lifts it.
    /// </summary>
    private void OnConsoleCommand_holdsettled(NotificationCenter.Notification n)
    {
        if (!float.TryParse(Arg(n, 0), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
        {
            Log.Warn("[SyncHarness] holdsettled: needs the number of seconds");
            return;
        }
        HoldWorldSettledUntil = seconds > 0f ? Time.unscaledTime + seconds : 0f;
        Log.Info($"[SyncHarness] holdsettled: the world looks unsettled for {Mathf.Max(0f, seconds):F0} s");
    }

    /// <summary>
    ///     <c>freezeinfo</c> logs what freezes the local player (PL3, PL4): <c>cinematicModeActive</c>, the teleport wait of the game, whether the world is settled, and the position
    /// </summary>
    /// <summary>
    ///     <c>subreject</c> (PL8): the player, standing in a base, leaves it by a forced change and is put back by an unforced one in the same frame. Vanilla refuses the second change
    ///     (<c>CanChangeSub</c>: fixedUpdateSinceLastChangedSub is 0 after the first), so the player is outside; the server must not be told that the player is in the base.
    /// </summary>
    private void OnConsoleCommand_subreject(NotificationCenter.Notification n)
    {
        SubRoot target = Player.main.currentSub;
        if (!target)
        {
            Log.Warn("[SyncHarness] subreject: the player is not in a base or a sub (use enterbase first)");
            return;
        }
        Player.main.SetCurrentSub(null, true);
        Player.main.SetCurrentSub(target, false);
        Log.Info($"[SyncHarness] subreject: asked to enter {target.name}, the player's current sub is now {(Player.main.currentSub ? Player.main.currentSub.name : "none")}");
    }

    private void OnConsoleCommand_freezeinfo(NotificationCenter.Notification n)
    {
        LargeWorldStreamer streamer = LargeWorldStreamer.main;
        Log.Info($"[SyncHarness] freezeinfo: t {Time.unscaledTime:F1} cinematicModeActive {Player.main.cinematicModeActive}, isWaitingForTeleportation {Player.main.isWaitingForTeleportation}, world settled {(streamer && streamer.IsWorldSettled())}, at {Player.main.transform.position.ToString("F1")}");
    }

    private static string Arg(NotificationCenter.Notification n, int index) => n?.data != null && n.data.Count > index && n.data[index] is string text ? text : "";

    private static string Flags(GameObject go)
    {
        string flags = "";
        flags += go.GetComponent<BreakableResource>() ? "B" : "";
        flags += go.GetComponent<Drillable>() ? "D" : "";
        flags += go.GetComponent<Pickupable>() ? "P" : "";
        flags += go.GetComponent<LiveMixin>() ? "L" : "";
        flags += go.GetComponent<ResourceTracker>() ? "T" : "";
        flags += go.GetComponent<Constructable>() ? "C" : "";
        return flags;
    }

    private static string ChunkText(Drillable drillable) => drillable && drillable.health != null ? string.Join(",", System.Array.ConvertAll(drillable.health, h => h.ToString("F0"))) : "-";

    private static bool TrackerListed(string uid)
    {
        System.Reflection.FieldInfo field = typeof(ResourceTrackerDatabase).GetField("resources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if (field?.GetValue(null) is not System.Collections.IDictionary byTech)
        {
            return false;
        }
        foreach (object inner in byTech.Values)
        {
            if (inner is System.Collections.IDictionary byId && byId.Contains(uid))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    ///     <c>warpto x,y,z</c> puts the local player at that place (the server's <c>/console</c> passes at most two words to a command, <c>/teleport</c> only moves the player who types it).
    /// </summary>
    private void OnConsoleCommand_warpto(NotificationCenter.Notification n)
    {
        string[] parts = Arg(n, 0).Split(',');
        if (parts.Length != 3 ||
            !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
        {
            Log.Warn("[SyncHarness] warpto: needs x,y,z (no spaces)");
            return;
        }
        Player.main.OnPlayerPositionCheat();
        Player.main.SetPosition(new Vector3(x, y, z));
        Log.Info($"[SyncHarness] warpto: now at {Player.main.transform.position.ToString("F1")}");
    }

    private static System.Collections.IEnumerator DeconstructAll(Constructable constructable, NitroxId id)
    {
        for (int step = 0; step < 900 && constructable; step++)
        {
            if (constructable.deconstructCoroutineRunning)
            {
                yield return null;
                continue;
            }
            yield return constructable.DeconstructAsync(new TaskResult<bool>(), new TaskResult<string>());
        }
        Log.Info($"[SyncHarness] resact: deconstruct of {id} done: {(constructable ? $"still standing, amount {constructable.constructedAmount:F2}" : "destroyed")}");
    }

    private static System.Collections.IEnumerator DestroyLater(GameObject target, NitroxId id)
    {
        yield return new WaitForSeconds(1f);
        if (target)
        {
            Log.Info($"[SyncHarness] resact: destroying {id} locally, like an unloading cell would");
            Destroy(target);
        }
        else
        {
            Log.Info($"[SyncHarness] resact: {id} was destroyed already");
        }
    }

    private void OnConsoleCommand_trackerhas(NotificationCenter.Notification n)
    {
        string uid = Arg(n, 0);
        Log.Info($"[SyncHarness] trackerhas: {uid} listed in the resource tracker database: {TrackerListed(uid)}");
    }

    /// <summary>
    ///     <c>spawnskips</c> (R4): how many world entities were not spawned because their cell was not awake, by the state the cell was in. <c>InAwakeAsync</c> (and <c>InAwakeAsyncToSleep</c>)
    ///     staying at zero is what shows entities never arrive while a cell is still awakening; <c>IsAsleep</c>, <c>InSleepAsync</c>, <c>QueuedForAwake</c> and <c>InSleepAsyncToAwake</c> are
    ///     answers that came after the player left the cell, which the next load of the cell repairs.
    /// </summary>
    private void OnConsoleCommand_spawnskips(NotificationCenter.Notification n)
    {
        NitroxClient.GameLogic.Entities entities = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.Entities>();
        NitroxClient.GameLogic.Spawning.SkippedSpawnTracker skipped = entities.SkippedSpawns;
        int awakening = skipped.TotalInState("InAwakeAsync") + skipped.TotalInState("InAwakeAsyncToSleep");
        Log.Info($"[SyncHarness] spawnskips: {skipped.Describe()}; skipped while the cell was awakening: {awakening}; waiting for a parent: {entities.PendingParentEntityCount}; queued to spawn: {entities.EntitiesToSpawn.Count}");

        // R4b: what the entities that wait for a parent are (by type and tech type), whether the parent exists by now, and whether the entity itself is already in the world
        System.Collections.Generic.Dictionary<string, int> byKind = new();
        System.Collections.Generic.HashSet<NitroxId> parents = new();
        int parentsPresent = 0;
        int spawnedAlready = 0;
        foreach ((NitroxId parentId, Nitrox.Model.Subnautica.DataStructures.GameLogic.Entity pending) in entities.PendingParents.Entries)
        {
            if (parents.Add(parentId) && NitroxEntity.TryGetObjectFrom(parentId, out GameObject _))
            {
                parentsPresent++;
            }
            spawnedAlready += NitroxEntity.TryGetObjectFrom(pending.Id, out GameObject _) ? 1 : 0;
            string kind = $"{pending.GetType().Name}/{pending.TechType}";
            byKind[kind] = byKind.TryGetValue(kind, out int count) ? count + 1 : 1;
        }
        System.Collections.Generic.List<string> top = new(byKind.Keys);
        top.Sort((a, b) => byKind[b].CompareTo(byKind[a]));
        Log.Info($"[SyncHarness] spawnskips: pending parents {entities.PendingParents.Count} entries under {parents.Count} parents, {parentsPresent} of those parents exist now, {spawnedAlready} of the entries are in the world already; most common: {string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(top, 6), kind => $"{kind} x{byKind[kind]}"))}");
    }

    private void OnConsoleCommand_resact(NotificationCenter.Notification n)
    {
        string action = Arg(n, 0);
        string part = Arg(n, 1);
        int count = 1;
        int colon = action.IndexOf(':');
        if (colon >= 0)
        {
            int.TryParse(action.Substring(colon + 1), out count);
            action = action.Substring(0, colon);
        }

        Vector3 here = Player.main.transform.position;
        System.Collections.Generic.List<(NitroxId Id, GameObject Go, float Distance)> found = [];
        foreach (System.Collections.Generic.KeyValuePair<NitroxId, GameObject> pair in NitroxEntity.GetGameObjects())
        {
            GameObject go = pair.Value;
            if (!go)
            {
                continue;
            }
            float distance = Vector3.Distance(go.transform.position, here);
            if (distance > 40f)
            {
                continue;
            }
            // Items in somebody's inventory and the players themselves are entities too, they are not what a resource scenario acts on
            Pickupable held = go.GetComponent<Pickupable>();
            if ((held && held.attached) || go.GetComponent<Player>() || go.GetComponent<NitroxClient.GameLogic.PlayerLogic.RemotePlayerIdentifier>())
            {
                continue;
            }
            string tech = CraftData.GetTechType(go).ToString();
            if (part != "" && go.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0 && tech.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                !pair.Key.ToString().StartsWith(part, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            found.Add((pair.Key, go, distance));
        }
        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        if (action == "list")
        {
            foreach ((NitroxId id, GameObject go, float distance) in found.GetRange(0, System.Math.Min(15, found.Count)))
            {
                Log.Info($"[SyncHarness] resact: {go.name} tech {CraftData.GetTechType(go)} id {id} at {go.transform.position.ToString("F2")} dist {distance:F1} flags [{Flags(go)}]");
            }
            Log.Info($"[SyncHarness] resact: list '{part}': {found.Count} entities within 40 m");
            return;
        }

        if (action == "items")
        {
            // the loose items (Pickupable, not in an inventory) around, nearest first
            int listed = 0;
            foreach ((NitroxId id, GameObject go, float distance) in found)
            {
                if (go.GetComponent<Pickupable>() && listed++ < 8)
                {
                    Log.Info($"[SyncHarness] resact: item {go.name} tech {CraftData.GetTechType(go)} id {id} at {go.transform.position.ToString("F2")} dist {distance:F1}");
                }
            }
            Log.Info($"[SyncHarness] resact: items '{part}': {listed} loose items within 40 m");
            return;
        }

        if (action == "constructables")
        {
            // world props with a Constructable (what the builder tool deconstructs), R1
            int listed = 0;
            foreach ((NitroxId id, GameObject go, float distance) in found)
            {
                if (go.GetComponent<Constructable>() && listed++ < 12)
                {
                    Log.Info($"[SyncHarness] resact: constructable {go.name} tech {CraftData.GetTechType(go)} id {id} at {go.transform.position.ToString("F2")} dist {distance:F1} amount {go.GetComponent<Constructable>().constructedAmount:F2}");
                }
            }
            Log.Info($"[SyncHarness] resact: constructables '{part}': {listed} within 40 m");
            return;
        }

        if (action == "lives")
        {
            // objects with a LiveMixin that are not creatures: what a knife or the drill arm can kill (R3)
            int listed = 0;
            foreach ((NitroxId id, GameObject go, float distance) in found)
            {
                if (go.GetComponent<LiveMixin>() && !go.GetComponent<Creature>() && !go.GetComponent<CreatureDeath>() && listed++ < 12)
                {
                    Log.Info($"[SyncHarness] resact: lives {go.name} tech {CraftData.GetTechType(go)} id {id} at {go.transform.position.ToString("F2")} dist {distance:F1} flags [{Flags(go)}]");
                }
            }
            Log.Info($"[SyncHarness] resact: lives '{part}': {listed} killable non-creatures within 40 m");
            return;
        }

        if (action == "have")
        {
            int have = System.Enum.TryParse(part, true, out TechType techType) ? Inventory.main.container.GetCount(techType) : -1;
            Log.Info($"[SyncHarness] resact: have {part}: {have} in the inventory");
            return;
        }

        // The nearest one the action can act on (a name part like "Copper" also matches the outcrop that dropped it)
        NitroxId targetId = null;
        GameObject target = null;
        float targetDistance = 0f;
        foreach ((NitroxId candidateId, GameObject candidate, float candidateDistance) in found)
        {
            bool fits = action switch
            {
                "pickup" => candidate.GetComponent<Pickupable>(),
                "break" => candidate.GetComponent<BreakableResource>(),
                "drill" => candidate.GetComponent<Drillable>(),
                "kill" or "killaway" or "killunload" or "killdestroy" => candidate.GetComponent<LiveMixin>(),
                "deconstruct" => candidate.GetComponent<Constructable>(),
                _ => true,
            };
            if (fits)
            {
                (targetId, target, targetDistance) = (candidateId, candidate, candidateDistance);
                break;
            }
        }
        if (!target)
        {
            Log.Warn($"[SyncHarness] resact: {action} '{part}': nothing within 40 m");
            return;
        }
        string who = $"{target.name} tech {CraftData.GetTechType(target)} id {targetId} at {target.transform.position.ToString("F2")} dist {targetDistance:F1} flags [{Flags(target)}]";

        switch (action)
        {
            case "state":
            {
                LiveMixin live = target.GetComponent<LiveMixin>();
                Log.Info($"[SyncHarness] resact: state {who}, active {target.activeInHierarchy}, health {(live ? live.health.ToString("F0") : "-")}, chunks [{ChunkText(target.GetComponent<Drillable>())}], tracker listed {TrackerListed(targetId.ToString())}");
                Rigidbody body = target.GetComponent<Rigidbody>();
                if (body)
                {
                    System.Collections.Generic.List<string> components = [];
                    foreach (Component component in target.GetComponents<Component>())
                    {
                        string componentName = component ? component.GetType().Name : "missing";
                        if (componentName is not ("Transform" or "MeshFilter" or "MeshRenderer" or "Rigidbody"))
                        {
                            components.Add(componentName);
                        }
                    }
                    Log.Info($"[SyncHarness] resact: physics of {targetId}: kinematic {body.isKinematic}, sleeping {body.IsSleeping()}, gravity {body.useGravity}, velocity {body.velocity.ToString("F2")}, drag {body.drag}, components [{string.Join(",", components)}]");
                }
                break;
            }
            case "kill":
            {
                LiveMixin live = target.GetComponent<LiveMixin>();
                if (!live)
                {
                    Log.Warn($"[SyncHarness] resact: kill: no LiveMixin on {who}");
                    break;
                }
                Log.Info($"[SyncHarness] resact: kill {who}, health {live.health:F0}");
                // A hit of 10000 took a coral plate from 50 to 25 hp only (damage is scaled on the way), so hit until it is dead like a knife would
                for (int hit = 0; hit < 12 && live && live.IsAlive(); hit++)
                {
                    live.TakeDamage(10000f, target.transform.position, DamageType.Normal, Player.main.gameObject);
                }
                Log.Info($"[SyncHarness] resact: kill of {targetId} done, health now {(live ? live.health.ToString("F0") : "destroyed")}");
                break;
            }
            case "sendmodify":
            {
                // R1, synthetic: the packet the builder tool sends when a deconstruction reaches zero, for any world entity (no Constructable needed; the loaded cells near the lifepod hold none)
                bool sent = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.Communication.Abstract.IPacketSender>().Send(new Nitrox.Model.Subnautica.Packets.ModifyConstructedAmount(targetId, 0f));
                Log.Info($"[SyncHarness] resact: sendmodify {who}, ModifyConstructedAmount(0) sent {sent}");
                break;
            }
            case "deconstruct":
            {
                // R1: what the builder tool does to a wreck prop it is aimed at (Constructable.DeconstructAsync once per call until the amount is used up and the prop is destroyed)
                Constructable constructable = target.GetComponent<Constructable>();
                if (!constructable)
                {
                    Log.Warn($"[SyncHarness] resact: deconstruct: no Constructable on {who}");
                    break;
                }
                Log.Info($"[SyncHarness] resact: deconstruct {who}, constructed {constructable.constructed}, amount {constructable.constructedAmount:F2}");
                StartCoroutine(DeconstructAll(constructable, targetId));
                break;
            }
            case "killaway":
            case "killunload":
            case "killdestroy":
            {
                // R3b: the watch that follows a kill announces what vanishes within 3 s only while the player is within 60 m of the place of death (a far player most likely lost the object to a cell unload).
                // killaway: kill and move the player 700 m away in the same frame (what vanishes with the kill is not announced, the documented price of the rule);
                // killunload: the same, and an object that survives its kill is destroyed locally 1 s later like an unloading cell would (must NOT be announced);
                // killdestroy: control, the player stays and an object that survives its kill is destroyed locally 1 s later (the watch announces it).
                LiveMixin live = target.GetComponent<LiveMixin>();
                if (!live)
                {
                    Log.Warn($"[SyncHarness] resact: {action}: no LiveMixin on {who}");
                    break;
                }
                Log.Info($"[SyncHarness] resact: kill {who}, health {live.health:F0} ({action})");
                for (int hit = 0; hit < 12 && live && live.IsAlive(); hit++)
                {
                    live.TakeDamage(10000f, target.transform.position, DamageType.Normal, Player.main.gameObject);
                }
                Log.Info($"[SyncHarness] resact: kill of {targetId} done, health now {(live ? live.health.ToString("F0") : "destroyed")}");
                if (action != "killdestroy")
                {
                    Player.main.OnPlayerPositionCheat();
                    Player.main.SetPosition(new Vector3(700f, 0f, 700f));
                    Log.Info($"[SyncHarness] resact: {action}: player moved to {Player.main.transform.position.ToString("F0")}");
                }
                if (action != "killaway" && target)
                {
                    StartCoroutine(DestroyLater(target, targetId));
                }
                break;
            }
            case "break":
            {
                BreakableResource breakable = target.GetComponent<BreakableResource>();
                if (!breakable)
                {
                    Log.Warn($"[SyncHarness] resact: break: no BreakableResource on {who}");
                    break;
                }
                Log.Info($"[SyncHarness] resact: break {who}");
                breakable.BreakIntoResources();
                break;
            }
            case "pickup":
            {
                Pickupable pickupable = target.GetComponent<Pickupable>();
                if (!pickupable)
                {
                    Log.Warn($"[SyncHarness] resact: pickup: no Pickupable on {who}");
                    break;
                }
                Log.Info($"[SyncHarness] resact: pickup {who}");
                bool picked = Inventory.main.Pickup(pickupable, false);
                Log.Info($"[SyncHarness] resact: pickup of {targetId} returned {picked}");
                break;
            }
            case "drill":
            {
                Drillable drillable = target.GetComponent<Drillable>();
                if (!drillable)
                {
                    Log.Warn($"[SyncHarness] resact: drill: no Drillable on {who}");
                    break;
                }
                Log.Info($"[SyncHarness] resact: drill {count} hits on {who}");
                for (int i = 0; i < count && drillable; i++)
                {
                    drillable.OnDrill(drillable.transform.position, null, out GameObject _);
                }
                Log.Info($"[SyncHarness] resact: drilled, chunks [{ChunkText(drillable)}]");
                break;
            }
            default:
                Log.Warn($"[SyncHarness] resact: unknown action '{action}' (list, state, kill, break, pickup, drill:N)");
                break;
        }
    }

    private void OnConsoleCommand_autowalk(NotificationCenter.Notification n)
    {
        float seconds = 30f;
        if (n?.data != null && n.data.Count > 0 && n.data[0] is string text)
        {
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
        }
        bool abrupt = n?.data != null && n.data.Count > 1 && n.data[1] is string mode && mode == "abrupt";
        StartCoroutine(AutoWalk(seconds, abrupt));
    }

    /// <summary>
    ///     Walks up and down the deck. Default: a sine of 2.5 m amplitude and 5 s period (peak speed 3.1 m/s, smooth turnarounds). "abrupt": 2.5 m/s in one direction for 1.2 s, then at once the other
    ///     way: a velocity reversal that no predictor can follow, the error at the reversal is twice the speed times the delay (2 x 2.5 m/s x 0.13 s = 0.65 m at 50-80 ms one way).
    /// </summary>
    private System.Collections.IEnumerator AutoWalk(float seconds, bool abrupt)
    {
        Log.Info($"[SyncHarness] autowalk: {seconds:F0} s{(abrupt ? ", abrupt reversals" : ", smooth")}");
        float start = Time.time;
        float end = start + seconds;
        float phaseStart = start;
        float sign = 1f;
        const float AMPLITUDE = 2.5f;
        const float PERIOD = 5f;
        while (Time.time < end)
        {
            yield return new WaitForFixedUpdate();
            if (!Player.main || !Player.main.TryGetComponent(out NitroxClient.MonoBehaviours.Cyclops.CyclopsMotor motor) || motor.Pawn == null || !motor.canControl || Player.main.isPiloting)
            {
                continue;
            }
            float velocity;
            if (abrupt)
            {
                if (Time.time - phaseStart > 1.2f)
                {
                    sign = -sign;
                    phaseStart = Time.time;
                }
                velocity = sign * 2.5f;
            }
            else
            {
                float omega = 2f * Mathf.PI / PERIOD;
                velocity = AMPLITUDE * omega * Mathf.Cos(omega * (Time.time - start));
            }
            // The pawn walks in the motionless copy of the Cyclops, whose axes are the Cyclops's own: +z is towards the stern
            motor.Pawn.Controller.Move(new Vector3(0f, 0f, velocity * Time.fixedDeltaTime));
        }
        Log.Info("[SyncHarness] autowalk: done");
    }

    private void OnConsoleCommand_pilotdump(NotificationCenter.Notification n)
    {
        Player local = Player.main;
        Log.Info($"[SyncHarness] pilotdump: local player {local.transform.position.ToString("F2")}, piloting {local.isPiloting}, parent {(local.transform.parent ? local.transform.parent.name : "none")}, camera {MainCamera.camera.transform.position.ToString("F2")}");
        Log.Info($"[SyncHarness] pilotdump: local player local position {local.transform.localPosition.ToString("F2")}, parent world {(local.transform.parent ? local.transform.parent.position.ToString("F2") : "none")}, parent local {(local.transform.parent ? local.transform.parent.localPosition.ToString("F2") : "none")}, grandparent {(local.transform.parent && local.transform.parent.parent ? local.transform.parent.parent.name : "none")}");
        foreach (PilotingChair chair in FindObjectsOfType<PilotingChair>())
        {
            Log.Info($"[SyncHarness] pilotdump: chair {chair.transform.position.ToString("F2")}, sittingPosition {(chair.sittingPosition ? chair.sittingPosition.transform.position.ToString("F2") : "none")}, sub {(chair.subRoot ? chair.subRoot.transform.position.ToString("F2") : "none")}");
        }
        foreach (NitroxClient.GameLogic.RemotePlayer remote in this.Resolve<NitroxClient.GameLogic.PlayerManager>().GetAll())
        {
            Transform head = null;
            foreach (Transform child in remote.PlayerModel.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.ToLowerInvariant().Contains("head"))
                {
                    head = child;
                    break;
                }
            }
            System.Collections.Generic.List<string> bones = [];
            foreach (Transform child in remote.PlayerModel.GetComponentsInChildren<Transform>(true))
            {
                string lower = child.name.ToLowerInvariant();
                if (lower is "head" or "neck" or "hips" or "pelvis" or "spine" or "root" or "head_end" || lower.EndsWith("_head") || lower.EndsWith("_hips") || lower.EndsWith("_pelvis"))
                {
                    bones.Add($"{child.name}={child.position.ToString("F2")}");
                }
            }
            Log.Info($"[SyncHarness] pilotdump: remote {remote.PlayerName} body local {remote.Body.transform.localPosition.ToString("F2")}, parent world {(remote.Body.transform.parent ? remote.Body.transform.parent.position.ToString("F2") : "none")}, bones [{string.Join(", ", bones)}]");
            string headText = head ? $"{head.name} {head.position.ToString("F2")}" : "none";
            Log.Info($"[SyncHarness] pilotdump: remote {remote.PlayerName} body {remote.Body.transform.position.ToString("F2")} parent {(remote.Body.transform.parent ? remote.Body.transform.parent.name : "none")}, model {remote.PlayerModel.transform.position.ToString("F2")}");
            Log.Info($"[SyncHarness] pilotdump: remote {remote.PlayerName} head {headText}, chair {(remote.PilotingChair ? remote.PilotingChair.name : "none")}");
            Log.Info($"[SyncHarness] pilotdump: remote {remote.PlayerName} pawn handle {(remote.Pawn != null && remote.Pawn.Handle ? remote.Pawn.Handle.transform.position.ToString("F2") : "none")}");
        }
    }

    private void OnConsoleCommand_remotedump(NotificationCenter.Notification n)
    {
        foreach (NitroxClient.GameLogic.RemotePlayer remote in this.Resolve<NitroxClient.GameLogic.PlayerManager>().GetAll())
        {
            if (!remote.Body)
            {
                Log.Info($"[SyncHarness] remotedump: {remote.PlayerName} has no body");
                continue;
            }

            Transform body = remote.Body.transform;
            Animator animator = remote.PlayerModel ? remote.PlayerModel.GetComponent<Animator>() : null;
            string rigidbody = remote.RigidBody ? $"kinematic {remote.RigidBody.isKinematic}, velocity {remote.RigidBody.velocity.ToString("F2")}" : "none";
            string pawn = remote.Pawn != null && remote.Pawn.Handle ? remote.Pawn.Handle.transform.localPosition.ToString("F2") : "none";
            Log.Info($"[SyncHarness] remotedump: {remote.PlayerName} body {body.position.ToString("F2")} euler {body.rotation.eulerAngles.ToString("F0")} local {body.localPosition.ToString("F2")} parent {(body.parent ? body.parent.name : "none")} active {remote.Body.activeInHierarchy} pawn local {pawn}");
            Log.Info($"[SyncHarness] remotedump: {remote.PlayerName} rigidbody {rigidbody}, animation velocity {remote.AnimationController.Velocity.ToString("F2")}, move_speed {(animator ? animator.GetFloat("move_speed").ToString("F2") : "none")}");
        }
    }

    private void OnConsoleCommand_mutemove(NotificationCenter.Notification n)
    {
        string[] parts = Arg(n, 0).Split(',');
        float delay = 0f;
        if (!float.TryParse(parts[parts.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds) ||
            (parts.Length > 1 && !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out delay)))
        {
            Log.Warn("[SyncHarness] mutemove: needs [delay,]seconds (no spaces)");
            return;
        }

        if (movementMuteActive)
        {
            // A second one would switch the broadcaster back on in the middle of the first
            Log.Warn("[SyncHarness] mutemove: already running, wait for \"movement broadcast resumed\"");
            return;
        }

        PlayerMovementBroadcaster broadcaster = FindObjectOfType<PlayerMovementBroadcaster>();
        if (!broadcaster)
        {
            Log.Warn("[SyncHarness] mutemove: no PlayerMovementBroadcaster (not in a multiplayer session)");
            return;
        }
        StartCoroutine(MuteMovement(broadcaster, delay, seconds));
    }

    private System.Collections.IEnumerator MuteMovement(PlayerMovementBroadcaster broadcaster, float delay, float seconds)
    {
        movementMuteActive = true;
        yield return new WaitForSeconds(delay);
        if (!broadcaster)
        {
            // Left the session during the delay
            movementMuteActive = false;
            yield break;
        }

        broadcaster.enabled = false;
        mutedBroadcaster = broadcaster;
        Log.Info($"[SyncHarness] mutemove: movement broadcast stopped for {seconds:F0} s, player at {(Player.main ? Player.main.transform.position.ToString("F2") : "unknown")}");
        yield return new WaitForSeconds(seconds);
        if (broadcaster)
        {
            broadcaster.enabled = true;
        }
        mutedBroadcaster = null;
        movementMuteActive = false;
        Log.Info("[SyncHarness] mutemove: movement broadcast resumed");
    }

    private void OnDestroy()
    {
        // The coroutine dies with this component: do not leave the player silent for the rest of the session
        if (mutedBroadcaster)
        {
            mutedBroadcaster.enabled = true;
        }
    }

    private void OnConsoleCommand_watchcyclops(NotificationCenter.Notification n)
    {
        StartCoroutine(WatchCyclops());
    }

    private System.Collections.IEnumerator WatchCyclops()
    {
        bool lastDestroyed = false;
        float lastHealth = float.NaN;
        for (int i = 0; i < 120; i++)
        {
            foreach (SubControl subControl in FindObjectsOfType<SubControl>())
            {
                if (!subControl.sub || !subControl.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId _))
                {
                    continue;
                }
                float health = subControl.sub.live ? subControl.sub.live.health : -1f;
                // Log the changes only
                if (subControl.sub.subDestroyed != lastDestroyed || !Mathf.Approximately(health, lastHealth))
                {
                    Log.Info($"[SyncHarness] cyclops-watch: health {health:F0}, subDestroyed {subControl.sub.subDestroyed}");
                    lastDestroyed = subControl.sub.subDestroyed;
                    lastHealth = health;
                }
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    private static SubFire FindNearestSubFire()
    {
        SubFire nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (SubControl subControl in FindObjectsOfType<SubControl>())
        {
            SubFire subFire = subControl.sub ? subControl.sub.GetComponentInChildren<SubFire>(true) : null;
            if (!subFire)
            {
                continue;
            }
            float distance = Vector3.Distance(subControl.transform.position, Player.main.transform.position);
            if (distance < nearestDistance)
            {
                nearest = subFire;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private void OnConsoleCommand_startfire(NotificationCenter.Notification n)
    {
        SubFire subFire = FindNearestSubFire();
        if (!subFire)
        {
            Log.Warn("[SyncHarness] startfire: no Cyclops loaded");
            return;
        }
        foreach (System.Collections.Generic.KeyValuePair<CyclopsRooms, SubFire.RoomFire> room in subFire.roomFires)
        {
            subFire.CreateFire(room.Value);
            Log.Info($"[SyncHarness] startfire: fire started in room {room.Key}, fire count now {subFire.fireCount}");
            return;
        }
    }

    private void OnConsoleCommand_dousefires(NotificationCenter.Notification n)
    {
        float amount = 20f;
        if (n?.data != null && n.data.Count > 0 && n.data[0] is string text)
        {
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount);
        }
        SubFire subFire = FindNearestSubFire();
        if (!subFire)
        {
            Log.Warn("[SyncHarness] dousefires: no Cyclops loaded");
            return;
        }
        int count = 0;
        foreach (Fire fire in subFire.subRoot.GetComponentsInChildren<Fire>(true))
        {
            if (fire.gameObject.activeInHierarchy && !fire.IsExtinguished())
            {
                fire.Douse(amount);
                count++;
            }
        }
        Log.Info($"[SyncHarness] dousefires: doused {count} fires with {amount}");
    }

    private void OnConsoleCommand_damagecyclops(NotificationCenter.Notification n)
    {
        float amount = 300f;
        if (n?.data != null && n.data.Count > 0 && n.data[0] is string text)
        {
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount);
        }

        SubControl nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (SubControl subControl in FindObjectsOfType<SubControl>())
        {
            float distance = Vector3.Distance(subControl.transform.position, Player.main.transform.position);
            if (distance < nearestDistance)
            {
                nearest = subControl;
                nearestDistance = distance;
            }
        }
        if (!nearest || !nearest.sub || !nearest.sub.live)
        {
            Log.Warn("[SyncHarness] damagecyclops: no Cyclops loaded");
            return;
        }
        nearest.sub.live.TakeDamage(amount, nearest.transform.position, DamageType.Normal, null);
        Log.Info($"[SyncHarness] damagecyclops: {amount} damage to {nearest.name} {nearestDistance:F0} m away, health now {nearest.sub.live.health:F0}/{nearest.sub.live.maxHealth:F0}");
    }

    private void OnConsoleCommand_cyclopsstate(NotificationCenter.Notification n)
    {
        int cyclops = 0;
        foreach (SubControl subControl in FindObjectsOfType<SubControl>())
        {
            cyclops++;
            CyclopsExternalDamageManager damageManager = subControl.sub ? subControl.sub.GetComponentInChildren<CyclopsExternalDamageManager>(true) : null;
            System.Collections.Generic.List<string> points = [];
            if (damageManager && damageManager.damagePoints != null)
            {
                foreach (CyclopsDamagePoint point in damageManager.damagePoints)
                {
                    if (point && point.gameObject.activeInHierarchy)
                    {
                        points.Add(point.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId pointId) ? pointId.ToString().Substring(0, 8) : "noid");
                    }
                }
            }
            SubFire subFire = subControl.sub ? subControl.sub.GetComponentInChildren<SubFire>(true) : null;
            subControl.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId cyclopsId);
            System.Collections.Generic.List<string> fires = [];
            foreach (Fire fire in subControl.sub ? subControl.sub.GetComponentsInChildren<Fire>(true) : System.Array.Empty<Fire>())
            {
                if (fire.gameObject.activeInHierarchy)
                {
                    fires.Add($"{(fire.transform.parent.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId fireId) ? fireId.ToString().Substring(0, 8) : "noid")}:{fire.livemixin.health:F0}{(fire.IsExtinguished() ? ":out" : "")}");
                }
            }
            Rigidbody body = subControl.GetComponent<Rigidbody>();
            WorldForces forces = subControl.GetComponent<WorldForces>();
            Stabilizer stabilizer = subControl.GetComponent<Stabilizer>();
            Log.Info($"[SyncHarness] cyclops: physics position {subControl.transform.position}, kinematic {(body ? body.isKinematic.ToString() : "none")}, gravity {(body ? body.useGravity.ToString() : "none")}, velocity {(body ? body.velocity : Vector3.zero)}, drag {(body ? body.drag : -1f)}");
            Log.Info($"[SyncHarness] cyclops: physics worldforces {(forces ? forces.enabled.ToString() : "none")}, stabilizer {(stabilizer ? stabilizer.stabilizerEnabled.ToString() : "none")}, replicator {subControl.GetComponent<MovementReplicator>() != null}");
            Log.Info($"[SyncHarness] cyclops: physics simulated here {subControl.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId simId) && this.Resolve<NitroxClient.GameLogic.SimulationOwnership>().HasAnyLockType(simId)}");
            PilotingChair chair = subControl.sub ? subControl.sub.GetComponentInChildren<PilotingChair>(true) : null;
            Log.Info($"[SyncHarness] cyclops: geometry sub {subControl.transform.position} euler {subControl.transform.eulerAngles}, chair {(chair ? chair.transform.position.ToString() : "none")}, player {Player.main.transform.position} in sub {(Player.main.currentSub ? Player.main.currentSub.name : "none")}");
            Log.Info($"[SyncHarness] cyclops: fire list [{string.Join(",", fires)}]");
            Log.Info($"[SyncHarness] cyclops: {cyclopsId?.ToString().Substring(0, 8) ?? "noid"} health {(subControl.sub && subControl.sub.live ? subControl.sub.live.health : -1):F0}, active damage points {points.Count} [{string.Join(",", points)}], fires {(subFire ? subFire.fireCount : -1)}");
        }
        Log.Info($"[SyncHarness] cyclops: {cyclops} Cyclops loaded");
    }

    private void OnConsoleCommand_where(NotificationCenter.Notification n)
    {
        CrashedShipExploder aurora = CrashedShipExploder.main;
        Log.Info($"[SyncHarness] where: aurora {(aurora ? aurora.transform.position.ToString("F1") : "not loaded")}");
        LaunchRocket launchRocket = FindObjectOfType<LaunchRocket>();
        Log.Info($"[SyncHarness] where: launch console {(launchRocket ? launchRocket.transform.position.ToString("F1") : "not loaded")}");
        Rocket rocket = FindObjectOfType<Rocket>();
        Log.Info($"[SyncHarness] where: rocket {(rocket ? rocket.transform.position.ToString("F1") : "not loaded")}");
        GameObject atmosphere = rocket ? rocket.gameObject.FindChild("AtmosphereVolume") : null;
        Log.Info($"[SyncHarness] where: rocket cockpit centre {(atmosphere ? atmosphere.transform.position.ToString("F1") : "not loaded")}");
        Log.Info($"[SyncHarness] where: lifepod {(EscapePod.main ? EscapePod.main.transform.position.ToString("F1") : "not loaded")}, player {(Player.main ? Player.main.transform.position.ToString("F1") : "none")}");
        StoryGoalCustomEventHandler sunbeamEvents = StoryGoalCustomEventHandler.main;
        Log.Info($"[SyncHarness] where: sunbeam landing site {(sunbeamEvents ? sunbeamEvents.landingSiteLocation.ToString("F1") : "not loaded")} radius {(sunbeamEvents ? sunbeamEvents.landingSiteRadius : 0f):F1}");
        int containers = 0;
        foreach (SpawnStoredLoot loot in FindObjectsOfType<SpawnStoredLoot>())
        {
            containers++;
            if (containers <= 12)
            {
                Log.Info($"[SyncHarness] where: loot container {loot.gameObject.GetFullHierarchyPath()} at {loot.transform.position.ToString("F1")}");
            }
        }
        Log.Info($"[SyncHarness] where: {containers} loaded SpawnStoredLoot containers");
        // Every storage container within 40 m of the player, with its id and contents: the two clients' lists are compared by hand (R12c)
        int nearby = 0;
        foreach (StorageContainer storage in FindObjectsOfType<StorageContainer>())
        {
            if (!Player.main || Vector3.Distance(storage.transform.position, Player.main.transform.position) > 40f || storage.container == null)
            {
                continue;
            }
            nearby++;
            System.Collections.Generic.List<string> items = [];
            foreach (TechType techType in storage.container.GetItemTypes())
            {
                items.Add($"{techType}:{storage.container.GetCount(techType)}");
            }
            items.Sort();
            string id = storage.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId nitroxId) ? nitroxId.ToString() : "none";
            Log.Info($"[SyncHarness] where: storage {storage.gameObject.name} at {storage.transform.position.ToString("F1")} id {id} items [{string.Join(",", items)}]");
        }
        Log.Info($"[SyncHarness] where: {nearby} storage containers within 40 m");
    }

    private static string PodText(EscapePod pod) => pod ? $"{(pod.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId podId) ? podId.ToString().Substring(0, 8) : "noid")}@{pod.transform.position.ToString("F0")}" : "none";

    private void OnConsoleCommand_vehicles(NotificationCenter.Notification n)
    {
        // V0 (docking, Mobile Vehicle Bay): every loaded vehicle with its id, place, dock state and the lock this client holds on it
        NitroxClient.GameLogic.SimulationOwnership simulationOwnership = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
        Vector3 here = Player.main.transform.position;
        int count = 0;
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            count++;
            string id = vehicle.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId vehicleId) ? vehicleId.ToString() : "noid";
            string lockText = vehicle.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId lockId) && simulationOwnership.TryGetLockType(lockId, out Nitrox.Model.DataStructures.SimulationLockType lockType) ? lockType.ToString() : "none";
            Rigidbody body = vehicle.GetComponent<Rigidbody>();
            Log.Info($"[SyncHarness] vehicles: {vehicle.name} tech {CraftData.GetTechType(vehicle.gameObject)} id {id} at {vehicle.transform.position.ToString("F1")} dist {Vector3.Distance(here, vehicle.transform.position):F0}, active {vehicle.gameObject.activeInHierarchy}, docked {vehicle.docked}, parent {(vehicle.transform.parent ? vehicle.transform.parent.name : "none")}, lock {lockText}, kinematic {(body ? body.isKinematic.ToString() : "-")}, piloted {vehicle.GetPilotingMode()}");
        }
        foreach (Constructor constructor in FindObjectsOfType<Constructor>())
        {
            string id = constructor.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId constructorId) ? constructorId.ToString() : "noid";
            Log.Info($"[SyncHarness] vehicles: Mobile Vehicle Bay {id} at {constructor.transform.position.ToString("F1")} dist {Vector3.Distance(here, constructor.transform.position):F0}, deployed {constructor.deployed}");
        }
        Log.Info($"[SyncHarness] vehicles: {count} vehicles loaded");
    }

    private void OnConsoleCommand_dockvehicle(NotificationCenter.Notification n)
    {
        // #2549 / docking: puts the vehicle the player is driving into the trigger of the nearest vehicle docking bay (a Cyclops' or a moonpool's), which is what a pilot flying in does
        Vehicle vehicle = Player.main.currentMountedVehicle;
        if (!vehicle)
        {
            Log.Warn("[SyncHarness] dockvehicle: the player is not in a vehicle");
            return;
        }
        VehicleDockingBay nearest = null;
        foreach (VehicleDockingBay bay in FindObjectsOfType<VehicleDockingBay>())
        {
            if (!nearest || Vector3.Distance(bay.transform.position, vehicle.transform.position) < Vector3.Distance(nearest.transform.position, vehicle.transform.position))
            {
                nearest = bay;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] dockvehicle: no vehicle docking bay is loaded");
            return;
        }
        StartCoroutine(DockAsync(vehicle, nearest));
    }

    private static System.Collections.IEnumerator DockAsync(Vehicle vehicle, VehicleDockingBay bay)
    {
        // The bay only takes a vehicle while its doors are open (requiresAnimationStateForDocking), and they open when a vehicle is near: first below the bay, then into the trigger once the doors are open
        Rigidbody body = vehicle.GetComponent<Rigidbody>();
        Vector3 bayPosition = bay.transform.position;
        // Say why a bay cannot take the vehicle, before anything is moved: it holds a vehicle already, or (in survival) has no power and its doors do not open
        if (bay.GetDockedVehicle())
        {
            Log.Info($"[SyncHarness] dockvehicle: the bay already holds {bay.GetDockedVehicle().name}");
            Log.Info($"[SyncHarness] dockvehicle: docked {vehicle.docked}, bay holds {bay.GetDockedVehicle().name}");
            yield break;
        }
        if (GameModeUtils.RequiresPower() && !bay.IsPowered())
        {
            Log.Info("[SyncHarness] dockvehicle: the bay has no power");
            Log.Info($"[SyncHarness] dockvehicle: docked {vehicle.docked}, bay holds nothing");
            yield break;
        }
        Vector3 origin = vehicle.transform.position;
        Teleport(vehicle, body, bayPosition + new Vector3(0f, -6f, 0f));
        Log.Info($"[SyncHarness] dockvehicle: {vehicle.name} moved below the docking bay of {(bay.subRoot ? bay.subRoot.name : "?")} at {bayPosition.ToString("F1")}, powered {bay.IsPowered()}");
        for (int i = 0; i < 60 && bay && !bay.animator.GetCurrentAnimatorStateInfo(0).IsName(bay.animationStateNameForDocking); i++)
        {
            Teleport(vehicle, body, bayPosition + new Vector3(0f, -6f, 0f));
            yield return new WaitForSeconds(0.25f);
        }
        Log.Info($"[SyncHarness] dockvehicle: doors open ({bay.animator.GetCurrentAnimatorStateInfo(0).IsName(bay.animationStateNameForDocking)}), into the trigger");
        Teleport(vehicle, body, bayPosition);
        yield return new WaitForSeconds(5f);
        if (vehicle && !vehicle.docked && !bay.GetDockedVehicle())
        {
            // Not docked: take it out of the bay's trigger and from under the bay again, or it docks later by itself or blocks the undocking clearance of the next vehicle
            Teleport(vehicle, body, origin);
            Log.Info("[SyncHarness] dockvehicle: not docked, put back");
        }
        Log.Info($"[SyncHarness] dockvehicle: docked {vehicle.docked}, bay holds {(bay.GetDockedVehicle() ? bay.GetDockedVehicle().name : "nothing")}");
    }

    private void OnConsoleCommand_lockinfo(NotificationCenter.Notification n)
    {
        // Bases audit (B12): does this client hold the simulation lock of each base, how far is the player, and is the base live here (active, its MonoBehaviours enabled)?
        foreach (Base @base in FindObjectsOfType<Base>())
        {
            if (@base.isGhost)
            {
                continue;
            }
            string baseId = "noid";
            bool hasLock = false;
            if (@base.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id))
            {
                baseId = id.ToString().Substring(0, 8);
                hasLock = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>().HasAnyLockType(id);
            }
            int enabled = 0;
            int total = 0;
            foreach (MonoBehaviour behaviour in @base.GetComponentsInChildren<MonoBehaviour>(true))
            {
                total++;
                enabled += behaviour && behaviour.isActiveAndEnabled ? 1 : 0;
            }
            BaseHullStrength hull = @base.GetComponent<BaseHullStrength>();
            Log.Info($"[SyncHarness] lockinfo: base {baseId} lock {hasLock} player {Vector3.Distance(Player.main.transform.position, @base.transform.position):F0} m away, in it {Player.main.currentSub && Player.main.currentSub.gameObject == @base.gameObject}, active {@base.gameObject.activeInHierarchy}, behaviours {enabled}/{total} enabled, hull strength {(hull ? hull.isActiveAndEnabled.ToString() : "none")}");
        }
    }

    private void OnConsoleCommand_bases(NotificationCenter.Notification n)
    {
        // Bases audit (B1-B11): every loaded base with its pieces, machines, containers, power and plants, in a form basediff.py compares between the clients and with the server's /bases
        int count = 0;
        Log.Info($"[SyncHarness] bases: player at {Player.main.transform.position.ToString("F1")}, current sub {(Player.main.currentSub ? (Player.main.currentSub.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId subId) ? subId.ToString().Substring(0, 8) : Player.main.currentSub.name) : "none")}");
        foreach (Base @base in FindObjectsOfType<Base>())
        {
            count++;
            string baseId = @base.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id) ? id.ToString().Substring(0, 8) : "noid";
            SubRoot subRoot = @base.GetComponent<SubRoot>();
            PowerRelay relay = subRoot ? subRoot.powerRelay : null;
            int cells = 0;
            if (@base.cellObjects != null)
            {
                foreach (Transform cell in @base.cellObjects)
                {
                    cells += cell ? 1 : 0;
                }
            }
            Log.Info($"[SyncHarness] bases: base {baseId} ghost {@base.isGhost} anchor {@base.GetAnchor()} size {@base.GetSize()} cells {cells} at {@base.transform.position.ToString("F1")}, power {(relay ? $"{relay.GetPower():F1}/{relay.GetMaxPower():F1}" : "-")}");
            foreach (NitroxEntity entity in @base.GetComponentsInChildren<NitroxEntity>(true))
            {
                if (entity.gameObject == @base.gameObject)
                {
                    continue;
                }
                GameObject go = entity.gameObject;
                string flags = "";
                string details = "";
                Constructable constructable = go.GetComponent<Constructable>();
                if (constructable)
                {
                    flags += "C";
                    details += $" constructed {constructable.constructed} amount {constructable.constructedAmount:F2}";
                }
                StorageContainer storage = go.GetComponent<StorageContainer>();
                if (storage)
                {
                    flags += "S";
                    System.Collections.Generic.List<string> items = [];
                    foreach (TechType techType in storage.container.GetItemTypes())
                    {
                        items.Add($"{techType}:{storage.container.GetCount(techType)}");
                    }
                    items.Sort();
                    details += $" storage {storage.container.count}/{storage.container.sizeX * storage.container.sizeY} [{string.Join(",", items)}]";
                }
                PowerSource source = go.GetComponent<PowerSource>();
                if (source)
                {
                    flags += "P";
                    details += $" power {source.GetPower():F1}/{source.GetMaxPower():F1}";
                }
                FiltrationMachine filtration = go.GetComponent<FiltrationMachine>();
                if (filtration)
                {
                    flags += "F";
                    details += $" filtration water {filtration.timeRemainingWater:F0} salt {filtration.timeRemainingSalt:F0}";
                }
                GhostCrafter crafter = go.GetComponent<GhostCrafter>();
                if (crafter && crafter._logic)
                {
                    flags += "X";
                    details += $" crafter inProgress {crafter._logic.inProgress} tech {crafter._logic.craftingTechType} crafted {crafter._logic.numCrafted}";
                }
                BaseBioReactor bioReactor = go.GetComponent<BaseBioReactor>();
                if (bioReactor)
                {
                    flags += "R";
                    details += $" bioreactor toConsume {bioReactor._toConsume:F1} producing {bioReactor.producingPower} items {bioReactor.container.count}";
                }
                Planter planter = go.GetComponent<Planter>();
                if (planter)
                {
                    flags += "G";
                    details += $" planter plants {planter.GetComponentsInChildren<GrowingPlant>(true).Length}";
                }
                Log.Info($"[SyncHarness] bases: base {baseId} item {go.name} tech {CraftData.GetTechType(go)} id {entity.Id.ToString().Substring(0, 8)} flags [{flags}] at {go.transform.position.ToString("F1")}{details}");
            }
        }
        Log.Info($"[SyncHarness] bases: {count} bases loaded");
    }

    private void OnConsoleCommand_dropbase(NotificationCenter.Notification n)
    {
        // Bases audit (B5): this client loses the nearest base (as after a failed spawn): the GameObject is destroyed locally, nothing is announced
        Base nearest = null;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (!candidate.isGhost && (!nearest || Vector3.Distance(candidate.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = candidate;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] dropbase: no base");
            return;
        }
        Log.Info($"[SyncHarness] dropbase: destroying the local copy of the base at {nearest.transform.position.ToString("F1")}");
        Object.Destroy(nearest.gameObject);
    }

    private void OnConsoleCommand_resyncbuild(NotificationCenter.Notification n)
    {
        // Bases audit (B5): the "Resync base buildings" button
        NitroxClient.GameLogic.Bases.BuildingHandler.Main.AskForResync();
        Log.Info("[SyncHarness] resyncbuild: asked the server for a resync");
    }

    private void OnConsoleCommand_basecolliders(NotificationCenter.Notification n)
    {
        // Bases audit: why does a ray from inside the room not hit its walls? Colliders under the nearest base (count by layer, triggers) and what six rays from the player hit
        Base nearest = null;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (!nearest || Vector3.Distance(candidate.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position))
            {
                nearest = candidate;
            }
        }
        if (nearest)
        {
            System.Collections.Generic.Dictionary<string, int> byLayer = new();
            int triggers = 0;
            foreach (Collider collider in nearest.GetComponentsInChildren<Collider>(true))
            {
                string key = $"{LayerMask.LayerToName(collider.gameObject.layer)}{(collider.enabled ? "" : "(off)")}";
                byLayer[key] = byLayer.TryGetValue(key, out int existing) ? existing + 1 : 1;
                triggers += collider.isTrigger ? 1 : 0;
            }
            Log.Info($"[SyncHarness] basecolliders: base at {nearest.transform.position.ToString("F1")}: {string.Join(", ", System.Linq.Enumerable.Select(byLayer, pair => $"{pair.Key} x{pair.Value}"))}, {triggers} triggers");
        }
        Vector3 origin = Player.main.transform.position + Vector3.up * 0.5f;
        foreach ((string name, Vector3 direction) in new (string, Vector3)[] { ("down", Vector3.down), ("up", Vector3.up), ("+x", Vector3.right), ("-x", Vector3.left), ("+z", Vector3.forward), ("-z", Vector3.back) })
        {
            bool hitSomething = Physics.Raycast(origin, direction, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Collide);
            Log.Info($"[SyncHarness] basecolliders: ray {name} from {origin.ToString("F1")}: {(hitSomething ? $"{hit.collider.name} layer {LayerMask.LayerToName(hit.collider.gameObject.layer)} trigger {hit.collider.isTrigger} at {hit.distance:F1} m, point {hit.point.ToString("F1")}" : "nothing")}");
        }
    }

    private void OnConsoleCommand_enterbase(NotificationCenter.Notification n)
    {
        // Bases audit: puts the player into the first cell of the nearest base and makes that base the current sub (what walking in through a hatch does)
        Base nearest = null;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (!nearest || Vector3.Distance(candidate.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position))
            {
                nearest = candidate;
            }
        }
        SubRoot subRoot = nearest ? nearest.GetComponent<SubRoot>() : null;
        Transform cell = null;
        if (nearest && nearest.cellObjects != null)
        {
            foreach (Transform candidate in nearest.cellObjects)
            {
                if (candidate)
                {
                    cell = candidate;
                    break;
                }
            }
        }
        if (!subRoot || !cell)
        {
            Log.Warn("[SyncHarness] enterbase: no base with a cell nearby");
            return;
        }
        // The cell objects sit where the base's own grid puts them, not necessarily where the room geometry is: stand in the middle of the colliders of the base
        Bounds bounds = new(nearest.transform.position, Vector3.zero);
        foreach (Collider collider in nearest.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled && !collider.isTrigger)
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        Vector3 inside = nearest.transform.position + Vector3.up * 0.3f;
        Player.main.OnPlayerPositionCheat();
        Player.main.SetPosition(inside);
        Player.main.SetCurrentSub(subRoot, true);
        Log.Info($"[SyncHarness] enterbase: player at {Player.main.transform.position.ToString("F1")}, collider bounds center {bounds.center.ToString("F1")} size {bounds.size.ToString("F1")}, cell object at {cell.position.ToString("F1")}, current sub {(Player.main.currentSub ? Player.main.currentSub.name : "none")}");
    }

    private void OnConsoleCommand_buildmodule(NotificationCenter.Notification n)
    {
        // Bases audit: places a base module (Locker, Fabricator, BaseBioReactor, BaseFiltrationMachine, Planter, ...) by hand inside the base the player stands in: floor (down from the player) or wall (along the camera's yaw)
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] buildmodule needs a TechType and floor or wall");
            return;
        }
        string[] mode = Arg(n, 1).Split(':');
        float.TryParse(mode.Length > 1 ? mode[1] : "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float first);
        float.TryParse(mode.Length > 2 ? mode[2] : "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float second);
        StartCoroutine(BuildModuleAsync(techType, mode[0] == "wall", first, second));
    }

    private static System.Collections.IEnumerator BuildModuleAsync(TechType techType, bool wall, float first, float second)
    {
        // floor:dx:dz puts it dx / dz meters (world axes) from the player; wall:yaw looks along that compass angle (0 = +z)
        Vector3 origin = wall ? Player.main.transform.position + Vector3.up * 0.5f : Player.main.transform.position + new Vector3(first, 0.5f, second);
        Vector3 direction = wall ? Quaternion.Euler(0f, first, 0f) * Vector3.forward : Vector3.down;
        yield return Builder.BeginAsync(techType);
        yield return null;
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            Log.Warn($"[SyncHarness] buildmodule: {techType}: nothing to put it on from {origin.ToString("F1")} towards {direction.ToString("F1")}");
            Builder.End();
            yield break;
        }
        // Aim the camera at the spot like a player does and let the builder find the face and the orientation (a module placed by hand has no base face: BaseAddModuleGhost.Finish throws)
        Vector3 toTarget = hit.point - MainCamera.camera.transform.position;
        MainCameraControl.main.rotationX = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        MainCameraControl.main.rotationY = -Mathf.Asin(-toTarget.y / toTarget.magnitude) * Mathf.Rad2Deg;
        for (int frame = 0; frame < 60 && !Builder.canPlace; frame++)
        {
            Builder.Update();
            yield return null;
        }
        bool placed = Builder.canPlace && Builder.TryPlace();
        Log.Info($"[SyncHarness] buildmodule: {techType} canPlace {Builder.canPlace}, placed {placed}, aimed at {hit.point.ToString("F1")} ({hit.collider.name}) from {MainCamera.camera.transform.position.ToString("F1")}, ghost {(Builder.ghostModel ? Builder.ghostModel.transform.position.ToString("F1") : "none")}, current sub {(Player.main.currentSub ? Player.main.currentSub.name : "none")}");
        yield return null;
        Builder.End();
    }

    private void OnConsoleCommand_lockeradd(NotificationCenter.Notification n)
    {
        // Bases audit (B6): adds N items of a tech type to the nearest storage container within 10 m, the way an inventory drag does (ItemsContainer.AddItem), until the container says it is full
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] lockeradd needs a TechType and a count");
            return;
        }
        int.TryParse(Arg(n, 1), out int amount);
        StorageContainer nearest = null;
        foreach (StorageContainer storage in FindObjectsOfType<StorageContainer>())
        {
            if (Vector3.Distance(storage.transform.position, Player.main.transform.position) < 10f && storage.container != null && (!nearest || Vector3.Distance(storage.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = storage;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] lockeradd: no storage container within 10 m");
            return;
        }
        StartCoroutine(LockerAddAsync(nearest, techType, amount));
    }

    private static System.Collections.IEnumerator LockerAddAsync(StorageContainer storage, TechType techType, int amount)
    {
        // Like a player: the items are first in the player's inventory (the server knows them there), then they are moved into the container one by one
        // (ItemsContainer.AddItem checks the room in this client's copy of the container only)
        System.Collections.Generic.List<Pickupable> inInventory = new();
        for (int i = 0; i < amount; i++)
        {
            TaskResult<GameObject> prefabResult = new();
            yield return CraftData.InstantiateFromPrefabAsync(techType, prefabResult, false);
            GameObject item = prefabResult.Get();
            Pickupable pickupable = item ? item.GetComponent<Pickupable>() : null;
            if (!pickupable)
            {
                break;
            }
            pickupable.Initialize();
            NitroxEntity.GetIdOrGenerateNew(item);
            if (Inventory.main.container.AddItem(pickupable) == null)
            {
                Object.Destroy(item);
                break;
            }
            inInventory.Add(pickupable);
        }
        Log.Info($"[SyncHarness] lockeradd: {inInventory.Count} of {amount} {techType} are in the inventory, moving them to {storage.name} in 3 s");
        yield return new WaitForSeconds(3f);

        int moved = 0;
        foreach (Pickupable pickupable in inInventory)
        {
            if (!storage.container.HasRoomFor(pickupable))
            {
                break;
            }
            Inventory.main.container.RemoveItem(pickupable, true);
            if (storage.container.AddItem(pickupable) != null)
            {
                moved++;
            }
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        Log.Info($"[SyncHarness] lockeradd: {moved} of {amount} {techType} moved to {storage.name}, it now holds {storage.container.count}/{storage.container.sizeX * storage.container.sizeY} in this client's copy");
    }

    private void OnConsoleCommand_bioreactoradd(NotificationCenter.Notification n)
    {
        // Bases audit (B7): puts N items of a tech type into the nearest bioreactor within 10 m, the way a player does (first in the inventory, where the server knows them, then moved into the reactor)
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] bioreactoradd needs a TechType and a count");
            return;
        }
        int.TryParse(Arg(n, 1), out int amount);
        BaseBioReactor nearest = null;
        foreach (BaseBioReactor reactor in FindObjectsOfType<BaseBioReactor>())
        {
            if (Vector3.Distance(reactor.transform.position, Player.main.transform.position) < 10f && (!nearest || Vector3.Distance(reactor.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = reactor;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] bioreactoradd: no bioreactor within 10 m");
            return;
        }
        StartCoroutine(BioreactorAddAsync(nearest, techType, amount));
    }

    private static System.Collections.IEnumerator BioreactorAddAsync(BaseBioReactor reactor, TechType techType, int amount)
    {
        System.Collections.Generic.List<Pickupable> inInventory = new();
        for (int i = 0; i < amount; i++)
        {
            TaskResult<GameObject> prefabResult = new();
            yield return CraftData.InstantiateFromPrefabAsync(techType, prefabResult, false);
            GameObject item = prefabResult.Get();
            Pickupable pickupable = item ? item.GetComponent<Pickupable>() : null;
            if (!pickupable)
            {
                break;
            }
            pickupable.Initialize();
            NitroxEntity.GetIdOrGenerateNew(item);
            if (Inventory.main.container.AddItem(pickupable) == null)
            {
                Object.Destroy(item);
                break;
            }
            inInventory.Add(pickupable);
        }
        Log.Info($"[SyncHarness] bioreactoradd: {inInventory.Count} of {amount} {techType} are in the inventory, moving them to the bioreactor in 3 s");
        yield return new WaitForSeconds(3f);

        int moved = 0;
        foreach (Pickupable pickupable in inInventory)
        {
            Inventory.main.container.RemoveItem(pickupable, true);
            if (reactor.container.AddItem(pickupable) != null)
            {
                moved++;
            }
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        Log.Info($"[SyncHarness] bioreactoradd: {moved} of {amount} {techType} moved, the bioreactor now holds {reactor.container.count} items in this client, toConsume {reactor._toConsume:F1}");
    }

    private void OnConsoleCommand_reactorburn(NotificationCenter.Notification n)
    {
        // Bases audit (B7): lets the nearest bioreactor (within 10 m) burn its first item in about N seconds by setting how much of it is burned already
        float.TryParse(Arg(n, 0), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds);
        BaseBioReactor nearest = null;
        foreach (BaseBioReactor reactor in FindObjectsOfType<BaseBioReactor>())
        {
            if (Vector3.Distance(reactor.transform.position, Player.main.transform.position) < 10f && (!nearest || Vector3.Distance(reactor.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = reactor;
            }
        }
        if (!nearest || nearest.container.count == 0)
        {
            Log.Warn("[SyncHarness] reactorburn: no bioreactor with fuel within 10 m");
            return;
        }
        TechType first = TechType.None;
        foreach (InventoryItem item in nearest.container)
        {
            first = item.item.GetTechType();
            break;
        }
        float charge = BaseBioReactor.GetCharge(first);
        float before = nearest._toConsume;
        nearest._toConsume = Mathf.Max(0f, charge - seconds * 0.8333333f);
        Log.Info($"[SyncHarness] reactorburn: bioreactor with {nearest.container.count} items, first {first} charge {charge:F0}, toConsume {before:F1} -> {nearest._toConsume:F1}, power {nearest.GetComponent<PowerSource>().power:F1}");
    }

    private void OnConsoleCommand_drainpower(NotificationCenter.Notification n)
    {
        // Bases audit (B10): uses energy from the base the player stands in, like a machine would
        float.TryParse(Arg(n, 0), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float amount);
        SubRoot subRoot = Player.main.currentSub;
        if (!subRoot || !subRoot.powerRelay)
        {
            Log.Warn("[SyncHarness] drainpower: the player is not in a base with a power relay");
            return;
        }
        // PowerRelay.ConsumeEnergy and PowerSource.ModifyPower take nothing in creative mode (GameModeUtils.RequiresPower), so the charge of the generators is lowered directly
        float remaining = amount;
        float consumed = 0f;
        int sources = 0;
        foreach (PowerSource source in subRoot.GetComponentsInChildren<PowerSource>(true))
        {
            sources++;
            float before = source.power;
            source.SetPower(Mathf.Max(0f, source.power - remaining));
            consumed += before - source.power;
            remaining = amount - consumed;
        }
        Log.Info($"[SyncHarness] drainpower: consumed {consumed:F1} of {amount:F1} from {sources} generators, power now {subRoot.powerRelay.GetPower():F1}/{subRoot.powerRelay.GetMaxPower():F1}");
    }

    private static object ReadMember(object o, string name)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        return o.GetType().GetField(name, flags)?.GetValue(o) ?? o.GetType().GetProperty(name, flags)?.GetValue(o);
    }

    private void OnConsoleCommand_patchcheck(NotificationCenter.Notification n)
    {
        // Bases audit: is a Harmony patch installed on this method of the running game? Arguments: full type name, method name. Harmony is read by reflection (NitroxClient does not reference it).
        // The console lower-cases its arguments, so type and method are found ignoring the case
        System.Type type = null;
        foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (System.Type candidate in assembly.GetTypes())
                {
                    if (string.Equals(candidate.FullName, Arg(n, 0), System.StringComparison.OrdinalIgnoreCase))
                    {
                        type = candidate;
                        break;
                    }
                }
            }
            catch (System.Reflection.ReflectionTypeLoadException)
            {
            }
            if (type != null)
            {
                break;
            }
        }
        System.Reflection.MethodInfo method = null;
        if (type != null)
        {
            foreach (System.Reflection.MethodInfo candidate in type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (string.Equals(candidate.Name, Arg(n, 1), System.StringComparison.OrdinalIgnoreCase) && candidate.GetParameters().Length <= 1)
                {
                    method = candidate;
                    break;
                }
            }
        }
        System.Type harmonyType = null;
        foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            harmonyType = assembly.GetType("HarmonyLib.Harmony");
            if (harmonyType != null)
            {
                break;
            }
        }
        if (method == null || harmonyType == null)
        {
            Log.Warn($"[SyncHarness] patchcheck: {Arg(n, 0)}.{Arg(n, 1)} not found (method {(method == null ? "missing" : "ok")}, harmony {(harmonyType == null ? "missing" : "ok")})");
            return;
        }
        object info = harmonyType.GetMethod("GetPatchInfo", new[] { typeof(System.Reflection.MethodBase) }).Invoke(null, new object[] { method });
        string Describe(string kind)
        {
            System.Collections.Generic.List<string> patches = new();
            if (info != null && ReadMember(info, kind) is System.Collections.IEnumerable list)
            {
                foreach (object patch in list)
                {
                    System.Reflection.MethodInfo patchMethod = ReadMember(patch, "PatchMethod") as System.Reflection.MethodInfo;
                    patches.Add($"{patchMethod?.DeclaringType?.Name}.{patchMethod?.Name}");
                }
            }
            return string.Join(",", patches);
        }
        Log.Info($"[SyncHarness] patchcheck: {Arg(n, 0)}.{Arg(n, 1)} prefixes [{Describe("Prefixes")}] postfixes [{Describe("Postfixes")}] transpilers [{Describe("Transpilers")}]");
    }

    private void OnConsoleCommand_beaconlabel(NotificationCenter.Notification n)
    {
        // Bases audit (B1, pr/2844): renames the nearest beacon within 10 m by calling BeaconLabel.SetLabel, the method the VR keyboard calls
        BeaconLabel nearest = null;
        foreach (BeaconLabel label in FindObjectsOfType<BeaconLabel>())
        {
            if (Vector3.Distance(label.transform.position, Player.main.transform.position) < 10f && (!nearest || Vector3.Distance(label.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = label;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] beaconlabel: no beacon within 10 m");
            return;
        }
        nearest.SetLabel(Arg(n, 0));
        Log.Info($"[SyncHarness] beaconlabel: renamed the beacon at {nearest.transform.position.ToString("F1")} to '{nearest.GetLabel()}'");
    }

    private void OnConsoleCommand_beaconlist(NotificationCenter.Notification n)
    {
        int count = 0;
        foreach (BeaconLabel label in FindObjectsOfType<BeaconLabel>())
        {
            if (Vector3.Distance(label.transform.position, Player.main.transform.position) < 40f)
            {
                string id = label.GetComponentInParent<NitroxEntity>() ? label.GetComponentInParent<NitroxEntity>().Id.ToString() : "none";
                Log.Info($"[SyncHarness] beaconlist: beacon id {id} at {label.transform.position.ToString("F1")} is called '{label.GetLabel()}'");
                count++;
            }
        }
        Log.Info($"[SyncHarness] beaconlist: {count} beacons within 40 m");
    }

    private void OnConsoleCommand_toolinfo(NotificationCenter.Notification n)
    {
        // Bases audit (B1, pr/2809): the battery of the nearest tool of this tech type within 40 m (id and charge)
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] toolinfo needs a TechType");
            return;
        }
        int count = 0;
        foreach (Pickupable pickupable in FindObjectsOfType<Pickupable>())
        {
            if (pickupable.GetTechType() != techType || Vector3.Distance(pickupable.transform.position, Player.main.transform.position) > 40f)
            {
                continue;
            }
            EnergyMixin energyMixin = pickupable.GetComponentInChildren<EnergyMixin>(true);
            string toolId = pickupable.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId nitroxId) ? nitroxId.ToString() : "none";
            string batteryId = "none";
            if (energyMixin && energyMixin.battery is MonoBehaviour batteryBehaviour && batteryBehaviour.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId batteryNitroxId))
            {
                batteryId = batteryNitroxId.ToString();
            }
            Log.Info($"[SyncHarness] toolinfo: {techType} id {toolId} at {pickupable.transform.position.ToString("F1")} {(pickupable.inventoryItem != null ? "in a container" : "in the world")}, battery id {batteryId} charge {(energyMixin ? energyMixin.charge : -1f):F1}/{(energyMixin ? energyMixin.capacity : -1f):F1}");
            count++;
        }
        Log.Info($"[SyncHarness] toolinfo: {count} {techType}");
    }

    private void OnConsoleCommand_pickdrop(NotificationCenter.Notification n)
    {
        // Bases audit (B1, pr/2809): drains the battery of the nearest tool of this tech type within 10 m to the second argument (percent), picks it up and drops it again
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] pickdrop needs a TechType");
            return;
        }
        float.TryParse(Arg(n, 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float percent);
        Pickupable nearest = null;
        foreach (Pickupable pickupable in FindObjectsOfType<Pickupable>())
        {
            if (pickupable.GetTechType() == techType && Vector3.Distance(pickupable.transform.position, Player.main.transform.position) < 10f && (!nearest || Vector3.Distance(pickupable.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = pickupable;
            }
        }
        if (!nearest)
        {
            Log.Warn($"[SyncHarness] pickdrop: no {techType} within 10 m");
            return;
        }
        StartCoroutine(PickDropAsync(nearest, percent));
    }

    private static System.Collections.IEnumerator PickDropAsync(Pickupable pickupable, float percent)
    {
        // A tool made by the spawn command has no battery until the first pickup (Nitrox creates the default battery then), so: pick up and drop once to get the battery,
        // drain it, then pick up and drop again. The second pickup must keep the drained battery (stock Nitrox adds a new full default battery on every pickup).
        EnergyMixin energyMixin = pickupable.GetComponentInChildren<EnergyMixin>(true);
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            bool pickedUp = Inventory.main.Pickup(pickupable, true);
            yield return new WaitForSeconds(2f);
            bool dropped = Inventory.main.InternalDropItem(pickupable, false);
            yield return new WaitForSeconds(3f);
            Log.Info($"[SyncHarness] pickdrop: cycle {cycle}, picked up {pickedUp}, dropped {dropped}, charge {(energyMixin ? energyMixin.charge : -1f):F1}");
            if (cycle == 1 && energyMixin && energyMixin.battery != null)
            {
                // ConsumeEnergy does nothing in creative mode, so the charge is set directly (the synced Battery.charge setter)
                energyMixin.battery.charge = energyMixin.capacity * percent / 100f;
                Log.Info($"[SyncHarness] pickdrop: drained to {energyMixin.charge:F1}");
                yield return new WaitForSeconds(2f);
            }
        }
    }

    private void OnConsoleCommand_filterstate(NotificationCenter.Notification n)
    {
        // Bases audit (B2): what every filtration machine in reach holds and how far its timers are
        foreach (FiltrationMachine machine in FindObjectsOfType<FiltrationMachine>())
        {
            System.Collections.Generic.List<string> items = new();
            foreach (TechType techType in machine.storageContainer.container.GetItemTypes())
            {
                items.Add($"{techType}:{machine.storageContainer.container.GetCount(techType)}");
            }
            string id = machine.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId nitroxId) ? nitroxId.ToString() : (machine.GetComponentInParent<NitroxEntity>() ? machine.GetComponentInParent<NitroxEntity>().Id.ToString() : "none");
            Log.Info($"[SyncHarness] filterstate: machine {id} constructed {machine.constructed:F1} timers water {machine.timeRemainingWater:F1} salt {machine.timeRemainingSalt:F1} holds [{string.Join(",", items)}]");
        }
    }

    private void OnConsoleCommand_filterpoke(NotificationCenter.Notification n)
    {
        // Bases audit (B2): makes every water filtration machine in reach produce in about a second (its real intervals are 7 and 14 minutes)
        int pokes = 0;
        foreach (FiltrationMachine machine in FindObjectsOfType<FiltrationMachine>())
        {
            machine.timeRemainingWater = 1f;
            machine.timeRemainingSalt = 1f;
            pokes++;
        }
        Log.Info($"[SyncHarness] filterpoke: {pokes} filtration machines set to produce within a second");
    }

    private void OnConsoleCommand_fabricate(NotificationCenter.Notification n)
    {
        // Bases audit: starts a craft at the nearest fabricator-like crafter within 10 m (GhostCrafter.Craft: what the fabricator menu does)
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] fabricate needs a TechType");
            return;
        }
        GhostCrafter nearest = null;
        foreach (GhostCrafter crafter in FindObjectsOfType<GhostCrafter>())
        {
            if (Vector3.Distance(crafter.transform.position, Player.main.transform.position) < 10f && (!nearest || Vector3.Distance(crafter.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = crafter;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] fabricate: no crafter within 10 m");
            return;
        }
        float duration = 2f;
        nearest.Craft(techType, duration);
        Log.Info($"[SyncHarness] fabricate: {techType} at {nearest.name}, duration {duration:F1}");
    }

    private void OnConsoleCommand_plant(NotificationCenter.Notification n)
    {
        // Bases audit: plants one seed of the given tech type in the nearest growbed (Planter) within 10 m
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] plant needs a TechType of a plantable");
            return;
        }
        Planter nearest = null;
        foreach (Planter planter in FindObjectsOfType<Planter>())
        {
            if (Vector3.Distance(planter.transform.position, Player.main.transform.position) < 10f && (!nearest || Vector3.Distance(planter.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = planter;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] plant: no growbed within 10 m");
            return;
        }
        StartCoroutine(PlantAsync(nearest, techType));
    }

    private static System.Collections.IEnumerator PlantAsync(Planter planter, TechType techType)
    {
        TaskResult<GameObject> prefabResult = new();
        yield return CraftData.InstantiateFromPrefabAsync(techType, prefabResult, false);
        GameObject item = prefabResult.Get();
        Plantable plantable = item ? item.GetComponent<Plantable>() : null;
        if (!plantable)
        {
            Log.Warn($"[SyncHarness] plant: {techType} is not plantable");
            yield break;
        }
        Pickupable pickupable = item.GetComponent<Pickupable>();
        pickupable.Initialize();
        planter.AddItem(new InventoryItem(pickupable));
        Log.Info($"[SyncHarness] plant: {techType} planted in {planter.name}");
    }

    private void OnConsoleCommand_groundprobe(NotificationCenter.Notification n)
    {
        // Bases audit: where is the seabed around the player and how flat is it (a 5 x 5 grid, 8 m apart, rays from 12 m above the player)
        Vector3 here = Player.main.transform.position;
        for (int ix = -2; ix <= 2; ix++)
        {
            string row = "";
            for (int iz = -2; iz <= 2; iz++)
            {
                Vector3 origin = new(here.x + ix * 8f, here.y + 12f, here.z + iz * 8f);
                row += Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore) ? $" ({hit.point.y:F1} n{hit.normal.y:F2})" : " (none)";
            }
            Log.Info($"[SyncHarness] groundprobe: x{here.x + ix * 8f:F0}:{row}");
        }
    }

    private void OnConsoleCommand_buildplace(NotificationCenter.Notification n)
    {
        // Bases audit: places a ghost of the given tech type where the camera looks, like a click with the builder tool (optional second argument: pitch in degrees below the horizon, default 35)
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] buildplace needs a TechType (BaseRoom, BaseCorridor, Locker, Fabricator, ...)");
            return;
        }
        float pitch = 35f;
        float.TryParse(Arg(n, 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pitch);
        StartCoroutine(BuildPlaceAsync(techType, pitch));
    }

    private static void BuildPlaceDescribe(string when)
    {
        // Direct observation of the model/root relation that decides the cell offset of the new Base: BaseGhost.Finish builds the Base at the ConstructableBase root and offsets the piece by
        // WorldToGrid(model position), which for a first piece is the model's position in the root's frame over the cell size, rounded (the BaseGhost.targetOffset field is only what UpdatePlacement stored: not used)
        if (!Builder.ghostModel)
        {
            return;
        }
        ConstructableBase root = Builder.ghostModel.GetComponentInParent<ConstructableBase>();
        Vector3 modelPosition = Builder.ghostModel.transform.position;
        string rootText = "no ConstructableBase root";
        string offsetText = "n/a";
        string differenceText = "n/a";
        if (root)
        {
            Vector3 rootPosition = root.transform.position;
            Vector3 local = Quaternion.Inverse(root.transform.rotation) * (modelPosition - rootPosition);
            rootText = $"root at {rootPosition.ToString("F1")} yaw {root.transform.eulerAngles.y:F0}";
            differenceText = (modelPosition - rootPosition).ToString("F1");
            offsetText = $"{Mathf.RoundToInt(local.x / Base.cellSize.x)},{Mathf.RoundToInt(local.y / Base.cellSize.y)},{Mathf.RoundToInt(local.z / Base.cellSize.z)}";
        }
        Log.Info($"[SyncHarness] buildplace: {when}: {rootText}, model at {modelPosition.ToString("F1")} yaw {Builder.ghostModel.transform.eulerAngles.y:F0}, model minus root {differenceText}, offset BaseGhost.Finish will use {offsetText}");
    }

    private static System.Collections.IEnumerator BuildPlaceAsync(TechType techType, float pitch)
    {
        yield return Builder.BeginAsync(techType);
        // The builder places the ghost where the camera ray meets a surface within a few meters: try a range of pitches around the asked one until it says canPlace
        float[] pitches = [pitch, pitch + 10f, pitch - 10f, pitch + 20f, pitch - 20f, pitch + 30f, 60f, 70f, 80f, 25f, 15f];
        foreach (float tryPitch in pitches)
        {
            MainCameraControl.main.rotationY = -tryPitch;
            for (int frame = 0; frame < 12 && !Builder.canPlace; frame++)
            {
                Builder.Update(); // the input handler that drives it in the game does not get its turn in a window the test does not give input to
                yield return null;
            }
            Log.Info($"[SyncHarness] buildplace: pitch {tryPitch:F0}, camera at {MainCamera.camera.transform.position.ToString("F1")} looking {MainCamera.camera.transform.forward.ToString("F2")}, canPlace {Builder.canPlace}, ghost at {(Builder.ghostModel ? Builder.ghostModel.transform.position.ToString("F1") : "none")}");
            if (Builder.canPlace)
            {
                break;
            }
        }
        if (!Builder.canPlace && Builder.ghostModel)
        {
            // The tool's own search found nothing (a base piece wants a particular surface in front of the camera): put the ghost on the seabed 4 m ahead by hand and say it may be placed
            Vector3 ahead = MainCamera.camera.transform.position + Vector3.ProjectOnPlane(MainCamera.camera.transform.forward, Vector3.up).normalized * 4f;
            if (Physics.Raycast(ahead + Vector3.up * 6f, Vector3.down, out RaycastHit ground, 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 center = ground.point + Vector3.up * 2.5f;
                // The root yaw of a vanilla placement is the camera yaw plus 180 (Builder.SetDefaultPlaceTransform looks along minus the camera's forward): the same yaw keeps the room where the earlier runs had it
                Quaternion rotation = Quaternion.Euler(0f, MainCamera.camera.transform.eulerAngles.y + 180f, 0f);
                BuildPlaceDescribe("forced branch, before the root moves");
                Builder.placePosition = center;
                Builder.placeRotation = rotation;
                // A base piece's ghost model hangs under its ConstructableBase and the builder always keeps the two on the same spot. BaseGhost.Finish builds the new Base at that root and
                // puts the piece at the model's cell offset from it, so a model moved alone made the builder's Base one cell wider (and the room one cell off) than the Base every other client
                // builds from the root's transform alone (seen in live two-player tests: flood grids of 12 cells against 9, a scanner room not found for its id). Both go to the forced point, like a real placement.
                ConstructableBase ghostRoot = Builder.ghostModel.GetComponentInParent<ConstructableBase>();
                if (ghostRoot)
                {
                    ghostRoot.transform.SetPositionAndRotation(center, rotation);
                }
                Builder.ghostModel.transform.SetPositionAndRotation(center, rotation);
                Builder.canPlace = true;
                Log.Info($"[SyncHarness] buildplace: forced the ghost to {center.ToString("F1")} (seabed {ground.point.y:F1})");
            }
        }
        if (Builder.canPlace)
        {
            BuildPlaceDescribe("before TryPlace");
            bool placed = Builder.TryPlace();
            Log.Info($"[SyncHarness] buildplace: placed {placed}");
        }
        yield return null;
        Builder.End();
    }

    private void OnConsoleCommand_buildfinish(NotificationCenter.Notification n)
    {
        StartCoroutine(BuildFinishAsync());
    }

    private static System.Collections.IEnumerator BuildFinishAsync()
    {
        // Builds every ghost within 40 m the way the tool does, one Construct() per frame
        int frames = 0;
        while (frames < 3000)
        {
            System.Collections.Generic.List<Constructable> open = [];
            foreach (Constructable constructable in Object.FindObjectsOfType<Constructable>())
            {
                if (constructable && !constructable.constructed && Vector3.Distance(constructable.transform.position, Player.main.transform.position) < 40f)
                {
                    open.Add(constructable);
                }
            }
            if (open.Count == 0)
            {
                break;
            }
            foreach (Constructable constructable in open)
            {
                constructable.Construct();
            }
            frames++;
            yield return null;
        }
        Log.Info($"[SyncHarness] buildfinish: done after {frames} frames");
    }

    private void OnConsoleCommand_refill(NotificationCenter.Notification n)
    {
        // Survival scenarios at depth: a full tank, as long as the scenario needs it
        Player.main.oxygenMgr.AddOxygen(1000f);
        Log.Info("[SyncHarness] refill: oxygen refilled");
    }

    private void OnConsoleCommand_pdastate(NotificationCenter.Notification n)
    {
        // R12e: the scanner's view of one tech type (or everything): partial progress, completed, known blueprint, how many fragments this client has marked as scanned
        string part = Arg(n, 0);
        System.Collections.Generic.List<string> partials = [];
        foreach (PDAScanner.Entry entry in PDAScanner.partial)
        {
            if (part == "" || entry.techType.ToString().IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PDAScanner.EntryData data = PDAScanner.GetEntryData(entry.techType);
                partials.Add($"{entry.techType}:{entry.unlocked}/{(data != null ? data.totalFragments : -1)}");
            }
        }
        System.Collections.Generic.List<string> complete = [];
        foreach (TechType techType in PDAScanner.complete)
        {
            if (part == "" || techType.ToString().IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                complete.Add(techType.ToString());
            }
        }
        bool known = System.Enum.TryParse(part, true, out TechType parsed) && KnownTech.Contains(parsed);
        Log.Info($"[SyncHarness] pdastate '{part}': partial [{string.Join(",", partials)}], complete [{string.Join(",", complete)}], blueprint known {known}, {PDAScanner.fragments.Count} scanned fragments marked");
    }

    private void OnConsoleCommand_scanfragment(NotificationCenter.Notification n)
    {
        // R12e: scans the nearest scannable object whose name, tech type or id starts with the argument, through PDAScanner.Scan() like the scanner tool does (progress set up front)
        string part = Arg(n, 0);
        Vector3 here = Player.main.transform.position;
        GameObject best = null;
        float bestDistance = 40f;
        NitroxId bestId = null;
        foreach (System.Collections.Generic.KeyValuePair<NitroxId, GameObject> pair in NitroxEntity.GetGameObjects())
        {
            GameObject go = pair.Value;
            if (!go)
            {
                continue;
            }
            float distance = Vector3.Distance(go.transform.position, here);
            if (distance >= bestDistance || (part != "" && go.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0 && CraftData.GetTechType(go).ToString().IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                                              !pair.Key.ToString().StartsWith(part, System.StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (PDAScanner.CanScan(go))
            {
                (best, bestDistance, bestId) = (go, distance, pair.Key);
            }
        }
        if (!best)
        {
            Log.Warn($"[SyncHarness] scanfragment '{part}': nothing scannable within 40 m");
            return;
        }
        Log.Info($"[SyncHarness] scanfragment: {best.name} tech {CraftData.GetTechType(best)} id {bestId} at {best.transform.position.ToString("F2")} dist {bestDistance:F1}");
        StartCoroutine(ScanAsync(best, bestId));
    }

    private static System.Collections.IEnumerator ScanAsync(GameObject target, NitroxId id)
    {
        Player.main.oxygenMgr.AddOxygen(1000f);
        PDAScanner.scanTarget.Initialize(target);
        PDAScanner.Result result = PDAScanner.Result.None;
        PDAScanner.scanTarget.progress = 0.99f;
        for (int frame = 0; frame < 300; frame++)
        {
            result = PDAScanner.Scan();
            if (result is PDAScanner.Result.Done or PDAScanner.Result.Researched or PDAScanner.Result.Known or PDAScanner.Result.Processed or PDAScanner.Result.None)
            {
                break;
            }
            yield return null;
        }
        Log.Info($"[SyncHarness] scanfragment: scan of {id} ended with {result}");
    }

    private void OnConsoleCommand_undockvehicle(NotificationCenter.Notification n)
    {
        // Docking: the click on the docked vehicle's hand target in the bay, which starts the undocking cinematic (Nitrox asks for the lock first and sends VehicleUndocking)
        DockedVehicleHandTarget nearest = null;
        foreach (DockedVehicleHandTarget target in FindObjectsOfType<DockedVehicleHandTarget>())
        {
            if (target.dockingBay && target.dockingBay.GetDockedVehicle() && (!nearest || Vector3.Distance(target.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position)))
            {
                nearest = target;
            }
        }
        if (!nearest)
        {
            Log.Warn("[SyncHarness] undockvehicle: no docked vehicle is loaded");
            return;
        }
        Log.Info($"[SyncHarness] undockvehicle: clicking the hand target of {nearest.dockingBay.GetDockedVehicle().name}, clearance {nearest.dockingBay.HasUndockingClearance()}");
        nearest.OnHandClick(Player.main.guiHand);
    }

    private static void Teleport(Vehicle vehicle, Rigidbody body, Vector3 position)
    {
        vehicle.transform.position = position;
        if (body)
        {
            body.position = position;
            body.velocity = Vector3.zero;
        }
    }

    private void OnConsoleCommand_buildvehicle(NotificationCenter.Notification n)
    {
        // #2768: what the Mobile Vehicle Bay's terminal does when a vehicle is picked (ConstructorInput.Craft), on the nearest bay within 80 m, deploying it first
        if (!System.Enum.TryParse(Arg(n, 0), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] buildvehicle needs a TechType (Seamoth, Exosuit)");
            return;
        }
        Constructor nearest = null;
        foreach (Constructor constructor in FindObjectsOfType<Constructor>())
        {
            if (!nearest || Vector3.Distance(constructor.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position))
            {
                nearest = constructor;
            }
        }
        if (!nearest || Vector3.Distance(nearest.transform.position, Player.main.transform.position) > 80f)
        {
            Log.Warn("[SyncHarness] buildvehicle: no Mobile Vehicle Bay within 80 m");
            return;
        }
        if (!nearest.deployed)
        {
            nearest.Deploy(true);
        }
        ConstructorInput input = nearest.GetComponentInChildren<ConstructorInput>(true);
        if (!input)
        {
            Log.Error("[SyncHarness] buildvehicle: no ConstructorInput under the Mobile Vehicle Bay");
            return;
        }
        Log.Info($"[SyncHarness] buildvehicle: {techType} at the Mobile Vehicle Bay {(nearest.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id) ? id.ToString() : "noid")}");
        input.Craft(techType, 10f);
    }

    private void OnConsoleCommand_introdump(NotificationCenter.Notification n)
    {
        // V2 touched EscapePodManager, SetIntroCinematicModeProcessor and the intro patch: which pod each player is in, whether the intro is over, and the fires around the pods
        NitroxClient.GameLogic.LocalPlayer localPlayer = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>();
        NitroxClient.GameLogic.PlayerManager playerManager = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.PlayerManager>();
        Player player = Player.main;
        Log.Info($"[SyncHarness] introdump: local {localPlayer.PlayerName}, intro mode {localPlayer.IntroCinematicMode}, at {(player ? player.transform.position.ToString("F1") : "none")}, current pod {PodText(player ? player.currentEscapePod : null)}, EscapePod.main {PodText(EscapePod.main)}");
        foreach (NitroxClient.GameLogic.RemotePlayer remote in playerManager.GetAll())
        {
            Log.Info($"[SyncHarness] introdump: remote {remote.PlayerName} at {remote.Body.transform.position.ToString("F1")}, in pod {PodText(remote.EscapePod)}");
        }
        foreach (EscapePod pod in FindObjectsOfType<EscapePod>())
        {
            float distance = player ? Vector3.Distance(player.transform.position, pod.transform.position) : -1f;
            int podFires = 0;
            foreach (Fire fire in pod.GetComponentsInChildren<Fire>(true))
            {
                podFires += fire.gameObject.activeInHierarchy && !fire.IsExtinguished() ? 1 : 0;
            }
            Transform introHolder = pod.transform.Find("Intro");
            Log.Info($"[SyncHarness] introdump: pod {PodText(pod)} {distance:F0} m from here, health {pod.liveMixin.health:F0}, burning fires inside {podFires}, intro holder {(introHolder ? "present" : "gone")}");
        }
        System.Collections.Generic.List<string> fires = [];
        foreach (Fire fire in FindObjectsOfType<Fire>())
        {
            if (!fire.gameObject.activeInHierarchy || fire.IsExtinguished() || (player && Vector3.Distance(player.transform.position, fire.transform.position) > 80f))
            {
                continue;
            }
            Transform holder = fire.transform.parent ? fire.transform.parent : fire.transform;
            string fireId = holder.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id) ? id.ToString().Substring(0, 8) : "noid";
            fires.Add($"{fireId}:{fire.livemixin.health:F0}@{fire.transform.position.ToString("F0")}");
        }
        Log.Info($"[SyncHarness] introdump: {fires.Count} burning fires within 80 m [{string.Join(",", fires)}]");
    }

    private void OnConsoleCommand_repairpod(NotificationCenter.Notification n)
    {
        EscapePod pod = EscapePod.main;
        if (!pod)
        {
            Log.Warn("[SyncHarness] repairpod: no escape pod");
            return;
        }
        pod.liveMixin.AddHealth(pod.liveMixin.maxHealth);
        if (pod.radioSpawner && pod.radioSpawner.spawnedObj && pod.radioSpawner.spawnedObj.TryGetComponent(out Radio radio))
        {
            radio.liveMixin.AddHealth(radio.liveMixin.maxHealth);
        }
        Log.Info($"[SyncHarness] repairpod: pod health {pod.liveMixin.health:F0}/{pod.liveMixin.maxHealth:F0}");
    }

    private void OnConsoleCommand_sunbeamrange(NotificationCenter.Notification n)
    {
        // ST1h: where is everybody relative to the Sunbeam landing site, what would this client propose, and which of the range check goals did this client complete or schedule
        StoryGoalCustomEventHandler eventHandler = StoryGoalCustomEventHandler.main;
        if (!eventHandler)
        {
            Log.Warn("[SyncHarness] sunbeamrange: no StoryGoalCustomEventHandler");
            return;
        }

        System.Collections.Generic.List<Nitrox.Model.DataStructures.Unity.NitroxVector3> positions = [];
        string local = "none";
        if (Player.main)
        {
            Vector3 position = Player.main.transform.position;
            positions.Add(position.ToDto());
            local = Vector3.Distance(position, eventHandler.landingSiteLocation).ToString("F0");
        }
        System.Collections.Generic.List<string> remotes = [];
        foreach (NitroxClient.GameLogic.RemotePlayer remote in Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.PlayerManager>().GetAll())
        {
            if (!remote.Body)
            {
                remotes.Add($"{remote.PlayerName}:nobody");
                continue;
            }
            Vector3 position = remote.Body.transform.position;
            positions.Add(position.ToDto());
            remotes.Add($"{remote.PlayerName}:{Vector3.Distance(position, eventHandler.landingSiteLocation):F0}");
        }
        bool anyone = NitroxClient.GameLogic.SunbeamRange.IsAnyoneAtLandingSite(positions, eventHandler.landingSiteLocation.ToDto(), eventHandler.landingSiteRadius);
        Log.Info($"[SyncHarness] sunbeamrange: landing site {eventHandler.landingSiteLocation.ToString("F1")} radius {eventHandler.landingSiteRadius:F1}, distance of the local player {local}, of the remote players [{string.Join(",", remotes)}], this client would propose {(anyone ? "in range" : "out of range")}");
        foreach (string key in new[] { Nitrox.Model.Subnautica.Packets.SunbeamRangeDecision.RANGE_CHECK_GOAL, Nitrox.Model.Subnautica.Packets.SunbeamRangeDecision.IN_RANGE_GOAL, Nitrox.Model.Subnautica.Packets.SunbeamRangeDecision.OUT_OF_RANGE_GOAL })
        {
            bool completed = Story.StoryGoalManager.main.completedGoals.Contains(key);
            bool scheduled = Story.StoryGoalScheduler.main.schedule.Exists(goal => goal.goalKey == key);
            Log.Info($"[SyncHarness] sunbeamrange: {key} completed {completed}, scheduled {scheduled}, in PDA log {PDALog.entries.ContainsKey(key)}");
        }
    }

    /// <summary>
    ///     <c>teleporter &lt;identifier&gt;</c> activates a Precursor teleporter pair the way the activation terminal does (<c>TeleporterManager.ActivateTeleporter</c>), so that the
    ///     story sweep can compare <c>teleportersActive</c> on the other client, for a late joiner and after a restart (PD1)
    /// </summary>
    private void OnConsoleCommand_teleporter(NotificationCenter.Notification n)
    {
        string identifier = Arg(n, 0);
        if (string.IsNullOrEmpty(identifier))
        {
            Log.Warn("[SyncHarness] teleporter needs an identifier (cragfieldtp, kooshzonetp, lostrivertp, mushroomforesttp, guntp, islandtp, finaltp, aquariumtp)");
            return;
        }
        TeleporterManager.ActivateTeleporter(identifier);
        Log.Info($"[SyncHarness] teleporter {identifier}: activated, active now [{string.Join(",", TeleporterManager.main.activeTeleporters)}]");
    }

    /// <summary>
    ///     <c>rocketbuild [stages]</c> builds the next stages of the escape rocket without ingredients and without the build bots: the same calls as <c>RocketConstructor.StartRocketConstruction</c>
    ///     makes after it paid (<c>Rocket.StartRocketConstruction</c>, the item goal), then the stage is finished at once and the rocket reports its state like the Nitrox patch does (ST21)
    /// </summary>
    private void OnConsoleCommand_rocketbuild(NotificationCenter.Notification n)
    {
        Rocket rocket = FindObjectOfType<Rocket>();
        if (!rocket)
        {
            Log.Warn("[SyncHarness] rocketbuild: no rocket loaded (go to the launch platform first)");
            return;
        }
        int stages = int.TryParse(Arg(n, 0), out int requested) ? requested : 1;
        for (int i = 0; i < stages && !rocket.IsFinished(); i++)
        {
            TechType stageTech = rocket.GetCurrentStageTech();
            GameObject built = rocket.StartRocketConstruction();
            Story.ItemGoalTracker.OnConstruct(stageTech);
            VFXConstructing constructing = built ? built.GetComponent<VFXConstructing>() : null;
            if (constructing)
            {
                constructing.EndGracefully();
            }
        }
        if (rocket.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId rocketId))
        {
            Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.Entities>().EntityMetadataChanged(rocket, rocketId);
        }
        Log.Info($"[SyncHarness] rocketbuild: stage now {rocket.currentRocketStage}, finished {rocket.IsFinished()}");
    }

    /// <summary>
    ///     <c>rocketchecks</c> completes every preflight check of the rocket the way a switch does (<c>RocketPreflightCheckManager.CompletePreflightCheck</c>, which the Nitrox patch reports)
    /// </summary>
    private void OnConsoleCommand_rocketchecks(NotificationCenter.Notification n)
    {
        Rocket rocket = FindObjectOfType<Rocket>();
        RocketPreflightCheckManager preflight = rocket ? rocket.GetComponent<RocketPreflightCheckManager>() : null;
        if (!preflight)
        {
            Log.Warn("[SyncHarness] rocketchecks: no rocket loaded");
            return;
        }
        foreach (PreflightCheck check in System.Enum.GetValues(typeof(PreflightCheck)))
        {
            if (!preflight.preflightChecks.Contains(check))
            {
                preflight.CompletePreflightCheck(check);
            }
        }
        Log.Info($"[SyncHarness] rocketchecks: completed [{string.Join(",", preflight.preflightChecks)}]");
    }

    /// <summary>
    ///     <c>goalitems</c> lists the story goals whose unlock gives items (<c>OnGoalUnlock.items</c>, Unity data that IL cannot show): every online player gets them when the goal completes, a
    ///     player who joins later does not (ST23d). If the list is empty nothing needs to be done.
    /// </summary>
    private void OnConsoleCommand_goalitems(NotificationCenter.Notification n)
    {
        Story.StoryGoalManager goalManager = Story.StoryGoalManager.main;
        if (!goalManager || !goalManager.onGoalUnlockTracker || !goalManager.onGoalUnlockTracker.unlockData)
        {
            Log.Warn("[SyncHarness] goalitems: no goal unlock data");
            return;
        }
        int withItems = 0;
        foreach (Story.OnGoalUnlock unlock in goalManager.onGoalUnlockTracker.unlockData.onGoalUnlocks)
        {
            if (unlock.items == null || unlock.items.Length == 0)
            {
                continue;
            }
            withItems++;
            Log.Info($"[SyncHarness] goalitems: {unlock.goal} gives {unlock.items.Length} item entries, completed {goalManager.completedGoals.Contains(unlock.goal)}");
        }
        Log.Info($"[SyncHarness] goalitems: {withItems} goals give items");
    }

    private void OnConsoleCommand_ongoalcomplete(NotificationCenter.Notification n)
    {
        if (n?.data == null || n.data.Count < 1 || n.data[0] is not string key || string.IsNullOrEmpty(key))
        {
            Log.Warn("[SyncHarness] ongoalcomplete needs a goal key");
            return;
        }
        bool added = Story.StoryGoalManager.main.OnGoalComplete(key);
        Log.Info($"[SyncHarness] ongoalcomplete {key}: newly completed {added}");
    }
}
#endif
