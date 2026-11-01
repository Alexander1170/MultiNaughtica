#if DEBUG
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.GameLogic.PlayerAnimation;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "passengerheight" : a passenger walking in a Cyclops must be drawn at the height she really stands (bug 6, PLAYERS.md PL9 (6)).
// Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg]" or
// "/consoleall <command>" and log "[SyncHarness] <name>: key=value ..." lines (no spaces inside a value).
//
//   passengerheight [seconds]   for that long (default 10, once a second, at least one sample) measures every body in the nearest Cyclops with the code of the log line that a Release
//                               build writes (PassengerHeightProbe): the root of the body in the space of the hull, the floor of the hull straight below the expected feet (a ray in the
//                               hull's own axes), the root above that floor (a player who walks: standheight + cameraOffset, 1.55 m with the vanilla numbers), the expected feet above it
//                               (0 when she walks on it) and the lowest foot bone above it (what is drawn). role=remote is the copy of a player on this client, role=local the player of this
//                               client. "sample" lines one per body and second, a header ("begin") and one "result" line per body with the medians. The check compares the SAME player
//                               on the two clients: role=remote on the pilot's client against role=local on hers. They have to agree within 10 cm (PassengerHeightRules.SAME_HEIGHT_TOLERANCE).
//   passengerswim               for every remote player of this client: the model in the walking pose and in the swimming pose with the root where it is (the animator is evaluated by hand,
//                               the pose is put back): how far the lowest foot bone, the hips and the neck are below the root in each, and the difference. This is the measurement of the
//                               hypothesis of bug 6: a copy in the swimming pose around the root of a walker has its feet that much too high. Vanilla stance step: 1.0 m. "theory=" says what
//                               the difference means (PassengerHeightRules.JudgePoseTheory): supported (the swimming pose has its feet about 1.0 m higher), refuted (any other difference:
//                               the swimming pose cannot make the meter of the report, the cause is not the pose) or unknown (nothing measured). Run it FIRST: it is the only reading that can
//                               say the report has another cause; every reading of a copy that walks is forced to the walking pose by the fix itself.
//   passengerstale              for every remote player of this client who has a pawn: writes the state "his client says underwater" into his copy (what a lost or a late event leaves
//                               behind) and reads the pose of the copy at once, in the same call. With the pawn rule in force the copy stays in the walking pose (poseAfter=stand with
//                               reportedAfter=True): this is the one check of the scenario that fails when the rule does not reach the animator. The sender announces its real state again
//                               within 5 s, which puts the state right and writes the late-correction line of RemotePlayer.UpdateAnimationAndCollider (expected after this command).
public partial class HarnessConsoleCommands
{
    private Coroutine passengerHeightRoutine;

    private sealed class PassengerHeightStats
    {
        public string Role = "";
        public string Pose = "none";
        public int Samples;
        public int FloatingSamples;
        public readonly List<float> RootHullY = [];
        public readonly List<float> RootAboveFloor = [];
        public readonly List<float> FeetGap = [];
        public readonly List<float> FeetBoneAboveFloor = [];
    }

    private sealed class PassengerPoseReading
    {
        public float Feet = float.NaN;
        public float Hips = float.NaN;
        public float Neck = float.NaN;
    }

    private static NitroxCyclops PassengerHeightFindCyclops()
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

    private static float PassengerMedian(List<float> values)
    {
        List<float> clean = [];
        foreach (float value in values)
        {
            if (!float.IsNaN(value))
            {
                clean.Add(value);
            }
        }

        if (clean.Count == 0)
        {
            return float.NaN;
        }

        clean.Sort();
        return clean[clean.Count / 2];
    }

    private void LiveCommand_passengerheight(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 10f), 1f, 300f);
        NitroxCyclops cyclops = PassengerHeightFindCyclops();
        if (!cyclops)
        {
            Log.Warn("[SyncHarness] passengerheight: failed reason=no_cyclops_loaded");
            return;
        }

        if (passengerHeightRoutine != null)
        {
            StopCoroutine(passengerHeightRoutine);
            passengerHeightRoutine = null;
        }

        passengerHeightRoutine = StartCoroutine(PassengerHeightLoop(cyclops, seconds));
    }

    private IEnumerator PassengerHeightLoop(NitroxCyclops cyclops, float seconds)
    {
        string hullId = cyclops.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "noid";
        Rigidbody hullBody = cyclops.GetComponent<Rigidbody>();
        PassengerHeightProbe.ControllerHeights(out float standHeight, out float swimHeight);
        Player player = Player.main;
        bool localAboard = player && player.currentSub && player.currentSub.gameObject == cyclops.gameObject;
        Log.Info($"[SyncHarness] passengerheight: begin hull={hullId} seconds={seconds:F0} standheight={standHeight:F2} swimheight={swimHeight:F2} standingRootAboveFeet={PassengerHeightProbe.StandingRootAboveFeet():F2} stanceStep={PassengerHeightRules.StanceStep(standHeight, swimHeight):F2} localAboard={localAboard} piloting={(player && player.isPiloting)} pawns={cyclops.Pawns.Count} physicsStep={Time.fixedDeltaTime:F3} hullKinematic={(hullBody ? hullBody.isKinematic.ToString() : "none")}");

        Dictionary<INitroxPlayer, Transform[]> feetBones = [];
        Dictionary<string, PassengerHeightStats> stats = [];
        float end = Time.time + seconds;
        int index = 0;
        while (cyclops && (index == 0 || Time.time < end))
        {
            List<KeyValuePair<INitroxPlayer, CyclopsPawn>> pawns = new(cyclops.Pawns);
            float hullSpeed = hullBody ? hullBody.velocity.magnitude : -1f;
            foreach (KeyValuePair<INitroxPlayer, CyclopsPawn> entry in pawns)
            {
                INitroxPlayer who = entry.Key;
                CyclopsPawn pawn = entry.Value;
                if (pawn == null || !pawn.RealObject)
                {
                    continue;
                }

                if (!feetBones.TryGetValue(who, out Transform[] feet))
                {
                    feet = PassengerHeightProbe.FindFeetBones(who.PlayerModel);
                    feetBones[who] = feet;
                }

                PassengerHeightSample sample = PassengerHeightProbe.Measure(cyclops.transform, pawn.RealObject, feet);
                RemotePlayer remote = who as RemotePlayer;
                string role = remote != null ? "remote" : "local";
                string name = who.PlayerName.Replace(' ', '_');
                string pose;
                string extra;
                if (remote != null)
                {
                    pose = remote.AnimationController ? (remote.AnimationController["is_underwater"] ? "swim" : "stand") : "none";
                    extra = $"reportedUnderwater={remote.PoseTracker.ReportedUnderwater} walkingInHull={remote.PoseTracker.WalkingInHull} kinematic={(remote.RigidBody ? remote.RigidBody.isKinematic.ToString() : "none")} atHelm={(remote.PilotingChair ? "True" : "False")}";
                }
                else
                {
                    pose = player && player.IsUnderwaterForSwimming() ? "swim" : "stand";
                    extra = $"piloting={(player && player.isPiloting)} handleHullY={(pawn.Handle ? pawn.Handle.transform.localPosition.y.ToString("F3") : "none")}";
                }

                Log.Info($"[SyncHarness] passengerheight: sample n={index} role={role} player={name} hull={hullId} rootHullY={sample.RootHullY:F3} floorHullY={sample.FloorHullY:F3} rootAboveFloor={sample.RootAboveFloor:F3} feetGap={sample.FeetGap:F3} floor={sample.FloorName.Replace(' ', '_')} feetBones={sample.FeetBones} feetBoneAboveFloor={sample.FeetBoneAboveFloor:F3} pose={pose} {extra} hullSpeed={hullSpeed:F2}");

                if (!stats.TryGetValue(name, out PassengerHeightStats stat))
                {
                    stat = new PassengerHeightStats();
                    stats[name] = stat;
                }

                stat.Role = role;
                stat.Pose = pose;
                stat.Samples++;
                stat.RootHullY.Add(sample.RootHullY);
                stat.RootAboveFloor.Add(sample.RootAboveFloor);
                stat.FeetGap.Add(sample.FeetGap);
                stat.FeetBoneAboveFloor.Add(sample.FeetBoneAboveFloor);
                if (PassengerHeightRules.IsFloating(sample.FeetGap))
                {
                    stat.FloatingSamples++;
                }
            }

            index++;
            yield return new WaitForSeconds(1f);
        }

        if (stats.Count == 0)
        {
            Log.Warn($"[SyncHarness] passengerheight: failed reason=nobody_walks_in_the_cyclops hull={hullId}");
        }

        foreach (KeyValuePair<string, PassengerHeightStats> entry in stats)
        {
            PassengerHeightStats stat = entry.Value;
            Log.Info($"[SyncHarness] passengerheight: result role={stat.Role} player={entry.Key} hull={hullId} samples={stat.Samples} rootHullY={PassengerMedian(stat.RootHullY):F3} rootAboveFloor={PassengerMedian(stat.RootAboveFloor):F3} feetGap={PassengerMedian(stat.FeetGap):F3} feetBoneAboveFloor={PassengerMedian(stat.FeetBoneAboveFloor):F3} pose={stat.Pose} floatingSamples={stat.FloatingSamples}");
        }

        passengerHeightRoutine = null;
    }

    private void LiveCommand_passengerswim(NotificationCenter.Notification n)
    {
        int measured = 0;
        foreach (RemotePlayer remote in this.Resolve<PlayerManager>().GetAll())
        {
            if (PassengerSwimOne(remote))
            {
                measured++;
            }
        }

        Log.Info($"[SyncHarness] passengerswim: done players={measured}");
    }

    /// <summary>
    /// The lowest foot bone, the hips and the neck below the root of the body (along its own up axis, meters) with the animator in the given pose, evaluated by hand for two seconds so that
    /// every transition has finished. NaN for a bone the model does not have.
    /// </summary>
    private static PassengerPoseReading PassengerReadPose(RemotePlayer remote, Animator animator, Transform[] feet, Transform hips, Transform neck, bool swimming)
    {
        remote.AnimationController["is_underwater"] = swimming;
        animator.Update(2f);

        Transform root = remote.Body.transform;
        PassengerPoseReading reading = new();
        float lowest = float.NegativeInfinity;
        foreach (Transform foot in feet)
        {
            if (foot)
            {
                lowest = Mathf.Max(lowest, Vector3.Dot(root.position - foot.position, root.up));
            }
        }

        if (!float.IsNegativeInfinity(lowest))
        {
            reading.Feet = lowest;
        }

        if (hips)
        {
            reading.Hips = Vector3.Dot(root.position - hips.position, root.up);
        }

        if (neck)
        {
            reading.Neck = Vector3.Dot(root.position - neck.position, root.up);
        }

        return reading;
    }

    private static bool PassengerSwimOne(RemotePlayer remote)
    {
        string name = remote.PlayerName.Replace(' ', '_');
        if (!remote.Body || !remote.AnimationController || !remote.PlayerModel)
        {
            Log.Info($"[SyncHarness] passengerswim: player={name} skipped reason=no_body");
            return false;
        }

        Animator animator = remote.PlayerModel.GetComponent<Animator>();
        if (!animator)
        {
            Log.Info($"[SyncHarness] passengerswim: player={name} skipped reason=no_animator");
            return false;
        }

        Transform[] feet = PassengerHeightProbe.FindFeetBones(remote.PlayerModel);
        Transform hips = null;
        Transform neck = null;
        foreach (Transform child in remote.PlayerModel.GetComponentsInChildren<Transform>(true))
        {
            string lower = child.name.ToLowerInvariant();
            if (!hips && lower == "hips")
            {
                hips = child;
            }
            else if (!neck && lower == "neck")
            {
                neck = child;
            }
        }

        bool original = remote.AnimationController["is_underwater"];
        AnimatorCullingMode culling = animator.cullingMode;
        bool rootMotion = animator.applyRootMotion;
        Vector3 rootBefore = remote.Body.transform.position;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        PassengerPoseReading standing = PassengerReadPose(remote, animator, feet, hips, neck, false);
        PassengerPoseReading swimming = PassengerReadPose(remote, animator, feet, hips, neck, true);
        PassengerPoseReading restored = PassengerReadPose(remote, animator, feet, hips, neck, original);

        animator.cullingMode = culling;
        animator.applyRootMotion = rootMotion;
        float rootMoved = (remote.Body.transform.position - rootBefore).magnitude;

        PassengerHeightProbe.ControllerHeights(out float standHeight, out float swimHeight);
        float delta = standing.Feet - swimming.Feet;
        PassengerHeightRules.OffsetKind kind = PassengerHeightRules.Classify(float.IsNaN(delta) ? 0f : delta, standHeight, swimHeight);
        PassengerHeightRules.PoseTheory theory = PassengerHeightRules.JudgePoseTheory(delta, standHeight, swimHeight);
        Log.Info($"[SyncHarness] passengerswim: player={name} pawn={remote.Pawn != null} walkingInHull={remote.PoseTracker.WalkingInHull} original={(original ? "swim" : "stand")} feetBones={feet.Length} standFeetBelowRoot={standing.Feet:F3} swimFeetBelowRoot={swimming.Feet:F3} feetDelta={delta:F3} standHipsBelowRoot={standing.Hips:F3} swimHipsBelowRoot={swimming.Hips:F3} standNeckBelowRoot={standing.Neck:F3} swimNeckBelowRoot={swimming.Neck:F3} restoredFeetBelowRoot={restored.Feet:F3} kind={kind} theory={theory} stanceStep={PassengerHeightRules.StanceStep(standHeight, swimHeight):F2} rootMoved={rootMoved:F3}");
        return true;
    }

    private void LiveCommand_passengerstale(NotificationCenter.Notification n)
    {
        int injected = 0;
        foreach (RemotePlayer remote in this.Resolve<PlayerManager>().GetAll())
        {
            string name = remote.PlayerName.Replace(' ', '_');
            if (remote.Pawn == null || !remote.AnimationController)
            {
                Log.Info($"[SyncHarness] passengerstale: player={name} skipped reason={(remote.Pawn == null ? "no_pawn" : "no_body")}");
                continue;
            }

            bool reportedBefore = remote.PoseTracker.ReportedUnderwater;
            string poseBefore = remote.AnimationController["is_underwater"] ? "swim" : "stand";
            // The report a lost or a late event leaves behind: the sender's state said underwater. The same call the animation event of the sender makes
            remote.UpdateAnimationAndCollider(AnimChangeType.UNDERWATER, AnimChangeState.ON);
            string poseAfter = remote.AnimationController["is_underwater"] ? "swim" : "stand";
            Log.Info($"[SyncHarness] passengerstale: player={name} pawn=True walkingInHull={remote.PoseTracker.WalkingInHull} reportedBefore={reportedBefore} reportedAfter={remote.PoseTracker.ReportedUnderwater} poseBefore={poseBefore} poseAfter={poseAfter} atHelm={(remote.PilotingChair ? "True" : "False")}");
            injected++;
        }

        Log.Info($"[SyncHarness] passengerstale: done players={injected}");
    }
}
#endif
