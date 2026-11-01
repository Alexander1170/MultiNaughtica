using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// The parking brake of a Cyclops hull which nobody steers, on the client that simulates it (V4, bug 11, VEHICLES.md section 7). Plain numbers, no game types, so that it can be
/// tested; <c>NitroxCyclops</c> reads the rigidbody and applies the factors.
/// </summary>
/// <remarks>
/// A parked Cyclops is a free body in the water: whatever force acts on its hull, however weak, moves it for as long as the force acts, and the other client then sees a hull which
/// wanders and is pulled back by the next snapshot. The brake takes the speed out of an unsteered hull which is slow: the horizontal speed and the turning about the vertical axis.
/// A push which is not slow (a creature, an impact) is left alone, so is everything vertical (buoyancy, flooding) and the pitch and roll that the stabilizer works against.
/// <para>
/// What the brake guarantees, and what it does not. It works on the speed of the rigidbody, once per physics step of dt seconds, before the step. A constant force of a m/s^2 gives
/// the hull a * dt of speed in every step. While a * dt is below <see cref="STOP_SPEED" /> (a below 0.2 m/s^2 at the 0.1 s step used when nobody is aboard, below 1 m/s^2 at the
/// 0.02 s step used while the local player walks in the hull) the brake stops that speed again before the next step and the hull drifts at a * dt, no faster. Above that
/// the stop never takes hold, only the decay by e^-3 per second does, and the hull drifts at a * dt / (1 - e^(-3 * dt)), about 0.39 * a at 0.1 s: braked, still visible
/// (0.3 m/s^2 is 12 cm/s, 7 m per minute). A push which does not go through the speed (a write to the transform or the pose, the push out of an overlap with
/// another collider) or which is above <see cref="RELEASE_SPEED" /> (an impact, a creature) is not held at all. The tests state these numbers
/// (<c>HullParkingRulesTest</c>), the cause of bug 11 is not known (VEHICLES.md section 7), so this is a mitigation, and <c>NitroxCyclops</c> logs when the hull
/// moves in a way that the brake does not cover.
/// </para>
/// </remarks>
public static class HullParkingRules
{
    /// <summary>
    /// Below this horizontal speed (m/s) an unsteered hull is braked.
    /// </summary>
    public const float BRAKE_SPEED = 0.5f;

    /// <summary>
    /// A braked hull which is faster than this (m/s) was pushed for real: the brake lets go (hysteresis, so that a speed which hovers around <see cref="BRAKE_SPEED" /> does not
    /// switch the brake on and off).
    /// </summary>
    public const float RELEASE_SPEED = 0.9f;

    /// <summary>
    /// A braked hull which is slower than this (m/s) is stopped. What a force of a m/s^2 still moves it by in a physics step of dt seconds is a * dt^2, so a drift cannot get
    /// faster than a * dt, but ONLY while a * dt is below this speed: beyond it the stop never takes hold (see the remarks of the class).
    /// </summary>
    public const float STOP_SPEED = 0.02f;

    /// <summary>
    /// <c>NitroxCyclops</c> writes at most one log line per this many seconds about a hull that nobody steers and which moved.
    /// </summary>
    public const float REPORT_INTERVAL = 30f;

    /// <summary>
    /// The line is written when the brake took this much distance (meters) out of the hull, or when something moved the hull this much without going through its speed, since the
    /// last line, or at once when the hull outran the brake.
    /// </summary>
    public const float REPORT_DISTANCE = 0.05f;

    /// <summary>
    /// A physics step which moved the hull by more than its speed accounts for, by less than this (meters), is noise of the coordinates (a hull 330 m from the origin has a
    /// float resolution of 0.03 mm), not a pose write or a push out of an overlap.
    /// </summary>
    public const float UNEXPLAINED_STEP_MIN = 0.002f;

    /// <summary>
    /// The same three limits for the turning about the vertical axis, in radians per second (0.05 rad/s moves the ends of the hull by 0.65 m/s).
    /// </summary>
    public const float BRAKE_YAW_RATE = 0.05f;

    public const float RELEASE_YAW_RATE = 0.1f;

    public const float STOP_YAW_RATE = 0.0015f;

    /// <summary>
    /// Per second: a braked speed falls by a factor e^-3 each second, so there is no jerk when the brake takes hold.
    /// </summary>
    public const float BRAKE_RATE = 3f;

    /// <summary>
    /// A hull flooded more than this sinks (<c>SubRoot.FixedUpdate</c>), nothing here may work against that.
    /// </summary>
    public const float FLOOD_LIMIT = 0.25f;

    /// <summary>
    /// Whether the hull gets the brake at all.
    /// </summary>
    /// <param name="steered">Somebody is at the helm, or the throttle is on: the hull is driven.</param>
    /// <param name="isReplica">A movement replicator draws the hull (another player simulates it): the transform is not ours to brake.</param>
    /// <param name="kinematic">The rigidbody is not simulated (nothing to brake).</param>
    /// <param name="destroyed">The Cyclops is destroyed or going down.</param>
    /// <param name="floodFraction">How much of the hull is flooded, 0 to 1.</param>
    public static bool ShouldBrake(bool steered, bool isReplica, bool kinematic, bool destroyed, float floodFraction)
    {
        if (steered || isReplica || kinematic || destroyed)
        {
            return false;
        }

        // Negated so that NaN counts as dry
        return !(floodFraction > FLOOD_LIMIT);
    }

    /// <summary>
    /// Whether an unsteered hull moves faster than the brake takes: the brake did not act (<paramref name="factor" /> 1, see <see cref="Brake.Step" />) and the horizontal speed is at
    /// or above the speed the brake starts at. Not a creep, a real push (a creature, an impact, a current): the brake lets it be.
    /// </summary>
    public static bool OutrunsBrake(float horizontalSpeed, float factor)
    {
        return horizontalSpeed >= BRAKE_SPEED && factor >= 1f;
    }

    /// <summary>
    /// How far one physics step moved the hull beyond what its new speed accounts for: the caller passes the length of the vector (position now - position at the last step - speed
    /// now * step). A body moves by its speed times the step, so a difference means that something moved it without going through the speed (a write to the transform or the pose,
    /// the push out of an overlap with another collider), which the brake can not hold.
    /// </summary>
    /// <returns>The distance in meters, or 0 for noise and for a broken measurement.</returns>
    public static float UnexplainedStep(float differenceMeters)
    {
        // NaN compares false, so a broken measurement counts as nothing
        return differenceMeters >= UNEXPLAINED_STEP_MIN && !float.IsInfinity(differenceMeters) ? differenceMeters : 0f;
    }

    /// <summary>
    /// Whether two physics steps can be compared: the step changes when the local player starts or stops walking in the hull (0.1 s to 0.02 s), and the one step after that says
    /// nothing about the movement.
    /// </summary>
    public static bool AreSameStep(float previousStep, float step)
    {
        // NaN compares false, so a broken step is never the same
        return step > 0f && Math.Abs(step - previousStep) < 1e-6f;
    }

    /// <summary>
    /// Whether the log line about a hull that nobody steers is due.
    /// </summary>
    /// <param name="secondsSinceLastReport">Seconds since the last line (infinite when there was none).</param>
    /// <param name="removedMeters">Distance the brake took out of the speed since then.</param>
    /// <param name="unexplainedMeters">Distance the hull moved since then without its speed (see <see cref="UnexplainedStep" />).</param>
    /// <param name="fastSteps">Steps since then in which the hull outran the brake (see <see cref="OutrunsBrake" />).</param>
    public static bool IsCreepNews(float secondsSinceLastReport, float removedMeters, float unexplainedMeters, int fastSteps)
    {
        // Negated so that a broken clock stays silent
        if (!(secondsSinceLastReport >= REPORT_INTERVAL))
        {
            return false;
        }

        return removedMeters >= REPORT_DISTANCE || unexplainedMeters >= REPORT_DISTANCE || fastSteps > 0;
    }

    public static Brake CreateLinearBrake() => new(BRAKE_SPEED, RELEASE_SPEED, STOP_SPEED);

    public static Brake CreateYawBrake() => new(BRAKE_YAW_RATE, RELEASE_YAW_RATE, STOP_YAW_RATE);

    /// <summary>
    /// The brake of one quantity (a speed, a turn rate) with its hysteresis.
    /// </summary>
    public sealed class Brake
    {
        private readonly float brakeBelow;
        private readonly float releaseAbove;
        private readonly float stopBelow;
        private bool braking;

        public Brake(float brakeBelow, float releaseAbove, float stopBelow)
        {
            this.brakeBelow = brakeBelow;
            this.releaseAbove = releaseAbove;
            this.stopBelow = stopBelow;
        }

        public bool Braking => braking;

        /// <param name="speed">The size of the quantity now (never negative).</param>
        /// <param name="deltaTime">The physics step in seconds.</param>
        /// <returns>The factor to multiply the quantity with: 1 leaves it alone, 0 stops it.</returns>
        public float Step(float speed, float deltaTime)
        {
            // Negated so that NaN and negative values leave the body alone
            if (!(speed >= 0f) || !(deltaTime > 0f))
            {
                braking = false;
                return 1f;
            }

            if (braking)
            {
                if (speed > releaseAbove)
                {
                    braking = false;
                }
            }
            else if (speed < brakeBelow)
            {
                braking = true;
            }

            if (!braking)
            {
                return 1f;
            }

            if (speed < stopBelow)
            {
                return 0f;
            }

            return (float)Math.Exp(-BRAKE_RATE * deltaTime);
        }

        /// <summary>
        /// The hull is steered, replicated or gone: the next time it is slow it starts from a released brake.
        /// </summary>
        public void Reset()
        {
            braking = false;
        }
    }
}
