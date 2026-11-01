#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "hullrest" : a parked Cyclops must not wander and snap back (bug 11, VEHICLES.md section 7). Debug builds only. Registered by
// HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command>" and log
// "[SyncHarness] <name>: key=value ..." lines (no spaces inside a value).
//
//   hullrest [seconds]        for that long (default 60) logs the nearest Cyclops every 100 ms: place, distance from the first sample, speed of its rigidbody, turn rate, whether this
//                             client simulates it or draws a copy (role=sim|replica), the speed the replicator extrapolates with, whether it judges the hull to be at rest, whether it
//                             holds the pose of the last snapshot, whether the parking brake holds it. A header line first (lock, rigidbody, physics step, autoSyncTransforms, the joints
//                             that are attached to the hull: a Floater is one) and a result line at the end with the largest excursion from the mean position and the verdict (limit 5 cm).
//                             The sample ends with the fields that the force probe needs, after "pawns=": vel, angVel (rad/s), vy, pivotVel (GetPointVelocity of the pivot, which differs from vel when the hull
//                             turns and its center of mass is not at the pivot), com and comLocal (world center of mass, and its offset from the pivot in the axes of the hull), pitch, roll, yaw (degrees; pitch
//                             is positive when the +z axis of the hull points up), restOffset (meters above the height at which the hull last stood still for 2 s, n/a until it did), depth (below the sea level).
//                             The colliders that touch the hull during the run are counted and named in "hullrest: contacts" lines (what pushes it).
//                             Who stands in the hull: "hullrest: pawns when=.. count=.." and one "hullrest: body when=.. player=.. kinematic=.. detectCollisions=.. interpolation=..
//                             triggers=.. solid=.." line per pawn (begin, end, and whenever the set or the physics of the bodies changes during the run). A remote body is kinematic outside
//                             the hull but not after its player left the helm inside it (RemotePlayer.SetPilotingChair), and a solid (non-trigger) collider on it would push the hull.
//   hullbrake [on|off|status] switches the parking brake of the hulls this client simulates (NitroxCyclops.ParkingBrakeEnabled), to compare a run with and without. Default status.
//   hullpush [speed] [yaw]    gives the hull this client simulates a push: speed in m/s along the hull's forward axis (default 0.3), yaw in rad/s (default 0). Logs where it is
//                             after 1, 2, 4 and 8 s ("hullpush: after=2s moved_cm=.. speed=.."). With the brake on a push of 0.3 m/s ends after about 10 cm.
//   hulleject                 the exit key at the helm (Player.TryEject): the player leaves the chair and stays aboard
//   hullforce on|off|dump|selftest|status
//                             the force probe (HullForceProbe, NitroxPatcher/Patches/Dynamic/HullForceProbe_Patches.cs): who calls AddForce, AddTorque, MovePosition, the velocity and pose setters on
//                             the Rigidbody of the nearest Cyclops, per caller and second ("hullforce: t=.. caller=.. calls=.. sumForce=(x,y,z) maxMag=.. mode=.. kind=.."), the physics steps whose
//                             horizontal speed changed without a call that explains it ("hullforce: unexplained ..") and a summary with the five callers of the largest impulse. "hullrest" arms it at its
//                             begin and disarms it at its end; "selftest" says which of the Harmony patches are live. Debug builds only.
//   NITROX_VEHICLE_TUNING=hold=0   (environment variable of the game) switches the hold of the pose of a copy off, the run "as before"
public partial class HarnessConsoleCommands
{
    private const float HULLREST_SAMPLE_SECONDS = 0.1f;
    private const float HULLREST_LIMIT_CM = 5f;

    private Coroutine hullRestRoutine;

    // The height at which each hull last stood still, learned from the samples of the runs (the first run of a session usually starts with a hull at rest)
    private static readonly Dictionary<string, HullForceRules.RestDepthTracker> hullRestDepths = new();

    private static NitroxCyclops HullRestFindTarget()
    {
        NitroxCyclops best = null;
        float bestDistance = float.MaxValue;
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        foreach (NitroxCyclops cyclops in FindObjectsOfType<NitroxCyclops>())
        {
            float distance = (cyclops.transform.position - here).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = cyclops;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static string HullRestId(Component component)
    {
        return component && component.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
    }

    private void LiveCommand_hullrest(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 60f), 2f, 900f);
        NitroxCyclops cyclops = HullRestFindTarget();
        if (!cyclops)
        {
            Log.Warn("[SyncHarness] hullrest: failed reason=no_cyclops_loaded");
            return;
        }

        if (hullRestRoutine != null)
        {
            StopCoroutine(hullRestRoutine);
            hullRestRoutine = null;
            HullForceProbe.Disarm("hullrest_restarted");
        }

        hullRestRoutine = StartCoroutine(HullRestLoop(cyclops, seconds));
    }

    private void HullRestHeader(NitroxCyclops cyclops, Rigidbody body)
    {
        string id = HullRestId(cyclops);
        string lockText = "none";
        if (cyclops.TryGetNitroxId(out NitroxId nitroxId) && this.Resolve<NitroxClient.GameLogic.SimulationOwnership>().TryGetLockType(nitroxId, out SimulationLockType lockType))
        {
            lockText = lockType.ToString();
        }

        MovementReplicator replicator = cyclops.GetComponent<MovementReplicator>();
        WorldForces forces = cyclops.GetComponent<WorldForces>();
        Stabilizer stabilizer = cyclops.GetComponent<Stabilizer>();
        SubRoot subRoot = cyclops.GetComponent<SubRoot>();
        Player player = Player.main;
        Log.Info($"[SyncHarness] hullrest: begin id={id} role={(replicator ? "replica" : "sim")} lock={lockText} pos={cyclops.transform.position.x:F2},{cyclops.transform.position.y:F2},{cyclops.transform.position.z:F2} playerAboard={player && player.currentSub == subRoot} playerMode={(player ? player.mode.ToString() : "none")} highPrecision={(player && player.RequiresHighPrecisionPhysics())}");
        Log.Info($"[SyncHarness] hullrest: physics kinematic={body.isKinematic} sleeping={body.IsSleeping()} interpolation={body.interpolation} drag={body.drag:F3} angularDrag={body.angularDrag:F3} mass={body.mass:F0} fixedDeltaTime={Time.fixedDeltaTime:F3} autoSyncTransforms={Physics.autoSyncTransforms} worldForces={(forces ? forces.enabled.ToString() : "none")} stabilizer={(stabilizer ? stabilizer.stabilizerEnabled.ToString() : "none")} brake={NitroxCyclops.ParkingBrakeEnabled} flood={(subRoot ? subRoot.floodFraction : -1f):F2}");

        List<string> joints = [];
        foreach (Joint joint in FindObjectsOfType<Joint>())
        {
            if (joint && joint.connectedBody == body)
            {
                joints.Add(joint.gameObject.name.Replace(' ', '_'));
            }
        }

        Log.Info($"[SyncHarness] hullrest: joints count={joints.Count} names={(joints.Count == 0 ? "none" : string.Join(",", joints.GetRange(0, Math.Min(joints.Count, 6))))}");
        HullRestLogBodies(cyclops, "begin");
    }

    /// <summary>
    /// One line per pawn in the hull: for a remote player the physics of his body (a dynamic child of the hull, or a solid collider, would push it), for the local player just the name.
    /// </summary>
    private static void HullRestLogBodies(NitroxCyclops cyclops, string when)
    {
        Log.Info($"[SyncHarness] hullrest: pawns when={when} count={cyclops.Pawns.Count}");
        foreach (NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract.INitroxPlayer pawnPlayer in cyclops.Pawns.Keys)
        {
            string playerName = pawnPlayer.PlayerName.Replace(' ', '_');
            if (pawnPlayer is not NitroxClient.GameLogic.RemotePlayer remote)
            {
                Log.Info($"[SyncHarness] hullrest: body when={when} player={playerName} local=True");
                continue;
            }

            Rigidbody bodyRigidbody = remote.RigidBody;
            List<string> solid = [];
            int triggers = 0;
            if (remote.Body)
            {
                foreach (Collider collider in remote.Body.GetComponentsInChildren<Collider>(false))
                {
                    if (!collider.enabled)
                    {
                        continue;
                    }

                    if (collider.isTrigger)
                    {
                        triggers++;
                    }
                    else
                    {
                        solid.Add($"{collider.GetType().Name}:{collider.name}".Replace(' ', '_'));
                    }
                }
            }

            string bodyText = bodyRigidbody ? $"kinematic={bodyRigidbody.isKinematic} detectCollisions={bodyRigidbody.detectCollisions} interpolation={bodyRigidbody.interpolation}" : "kinematic=- detectCollisions=- interpolation=-";
            Log.Info($"[SyncHarness] hullrest: body when={when} player={playerName} local=False {bodyText} atHelm={remote.PilotingChair != null} triggers={triggers} solidCount={solid.Count} solid={(solid.Count == 0 ? "none" : string.Join(",", solid.GetRange(0, Math.Min(solid.Count, 6))))}");
            HullRestLogIgnore(cyclops, remote.Body, playerName, when);
        }
    }

    /// <summary>
    /// A hypothesis about a hull bug: on the simulator's client the remote body of the player who is aboard is a kinematic body that is moved every frame; when its solid colliders are not
    /// ignored against the colliders of the hull, a kinematic mover pushes the dynamic hull. This line says for every solid collider of that body against every solid collider that belongs to the hull's
    /// rigidbody whether the pair is inert (Physics.IgnoreCollision was set for it, or the layer matrix ignores the two layers) or live (it can push): "live" greater than 0 is a candidate for the drift.
    /// </summary>
    private static void HullRestLogIgnore(NitroxCyclops cyclops, GameObject bodyObject, string playerName, string when)
    {
        try
        {
            Rigidbody hullBody = cyclops.GetComponent<Rigidbody>();
            if (!bodyObject || !hullBody)
            {
                Log.Info($"[SyncHarness] hullrest: ignore when={when} player={playerName} no_body_or_hull");
                return;
            }

            List<Collider> bodyColliders = [];
            foreach (Collider collider in bodyObject.GetComponentsInChildren<Collider>(false))
            {
                if (collider.enabled && !collider.isTrigger)
                {
                    bodyColliders.Add(collider);
                }
            }

            List<Collider> hullColliders = [];
            foreach (Collider collider in cyclops.GetComponentsInChildren<Collider>(false))
            {
                if (collider.enabled && !collider.isTrigger && collider.attachedRigidbody == hullBody)
                {
                    hullColliders.Add(collider);
                }
            }

            int pairs = 0;
            int ignoredByPair = 0;
            int ignoredByLayer = 0;
            List<string> live = [];
            foreach (Collider bodyCollider in bodyColliders)
            {
                foreach (Collider hullCollider in hullColliders)
                {
                    pairs++;
                    if (Physics.GetIgnoreCollision(bodyCollider, hullCollider))
                    {
                        ignoredByPair++;
                    }
                    else if (Physics.GetIgnoreLayerCollision(bodyCollider.gameObject.layer, hullCollider.gameObject.layer))
                    {
                        ignoredByLayer++;
                    }
                    else if (live.Count < 6)
                    {
                        live.Add($"{bodyCollider.name}({LayerMask.LayerToName(bodyCollider.gameObject.layer)})x{hullCollider.name}({LayerMask.LayerToName(hullCollider.gameObject.layer)})".Replace(' ', '_'));
                    }
                }
            }

            int liveCount = pairs - ignoredByPair - ignoredByLayer;
            Log.Info($"[SyncHarness] hullrest: ignore when={when} player={playerName} bodyColliders={bodyColliders.Count} hullColliders={hullColliders.Count} pairs={pairs} ignoredByPair={ignoredByPair} ignoredByLayer={ignoredByLayer} live={liveCount} sample={(live.Count == 0 ? "none" : string.Join(",", live))}");
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] hullrest: ignore when={when} player={playerName} failed {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// The fields at the end of a "hullrest: t=" line (see the header of this file): the full velocity and angular velocity of the Rigidbody, the velocity of its pivot (the point the
    /// transform stands at) next to it, the center of mass, the attitude of the hull and its height above where it rests. Everything is in meters, seconds, degrees and radians.
    /// </summary>
    private static string HullRestSampleExtra(Rigidbody body, Transform hull, HullForceRules.RestDepthTracker restDepth)
    {
        Vector3 velocity = body.velocity;
        Vector3 angular = body.angularVelocity;
        Vector3 pivotVelocity = body.GetPointVelocity(body.position);
        Vector3 center = body.worldCenterOfMass;
        Vector3 centerLocal = body.centerOfMass;
        Vector3 forward = hull.forward;
        float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Asin(Mathf.Clamp(hull.right.y, -1f, 1f)) * Mathf.Rad2Deg;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float y = hull.position.y;
        restDepth.Note(y, velocity.magnitude, angular.magnitude, Time.unscaledTime);
        float offset = restDepth.Offset(y);
        string offsetText = float.IsNaN(offset) ? "n/a" : HullForceRules.Fixed(offset, 3);
        float depth = Ocean.GetOceanLevel() - y;
        return $" vel={HullForceRules.FormatVector(velocity.x, velocity.y, velocity.z, 4)} angVel={HullForceRules.FormatVector(angular.x, angular.y, angular.z, 5)} vy={HullForceRules.Fixed(velocity.y, 4)} pivotVel={HullForceRules.FormatVector(pivotVelocity.x, pivotVelocity.y, pivotVelocity.z, 4)} com={HullForceRules.FormatVector(center.x, center.y, center.z, 3)} comLocal={HullForceRules.FormatVector(centerLocal.x, centerLocal.y, centerLocal.z, 3)} pitch={HullForceRules.Fixed(pitch, 3)} roll={HullForceRules.Fixed(roll, 3)} yaw={HullForceRules.Fixed(yaw, 3)} restOffset={offsetText} depth={HullForceRules.Fixed(depth, 3)}";
    }

    private IEnumerator HullRestLoop(NitroxCyclops cyclops, float seconds)
    {
        Rigidbody body = cyclops.GetComponent<Rigidbody>();
        if (!body)
        {
            Log.Warn("[SyncHarness] hullrest: failed reason=no_rigidbody");
            yield break;
        }

        HullContactRecorder contacts = cyclops.gameObject.AddComponent<HullContactRecorder>();
        HullRestHeader(cyclops, body);
        HullForceProbe.Arm(cyclops, body, "hullrest");
        string hullId = HullRestId(cyclops);
        if (!hullRestDepths.TryGetValue(hullId, out HullForceRules.RestDepthTracker restDepth))
        {
            restDepth = new HullForceRules.RestDepthTracker();
            hullRestDepths[hullId] = restDepth;
        }

        List<Vector3> positions = [];
        Vector3 first = cyclops.transform.position;
        Vector3 previous = first;
        Quaternion previousRotation = cyclops.transform.rotation;
        float pathLength = 0f;
        float maxExtrapolation = 0f;
        float extrapolationSum = 0f;
        float maxBodySpeed = 0f;
        float start = Time.unscaledTime;
        string role = "sim";
        string lastBodies = cyclops.DescribePawns();
        while (cyclops && Time.unscaledTime - start < seconds)
        {
            // The occupants or the physics of their bodies changed (somebody left the helm, entered or left the hull): say so once, with the same lines as at the start
            string bodies = cyclops.DescribePawns();
            if (bodies != lastBodies)
            {
                lastBodies = bodies;
                HullRestLogBodies(cyclops, $"t{Time.unscaledTime - start:F1}");
            }

            Transform hull = cyclops.transform;
            MovementReplicator replicator = cyclops.GetComponent<MovementReplicator>();
            role = replicator ? "replica" : "sim";
            Vector3 position = hull.position;
            positions.Add(position);
            pathLength += (position - previous).magnitude;
            float turned = Quaternion.Angle(previousRotation, hull.rotation) / HULLREST_SAMPLE_SECONDS;
            previous = position;
            previousRotation = hull.rotation;

            float extrapolation = replicator ? replicator.DebugExtrapolationSpeed : 0f;
            maxExtrapolation = Mathf.Max(maxExtrapolation, extrapolation);
            extrapolationSum += extrapolation;
            float bodySpeed = body.velocity.magnitude;
            maxBodySpeed = Mathf.Max(maxBodySpeed, bodySpeed);
            Log.Info($"[SyncHarness] hullrest: t={Time.unscaledTime - start:F1} role={role} pos={position.x:F3},{position.y:F3},{position.z:F3} dev_cm={(position - first).magnitude * 100f:F2} bodySpeed={bodySpeed:F4} yawRate={body.angularVelocity.y:F4} turnDegPerS={turned:F3} extrap={extrapolation:F4} atRest={(replicator ? replicator.DebugAtRest.ToString() : "-")} hold={(replicator ? replicator.DebugHolding.ToString() : "-")} brake={cyclops.DebugBraking} pawns={cyclops.Pawns.Count}{HullRestSampleExtra(body, hull, restDepth)}");
            yield return new WaitForSecondsRealtime(HULLREST_SAMPLE_SECONDS);
        }

        if (positions.Count == 0)
        {
            HullForceProbe.Disarm("cyclops_gone");
            Log.Warn("[SyncHarness] hullrest: failed reason=cyclops_gone");
            yield break;
        }

        Vector3 mean = Vector3.zero;
        foreach (Vector3 position in positions)
        {
            mean += position;
        }

        mean /= positions.Count;
        List<float> excursions = [];
        foreach (Vector3 position in positions)
        {
            excursions.Add((position - mean).magnitude * 100f);
        }

        excursions.Sort();
        float maxExcursion = excursions[excursions.Count - 1];
        float p95 = excursions[Math.Min(excursions.Count - 1, (int)(excursions.Count * 0.95f))];
        string verdict = maxExcursion <= HULLREST_LIMIT_CM ? "PASS" : "FAIL";
        Log.Info($"[SyncHarness] hullrest: result role={role} samples={positions.Count} seconds={Time.unscaledTime - start:F1} excursion_cm={maxExcursion:F2} p95_cm={p95:F2} path_cm={pathLength * 100f:F1} maxBodySpeed_cm_s={maxBodySpeed * 100f:F2} maxExtrap_cm_s={maxExtrapolation * 100f:F2} meanExtrap_cm_s={extrapolationSum / positions.Count * 100f:F2} limit_cm={HULLREST_LIMIT_CM:F0} verdict={verdict}");

        if (cyclops)
        {
            HullRestLogBodies(cyclops, "end");
        }

        // The summary of the force probe goes before the contacts lines: the scripts wait for the contacts and then read everything
        HullForceProbe.Disarm("hullrest_end");

        if (contacts)
        {
            contacts.Report();
            Destroy(contacts);
        }

        hullRestRoutine = null;
    }

    private void LiveCommand_hullbrake(NotificationCenter.Notification n)
    {
        string mode = LiveArgString(n, 0, "status");
        if (mode == "on")
        {
            NitroxCyclops.ParkingBrakeEnabled = true;
        }
        else if (mode == "off")
        {
            NitroxCyclops.ParkingBrakeEnabled = false;
        }
        else if (mode != "status")
        {
            Log.Warn($"[SyncHarness] hullbrake: command error, unknown mode '{mode}', use on, off or status");
            return;
        }

        Log.Info($"[SyncHarness] hullbrake: enabled={NitroxCyclops.ParkingBrakeEnabled}");
    }

    private void LiveCommand_hullpush(NotificationCenter.Notification n)
    {
        float speed = LiveArgFloat(n, 0, 0.3f);
        float yaw = LiveArgFloat(n, 1, 0f);
        NitroxCyclops cyclops = HullRestFindTarget();
        if (!cyclops)
        {
            Log.Warn("[SyncHarness] hullpush: failed reason=no_cyclops_loaded");
            return;
        }

        Rigidbody body = cyclops.GetComponent<Rigidbody>();
        if (!body || body.isKinematic || cyclops.GetComponent<MovementReplicator>())
        {
            Log.Warn("[SyncHarness] hullpush: failed reason=not_simulated_here");
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(cyclops.transform.forward, Vector3.up).normalized;
        body.velocity = forward * speed;
        body.angularVelocity = new Vector3(0f, yaw, 0f);
        Log.Info($"[SyncHarness] hullpush: applied speed={speed:F3} yaw={yaw:F4} brake={NitroxCyclops.ParkingBrakeEnabled} id={HullRestId(cyclops)}");
        StartCoroutine(HullPushLoop(cyclops, body));
    }

    private void LiveCommand_hulleject(NotificationCenter.Notification n)
    {
        Player player = Player.main;
        if (!player)
        {
            Log.Warn("[SyncHarness] hulleject: failed reason=no_player");
            return;
        }

        // What the exit key does at the helm (SubControl.Update -> Player.TryEject): leave the chair and stay aboard
        bool wasPiloting = player.isPiloting;
        player.TryEject();
        Log.Info($"[SyncHarness] hulleject: wasPiloting={wasPiloting} nowPiloting={player.isPiloting} mode={player.mode} inSub={player.IsInSub()}");
    }

    private void LiveCommand_hullforce(NotificationCenter.Notification n)
    {
        string mode = LiveArgString(n, 0, "status");
        switch (mode)
        {
            case "on":
            {
                NitroxCyclops cyclops = HullRestFindTarget();
                Rigidbody body = cyclops ? cyclops.GetComponent<Rigidbody>() : null;
                if (!cyclops || !body)
                {
                    Log.Warn("[SyncHarness] hullforce: failed reason=no_cyclops_loaded");
                    return;
                }

                HullForceProbe.Arm(cyclops, body, "manual");
                break;
            }
            case "off":
                HullForceProbe.Disarm("manual_off");
                break;
            case "dump":
                HullForceProbe.Dump();
                break;
            case "selftest":
                HullForceProbe.RunSelfTest();
                break;
            case "status":
                Log.Info($"[SyncHarness] hullforce: status {HullForceProbe.Status()}");
                break;
            default:
                Log.Warn($"[SyncHarness] hullforce: command error, unknown mode '{mode}', use on, off, dump, selftest or status");
                break;
        }
    }

    private IEnumerator HullPushLoop(NitroxCyclops cyclops, Rigidbody body)
    {
        Vector3 start = cyclops.transform.position;
        float startTime = Time.unscaledTime;
        foreach (float after in new[] { 1f, 2f, 4f, 8f })
        {
            while (cyclops && Time.unscaledTime - startTime < after)
            {
                yield return null;
            }

            if (!cyclops)
            {
                yield break;
            }

            Log.Info($"[SyncHarness] hullpush: after={after:F0}s moved_cm={(cyclops.transform.position - start).magnitude * 100f:F1} speed={body.velocity.magnitude:F4} yawRate={body.angularVelocity.y:F4}");
        }
    }

    /// <summary>
    /// Counts what touches the hull while a "hullrest" run is going: the colliders (and the rigidbody they belong to), the sum of the impulses. The force probe counts the contacts of
    /// its own (a relay that it puts on the hull when it is armed, so also for a drive armed by hand): this recorder only writes the "hullrest: contacts" lines.
    /// </summary>
    private sealed class HullContactRecorder : MonoBehaviour
    {
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private readonly Dictionary<string, float> impulses = new Dictionary<string, float>();

        public void OnCollisionEnter(Collision collision)
        {
            Record(collision);
        }

        public void OnCollisionStay(Collision collision)
        {
            Record(collision);
        }

        private void Record(Collision collision)
        {
            string rigidbodyText = collision.rigidbody ? $"{collision.rigidbody.name}({(collision.rigidbody.isKinematic ? "kinematic" : "dynamic")})" : "static";
            string key = $"{(collision.collider ? collision.collider.name : "?")}@{rigidbodyText}".Replace(' ', '_');
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
            impulses.TryGetValue(key, out float impulse);
            impulses[key] = impulse + collision.impulse.magnitude;
        }

        public void Report()
        {
            if (counts.Count == 0)
            {
                Log.Info("[SyncHarness] hullrest: contacts none");
                return;
            }

            List<KeyValuePair<string, int>> ordered = new List<KeyValuePair<string, int>>(counts);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < Math.Min(ordered.Count, 6); i++)
            {
                string key = ordered[i].Key;
                Log.Info($"[SyncHarness] hullrest: contacts collider={key} contacts={ordered[i].Value} impulse={impulses[key]:F1}");
            }
        }
    }
}
#endif
