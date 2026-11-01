using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// One measurement of how high a body (a remote copy or the local player) stands in a Cyclops, everything in the space of the hull. Made by <see cref="PassengerHeightProbe" />.
/// </summary>
public sealed class PassengerHeightSample
{
    /// <summary>The root of the body (the head) in the space of the hull.</summary>
    public float RootHullY;

    /// <summary><c>standheight + cameraOffset</c> of the prefab: the distance from the root to the floor of a walking player.</summary>
    public float StandingRootAboveFeet;

    /// <summary>A collider of the hull was found below the expected feet.</summary>
    public bool FloorFound;

    /// <summary>The name of that collider.</summary>
    public string FloorName = "none";

    /// <summary>The names from the hull down to that collider (the last four at most): a console and the deck are told apart by it, the bare name of a collider often is not enough.</summary>
    public string FloorPath = "none";

    /// <summary>Where the ray hit it, in the space of the hull.</summary>
    public float FloorHullY = float.NaN;

    /// <summary>The root above that floor (a walking player: <see cref="StandingRootAboveFeet" />). <see cref="float.NaN" /> without a floor.</summary>
    public float RootAboveFloor = float.NaN;

    /// <summary>The expected feet (the root minus <see cref="StandingRootAboveFeet" />) above the floor: zero for a player who walks on it.</summary>
    public float FeetGap = float.NaN;

    /// <summary>How many foot bones of the model were found (0: only the root is measured).</summary>
    public int FeetBones;

    /// <summary>The lowest foot bone in the space of the hull.</summary>
    public float FeetBoneHullY = float.NaN;

    /// <summary>The lowest foot bone above the floor (what is drawn, against <see cref="FeetGap" />, what is expected).</summary>
    public float FeetBoneAboveFloor = float.NaN;
}

/// <summary>
/// Measures how high a body stands above the floor of a Cyclops (bug 6, PLAYERS.md PL9 (6)): the same code for the log line of a real session (<see cref="PassengerHeightWatch" />) and for the
/// Debug command <c>passengerheight</c> of the live check, so that the two say the same thing.
/// </summary>
public static class PassengerHeightProbe
{
    /// <summary>
    /// The ray starts this far (meters) above the expected feet, so that a floor a few centimeters above them is found as well.
    /// </summary>
    private const float RAY_START_ABOVE_FEET = 0.4f;

    /// <summary>
    /// And reaches this far below them.
    /// </summary>
    private const float RAY_RANGE_BELOW_FEET = 3.5f;

    private static readonly RaycastHit[] hits = new RaycastHit[32];

    /// <summary>
    /// The distance from the root of a walking player to his feet by the prefab (<c>standheight + cameraOffset</c>), the default of the vanilla constructor before the player exists.
    /// </summary>
    public static float StandingRootAboveFeet()
    {
        Player player = Player.main;
        PlayerController controller = player ? player.playerController : null;
        return controller ? controller.standheight + controller.cameraOffset : PassengerHeightRules.STANDING_ROOT_ABOVE_FEET_DEFAULT;
    }

    /// <summary>
    /// The controller heights of the prefab (vanilla constructor: stand 1.5, swim 0.5); their difference is the step the game lifts the root of a swimmer who gets up from the water.
    /// </summary>
    public static void ControllerHeights(out float standHeight, out float swimHeight)
    {
        Player player = Player.main;
        PlayerController controller = player ? player.playerController : null;
        standHeight = controller ? controller.standheight : PassengerHeightRules.STAND_HEIGHT_DEFAULT;
        swimHeight = controller ? controller.swimheight : PassengerHeightRules.SWIM_HEIGHT_DEFAULT;
    }

    /// <summary>
    /// The foot bones of a player model (the bones of its skinned meshes whose name says foot, toe or ankle), to be found once and kept.
    /// </summary>
    public static Transform[] FindFeetBones(GameObject model)
    {
        if (!model)
        {
            return Array.Empty<Transform>();
        }

        HashSet<Transform> seen = [];
        List<Transform> feet = [];
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (Transform bone in renderer.bones)
            {
                if (bone && IsFootName(bone.name) && seen.Add(bone))
                {
                    feet.Add(bone);
                }
            }
        }

        return feet.ToArray();
    }

    private static bool IsFootName(string boneName)
    {
        string lower = boneName.ToLowerInvariant();
        return lower.Contains("foot") || lower.Contains("toe") || lower.Contains("ankle");
    }

    /// <summary>
    /// The names from <paramref name="hull" /> (not included) down to <paramref name="leaf" />, the last four at most, joined by "/".
    /// </summary>
    private static string PathUnder(Transform hull, Transform leaf)
    {
        List<string> names = [];
        for (Transform current = leaf; current && current != hull && names.Count < 4; current = current.parent)
        {
            names.Add(current.name);
        }

        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>
    /// Where the root of <paramref name="body" /> is in the space of <paramref name="hull" />, how far above the floor of the hull it stands (a ray straight down in the hull's own axes from a
    /// little above where the feet of a walking player would be) and how high the lowest foot bone is. Colliders of the body itself, of other players and triggers are not floors.
    /// </summary>
    public static PassengerHeightSample Measure(Transform hull, GameObject body, Transform[] feetBones)
    {
        PassengerHeightSample sample = new();
        Transform root = body.transform;
        sample.RootHullY = hull.InverseTransformPoint(root.position).y;
        sample.StandingRootAboveFeet = StandingRootAboveFeet();

        Vector3 down = -hull.up;
        Vector3 expectedFeet = root.position + down * sample.StandingRootAboveFeet;
        Vector3 origin = expectedFeet - down * RAY_START_ABOVE_FEET;
        int count = Physics.RaycastNonAlloc(origin, down, hits, RAY_START_ABOVE_FEET + RAY_RANGE_BELOW_FEET, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        Collider floor = null;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (!collider || hits[i].distance >= nearest || !collider.transform.IsChildOf(hull) || collider.transform.IsChildOf(root))
            {
                continue;
            }

            if (collider.GetComponentInParent<RemotePlayerIdentifier>() || collider.GetComponentInParent<Player>())
            {
                continue;
            }

            nearest = hits[i].distance;
            floor = collider;
        }

        if (floor)
        {
            sample.FloorFound = true;
            sample.FloorName = floor.name;
            sample.FloorPath = PathUnder(hull, floor.transform);
            sample.FloorHullY = hull.InverseTransformPoint(origin + down * nearest).y;
            sample.RootAboveFloor = PassengerHeightRules.RootAboveFloor(sample.RootHullY, sample.FloorHullY);
            sample.FeetGap = PassengerHeightRules.FloorGap(RAY_START_ABOVE_FEET, nearest);
        }

        float lowest = float.PositiveInfinity;
        int found = 0;
        foreach (Transform bone in feetBones)
        {
            if (!bone)
            {
                continue;
            }

            found++;
            lowest = Mathf.Min(lowest, hull.InverseTransformPoint(bone.position).y);
        }

        sample.FeetBones = found;
        if (found > 0)
        {
            sample.FeetBoneHullY = lowest;
            sample.FeetBoneAboveFloor = sample.FloorFound ? lowest - sample.FloorHullY : float.NaN;
        }

        return sample;
    }
}

/// <summary>
/// Added to every Cyclops: once a second it measures how high the bodies of the players in it stand above its floor, and writes a line to the log (also in a Release build, which is what the
/// players run) when a body has stood well above the floor for a while (<see cref="PassengerHeightRules.FloatTracker" />), and one line when a body is measured for the first time. Bug 6
/// (PLAYERS.md PL9 (6)): a passenger was drawn about a meter above the floor on the screen of the pilot, the logs of the session said nothing; the line says whether the root, the pose or
/// the floor is at fault. Three more lines say how a boarding went: one when the pawn is created (<see cref="NotePawnCreated" />: where the root is, what is under it, where the pawn
/// handle and the helm seat are), one when the pawn is <see cref="PassengerHeightRules.BOARDING_SETTLE_SECONDS" /> old (where it came to rest, and on what: a console and the deck are told
/// apart by the path of the collider and its height), and a warning for a copy that walks and still reports the swimming state after the refresh of its sender
/// (<see cref="RemotePoseTracker.IsStuckUnderwater" />). It changes nothing.
/// </summary>
public class PassengerHeightWatch : MonoBehaviour
{
    private const float SAMPLE_INTERVAL = 1f;

    private sealed class Tracked
    {
        public readonly PassengerHeightRules.FloatTracker Floating = new();
        public Transform[] FeetBones;
        public bool LoggedFirst;

        /// <summary>The state "walks and still reports underwater" has been written once for this stretch; it is cleared when the state ends.</summary>
        public bool LoggedStuck;

        /// <summary>The pawn that gets no pawn packets has been written once (<see cref="PassengerHeightRules.IsPawnSilent" />).</summary>
        public bool LoggedSilent;

        /// <summary>When the pawn of this player was created (<see cref="float.NaN" /> for a pawn the watch did not see being made).</summary>
        public float CreatedAt = float.NaN;

        public bool SettledLogged;
    }

    private readonly Dictionary<INitroxPlayer, Tracked> tracked = [];
    private readonly List<INitroxPlayer> gone = [];
    private NitroxCyclops cyclops;
    private PilotingChair helmChair;
    private float nextSample;
    private float lastSample = -1f;

    public void Start()
    {
        cyclops = GetComponent<NitroxCyclops>();
    }

    public void Update()
    {
        float now = Time.unscaledTime;
        if (!cyclops || now < nextSample)
        {
            return;
        }

        float deltaTime = lastSample < 0f ? SAMPLE_INTERVAL : now - lastSample;
        lastSample = now;
        nextSample = now + SAMPLE_INTERVAL;

        if (cyclops.Pawns.Count == 0)
        {
            tracked.Clear();
            return;
        }

        try
        {
            foreach (KeyValuePair<INitroxPlayer, CyclopsPawn> entry in cyclops.Pawns)
            {
                SampleOne(entry.Key, entry.Value, deltaTime, now);
            }
        }
        catch (Exception exception)
        {
            // A measurement that fails once is not worth a line every second: the watch only reports, it switches itself off
            Log.Error(exception, "[PassengerHeight] The measurement failed, the watch of this Cyclops is switched off");
            enabled = false;
            return;
        }

        gone.Clear();
        foreach (INitroxPlayer player in tracked.Keys)
        {
            if (!cyclops.Pawns.ContainsKey(player))
            {
                gone.Add(player);
            }
        }

        foreach (INitroxPlayer player in gone)
        {
            tracked.Remove(player);
        }
    }

    /// <summary>
    /// The pawn of <paramref name="player" /> has just been made (called by <see cref="NitroxCyclops.AddPawnForPlayer" />): writes the line of the moment of the boarding. The pawn starts
    /// at the root the body has at that moment (<c>CyclopsPawn.Initialize</c>: the local position of the real object, the root of a swimmer for a player who climbs in out of the water),
    /// and the walking motor lets it fall to what is below; the line says where that is, and the one 2 s later (<see cref="SampleOne" />) where it came to rest. Never throws.
    /// </summary>
    public void NotePawnCreated(INitroxPlayer player, CyclopsPawn pawn)
    {
        try
        {
            if (pawn == null || !pawn.RealObject)
            {
                return;
            }

            Tracked state = TrackedFor(player);
            // A new pawn of a player the watch knows (he left the helm, the pawn was made again): the stretch starts over
            state.CreatedAt = Time.unscaledTime;
            state.SettledLogged = false;
            state.LoggedSilent = false;
            state.LoggedStuck = false;
            LogBoarding("pawn created", player, pawn, state);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PassengerHeight] The line about a new pawn failed");
        }
    }

    private Tracked TrackedFor(INitroxPlayer player)
    {
        if (!tracked.TryGetValue(player, out Tracked state))
        {
            state = new Tracked();
            tracked.Add(player, state);
        }

        return state;
    }

    /// <summary>
    /// The height of the seat of the helm in the space of the hull, <see cref="float.NaN" /> when the hull has none. The pilot's root is 1.0 m above it and a walking player's root
    /// 1.55 m above the deck, so a body that stands on the console shows up as a floor about a meter above the deck that this number is a reference for.
    /// </summary>
    private float HelmSeatY()
    {
        if (!helmChair)
        {
            helmChair = GetComponentInChildren<PilotingChair>(true);
        }

        return helmChair && helmChair.sittingPosition ? transform.InverseTransformPoint(helmChair.sittingPosition.position).y : float.NaN;
    }

    private void LogBoarding(string moment, INitroxPlayer player, CyclopsPawn pawn, Tracked state)
    {
        if (state.FeetBones == null)
        {
            state.FeetBones = PassengerHeightProbe.FindFeetBones(player.PlayerModel);
        }

        PassengerHeightSample sample = PassengerHeightProbe.Measure(transform, pawn.RealObject, state.FeetBones);
        float handleY = pawn.Handle ? pawn.Handle.transform.localPosition.y : float.NaN;
        Log.Info($"[PassengerHeight] boarding, {moment}: {Describe(player, player as RemotePlayer, pawn, sample)}; pawn handle y {handleY:F2} in the hull, helm seat y {HelmSeatY():F2}");
    }

    private void SampleOne(INitroxPlayer player, CyclopsPawn pawn, float deltaTime, float now)
    {
        GameObject body = pawn.RealObject;
        if (!body || !body.activeInHierarchy)
        {
            return;
        }

        RemotePlayer remote = player as RemotePlayer;
        Tracked state = TrackedFor(player);

        // Where the pawn came to rest, whether or not the player walks yet (a copy has to have had its first pawn packet to be a walker, and the line is also for a pawn that never gets one)
        if (!state.SettledLogged && PassengerHeightRules.IsBoardingSettled(now - state.CreatedAt))
        {
            state.SettledLogged = true;
            LogBoarding("pawn settled", player, pawn, state);
        }

        // A copy that walks and still reports the swimming state, after the sender announced its state again: not the race of the boarding (RemotePoseTracker.IsStuckUnderwater)
        bool stuck = remote != null && !remote.PilotingChair && remote.PoseTracker.IsStuckUnderwater(now);
        if (stuck && !state.LoggedStuck)
        {
            state.LoggedStuck = true;
            string id = this.TryGetNitroxId(out NitroxId stuckId) ? stuckId.ToString() : "noid";
            Log.Warn($"[PassengerHeight] {player.PlayerName} has walked in Cyclops {id} for {remote.PoseTracker.WalkingFor(now):F0} s and his copy still has the swimming state, although his client announces its state every {PoseRefreshRules.REFRESH_INTERVAL:F0} s: he really reports underwater while he walks in the hull (the swimming in the hull of PL8?) or his announcements do not arrive. The copy is drawn walking (the pawn rule). reported underwater {remote.PoseTracker.ReportedUnderwater}, an event was seen {remote.PoseTracker.HasReport}");
        }
        else if (!stuck)
        {
            state.LoggedStuck = false;
        }

        // A copy with a pawn that has received no pawn packet at all: his client sends movement of another kind while his body is in the hull (no pawn on his side), the pawn rule never
        // starts, and the copy is drawn in the pose of his last report
        if (remote != null && !state.LoggedSilent && !remote.PilotingChair && PassengerHeightRules.IsPawnSilent(now - state.CreatedAt, remote.PoseTracker.WalkingInHull))
        {
            state.LoggedSilent = true;
            string silentId = this.TryGetNitroxId(out NitroxId hullId) ? hullId.ToString() : "noid";
            Log.Warn($"[PassengerHeight] {player.PlayerName} has had a pawn in Cyclops {silentId} for {now - state.CreatedAt:F0} s and has sent no pawn packet (PlayerInCyclopsMovement): his client sends movement of another kind while his body is in the hull, so his copy is no walker for the pawn rule and is drawn in the pose of his last report (reported underwater {remote.PoseTracker.ReportedUnderwater}, animator is_underwater {(remote.AnimationController ? remote.AnimationController["is_underwater"].ToString() : "none")})");
        }

        // Only a body that walks is measured: a remote copy that has had pawn packets and does not sit at the helm, the local player when he is in this Cyclops and does not pilot it
        bool walking;
        if (remote != null)
        {
            walking = remote.PoseTracker.WalkingInHull && !remote.PilotingChair;
        }
        else
        {
            Player local = Player.main;
            walking = local && local.currentSub && local.currentSub.gameObject == gameObject && !local.isPiloting;
        }

        if (!walking)
        {
            state.Floating.Step(float.NaN, deltaTime);
            return;
        }

        if (state.FeetBones == null)
        {
            state.FeetBones = PassengerHeightProbe.FindFeetBones(player.PlayerModel);
        }

        PassengerHeightSample sample = PassengerHeightProbe.Measure(transform, body, state.FeetBones);
        if (!state.LoggedFirst && sample.FloorFound)
        {
            state.LoggedFirst = true;
            Log.Info($"[PassengerHeight] {Describe(player, remote, pawn, sample)}");
        }

        // Judged by the larger of the expected feet (what the position says) and the lowest foot bone (what is drawn): a right root in a wrong pose floats for the player who looks at it as
        // well as a wrong root in a right pose; the line has both numbers, and the animator, so that they can be told apart. Only for a copy: the model of the local player is the one of his
        // own first-person view, whose bones nobody has measured against his feet
        float judgedGap = remote != null ? PassengerHeightRules.JudgedGap(sample.FeetGap, sample.FeetBoneAboveFloor) : sample.FeetGap;
        if (state.Floating.Step(judgedGap, deltaTime))
        {
            PassengerHeightProbe.ControllerHeights(out float standHeight, out float swimHeight);
            PassengerHeightRules.OffsetKind kind = PassengerHeightRules.Classify(judgedGap, standHeight, swimHeight);
            Log.Warn($"[PassengerHeight] {Describe(player, remote, pawn, sample)}. The body has been {judgedGap:F2} m above the floor for {state.Floating.FloatingFor:F0} s (expected feet {sample.FeetGap:F2} m, lowest foot bone {sample.FeetBoneAboveFloor:F2} m; {kind}; the swim stance differs from the walking stance by {PassengerHeightRules.StanceStep(standHeight, swimHeight):F2} m)");
        }
    }

    private string Describe(INitroxPlayer player, RemotePlayer remote, CyclopsPawn pawn, PassengerHeightSample sample)
    {
        string id = this.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "noid";
        string who = remote != null ? "remote copy" : "own body";
        string state;
        if (remote != null)
        {
            Rigidbody rigidbody = remote.RigidBody;
            state = $"animator is_underwater {(remote.AnimationController ? remote.AnimationController["is_underwater"].ToString() : "none")}, reported underwater {remote.PoseTracker.ReportedUnderwater}, walking {remote.PoseTracker.WalkingInHull}, kinematic {(rigidbody ? rigidbody.isKinematic.ToString() : "none")}";
        }
        else
        {
            Player local = Player.main;
            state = $"swimming flag {(local ? local.IsUnderwaterForSwimming().ToString() : "none")}, pawn handle y {(pawn != null && pawn.Handle ? pawn.Handle.transform.localPosition.y.ToString("F2") : "none")}";
        }

        return $"{player.PlayerName} ({who}) in Cyclops {id}: root y {sample.RootHullY:F2} in the hull, {sample.RootAboveFloor:F2} m above the floor ({sample.FloorPath} at y {sample.FloorHullY:F2}; a walking player needs {sample.StandingRootAboveFeet:F2}), expected feet {sample.FeetGap:F2} m above it, {sample.FeetBones} foot bones, the lowest {sample.FeetBoneAboveFloor:F2} m above it, {state}";
    }
}
