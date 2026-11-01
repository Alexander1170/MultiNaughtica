using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Settings;
using NitroxClient.MonoBehaviours.Cyclops;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public abstract class MovementReplicator : MonoBehaviour
{
    public const float INTERPOLATION_TIME = 4 * MovementBroadcaster.BROADCAST_PERIOD;
    public const float SNAPSHOT_EXPIRATION_TIME = 5f * INTERPOLATION_TIME;

    /// <summary>
    /// How far (seconds) past the newest snapshot the remote vehicle is allowed to be extrapolated. A snapshot is already as old as the network latency when it arrives,
    /// this is what lets the remote vehicle be drawn where the driver's vehicle is now instead of where it was a quarter of a second ago.
    /// </summary>
    public const float MAX_EXTRAPOLATION_TIME = 0.3f;

    /// <summary>
    /// Time constant (seconds) with which the drawn vehicle closes the gap to the extrapolated position. The drawn vehicle moves with the estimated velocity in between,
    /// so a steady drive has no lag from this smoothing.
    /// </summary>
    public const float CORRECTION_TIME_CONSTANT = 0.06f;

    /// <summary>
    /// A gap bigger than this (meters) is closed at once.
    /// </summary>
    public const float SNAP_DISTANCE = 5f;

    /// <summary>
    /// After the replicator let go (no snapshots for a while) the next draw places the vehicle at once only if it is further off than this (meters), otherwise it eases in as usual.
    /// </summary>
    private const float RESUME_SNAP_DISTANCE = 0.5f;

    /// <summary>
    /// Velocity is estimated over the last snapshots (at most this many seconds back), older ones say nothing about the current movement.
    /// </summary>
    private const float MAX_VELOCITY_WINDOW = 0.25f;

    private TimeManager timeManager;

    private readonly ExpandableRingBuffer<Snapshot> buffer = [];
    /// <summary>
    /// To ensure a smooth experience, we need a max allowed latency value which should top the incoming latencies at all times.
    /// Big increments and any decrements of this value will likely cause stutter, so we try to avoid changing this value too much.
    /// But it is required that after a lag spike, we eventually lower down that value, which is done periodically <see cref="NitroxPrefs.LatencyUpdatePeriod"/>.
    /// </summary>
    public float MaxAllowedLatency;

    private double latestLatencyBumpTime;
    private float maxLatencyDetectedRecently;

    /// <summary>
    /// When encountering a latency bump, we must expect worse happening right after, so we add this margin to our new <see cref="MaxAllowedLatency"/>.
    /// After each periodical latency update (<see cref="LatencyUpdatePeriod"/>), we only want to lower the latency if it's way smaller than the current variable latency.
    /// The safety threshold is defined by this value.
    /// </summary>
    private float SafetyLatencyMargin => ReplicatorTuning.Margin(NitroxPrefs.SafetyLatencyMargin.Value);

    private float LatencyUpdatePeriod => NitroxPrefs.LatencyUpdatePeriod.Value;

    private Rigidbody rigidbody;
    public NitroxId objectId { get; private set; }

#if DEBUG
    private readonly HullJudderMeter judderMeter = new();
    private Vector3 lastSetPosition;
#endif

    /// <summary>
    /// The copy of a vehicle on a client that does not simulate it has a rigidbody that is neither kinematic nor under forces (WorldForces is off, no drag): whatever velocity
    /// it had when the replicator stopped moving it (a Cyclops that was still sinking while it was spawned, a vehicle the owner parked) carried it away forever, the snapshots
    /// pulled it back every time one arrived. The transform is set by the replicator, so the rigidbody must not move on its own.
    /// </summary>
    private void StopRigidbody()
    {
        if (rigidbody && !rigidbody.isKinematic)
        {
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Used time must be based on real time to avoid effects from time changes/speed.
    /// </summary>
    private double RealTime => MovementClock.Now(timeManager.RealTimeElapsed);

    /// <summary>
    /// Draw the vehicle at the position the movement is predicted to have now (see <see cref="MAX_EXTRAPOLATION_TIME"/>) instead of playing the snapshots back with a delay.
    /// The Cyclops keeps the delayed playback: the players walking in it are positioned relative to it.
    /// </summary>
    protected virtual bool UsesExtrapolation => ReplicatorTuning.Extrapolate;

    private readonly Snapshot[] recentSnapshots = new Snapshot[3];
    private int recentCount;
    private bool hasDrawnPosition;
    private double lastLatencyLogTime;

    /// <summary>
    /// Whether the newest snapshots show standstill: then the vehicle is drawn where the newest snapshot says and nothing is extrapolated (V4).
    /// </summary>
    private readonly HullRestRules.RestTracker restTracker = new();

    /// <summary>
    /// Set when the snapshots ran out (the vehicle stopped sending): the hull stays at the pose of the last snapshot instead of being let go, see <see cref="HoldsPoseWhenIdle" />.
    /// </summary>
    private bool holdingIdlePose;
    private Vector3 heldPosition;
    private Quaternion heldRotation;
    private bool hasHeldPose;

    /// <summary>
    /// When the snapshots of this hull last showed it moving (or when the first hold began): a displacement of the held copy only counts as a push once
    /// <see cref="HullRestRules.HOLD_SETTLE_SECONDS" /> have passed since, right after a drive the drawn hull is still being eased onto the last snapshot.
    /// </summary>
    private float lastMotionTime;

    /// <summary>
    /// What the hold had to put back (something on this client moved the copy), for the log line of <see cref="ReportHoldDisplacement" />: the hold repairs the picture
    /// without a word otherwise, and in a Release build this line is the only trace of a push on the copy.
    /// </summary>
    private readonly HullRestRules.DisplacementTally holdTally = new();

    /// <summary>
    /// A hull (the Cyclops) which stops getting snapshots keeps the pose of the last one, put back every frame, instead of being let go. The client that simulates it does not
    /// send a hull which nobody steers unless it moved by 5 cm (or every 5 s), so a copy that stands somewhere else was moved by something on this client, and the next
    /// snapshot would then pull it back: a hull which drifts a little and snaps back (V4, bug 11). Other vehicles are let go as before: a docking animation moves them.
    /// </summary>
    protected virtual bool HoldsPoseWhenIdle => false;

    /// <summary>
    /// Who stands in the hull and what their bodies are like, appended to the log line when the hold had to put the copy back (see <see cref="ReportHoldDisplacement" />).
    /// </summary>
    protected virtual string DescribeHoldContext() => string.Empty;

#if DEBUG
    /// <summary>
    /// The speed (m/s) the last frame extrapolated with, for the harness probe <c>hullrest</c>.
    /// </summary>
    public float DebugExtrapolationSpeed { get; private set; }
    public bool DebugAtRest => restTracker.AtRest;
    public bool DebugHolding => holdingIdlePose;

    // The writes of this replicator to the transform of the hull, for the force probe (HullForceProbe, "hullforce"): the pose before the write is only read while the probe is armed
    private Vector3 probePositionBefore;
    private Quaternion probeRotationBefore;

    private void ProbeBeforeWrite()
    {
        if (HullForceProbe.Armed)
        {
            probePositionBefore = transform.position;
            probeRotationBefore = transform.rotation;
        }
    }

    private void ProbeAfterWrite(string site)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteReplicatorWrite(transform, site, probePositionBefore, transform.position, Quaternion.Angle(probeRotationBefore, transform.rotation));
        }
    }
#endif

    public void Awake()
    {
        timeManager = this.Resolve<TimeManager>();
    }

    public void AddSnapshot(MovementData movementData, double snapshotRealTime)
    {
        double realTime = RealTime;
        float latency = MovementTimeRules.Age(realTime, snapshotRealTime);

        RecalculateMaxAllowedLatency(latency, realTime);
        LogLatency(latency, realTime);

        if (UsesExtrapolation)
        {
            AddRecentSnapshot(movementData, snapshotRealTime);
            return;
        }

        double occurrenceTime = snapshotRealTime + ReplicatorTuning.InterpolationTime(INTERPOLATION_TIME) + MaxAllowedLatency;

        // Cleaning any previous value change that would occur later than the newly received snapshot
        while (!buffer.IsEmpty())
        {
            if (buffer.Last.IsSnapshotNewer(occurrenceTime))
            {
                buffer.RemoveLast();
            }
            else
            {
                break;
            }
        }

        buffer.Add(new Snapshot(movementData, occurrenceTime));
    }

    private void AddRecentSnapshot(MovementData movementData, double snapshotRealTime)
    {
        // Late or repeated snapshots say nothing new about where the vehicle is
        if (recentCount > 0 && snapshotRealTime <= recentSnapshots[recentCount - 1].Time)
        {
            return;
        }

        if (recentCount == recentSnapshots.Length)
        {
            for (int i = 1; i < recentSnapshots.Length; i++)
            {
                recentSnapshots[i - 1] = recentSnapshots[i];
            }
            recentCount--;
        }

        recentSnapshots[recentCount] = new Snapshot(movementData, snapshotRealTime);
        recentCount++;
    }

    private void RecalculateMaxAllowedLatency(float latency, double realTime)
    {
        if (latency > MaxAllowedLatency)
        {
            MaxAllowedLatency = latency + SafetyLatencyMargin;
            latestLatencyBumpTime = realTime;
            maxLatencyDetectedRecently = 0;
            return;
        }

        maxLatencyDetectedRecently = Mathf.Max(latency, maxLatencyDetectedRecently);

        if (realTime - latestLatencyBumpTime < LatencyUpdatePeriod)
        {
            return;
        }

        if (maxLatencyDetectedRecently < MaxAllowedLatency - 2 * SafetyLatencyMargin)
        {
            MaxAllowedLatency = maxLatencyDetectedRecently + SafetyLatencyMargin; // regular gameplay latency variation
        }
        latestLatencyBumpTime = realTime;
        maxLatencyDetectedRecently = 0;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void LogLatency(float latency, double realTime)
    {
        if (realTime - lastLatencyLogTime < 1f)
        {
            return;
        }
        lastLatencyLogTime = realTime;
        Log.Debug($"[{nameof(MovementReplicator)}] {name}: snapshot latency {latency * 1000f:F0} ms, max allowed latency {MaxAllowedLatency * 1000f:F0} ms, mode {(UsesExtrapolation ? "extrapolate" : "delayed playback")}");
    }

    public void ClearBuffer()
    {
        buffer.Clear();
        recentCount = 0;
        hasDrawnPosition = false;
        holdingIdlePose = false;
        restTracker.Reset();
    }

    public void Start()
    {
        if (!gameObject.TryGetNitroxId(out NitroxId _objectId))
        {
            Log.Error($"Can't start a {nameof(MovementReplicator)} on {name} because it doesn't have an attached: {nameof(NitroxEntity)}");
            Destroy(this);
            return;
        }
        objectId = _objectId;

        rigidbody = GetComponent<Rigidbody>();
        if (gameObject.TryGetComponent(out NitroxCyclops nitroxCyclops))
        {
            nitroxCyclops.SetReceiving();
        }
        else
        {
            if (gameObject.TryGetComponent(out WorldForces worldForces))
            {
                worldForces.enabled = false;
            }
            rigidbody.isKinematic = false;
        }

        MovementBroadcaster.RegisterReplicator(this);
    }

    public void OnDestroy()
    {
        if (gameObject.TryGetComponent(out NitroxCyclops nitroxCyclops))
        {
            nitroxCyclops.SetBroadcasting();
        }
        else
        {
            if (gameObject.TryGetComponent(out WorldForces worldForces))
            {
                worldForces.enabled = true;
            }
        }

        MovementBroadcaster.UnregisterReplicator(this);
    }

    /// <summary>The copy is carried by something else for now (a scout drone in its dock rides on the hull): the replicator places nothing.</summary>
    protected virtual bool IsSuspended => false;

    public void Update()
    {
        if (IsSuspended)
        {
            return;
        }
        if (UsesExtrapolation)
        {
            UpdateExtrapolated();
            return;
        }

        if (buffer.IsEmpty())
        {
            StopRigidbody();
            return;
        }

        double realTime = RealTime;

        // Sorting out expired nodes
        while (!buffer.IsEmpty() && buffer.First.IsExpired(realTime))
        {
            buffer.RemoveFirst();
        }

        // No usable nodes left
        if (buffer.IsEmpty())
        {
            StopRigidbody();
            return;
        }

        // Current node is not useable yet
        if (buffer.First.IsSnapshotNewer(realTime))
        {
            return;
        }

        // Purging the next nodes if they should have already happened
        while (buffer.Count > 1)
        {
            if (!buffer[1].IsSnapshotNewer(realTime))
            {
                buffer.RemoveFirst();
            }
            else
            {
                break;
            }
        }

        // Need at least two snapshots to interpolate
        if (buffer.Count < 2)
        {
            return;
        }

        // Interpolation
        Snapshot previousSnapshot = buffer.First;
        Snapshot nextSnapshot = buffer[1];

        float t = MovementTimeRules.InterpolationFactor(realTime, previousSnapshot.Time, nextSnapshot.Time);

        StopRigidbody();
#if DEBUG
        ProbeBeforeWrite();
#endif
        transform.position = Vector3.Lerp(previousSnapshot.Data.Position.ToUnity(), nextSnapshot.Data.Position.ToUnity(), t);

        transform.rotation = Quaternion.Lerp(previousSnapshot.Data.Rotation.ToUnity(), nextSnapshot.Data.Rotation.ToUnity(), t);
#if DEBUG
        ProbeAfterWrite("MovementReplicator.Update.delayedPlayback");
#endif

        ApplyNewMovementData(nextSnapshot.Data);
#if DEBUG
        judderMeter.Record(name, "delayed playback", transform.position, false);
#endif

        // TODO: fix remote players being able to go through the object (ex: cyclops)
    }

    /// <summary>
    /// Draws the vehicle where the newest snapshots say it is now: the newest position moved on by the velocity of the last snapshots for as long as ago the snapshot
    /// was taken (network latency included), capped by <see cref="MAX_EXTRAPOLATION_TIME"/>. The drawn vehicle follows that target with a short smoothing so that a change
    /// of course or a late snapshot does not pop.
    /// </summary>
    private void UpdateExtrapolated()
    {
        if (recentCount == 0)
        {
#if DEBUG
            DebugExtrapolationSpeed = 0f;
#endif
            StopRigidbody();
            HoldIdlePose();
            return;
        }

        holdingIdlePose = false;

        double now = RealTime;
        Snapshot newest = recentSnapshots[recentCount - 1];

        // Same lifetime as the delayed playback: once the snapshots stop (parked vehicle, docked, lock moved on) the replicator lets go of the transform,
        // so docking animations, parents and physics are not fought over. A hull (HoldsPoseWhenIdle) is not let go, it stays where the last snapshot put it.
        if (now - newest.Time > SNAPSHOT_EXPIRATION_TIME)
        {
            if (HoldsPoseWhenIdle)
            {
                if (!hasHeldPose)
                {
                    // The first hold brings a copy that was spawned elsewhere (a join, a streamed-in cell) to the pose of the snapshots, that is not a push
                    hasHeldPose = true;
                    lastMotionTime = Time.unscaledTime;
                }

                holdingIdlePose = true;
                heldPosition = newest.Data.Position.ToUnity();
                heldRotation = newest.Data.Rotation.ToUnity();
            }

            recentCount = 0;
            hasDrawnPosition = false;
            restTracker.Reset();
            StopRigidbody();
            HoldIdlePose();
            return;
        }

        Vector3 newestPosition = newest.Data.Position.ToUnity();
        Quaternion newestRotation = newest.Data.Rotation.ToUnity();

        Vector3 velocity = Vector3.zero;
        Quaternion rotationStep = Quaternion.identity;
        float windowSeconds = 0f;
        if (recentCount >= 2)
        {
            Snapshot oldest = recentSnapshots[0];
            windowSeconds = MovementTimeRules.Age(newest.Time, oldest.Time);
            if (windowSeconds > 0.001f && windowSeconds <= MAX_VELOCITY_WINDOW)
            {
                Vector3 oldestPosition = oldest.Data.Position.ToUnity();
                Quaternion oldestRotation = oldest.Data.Rotation.ToUnity();
                if (restTracker.IsAtRest(windowSeconds, (newestPosition - oldestPosition).magnitude, Quaternion.Angle(oldestRotation, newestRotation)))
                {
                    // The newest snapshots show standstill (a keep-alive burst, a hull which creeps): draw it where the newest snapshot says, nothing is extrapolated
                    windowSeconds = 0f;
                }
                else
                {
                    lastMotionTime = Time.unscaledTime;
                    velocity = (newestPosition - oldestPosition) / windowSeconds;
                    rotationStep = newestRotation * Quaternion.Inverse(oldestRotation);
                }
            }
            else
            {
                windowSeconds = 0f;
            }
        }

        float maxHorizon = ReplicatorTuning.MaxExtrapolationTime(MAX_EXTRAPOLATION_TIME);
        float horizon = Mathf.Clamp(MovementTimeRules.Age(now, newest.Time), 0f, maxHorizon);
        // Past the cap the newest snapshot is too old to say where the vehicle is heading: the target stands still, so must the drawn vehicle
        bool pastHorizon = now - newest.Time >= maxHorizon;
        Vector3 targetPosition = newestPosition + velocity * horizon;
        Quaternion targetRotation = windowSeconds > 0f
            ? Quaternion.SlerpUnclamped(Quaternion.identity, rotationStep, horizon / windowSeconds) * newestRotation
            : newestRotation;

        StopRigidbody();
        float deltaTime = Time.deltaTime;
        float gapSqr = (targetPosition - transform.position).sqrMagnitude;
#if DEBUG
        // Where the vehicle stands now against where this replicator left it last frame: anything else that moves it (physics, the vehicle's own scripts) shows here
        judderMeter.Note(MovementTimeRules.Age(now, newest.Time), windowSeconds, velocity.magnitude, Mathf.Sqrt(gapSqr), recentCount, (transform.position - lastSetPosition).magnitude, (newestPosition - transform.position).magnitude, deltaTime, 1f - Mathf.Exp(-deltaTime / ReplicatorTuning.CorrectionTimeConstant(CORRECTION_TIME_CONSTANT)));
#endif
        // The first draw after the snapshots had stopped (the replicator lets go after 0.67 s without) only places the vehicle at once when it is really elsewhere: a vehicle that
        // stood still and starts again is a few centimeters off, and a hard placement then is a visible pop at every start
        bool snapped = gapSqr > SNAP_DISTANCE * SNAP_DISTANCE || (!hasDrawnPosition && gapSqr > RESUME_SNAP_DISTANCE * RESUME_SNAP_DISTANCE);
        if (snapped)
        {
#if DEBUG
            ProbeBeforeWrite();
#endif
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            hasDrawnPosition = true;
#if DEBUG
            ProbeAfterWrite("MovementReplicator.UpdateExtrapolated.snap");
#endif
        }
        else
        {
            hasDrawnPosition = true;
            // Moves on with the estimated velocity, then closes part of what is left to the target
            Vector3 advanced = transform.position + (pastHorizon ? Vector3.zero : velocity) * deltaTime;
            float closing = 1f - Mathf.Exp(-deltaTime / ReplicatorTuning.CorrectionTimeConstant(CORRECTION_TIME_CONSTANT));
#if DEBUG
            ProbeBeforeWrite();
#endif
            transform.SetPositionAndRotation(Vector3.Lerp(advanced, targetPosition, closing), Quaternion.Slerp(transform.rotation, targetRotation, closing));
#if DEBUG
            ProbeAfterWrite("MovementReplicator.UpdateExtrapolated.ease");
#endif
        }

        ApplyNewMovementData(newest.Data);
#if DEBUG
        DebugExtrapolationSpeed = velocity.magnitude;
        lastSetPosition = transform.position;
        judderMeter.Record(name, "extrapolated", transform.position, snapped);
#endif
    }

    /// <summary>
    /// Keeps a hull at the pose of its last snapshot while no snapshots come (see <see cref="HoldsPoseWhenIdle" />): puts it back when anything moved it, closes a bigger gap
    /// with the usual smoothing.
    /// </summary>
    private void HoldIdlePose()
    {
        if (!holdingIdlePose || !ReplicatorTuning.HoldIdlePose)
        {
            return;
        }

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        float distance = (heldPosition - position).magnitude;
        float degrees = Quaternion.Angle(rotation, heldRotation);
        HullRestRules.IdleHoldAction action = HullRestRules.DecideIdleHold(distance, degrees);
        if (action != HullRestRules.IdleHoldAction.None && HullRestRules.IsHoldSettled(Time.unscaledTime - lastMotionTime))
        {
            holdTally.Add(distance, degrees);
        }

        if (action == HullRestRules.IdleHoldAction.Place)
        {
#if DEBUG
            ProbeBeforeWrite();
#endif
            transform.SetPositionAndRotation(heldPosition, heldRotation);
#if DEBUG
            ProbeAfterWrite("MovementReplicator.HoldIdlePose.place");
#endif
        }
        else if (action == HullRestRules.IdleHoldAction.Ease)
        {
            float closing = 1f - Mathf.Exp(-Time.deltaTime / ReplicatorTuning.CorrectionTimeConstant(CORRECTION_TIME_CONSTANT));
#if DEBUG
            ProbeBeforeWrite();
#endif
            transform.SetPositionAndRotation(Vector3.Lerp(position, heldPosition, closing), Quaternion.Slerp(rotation, heldRotation, closing));
#if DEBUG
            ProbeAfterWrite("MovementReplicator.HoldIdlePose.ease");
#endif
        }

        ReportHoldDisplacement();
    }

    /// <summary>
    /// Says in the log, at most every <see cref="HullRestRules.HOLD_REPORT_INTERVAL" /> seconds, that the hold had to put a copy back: something on this client moved a hull which
    /// only the snapshots are meant to move. The sum, the largest single displacement and who stands in the hull (<see cref="DescribeHoldContext" />) are the signature of a push
    /// on the copy (VEHICLES.md section 7); Release builds write it too, the hold repairs the picture silently otherwise.
    /// </summary>
    private void ReportHoldDisplacement()
    {
        if (!holdTally.TryTake(Time.unscaledTime, HullRestRules.HOLD_REPORT_INTERVAL, HullRestRules.HOLD_DEADBAND_DISTANCE, HullRestRules.HOLD_DEADBAND_DEGREES, out HullRestRules.DisplacementSummary summary))
        {
            return;
        }

        Log.Info($"[{nameof(MovementReplicator)}] {name} {objectId}: this copy of a hull without snapshots was moved by something on this client and put back {summary.Count} times, {summary.Meters:F3} m and {summary.Degrees:F2} degrees in all (largest {summary.MaxMeters:F3} m, {summary.MaxDegrees:F2} degrees). {DescribeHoldContext()}");
    }

    public abstract void ApplyNewMovementData(MovementData newMovementData);

    public record struct Snapshot(MovementData Data, double Time)
    {
        public bool IsSnapshotNewer(double comparedTime) => comparedTime < Time;

        public bool IsExpired(double comparedTime) => comparedTime > Time + SNAPSHOT_EXPIRATION_TIME;
    }

    public static MovementReplicator AddReplicatorToObject(GameObject gameObject)
    {
        if (gameObject.GetComponent<SeaMoth>())
        {
            return gameObject.AddComponent<SeamothMovementReplicator>();
        }
        if (gameObject.GetComponent<Exosuit>())
        {
            return gameObject.AddComponent<ExosuitMovementReplicator>();
        }
        if (gameObject.GetComponent<SubControl>())
        {
            return gameObject.AddComponent<CyclopsMovementReplicator>();
        }
        if (gameObject.GetComponent<ScoutDrone>())
        {
            return gameObject.AddComponent<DroneMovementReplicator>();
        }
        return gameObject.AddComponent<MovementReplicator>();
    }
}

/// <summary>
/// The playback constants of the movement replicators. Release builds use the constants. Debug builds read <c>NITROX_VEHICLE_TUNING</c> (for example
/// <c>extrapolate=0;interp=4;margin=0.05</c>, keys: extrapolate 0/1, interp (snapshot periods of delayed playback), margin (seconds added to the latency),
/// tau (smoothing seconds), maxextrap (seconds), hold 0/1: a hull keeps the pose of its last snapshot) so that the harness can compare settings with one build.
/// </summary>
internal static class ReplicatorTuning
{
#if DEBUG
    private static readonly System.Collections.Generic.Dictionary<string, float> overrides = Parse(Environment.GetEnvironmentVariable("NITROX_VEHICLE_TUNING"));

    private static System.Collections.Generic.Dictionary<string, float> Parse(string text)
    {
        System.Collections.Generic.Dictionary<string, float> result = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string pair in text.Split(';'))
            {
                string[] parts = pair.Split('=');
                if (parts.Length == 2 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    result[parts[0].Trim()] = value;
                }
            }
            Log.Info($"[{nameof(ReplicatorTuning)}] NITROX_VEHICLE_TUNING = {text}");
        }
        return result;
    }

    private static float Get(string key, float fallback) => overrides.TryGetValue(key, out float value) ? value : fallback;

    public static bool Extrapolate => Get("extrapolate", 1f) > 0.5f;
    public static bool CyclopsExtrapolate => Extrapolate && Get("cyclops", 1f) > 0.5f;
    // hold=0: a hull is let go when its snapshots stop, like the other vehicles (V4, the harness check of bug 11 compares the two)
    public static bool HoldIdlePose => Get("hold", 1f) > 0.5f;
    public static float InterpolationTime(float fallback) => Get("interp", fallback / MovementBroadcaster.BROADCAST_PERIOD) * MovementBroadcaster.BROADCAST_PERIOD;
    public static float MaxExtrapolationTime(float fallback) => Get("maxextrap", fallback);
    public static float CorrectionTimeConstant(float fallback) => Get("tau", fallback);
    public static float Margin(float fallback) => Get("margin", fallback);
#else
    public static bool Extrapolate => true;
    public static bool CyclopsExtrapolate => true;
    public static bool HoldIdlePose => true;
    public static float InterpolationTime(float fallback) => fallback;
    public static float MaxExtrapolationTime(float fallback) => fallback;
    public static float CorrectionTimeConstant(float fallback) => fallback;
    public static float Margin(float fallback) => fallback;
#endif
}

#if DEBUG
/// <summary>
/// Debug-only measure of how steadily a replicated vehicle is drawn (what a passenger sees as judder): the velocity of each rendered frame against the velocity smoothed over 0.25 s.
/// A hull that is drawn on a straight line at a steady speed has a deviation near zero whatever the frame rate; corrections, snapshot steps and extrapolation errors show up as deviation.
/// Logs every 5 s while the vehicle moves: <c>[Judder] name (mode): mean speed, deviation rms, deviation max</c>.
/// </summary>
internal sealed class HullJudderMeter
{
    private Vector3 lastPosition;
    private bool hasLast;
    private Vector3 smoothVelocity;
    private double sumSquares;
    private double speedSum;
    private float maxDeviation;
    private int count;
    private float windowStart = float.NaN;
    private float noteAge;
    private float noteWindow;
    private float noteSpeed;
    private float noteGap;
    private int noteSnapshots;
    private float lastFastLog = float.NegativeInfinity;

    private float noteMovedElsewhere;
    private float noteBehindNewest;

    private float noteDeltaTime;
    private float noteClosing;

    public void Note(float age, float window, float estimatedSpeed, float gap, int snapshots, float movedElsewhere, float behindNewest, float deltaTime, float closing)
    {
        noteDeltaTime = deltaTime;
        noteClosing = closing;
        noteMovedElsewhere = movedElsewhere;
        noteBehindNewest = behindNewest;
        noteAge = age;
        noteWindow = window;
        noteSpeed = estimatedSpeed;
        noteGap = gap;
        noteSnapshots = snapshots;
    }

    public void Record(string name, string mode, Vector3 position, bool snapped)
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (float.IsNaN(windowStart))
        {
            windowStart = Time.unscaledTime;
        }
        if (hasLast && deltaTime > 0f)
        {
            Vector3 velocity = (position - lastPosition) / deltaTime;
            smoothVelocity += (velocity - smoothVelocity) * (1f - Mathf.Exp(-deltaTime / 0.25f));
            float deviation = (velocity - smoothVelocity).magnitude;
            if (deviation > 4f)
            {
                Log.Info($"[Judder] spike {name} ({mode}): frame velocity {velocity.magnitude:F1} m/s against smoothed {smoothVelocity.magnitude:F1} m/s, a step of {(position - lastPosition).magnitude:F2} m in {deltaTime * 1000f:F0} ms, placed at once: {snapped}");
            }
            sumSquares += deviation * deviation;
            speedSum += smoothVelocity.magnitude;
            maxDeviation = Mathf.Max(maxDeviation, deviation);
            count++;
        }
        if (hasLast && deltaTime > 0f && noteSpeed > 6f && Time.unscaledTime - lastFastLog > 0.4f)
        {
            lastFastLog = Time.unscaledTime;
            Log.Info($"[Judder] fast {name} ({mode}): estimated speed {noteSpeed:F1} m/s from {noteSnapshots} snapshots over {noteWindow * 1000f:F0} ms, newest snapshot {noteAge * 1000f:F0} ms old, drawn speed {(position - lastPosition).magnitude / deltaTime:F1} m/s, {noteGap:F2} m from the extrapolated target, {noteBehindNewest:F2} m from the newest snapshot, moved {noteMovedElsewhere:F2} m by something else since the last frame, frame {noteDeltaTime * 1000f:F0} ms, closing {noteClosing:F2} of the gap per frame");
        }
        lastPosition = position;
        hasLast = true;

        if (Time.unscaledTime - windowStart >= 5f)
        {
            if (count > 0 && speedSum / count > 1.0)
            {
                Log.Info($"[Judder] {name} ({mode}): mean speed {speedSum / count:F1} m/s, frame velocity deviation rms {Mathf.Sqrt((float)(sumSquares / count)):F3} m/s, max {maxDeviation:F2} m/s, {count} frames");
            }
            sumSquares = 0;
            speedSum = 0;
            maxDeviation = 0;
            count = 0;
            windowStart = Time.unscaledTime;
        }
    }
}
#endif
