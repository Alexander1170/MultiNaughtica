#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live batch check po3 : PO3 / VD1, creature hits on a vehicle another player holds are relayed to its holder and counted once.
//
// Commands (all log "[SyncHarness] <command>: ..." lines; the arguments are the two words /console <player> <command> <arg1> <arg2> can carry):
//   po3health                 every vehicle and Cyclops loaded here: tech, id, health/max, the simulation lock THIS client holds ("none" = another player or nobody), distance
//   po3watch [seconds]        logs every change of the health of every loaded vehicle and Cyclops ("po3watch: step ... health <old> -> <new>"), checked every frame (default 600 s)
//   po3info [creature]        lists the loaded creatures that match (an id prefix of 6 or more hex characters, or a tech type name; nothing = all): tech, id, lock this client holds, distance
//   po3target vehicle|cyclops what po3hit, po3src, po3bare and po3pair hit: the nearest vehicle (Seamoth, Prawn) or the nearest Cyclops (default vehicle)
//   po3hit <damage> [creature]    vehicle.liveMixin.TakeDamage(damage, position, Normal, creature): the call every melee attack makes (mode=dealer)
//   po3src <damage> [creature]    TakeDamage with NO dealer while CreatureDamageSource names the creature: what the Reaper's grip, the Sea Dragon's hold and the Crashfish's explosion do (mode=source)
//   po3bare <damage>              TakeDamage with no dealer and no creature source: environment damage (mode=bare, a control)
//   po3pair <damage>:<relayMs>:<holderMs> [creature]    like po3hit, but the client that simulates the creature hits after relayMs and the client that holds the vehicle after holderMs (run it on both clients with /consoleall)
//   po3grip [creature]            the vanilla ReaperLeviathan.DamageVehicle() of the nearest (or the given) Reaper, with the nearest vehicle set as its held one for the call (mode=grip), so the Harmony patch on that method names the Reaper
// Every hit logs one "po3hit: mode=<m> role=<holder|relay|both|neither|other> vehicle=<id> tech=<t> creature=<id> damage=<d> calc=<c> before=<h> after=<h> t=<time>" line, or "po3hit: mode=<m> skipped: <why>".
// calc is DamageSystem.CalculateDamage of the damage for that vehicle, i.e. what a hit that is applied takes off its health.
public partial class HarnessConsoleCommands
{
    private static string po3TargetKind = "vehicle";
    private bool po3Watching;

    private sealed class Po3Target
    {
        public LiveMixin Live;
        public NitroxId Id;
        public string Tech;
        public Vehicle VehicleComponent;
    }

    private sealed class Po3Plan
    {
        public string Mode;
        public string Role;
        public Po3Target Target;
        public GameObject Creature;
        public NitroxId CreatureId;
    }

    private sealed class Po3WatchState
    {
        public readonly Dictionary<LiveMixin, float> Last = new Dictionary<LiveMixin, float>();
        public List<Po3Target> Targets = new List<Po3Target>();
        public float NextScan;
        public bool Failed;
    }

    private static string Po3F(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string Po3Lock(NitroxId id)
    {
        if (id == null)
        {
            return "none";
        }
        NitroxClient.GameLogic.SimulationOwnership ownership = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
        SimulationLockType lockType;
        return ownership.TryGetLockType(id, out lockType) ? lockType.ToString() : "none";
    }

    private static bool Po3IsHex(string text)
    {
        foreach (char c in text)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex)
            {
                return false;
            }
        }
        return text.Length > 0;
    }

    private static float Po3Part(string[] parts, int index, float defaultValue)
    {
        float value;
        if (index < parts.Length && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }
        return defaultValue;
    }

    /// <summary>
    ///     Every vehicle (Seamoth, Prawn, ...) and Cyclops loaded here whose LiveMixin has a NitroxId, which is what the damage patch needs to relay or to apply a hit.
    /// </summary>
    private static List<Po3Target> Po3Targets()
    {
        List<Po3Target> targets = new List<Po3Target>();
        foreach (Vehicle vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
        {
            NitroxId vehicleId;
            if (vehicle.liveMixin && vehicle.liveMixin.TryGetNitroxId(out vehicleId))
            {
                Po3Target found = new Po3Target();
                found.Live = vehicle.liveMixin;
                found.Id = vehicleId;
                found.Tech = CraftData.GetTechType(vehicle.gameObject).ToString();
                found.VehicleComponent = vehicle;
                targets.Add(found);
            }
        }
        foreach (SubControl subControl in UnityEngine.Object.FindObjectsOfType<SubControl>())
        {
            NitroxId cyclopsId;
            if (subControl.sub && subControl.sub.live && subControl.sub.live.TryGetNitroxId(out cyclopsId))
            {
                Po3Target found = new Po3Target();
                found.Live = subControl.sub.live;
                found.Id = cyclopsId;
                found.Tech = "Cyclops";
                found.VehicleComponent = null;
                targets.Add(found);
            }
        }
        return targets;
    }

    private static Po3Target Po3FindTarget(string kind)
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        bool wantCyclops = kind == "cyclops";
        Po3Target best = null;
        float bestDistance = float.MaxValue;
        foreach (Po3Target target in Po3Targets())
        {
            if ((target.Tech == "Cyclops") != wantCyclops)
            {
                continue;
            }
            float distance = Vector3.Distance(here, target.Live.transform.position);
            if (distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static bool Po3CreatureMatches(Creature creature, string spec, out NitroxId id)
    {
        id = null;
        NitroxId creatureId;
        if (!creature || !creature.gameObject.activeInHierarchy || !creature.TryGetNitroxId(out creatureId))
        {
            return false;
        }
        id = creatureId;
        if (spec.Length >= 6 && Po3IsHex(spec))
        {
            return creatureId.ToString().StartsWith(spec, StringComparison.OrdinalIgnoreCase);
        }
        return spec.Length == 0 || string.Equals(CraftData.GetTechType(creature.gameObject).ToString(), spec, StringComparison.OrdinalIgnoreCase);
    }

    private static GameObject Po3FindCreature(string spec, Vector3 near, out NitroxId id)
    {
        id = null;
        GameObject best = null;
        float bestDistance = float.MaxValue;
        foreach (Creature creature in UnityEngine.Object.FindObjectsOfType<Creature>())
        {
            NitroxId creatureId;
            if (!Po3CreatureMatches(creature, spec, out creatureId))
            {
                continue;
            }
            float distance = Vector3.Distance(creature.transform.position, near);
            if (distance < bestDistance)
            {
                best = creature.gameObject;
                bestDistance = distance;
                id = creatureId;
            }
        }
        return best;
    }

    /// <summary>
    ///     What a hit command works with: the vehicle, the creature (none for mode bare) and the role of this client: holder = holds the vehicle's simulation lock, relay = holds the
    ///     creature's lock (its hit has to be relayed), both, neither, other = bare mode on a client that does not hold the vehicle.
    /// </summary>
    private static Po3Plan Po3MakePlan(string mode, string creatureSpec, string kind)
    {
        Po3Target target = Po3FindTarget(kind);
        if (target == null)
        {
            Log.Info($"[SyncHarness] po3hit: mode={mode} skipped: no {kind} with a NitroxId is loaded on this client");
            return null;
        }
        NitroxClient.GameLogic.SimulationOwnership ownership = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
        bool holdsVehicle = ownership.HasAnyLockType(target.Id);
        Po3Plan plan = new Po3Plan();
        plan.Mode = mode;
        plan.Target = target;
        if (mode == "bare")
        {
            plan.Role = holdsVehicle ? "holder" : "other";
            return plan;
        }

        NitroxId creatureId;
        GameObject creature = Po3FindCreature(creatureSpec, target.Live.transform.position, out creatureId);
        if (!creature)
        {
            Log.Info($"[SyncHarness] po3hit: mode={mode} skipped: no creature '{creatureSpec}' with a NitroxId is loaded on this client");
            return null;
        }
        bool simulatesCreature = ownership.HasAnyLockType(creatureId);
        plan.Creature = creature;
        plan.CreatureId = creatureId;
        if (holdsVehicle && simulatesCreature)
        {
            plan.Role = "both";
        }
        else if (holdsVehicle)
        {
            plan.Role = "holder";
        }
        else
        {
            plan.Role = simulatesCreature ? "relay" : "neither";
        }
        return plan;
    }

    /// <summary>
    ///     The hit itself, with the call every melee attack makes (dealer), or without a dealer (source: while CreatureDamageSource names the creature, like the three attack patches do; bare: not even that).
    /// </summary>
    private static void Po3Apply(Po3Plan plan, float damage)
    {
        LiveMixin live = plan.Target.Live;
        GameObject dealer = plan.Mode == "dealer" ? plan.Creature : null;
        float before = live.health;
        float calc = DamageSystem.CalculateDamage(damage, DamageType.Normal, live.gameObject, dealer);
        Vector3 position = live.transform.position;
        GameObject previousSource = null;
        bool sourceEntered = false;
        try
        {
            if (plan.Mode == "source")
            {
                previousSource = NitroxClient.GameLogic.CreatureDamageSource.Enter(plan.Creature);
                sourceEntered = true;
            }
            live.TakeDamage(damage, position, DamageType.Normal, dealer);
        }
        finally
        {
            if (sourceEntered)
            {
                NitroxClient.GameLogic.CreatureDamageSource.Exit(previousSource);
            }
        }
        float after = live.health;
        string creatureText = plan.CreatureId != null ? plan.CreatureId.ToString() : "none";
        string line = $"[SyncHarness] po3hit: mode={plan.Mode} role={plan.Role} vehicle={plan.Target.Id} tech={plan.Target.Tech} creature={creatureText} damage={Po3F(damage)} calc={Po3F(calc)} before={Po3F(before)} after={Po3F(after)} t={Po3F(Time.time)}";
        Log.Info(line);
    }

    private void Po3HitCommand(NotificationCenter.Notification n, string mode)
    {
        try
        {
            float damage = LiveArgFloat(n, 0, 10f);
            Po3Plan plan = Po3MakePlan(mode, LiveArgString(n, 1, ""), po3TargetKind);
            if (plan != null)
            {
                Po3Apply(plan, damage);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"[SyncHarness] po3hit: mode={mode} failed");
        }
    }

    public void LiveCommand_po3hit(NotificationCenter.Notification n)
    {
        Po3HitCommand(n, "dealer");
    }

    public void LiveCommand_po3src(NotificationCenter.Notification n)
    {
        Po3HitCommand(n, "source");
    }

    public void LiveCommand_po3bare(NotificationCenter.Notification n)
    {
        Po3HitCommand(n, "bare");
    }

    public void LiveCommand_po3pair(NotificationCenter.Notification n)
    {
        try
        {
            string[] parts = LiveArgString(n, 0, "10:0:150").Split(':');
            float damage = Po3Part(parts, 0, 10f);
            float relayMilliseconds = Po3Part(parts, 1, 0f);
            float holderMilliseconds = Po3Part(parts, 2, 150f);
            Po3Plan plan = Po3MakePlan("dealer", LiveArgString(n, 1, ""), po3TargetKind);
            if (plan == null)
            {
                return;
            }
            if (plan.Role != "relay" && plan.Role != "holder")
            {
                Log.Info($"[SyncHarness] po3hit: mode=dealer skipped: this client is neither the simulator of the creature nor the holder of the vehicle (role {plan.Role})");
                return;
            }
            float delay = (plan.Role == "relay" ? relayMilliseconds : holderMilliseconds) / 1000f;
            StartCoroutine(Po3HitAfter(plan, damage, delay));
        }
        catch (Exception e)
        {
            Log.Error(e, "[SyncHarness] po3hit: po3pair failed");
        }
    }

    private IEnumerator Po3HitAfter(Po3Plan plan, float damage, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }
        try
        {
            if (plan.Target.Live && plan.Creature)
            {
                Po3Apply(plan, damage);
            }
            else
            {
                Log.Info("[SyncHarness] po3hit: mode=dealer skipped: the vehicle or the creature is gone");
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "[SyncHarness] po3hit: delayed hit failed");
        }
    }

    public void LiveCommand_po3grip(NotificationCenter.Notification n)
    {
        try
        {
            string spec = LiveArgString(n, 0, "");
            Po3Target target = Po3FindTarget("vehicle");
            if (target == null || !target.VehicleComponent)
            {
                Log.Info("[SyncHarness] po3hit: mode=grip skipped: no vehicle with a NitroxId is loaded on this client");
                return;
            }

            ReaperLeviathan reaper = null;
            NitroxId reaperId = null;
            float bestDistance = float.MaxValue;
            foreach (ReaperLeviathan candidate in UnityEngine.Object.FindObjectsOfType<ReaperLeviathan>())
            {
                NitroxId candidateId;
                if (!Po3CreatureMatches(candidate, spec, out candidateId))
                {
                    continue;
                }
                float distance = Vector3.Distance(candidate.transform.position, target.Live.transform.position);
                if (distance < bestDistance)
                {
                    reaper = candidate;
                    reaperId = candidateId;
                    bestDistance = distance;
                }
            }
            if (!reaper)
            {
                Log.Info($"[SyncHarness] po3hit: mode=grip skipped: no ReaperLeviathan '{spec}' with a NitroxId is loaded on this client");
                return;
            }

            FieldInfo holdingField = typeof(ReaperLeviathan).GetField("holdingVehicle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo damageMethod = typeof(ReaperLeviathan).GetMethod("DamageVehicle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (holdingField == null || damageMethod == null)
            {
                Log.Info("[SyncHarness] po3hit: mode=grip skipped: ReaperLeviathan has no holdingVehicle field or DamageVehicle method in this game version");
                return;
            }

            NitroxClient.GameLogic.SimulationOwnership ownership = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
            bool holdsVehicle = ownership.HasAnyLockType(target.Id);
            bool simulatesReaper = ownership.HasAnyLockType(reaperId);
            string role = holdsVehicle && simulatesReaper ? "both" : holdsVehicle ? "holder" : simulatesReaper ? "relay" : "neither";
            float perSecond = reaper.seamothDamagePerSecond;
            float calc = DamageSystem.CalculateDamage(perSecond, DamageType.Normal, target.Live.gameObject, null);
            float before = target.Live.health;

            // The grip of the game: InvokeRepeating("DamageVehicle") hits the held vehicle once a second. The held vehicle is set for this one call only (no grab, nothing is attached).
            object previousHolding = holdingField.GetValue(reaper);
            holdingField.SetValue(reaper, target.VehicleComponent);
            try
            {
                damageMethod.Invoke(reaper, null);
            }
            finally
            {
                holdingField.SetValue(reaper, previousHolding);
            }

            string line = $"[SyncHarness] po3hit: mode=grip role={role} vehicle={target.Id} tech={target.Tech} creature={reaperId} damage={Po3F(perSecond)} calc={Po3F(calc)} before={Po3F(before)} after={Po3F(target.Live.health)} t={Po3F(Time.time)}";
            Log.Info(line);
        }
        catch (Exception e)
        {
            Log.Error(e, "[SyncHarness] po3hit: mode=grip failed");
        }
    }

    public void LiveCommand_po3target(NotificationCenter.Notification n)
    {
        po3TargetKind = LiveArgString(n, 0, "vehicle").ToLowerInvariant() == "cyclops" ? "cyclops" : "vehicle";
        Log.Info($"[SyncHarness] po3target: {po3TargetKind}");
    }

    public void LiveCommand_po3health(NotificationCenter.Notification n)
    {
        try
        {
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
            int count = 0;
            foreach (Po3Target target in Po3Targets())
            {
                count++;
                string line = $"[SyncHarness] po3health: {target.Tech} id {target.Id} health {Po3F(target.Live.health)}/{Po3F(target.Live.maxHealth)} lock {Po3Lock(target.Id)} dist {Po3F(Vector3.Distance(here, target.Live.transform.position))}";
                Log.Info(line);
            }
            Log.Info($"[SyncHarness] po3health: {count} targets");
        }
        catch (Exception e)
        {
            Log.Error(e, "[SyncHarness] po3health failed");
        }
    }

    public void LiveCommand_po3info(NotificationCenter.Notification n)
    {
        try
        {
            string spec = LiveArgString(n, 0, "");
            Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
            int listed = 0;
            foreach (Creature creature in UnityEngine.Object.FindObjectsOfType<Creature>())
            {
                NitroxId creatureId;
                if (!Po3CreatureMatches(creature, spec, out creatureId))
                {
                    continue;
                }
                listed++;
                if (listed <= 12)
                {
                    string line = $"[SyncHarness] po3info: {CraftData.GetTechType(creature.gameObject)} id {creatureId} lock {Po3Lock(creatureId)} dist {Po3F(Vector3.Distance(here, creature.transform.position))}";
                    Log.Info(line);
                }
            }
            Log.Info($"[SyncHarness] po3info: {listed} creatures match '{spec}'");
        }
        catch (Exception e)
        {
            Log.Error(e, "[SyncHarness] po3info failed");
        }
    }

    public void LiveCommand_po3watch(NotificationCenter.Notification n)
    {
        if (po3Watching)
        {
            Log.Info("[SyncHarness] po3watch: started (it was running already)");
            return;
        }
        float seconds = LiveArgFloat(n, 0, 600f);
        po3Watching = true;
        StartCoroutine(Po3Watch(seconds));
        Log.Info($"[SyncHarness] po3watch: started for {Po3F(seconds)} s");
    }

    private IEnumerator Po3Watch(float seconds)
    {
        Po3WatchState state = new Po3WatchState();
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            Po3WatchTick(state);
            yield return null;
        }
        po3Watching = false;
        Log.Info("[SyncHarness] po3watch: stopped");
    }

    private static void Po3WatchTick(Po3WatchState state)
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            if (now >= state.NextScan)
            {
                state.Targets = Po3Targets();
                state.NextScan = now + 1f;
            }
            foreach (Po3Target target in state.Targets)
            {
                if (!target.Live)
                {
                    continue;
                }
                float health = target.Live.health;
                float previous;
                if (!state.Last.TryGetValue(target.Live, out previous))
                {
                    state.Last[target.Live] = health;
                    continue;
                }
                if (Mathf.Abs(health - previous) > 0.001f)
                {
                    state.Last[target.Live] = health;
                    string line = $"[SyncHarness] po3watch: step {target.Tech} id {target.Id} health {Po3F(previous)} -> {Po3F(health)} t={Po3F(Time.time)} lock {Po3Lock(target.Id)}";
                    Log.Info(line);
                }
            }
        }
        catch (Exception e)
        {
            if (!state.Failed)
            {
                state.Failed = true;
                Log.Error(e, "[SyncHarness] po3watch: a check failed (it keeps running, this is logged once)");
            }
        }
    }
}
#endif
