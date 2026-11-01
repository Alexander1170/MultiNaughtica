#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live batch checks K16 (a Reaper grabs a Seamoth or Prawn suit on one client, the copy of the Reaper on the other client follows) and K18a (the Crabsquid's EMP blast is made on the
// other client too), Debug builds only. Docs: a harness script that is not part of this repository and k18a.md. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>),
// run with the server's "/console <player> <command> [arg] [arg]" (at most two arguments, all lower case) and log one "[SyncHarness] <name>: key=value ..." line per fact.
//
//   reaperstate                 every loaded Reaper (id, whether THIS client holds its simulation lock, whether it holds a vehicle, which, the vehicle's movement replicator) and every Seamoth / Prawn suit
//   reapergrab [kind] [mine]    kind = seamoth | exosuit | any; "mine" = only a Reaper whose simulation lock this client holds. Makes the nearest such Reaper grab the nearest free vehicle of that kind
//                               through ReaperLeviathan.GrabSeamoth / GrabExosuit, the calls that the Reaper's own bite (ReaperMeleeAttack.OnTouch) makes and that end in the patched GrabVehicle. Waits up to 15 s for both to exist
//                               ("reapergrab: grabbed ..." at once, "reapergrab: after 1.5 s ..." with the vehicle's distance to the Reaper's mouth 1.5 s later)
//   reaperrelease               the nearest Reaper that holds a vehicle lets go through ReleaseVehicle (what its 8 to 13 s timer calls)
//   reaperwatch [seconds]       for that long (default 45 s) logs every Reaper of this client that starts or stops holding a vehicle, with the vehicle's movement replicator at that moment
//   k16spawn <TechType> <x,y,z> spawns a prefab on THIS client the way /spawncreature does on the issuing client (DebugSpawnEntityProcessor), so that this player simulates it
//   empstate                    every Crabsquid (EMPAttack): id, simulation lock, alive; every vehicle with its EnergyMixin state (electronicsDisabled, charge); the EMP blasts alive
//   empfire                     the nearest Crabsquid's EMPAttack.CastEMPBlast(), a plain call like the AI's
//   empwatch [seconds]          for that long (default 30 s), logs every new EMP blast and every vehicle whose electronics go off or on
public partial class HarnessConsoleCommands
{
    private static NitroxClient.GameLogic.SimulationOwnership K16Ownership()
    {
        return Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
    }

    private static string K16Id(Component component)
    {
        NitroxId id;
        if (component && component.TryGetNitroxId(out id))
        {
            return id.ToString();
        }
        return "noid";
    }

    private static bool K16Simulated(Component component)
    {
        NitroxId id;
        return component && component.TryGetNitroxId(out id) && K16Ownership().HasAnyLockType(id);
    }

    private static string K16Pos(Component component)
    {
        return component ? component.transform.position.ToString("F1") : "-";
    }

    private static string K16Tech(Vehicle vehicle)
    {
        return (vehicle is SeaMoth) ? "Seamoth" : "Exosuit";
    }

    private static string K16Replicator(Vehicle vehicle)
    {
        if (!vehicle)
        {
            return "none";
        }
        VehicleMovementReplicator replicator = vehicle.GetComponent<VehicleMovementReplicator>();
        if (!replicator)
        {
            return "none";
        }
        return replicator.enabled ? "enabled" : "disabled";
    }

    // How far the held vehicle is from the Reaper's mouth (its attach point): the Reaper's own Update puts the vehicle there within a second of the grab, so a small number means that the
    // vehicle rides with the Reaper on this client; -1 when nothing is held or there is no attach point
    private static float K16AttachDistance(ReaperLeviathan reaper)
    {
        if (!reaper || !reaper.IsHoldingVehicle() || !reaper.seamothAttachPoint)
        {
            return -1f;
        }
        return Vector3.Distance(reaper.holdingVehicle.transform.position, reaper.seamothAttachPoint.position);
    }

    private static bool K16HeldByReaper(Vehicle vehicle)
    {
        foreach (ReaperLeviathan reaper in FindObjectsOfType<ReaperLeviathan>())
        {
            if (reaper && reaper.IsHoldingVehicle() && reaper.holdingVehicle == vehicle)
            {
                return true;
            }
        }
        return false;
    }

    private static ReaperLeviathan K16FindReaper(bool onlyMine, bool onlyHolding)
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        ReaperLeviathan best = null;
        float bestDistance = float.MaxValue;
        foreach (ReaperLeviathan reaper in FindObjectsOfType<ReaperLeviathan>())
        {
            if (!reaper || !reaper.gameObject.activeInHierarchy)
            {
                continue;
            }
            if (onlyHolding && !reaper.IsHoldingVehicle())
            {
                continue;
            }
            if (onlyMine && !K16Simulated(reaper))
            {
                continue;
            }
            float distance = Vector3.Distance(here, reaper.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = reaper;
            }
        }
        return best;
    }

    private static Vehicle K16FindVehicle(string kind, Vector3 from)
    {
        Vehicle best = null;
        float bestDistance = float.MaxValue;
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            bool isSeamoth = vehicle is SeaMoth;
            bool isExosuit = vehicle is Exosuit;
            if (!isSeamoth && !isExosuit)
            {
                continue;
            }
            if ((kind == "seamoth" && !isSeamoth) || ((kind == "exosuit" || kind == "prawn") && !isExosuit))
            {
                continue;
            }
            if (!vehicle.gameObject.activeInHierarchy || vehicle.docked || K16HeldByReaper(vehicle))
            {
                continue;
            }
            float distance = Vector3.Distance(from, vehicle.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = vehicle;
            }
        }
        return best;
    }

    // The vanilla call of the Reaper's bite; the exception text (empty when it worked) instead of an exception, so that a failure is a log line
    private static string K16TryGrab(ReaperLeviathan reaper, Vehicle vehicle)
    {
        try
        {
            SeaMoth seamoth = vehicle as SeaMoth;
            Exosuit exosuit = vehicle as Exosuit;
            if (seamoth)
            {
                reaper.GrabSeamoth(seamoth);
                return "";
            }
            if (exosuit)
            {
                reaper.GrabExosuit(exosuit);
                return "";
            }
            return "the vehicle is neither a Seamoth nor a Prawn suit";
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
    }

    private void LiveCommand_reaperstate(NotificationCenter.Notification n)
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        int reaperCount = 0;
        foreach (ReaperLeviathan reaper in FindObjectsOfType<ReaperLeviathan>())
        {
            reaperCount++;
            bool holding = reaper.IsHoldingVehicle();
            string heldKind = reaper.IsHoldingSeamoth() ? "Seamoth" : (reaper.IsHoldingExosuit() ? "Exosuit" : "none");
            string heldId = holding ? K16Id(reaper.holdingVehicle) : "none";
            string heldReplicator = holding ? K16Replicator(reaper.holdingVehicle) : "-";
            bool timerPending = reaper.IsInvoking("ReleaseVehicle");
            float reaperDistance = Vector3.Distance(here, reaper.transform.position);
            float attachDistance = K16AttachDistance(reaper);
            string reaperLine = $"[SyncHarness] reaperstate: reaper id={K16Id(reaper)} simulated={K16Simulated(reaper)} holding={holding} kind={heldKind} vehicle={heldId} replicator={heldReplicator} active={reaper.isActiveAndEnabled} releaseTimer={timerPending} dist={reaperDistance:F0} attachDist={attachDistance:F1} pos={K16Pos(reaper)}";
            Log.Info(reaperLine);
        }
        int vehicleCount = 0;
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            if (!(vehicle is SeaMoth) && !(vehicle is Exosuit))
            {
                continue;
            }
            vehicleCount++;
            string vehicleLine = $"[SyncHarness] reaperstate: vehicle tech={K16Tech(vehicle)} id={K16Id(vehicle)} simulated={K16Simulated(vehicle)} replicator={K16Replicator(vehicle)} docked={vehicle.docked} heldByReaper={K16HeldByReaper(vehicle)} pos={K16Pos(vehicle)}";
            Log.Info(vehicleLine);
        }
        Log.Info($"[SyncHarness] reaperstate: summary reapers={reaperCount} vehicles={vehicleCount}");
    }

    private void LiveCommand_reapergrab(NotificationCenter.Notification n)
    {
        string kind = LiveArgString(n, 0, "any");
        bool onlyMine = LiveArgString(n, 1, "any") == "mine";
        StartCoroutine(K16GrabRoutine(kind, onlyMine));
    }

    private IEnumerator K16GrabRoutine(string kind, bool onlyMine)
    {
        float giveUpAt = Time.time + 15f;
        string reason = "nothing looked at yet";
        while (Time.time < giveUpAt)
        {
            ReaperLeviathan reaper = K16FindReaper(onlyMine, false);
            NitroxId reaperId;
            NitroxId vehicleId;
            if (!reaper)
            {
                reason = onlyMine ? "no Reaper that this client simulates is loaded" : "no Reaper is loaded";
            }
            else if (!reaper.TryGetNitroxId(out reaperId))
            {
                reason = "the Reaper has no NitroxId";
            }
            else if (reaper.IsHoldingVehicle())
            {
                Log.Info($"[SyncHarness] reapergrab: gave up, reaper={reaperId} already holds a vehicle");
                yield break;
            }
            else
            {
                Vehicle vehicle = K16FindVehicle(kind, reaper.transform.position);
                if (!vehicle)
                {
                    reason = $"no free {kind} is loaded";
                }
                else if (!vehicle.TryGetNitroxId(out vehicleId))
                {
                    reason = "the vehicle has no NitroxId";
                }
                else
                {
                    bool canGrab = reaper.GetCanGrabVehicle();
                    bool simulated = K16Simulated(reaper);
                    float distance = Vector3.Distance(reaper.transform.position, vehicle.transform.position);
                    // A Reaper that swam off since it was spawned may be outside the cells the other player has loaded (that client would not have it): it is put back near the vehicle first
                    string pulled = "no";
                    if (distance > 60f)
                    {
                        Vector3 pulledTo = vehicle.transform.position + new Vector3(0f, -10f, 25f);
                        reaper.transform.position = pulledTo;
                        pulled = $"yes, from {distance:F0} m away";
                        distance = Vector3.Distance(reaper.transform.position, vehicle.transform.position);
                    }
                    string error = K16TryGrab(reaper, vehicle);
                    if (error.Length > 0)
                    {
                        Log.Warn($"[SyncHarness] reapergrab: gave up, the grab call threw ({error})");
                        yield break;
                    }
                    string grabLine = $"[SyncHarness] reapergrab: grabbed reaper={reaperId} vehicle={vehicleId} tech={K16Tech(vehicle)} simulated={simulated} holding={reaper.IsHoldingVehicle()} replicator={K16Replicator(vehicle)} canGrab={canGrab} pulled={pulled} dist={distance:F1} reaperPos={K16Pos(reaper)} vehiclePos={K16Pos(vehicle)}";
                    Log.Info(grabLine);
                    // the Reaper's own Update has put the vehicle at its mouth one second after the grab
                    yield return new WaitForSeconds(1.5f);
                    string afterLine = $"[SyncHarness] reapergrab: after 1.5 s reaper={reaperId} holding={reaper.IsHoldingVehicle()} attachDist={K16AttachDistance(reaper):F1} replicator={K16Replicator(vehicle)}";
                    Log.Info(afterLine);
                    yield break;
                }
            }
            yield return new WaitForSeconds(0.5f);
        }
        Log.Info($"[SyncHarness] reapergrab: gave up after 15 s ({reason})");
    }

    private void LiveCommand_reaperrelease(NotificationCenter.Notification n)
    {
        ReaperLeviathan reaper = K16FindReaper(false, true);
        if (!reaper)
        {
            Log.Info("[SyncHarness] reaperrelease: gave up, no Reaper that holds a vehicle is loaded here");
            return;
        }
        Vehicle held = reaper.holdingVehicle;
        string reaperId = K16Id(reaper);
        string vehicleId = K16Id(held);
        string tech = K16Tech(held);
        string error = "";
        try
        {
            reaper.ReleaseVehicle();
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
        }
        Log.Info($"[SyncHarness] reaperrelease: released reaper={reaperId} vehicle={vehicleId} tech={tech} holding={reaper.IsHoldingVehicle()} replicator={K16Replicator(held)} error={error}");
    }

    // For that many seconds (default 45), four times a second: logs every Reaper of this client that starts or stops holding a vehicle (so the follower's state is read at the moment it changes,
    // not some chat commands later) and, 1.2 s into a hold, how far the vehicle is from the Reaper's mouth
    private void LiveCommand_reaperwatch(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 45f), 1f, 180f);
        StartCoroutine(K16WatchRoutine(seconds));
    }

    private IEnumerator K16WatchRoutine(float seconds)
    {
        float endTime = Time.time + seconds;
        Dictionary<int, Vehicle> heldVehicle = new Dictionary<int, Vehicle>();
        Dictionary<int, float> heldSince = new Dictionary<int, float>();
        HashSet<int> attachLogged = new HashSet<int>();
        Log.Info($"[SyncHarness] reaperwatch: watching for {seconds:F0} s");
        while (Time.time < endTime)
        {
            foreach (ReaperLeviathan reaper in FindObjectsOfType<ReaperLeviathan>())
            {
                int key = reaper.GetInstanceID();
                bool holding = reaper.IsHoldingVehicle();
                Vehicle previous;
                bool wasHolding = heldVehicle.TryGetValue(key, out previous);
                if (holding && !wasHolding)
                {
                    heldVehicle[key] = reaper.holdingVehicle;
                    heldSince[key] = Time.time;
                    attachLogged.Remove(key);
                    string grabbedLine = $"[SyncHarness] reaperwatch: grabbed reaper={K16Id(reaper)} vehicle={K16Id(reaper.holdingVehicle)} tech={K16Tech(reaper.holdingVehicle)} simulated={K16Simulated(reaper)} replicator={K16Replicator(reaper.holdingVehicle)} active={reaper.isActiveAndEnabled} t={Time.time:F1}";
                    Log.Info(grabbedLine);
                }
                else if (!holding && wasHolding)
                {
                    heldVehicle.Remove(key);
                    string letGoLine = $"[SyncHarness] reaperwatch: let go reaper={K16Id(reaper)} vehicle={K16Id(previous)} replicator={K16Replicator(previous)} t={Time.time:F1}";
                    Log.Info(letGoLine);
                }
                else if (holding && !attachLogged.Contains(key) && Time.time - heldSince[key] >= 1.2f)
                {
                    attachLogged.Add(key);
                    string attachLine = $"[SyncHarness] reaperwatch: attach reaper={K16Id(reaper)} vehicle={K16Id(reaper.holdingVehicle)} attachDist={K16AttachDistance(reaper):F1} t={Time.time:F1}";
                    Log.Info(attachLine);
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
        Log.Info("[SyncHarness] reaperwatch: done");
    }

    private void LiveCommand_k16spawn(NotificationCenter.Notification n)
    {
        string typeText = LiveArgString(n, 0, "");
        string[] parts = LiveArgString(n, 1, "").Split(',');
        float x = 0f;
        float y = 0f;
        float z = 0f;
        TechType techType;
        bool understood = Enum.TryParse(typeText, true, out techType) && techType != TechType.None && parts.Length == 3 &&
                          float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                          float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) &&
                          float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
        if (!understood)
        {
            Log.Warn("[SyncHarness] k16spawn: needs <TechType> <x,y,z> (no spaces in the position)");
            return;
        }
        NitroxClient.GameLogic.NitroxConsole nitroxConsole = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.NitroxConsole>();
        NitroxClient.Communication.Packets.Processors.DebugSpawnEntityProcessor processor = new NitroxClient.Communication.Packets.Processors.DebugSpawnEntityProcessor(nitroxConsole);
        Nitrox.Model.Subnautica.Packets.DebugSpawnEntity packet = new Nitrox.Model.Subnautica.Packets.DebugSpawnEntity(techType.ToDto(), new Vector3(x, y, z).ToDto(), Quaternion.identity.ToDto());
        _ = processor.Process(null, packet);
        Log.Info($"[SyncHarness] k16spawn: this client was asked to spawn {techType} at ({x:F1}, {y:F1}, {z:F1})");
    }

    private static EMPAttack K18aFindAttack()
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        EMPAttack best = null;
        float bestDistance = float.MaxValue;
        foreach (EMPAttack attack in FindObjectsOfType<EMPAttack>())
        {
            if (!attack)
            {
                continue;
            }
            float distance = Vector3.Distance(here, attack.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = attack;
            }
        }
        return best;
    }

    // Whether any energy source of the vehicle has its electronics off (set by EnergyMixin.DisableElectronicsForTime, which an EMP blast calls) and the charge it reports (0 while off)
    private static bool K18aDisabled(Vehicle vehicle, out float charge)
    {
        bool disabled = false;
        charge = 0f;
        foreach (EnergyMixin mixin in vehicle.GetComponentsInChildren<EnergyMixin>(true))
        {
            disabled = disabled || mixin.electronicsDisabled;
            charge += mixin.charge;
        }
        return disabled;
    }

    private void LiveCommand_empstate(NotificationCenter.Notification n)
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        int attackCount = 0;
        Vector3 firstPosition = Vector3.zero;
        foreach (EMPAttack attack in FindObjectsOfType<EMPAttack>())
        {
            Creature creature = attack.GetComponentInParent<Creature>();
            LiveMixin liveMixin = attack.GetComponentInParent<LiveMixin>();
            bool alive = !liveMixin || liveMixin.IsAlive();
            if (attackCount == 0)
            {
                firstPosition = attack.transform.position;
            }
            attackCount++;
            float attackDistance = Vector3.Distance(here, attack.transform.position);
            Log.Info($"[SyncHarness] empstate: crabsquid id={K16Id(creature)} simulated={K16Simulated(creature)} alive={alive} dist={attackDistance:F0} pos={K16Pos(attack)}");
        }
        int vehicleCount = 0;
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            vehicleCount++;
            float charge;
            bool disabled = K18aDisabled(vehicle, out charge);
            float crabsquidDistance = attackCount > 0 ? Vector3.Distance(vehicle.transform.position, firstPosition) : -1f;
            Log.Info($"[SyncHarness] empstate: vehicle tech={vehicle.name} id={K16Id(vehicle)} electronicsDisabled={disabled} charge={charge:F1} distToCrabsquid={crabsquidDistance:F1} pos={K16Pos(vehicle)}");
        }
        int blastsAlive = FindObjectsOfType<EMPBlast>().Length;
        Log.Info($"[SyncHarness] empstate: summary crabsquids={attackCount} vehicles={vehicleCount} blastsAlive={blastsAlive}");
    }

    private void LiveCommand_empfire(NotificationCenter.Notification n)
    {
        EMPAttack attack = K18aFindAttack();
        if (!attack)
        {
            Log.Info("[SyncHarness] empfire: gave up, no Crabsquid (EMPAttack) is loaded here");
            return;
        }
        Creature creature = attack.GetComponentInParent<Creature>();
        LiveMixin liveMixin = attack.GetComponentInParent<LiveMixin>();
        bool alive = !liveMixin || liveMixin.IsAlive();
        string creatureId = K16Id(creature);
        bool simulated = K16Simulated(creature);
        // EMPAttack_CastEMPBlast_Patch sends EmpFired when the creature has a NitroxId and this client holds any lock on it
        bool announces = creatureId != "noid" && simulated;
        string creatureName = creature ? creature.name : "none";
        float distance = Player.main ? Vector3.Distance(Player.main.transform.position, attack.transform.position) : -1f;
        string error = "";
        try
        {
            attack.CastEMPBlast();
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
        }
        Log.Info($"[SyncHarness] empfire: fired creature={creatureName} id={creatureId} simulated={simulated} announces={announces} alive={alive} dist={distance:F0} error={error}");
    }

    private void LiveCommand_empwatch(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 30f), 1f, 120f);
        StartCoroutine(K18aWatchRoutine(seconds));
    }

    private IEnumerator K18aWatchRoutine(float seconds)
    {
        float endTime = Time.time + seconds;
        int blastsAlive = 0;
        int blastsSeen = 0;
        int vehiclesHit = 0;
        Dictionary<int, bool> wasDisabled = new Dictionary<int, bool>();
        Log.Info($"[SyncHarness] empwatch: watching for {seconds:F0} s");
        while (Time.time < endTime)
        {
            int blastsNow = FindObjectsOfType<EMPBlast>().Length;
            if (blastsNow > blastsAlive)
            {
                blastsSeen += blastsNow - blastsAlive;
                Log.Info($"[SyncHarness] empwatch: blast appeared, {blastsNow} alive, {blastsSeen} seen, t={Time.time:F1}");
            }
            blastsAlive = blastsNow;
            foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
            {
                float charge;
                bool disabled = K18aDisabled(vehicle, out charge);
                bool before;
                if (!wasDisabled.TryGetValue(vehicle.GetInstanceID(), out before))
                {
                    before = false;
                }
                if (disabled != before)
                {
                    if (disabled)
                    {
                        vehiclesHit++;
                    }
                    Log.Info($"[SyncHarness] empwatch: vehicle tech={vehicle.name} id={K16Id(vehicle)} electronicsDisabled={disabled} was={before} charge={charge:F1} t={Time.time:F1}");
                }
                wasDisabled[vehicle.GetInstanceID()] = disabled;
            }
            yield return new WaitForSeconds(0.1f);
        }
        Log.Info($"[SyncHarness] empwatch: done, blasts seen {blastsSeen}, vehicles that lost power {vehiclesHit}");
    }
}
#endif
