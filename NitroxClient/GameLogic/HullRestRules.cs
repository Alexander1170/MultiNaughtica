using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// What the copy of a vehicle hull does when the snapshots of the client that simulates it say the hull stands still (V4, bug 11, VEHICLES.md section 7). Plain numbers, no game
/// types, so that it can be tested; <c>MovementReplicator</c> asks and acts.
/// </summary>
/// <remarks>
/// The sender (<c>WatchedEntry</c>) sends a hull that nobody steers only when it moved by 5 cm or turned by 0.05 degrees, plus a burst of snapshots every 5 s. The packets carry no
/// velocity: the receiver measures one over its newest snapshots. A hull whose measured speed is that of noise (a keep-alive burst, a creeping hull) must be drawn where it is,
/// and a copy which the rest of the game pushed (nothing of that is meant to move a copy) must be put back where the last snapshot says it is.
/// </remarks>
public static class HullRestRules
{
    /// <summary>
    /// Below this speed (m/s) over the newest snapshots, the hull stands still: no extrapolation. A steering Cyclops is far above it from the first snapshots of a start (a start
    /// at 1 m/s moves 6.6 cm in the 66 ms of three snapshots), a noise of a few millimeters between two frames (33 ms) is far below.
    /// </summary>
    public const float REST_SPEED = 0.05f;

    /// <summary>
    /// Above this speed (m/s) a hull which stood still moves again. Between <see cref="REST_SPEED" /> and this value the state stays as it was, so that a speed which hovers around
    /// one limit does not switch the extrapolation on and off with every snapshot.
    /// </summary>
    public const float MOVE_SPEED = 0.12f;

    /// <summary>
    /// The same pair for the turning: degrees per second over the newest snapshots. The hull is 27 m long, one degree per second moves its ends by 0.24 m/s.
    /// </summary>
    public const float REST_TURN_RATE = 0.5f;

    public const float MOVE_TURN_RATE = 1.2f;

    /// <summary>
    /// A held hull this close (meters) to the pose it is held at is left alone: a rigidbody which sleeps is not woken for a fraction of a millimeter.
    /// </summary>
    public const float HOLD_DEADBAND_DISTANCE = 0.0005f;

    public const float HOLD_DEADBAND_DEGREES = 0.005f;

    /// <summary>
    /// A held hull off its pose by less than this is put back at once: whatever shoved a copy (nothing is meant to) must not be seen at all.
    /// </summary>
    public const float HOLD_PLACE_DISTANCE = 0.02f;

    public const float HOLD_PLACE_DEGREES = 0.2f;

    /// <summary>
    /// A held hull this far (meters) off its pose is put back at once as well: easing a hull over several meters would drag its passengers through the sea (the same limit as
    /// <c>MovementReplicator.SNAP_DISTANCE</c>).
    /// </summary>
    public const float HOLD_SNAP_DISTANCE = 5f;

    /// <summary>
    /// The hold only counts a displacement of the copy as "moved by something on this client" once it has held the pose this long (seconds): right after a drive the drawn hull
    /// still stands a few centimeters from the last snapshot (the extrapolation overshoots a stop by 6 to 18 cm) and is eased onto it, that is not a push.
    /// </summary>
    public const float HOLD_SETTLE_SECONDS = 2f;

    /// <summary>
    /// The log line about a copy that something on its own client moved (<see cref="DisplacementTally" />) is written at most this often (seconds).
    /// </summary>
    public const float HOLD_REPORT_INTERVAL = 30f;

    public enum IdleHoldAction
    {
        /// <summary>The hull is where it is held.</summary>
        None,
        /// <summary>A little off: closed with the smoothing of the replicator, like any other correction.</summary>
        Ease,
        /// <summary>Put back at once: the offset is too small to be seen or too big to be eased.</summary>
        Place
    }

    /// <summary>
    /// What to do with a hull which holds the pose of the last snapshot (no snapshots for the expiry time of the replicator) and stands <paramref name="distance" /> meters and
    /// <paramref name="degrees" /> degrees off it. NaN counts as far off.
    /// </summary>
    public static IdleHoldAction DecideIdleHold(float distance, float degrees)
    {
        // Negated comparisons so that NaN falls through to the placement
        if (!(distance > HOLD_DEADBAND_DISTANCE) && !(degrees > HOLD_DEADBAND_DEGREES) && distance >= 0f && degrees >= 0f)
        {
            return IdleHoldAction.None;
        }

        if (distance <= HOLD_PLACE_DISTANCE && degrees <= HOLD_PLACE_DEGREES)
        {
            return IdleHoldAction.Place;
        }

        return distance > HOLD_SNAP_DISTANCE || !(distance >= 0f) || !(degrees >= 0f) ? IdleHoldAction.Place : IdleHoldAction.Ease;
    }

    /// <summary>
    /// Whether a hull which has held its pose for <paramref name="secondsHeld" /> seconds has settled, so that an offset from the pose is a push and not the end of a drive.
    /// </summary>
    public static bool IsHoldSettled(float secondsHeld)
    {
        // NaN compares false, so a broken clock is never settled
        return secondsHeld >= HOLD_SETTLE_SECONDS;
    }

    /// <summary>
    /// What <see cref="DisplacementTally.TryTake" /> hands out.
    /// </summary>
    public readonly record struct DisplacementSummary(float Meters, float Degrees, float MaxMeters, float MaxDegrees, int Count);

    /// <summary>
    /// Adds up the corrections which the hold of a copy had to make (something on this client moved a hull which only the snapshots are meant to move) and says when that is worth a
    /// log line: at most once per interval, and only when the sum is more than the dead band. The caller owns the clock. In the real logs this is the only trace of a push on the
    /// copy, the hold repairs the picture without a word otherwise (VEHICLES.md section 7).
    /// </summary>
    public sealed class DisplacementTally
    {
        private float meters;
        private float degrees;
        private float maxMeters;
        private float maxDegrees;
        private int count;
        private float lastReportTime = float.NegativeInfinity;

        /// <summary>How many corrections are waiting for a report.</summary>
        public int Count => count;

        /// <param name="distance">Meters the hull was found off its pose.</param>
        /// <param name="turned">Degrees the hull was found off its pose.</param>
        public void Add(float distance, float turned)
        {
            // NaN, negative and infinite values are a broken measurement, not a displacement
            float usableDistance = distance >= 0f && !float.IsInfinity(distance) ? distance : 0f;
            float usableTurned = turned >= 0f && !float.IsInfinity(turned) ? turned : 0f;
            if (usableDistance <= 0f && usableTurned <= 0f)
            {
                return;
            }

            meters += usableDistance;
            degrees += usableTurned;
            maxMeters = Math.Max(maxMeters, usableDistance);
            maxDegrees = Math.Max(maxDegrees, usableTurned);
            count++;
        }

        /// <param name="now">The clock of the caller, in seconds.</param>
        /// <param name="minInterval">Seconds that must pass between two reports.</param>
        /// <param name="minMeters">Report when the corrections add up to this many meters ...</param>
        /// <param name="minDegrees">... or this many degrees.</param>
        /// <returns>True when a report is due: the sums since the last one are in <paramref name="summary" /> and are reset.</returns>
        public bool TryTake(float now, float minInterval, float minMeters, float minDegrees, out DisplacementSummary summary)
        {
            summary = default;

            // Negated so that a broken clock stays silent
            if (count == 0 || !(now - lastReportTime >= minInterval))
            {
                return false;
            }

            if (!(meters >= minMeters) && !(degrees >= minDegrees))
            {
                return false;
            }

            summary = new DisplacementSummary(meters, degrees, maxMeters, maxDegrees, count);
            meters = 0f;
            degrees = 0f;
            maxMeters = 0f;
            maxDegrees = 0f;
            count = 0;
            lastReportTime = now;
            return true;
        }
    }

    /// <summary>
    /// Whether the newest snapshots of one replicated hull show standstill, with the hysteresis of <see cref="REST_SPEED" /> and <see cref="MOVE_SPEED" />.
    /// </summary>
    public sealed class RestTracker
    {
        private bool atRest = true;

        public bool AtRest => atRest;

        /// <param name="windowSeconds">The time between the oldest and the newest of the snapshots that are compared (more than 0).</param>
        /// <param name="distance">Meters between the two positions.</param>
        /// <param name="turnedDegrees">Degrees between the two rotations.</param>
        /// <returns>True when the hull stands still: draw it where the newest snapshot says, extrapolate nothing.</returns>
        public bool IsAtRest(float windowSeconds, float distance, float turnedDegrees)
        {
            if (!(windowSeconds > 0f) || !(distance >= 0f) || !(turnedDegrees >= 0f))
            {
                // No usable window: the caller has no velocity to extrapolate with either
                return atRest;
            }

            float speed = distance / windowSeconds;
            float turnRate = turnedDegrees / windowSeconds;
            if (atRest)
            {
                if (speed > MOVE_SPEED || turnRate > MOVE_TURN_RATE)
                {
                    atRest = false;
                }
            }
            else if (speed < REST_SPEED && turnRate < REST_TURN_RATE)
            {
                atRest = true;
            }

            return atRest;
        }

        /// <summary>
        /// The snapshots were dropped (the replicator let go, the buffer was cleared): the next window starts from standstill again.
        /// </summary>
        public void Reset()
        {
            atRest = true;
        }
    }
}
