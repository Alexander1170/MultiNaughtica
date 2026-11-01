using System;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// Moves the body of a remote player from timestamped snapshots, like vehicles are moved (<see cref="MovementReplicator"/>): the body is drawn where the newest snapshot
/// says the player is NOW, the newest position moved on by the velocity of the last snapshots for as long as the snapshot is old (network latency included).
/// It replaces the velocity chase of the rigidbody (velocity = gap / fixedDeltaTime on every packet), which lagged by a physics step (0.1 s unless a high-precision
/// behaviour is registered, 0.02 s in a Cyclops), overshot between packets and rubber-banded under jitter.
/// </summary>
public class RemotePlayerMovementReplicator : MonoBehaviour
{
    /// <summary>
    /// How far (seconds) past the newest snapshot the body may be extrapolated.
    /// </summary>
    public const float MAX_EXTRAPOLATION_TIME = 0.25f;

    /// <summary>
    /// Time constant (seconds) with which the drawn body closes the gap to the extrapolated position, and the rotation the gap to the newest rotation.
    /// </summary>
    public const float CORRECTION_TIME_CONSTANT = 0.05f;

    /// <summary>
    /// A gap bigger than this (meters) is closed at once.
    /// </summary>
    public const float SNAP_DISTANCE = 5f;

    /// <summary>
    /// After the replicator let go (no snapshots for a while, or <see cref="Reset" />) the next draw places the body at once only if it is further off than this (meters).
    /// </summary>
    public const float RESUME_SNAP_DISTANCE = 0.5f;

    /// <summary>
    /// Snapshots older than this (seconds) are no longer used to say where the player is heading: the body stops where it is.
    /// </summary>
    public const float EXPIRATION_TIME = 0.7f;

    /// <summary>
    /// Velocity is estimated over the last snapshots, at most this many seconds back.
    /// </summary>
    private const float MAX_VELOCITY_WINDOW = 0.25f;

    /// <summary>
    /// Fastest walking velocity (meters per second) the pawn of a player in a Cyclops is extrapolated with (sprint is about 7).
    /// </summary>
    private const float MAX_LOCAL_SPEED = 10f;

    private struct Sample
    {
        public double Time;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion BodyRotation;
        public Quaternion AimingRotation;
    }

    private readonly Sample[] recent = new Sample[3];
    private int recentCount;
    private bool hasDrawn;

    /// <summary>
    /// The snapshots are positions in the space of a Cyclops, the pawn of the player in the Cyclops is moved (the body follows it, <see cref="NitroxClient.MonoBehaviours.Cyclops.NitroxCyclops.MaintainPawns" />)
    /// instead of the body in world space.
    /// </summary>
    private bool localSnapshots;

    /// <summary>
    /// The player whose pawn is moved, set while the player is in a Cyclops.
    /// </summary>
    public RemotePlayer Owner { get; set; }
    private TimeManager timeManager;

    private AnimationController animationController;

    public void Initialize(AnimationController animationController)
    {
        this.animationController = animationController;
    }

    public void Awake()
    {
        timeManager = this.Resolve<TimeManager>();
    }

    private double RealTime => MovementClock.Now(timeManager.RealTimeElapsed);

    public void AddSnapshot(double snapshotTime, Vector3 position, Vector3 velocity, Quaternion bodyRotation, Quaternion aimingRotation)
    {
        AddSnapshot(snapshotTime, position, velocity, bodyRotation, aimingRotation, false);
    }

    /// <summary>
    /// A snapshot of a player walking in a Cyclops: position, velocity and rotation in the space of the Cyclops.
    /// </summary>
    public void AddLocalSnapshot(double snapshotTime, Vector3 localPosition, Vector3 localVelocity, Quaternion localRotation)
    {
        AddSnapshot(snapshotTime, localPosition, localVelocity, localRotation, localRotation, true);
    }

    private void AddSnapshot(double snapshotTime, Vector3 position, Vector3 velocity, Quaternion bodyRotation, Quaternion aimingRotation, bool local)
    {
        if (!IsFinite(position) || !IsFinite(velocity) || !IsFinite(bodyRotation))
        {
            return;
        }

        // Snapshots of the other kind (late packets from before the player entered or left the Cyclops) say nothing about where this one is
        if (localSnapshots != local)
        {
            Reset();
            localSnapshots = local;
        }

        // Late or repeated snapshots say nothing new
        if (recentCount > 0 && snapshotTime <= recent[recentCount - 1].Time)
        {
            return;
        }

        if (recentCount == recent.Length)
        {
            for (int i = 1; i < recent.Length; i++)
            {
                recent[i - 1] = recent[i];
            }
            recentCount--;
        }
        recent[recentCount++] = new Sample { Time = snapshotTime, Position = position, Velocity = velocity, BodyRotation = bodyRotation, AimingRotation = aimingRotation };
    }

    /// <summary>
    /// The body is teleported (respawn, warp, leaving a vehicle): the next snapshot places it at once.
    /// </summary>
    public void Reset()
    {
        recentCount = 0;
        hasDrawn = false;
    }

    public void Update()
    {
        if (recentCount == 0)
        {
            return;
        }

        CyclopsPawn pawn = Owner?.Pawn;
        if (localSnapshots != (pawn != null))
        {
            // Entered or left a Cyclops since these snapshots were taken
            Reset();
            return;
        }

        double now = RealTime;
        Sample newest = recent[recentCount - 1];
        if (now - newest.Time > EXPIRATION_TIME)
        {
            // No packets any more (vehicle, piloting chair, Cyclops pawn, disconnected, dead, freecam): let go of the body. The animation follows the velocity of the last packet, so it is
            // stopped too, otherwise the player swims in place
            Reset();
            if (animationController)
            {
                animationController.Velocity = Vector3.zero;
            }
            return;
        }

        // The velocity the player had when the snapshot was taken (zero in the first packet after a stop); the estimate from the last positions is the alternative
        Vector3 velocity = newest.Velocity;
        if (localSnapshots && velocity.sqrMagnitude > MAX_LOCAL_SPEED * MAX_LOCAL_SPEED)
        {
            // Nobody walks in a Cyclops faster than this: whatever the sender measured, do not throw the pawn through the hull with it
            velocity = velocity.normalized * MAX_LOCAL_SPEED;
        }
        if (!PlayerMovementTuning.PacketVelocity && recentCount >= 2)
        {
            Sample oldest = recent[0];
            float window = MovementTimeRules.Age(newest.Time, oldest.Time);
            velocity = window > 0.001f && window <= MAX_VELOCITY_WINDOW ? (newest.Position - oldest.Position) / window : Vector3.zero;
        }

        float horizon = Mathf.Clamp(MovementTimeRules.Age(now, newest.Time), 0f, MAX_EXTRAPOLATION_TIME);
        bool pastHorizon = now - newest.Time >= MAX_EXTRAPOLATION_TIME;
        Vector3 targetPosition = newest.Position + velocity * horizon;

        // The body in world space, or the pawn in the space of the Cyclops (the pawn is in the motionless copy, the body follows it every frame)
        Transform movedTransform = pawn != null ? pawn.Handle.transform : transform;
        bool local = pawn != null;
        Vector3 currentPosition = local ? movedTransform.localPosition : movedTransform.position;
        Quaternion currentRotation = local ? movedTransform.localRotation : movedTransform.rotation;
        float deltaTime = Time.deltaTime;
        Vector3 drawnPosition;
        Quaternion drawnRotation;
        float gapSqr = (targetPosition - currentPosition).sqrMagnitude;
        // After the snapshots had stopped (a player standing still) only a body that is really elsewhere is placed at once, otherwise it eases in
        if (gapSqr > SNAP_DISTANCE * SNAP_DISTANCE || (!hasDrawn && gapSqr > RESUME_SNAP_DISTANCE * RESUME_SNAP_DISTANCE))
        {
            drawnPosition = targetPosition;
            drawnRotation = newest.BodyRotation;
            hasDrawn = true;
        }
        else
        {
            hasDrawn = true;
            float closing = 1f - Mathf.Exp(-deltaTime / CORRECTION_TIME_CONSTANT);
            Vector3 advanced = currentPosition + (pastHorizon ? Vector3.zero : velocity) * deltaTime;
            // What is closed on top of the movement itself is limited: a player who reverses at once is drawn 2 x speed x delay (0.65 m at 2.5 m/s and 130 ms) too far on the old way until the
            // news arrives, and the exponential pull back would take that in about 0.1 s (a lurch of 0.6-0.9 m in a step where the player moved 0.28 m). Limited, it takes a little longer.
            Vector3 correction = (targetPosition - advanced) * closing;
            float maxCorrection = PlayerMovementTuning.CatchUpSpeed * deltaTime;
            if (correction.sqrMagnitude > maxCorrection * maxCorrection)
            {
                correction = correction.normalized * maxCorrection;
            }
            drawnPosition = advanced + correction;
            drawnRotation = Quaternion.Slerp(currentRotation, newest.BodyRotation, closing);
        }

        if (local)
        {
            movedTransform.localPosition = drawnPosition;
            movedTransform.localRotation = drawnRotation;
            if (pawn.MaintainPredicate())
            {
                pawn.MaintainPosition();
            }
        }
        else
        {
            movedTransform.SetPositionAndRotation(drawnPosition, drawnRotation);
        }

        if (animationController && !local)
        {
            animationController.AimingRotation = newest.AimingRotation;
        }
    }

    private static bool IsFinite(Vector3 vector) => !(float.IsNaN(vector.x) || float.IsNaN(vector.y) || float.IsNaN(vector.z) || float.IsInfinity(vector.x) || float.IsInfinity(vector.y) || float.IsInfinity(vector.z));

    private static bool IsFinite(Quaternion quaternion) => !(float.IsNaN(quaternion.x) || float.IsNaN(quaternion.y) || float.IsNaN(quaternion.z) || float.IsNaN(quaternion.w) ||
                                                              float.IsInfinity(quaternion.x) || float.IsInfinity(quaternion.y) || float.IsInfinity(quaternion.z) || float.IsInfinity(quaternion.w));
}

/// <summary>
/// Debug builds read <c>NITROX_PLAYER_TUNING</c> (<c>extrapolate=0|1;throttle=0|1;packetvelocity=0|1</c>) so that the harness can compare the old movement of remote players with the new one from the
/// same build. Release builds always use the new one.
/// </summary>
internal static class PlayerMovementTuning
{
    /// <summary>
    /// The most (meters per second) the drawn body closes on its extrapolated target on top of its own movement. The harness counts a step as a jump when it is more than 0.3 m larger
    /// than the owner's step in 0.1 s, so 2.5 m/s (0.25 m) cannot make one by itself.
    /// </summary>
    private const float MAX_CATCH_UP_SPEED = 2.5f;

#if DEBUG
    private static readonly System.Collections.Generic.Dictionary<string, float> overrides = Parse(Environment.GetEnvironmentVariable("NITROX_PLAYER_TUNING"));

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
            Log.Info($"[{nameof(PlayerMovementTuning)}] NITROX_PLAYER_TUNING = {text}");
        }
        return result;
    }

    private static float Get(string key, float fallback) => overrides.TryGetValue(key, out float value) ? value : fallback;

    public static bool Extrapolate => Get("extrapolate", 1f) > 0.5f;
    public static bool Throttle => Get("throttle", 1f) > 0.5f;
    public static bool PacketVelocity => Get("packetvelocity", 1f) > 0.5f;
    public static float CatchUpSpeed => Get("catchup", MAX_CATCH_UP_SPEED);
#else
    public static bool Extrapolate => true;
    public static bool Throttle => true;
    public static bool PacketVelocity => true;
    public static float CatchUpSpeed => MAX_CATCH_UP_SPEED;
#endif
}
