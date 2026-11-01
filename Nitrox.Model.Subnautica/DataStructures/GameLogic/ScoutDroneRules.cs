using System;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The pure rules of the Cyclops scout drone (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 4): the thrust that makes it a multiple of the Seamoth, the static and the black fade of a weak signal and the leash
/// past the range, the battery. The drone itself is the game's camera drone (<c>MapRoomCamera</c>); these are the numbers that the mod changes about it. The numbers of the game that they build on are read from its code
/// and from the drone's prefab (see the design note scout_drone_design.md).
/// </summary>
public static class ScoutDroneRules
{
    /// <summary>The name of the Scout Drone's TechType as an entity of the server carries it (the item's number: the game's enum does not know it; pinned against <c>CoopItems.SCOUT_DRONE</c> by a test).</summary>
    public const string ITEM_TECH_TYPE_NAME = "20017";

    /// <summary>How long the client waits for the server to answer a request for the seat before it gives up, seconds (the server answers at once or not at all).</summary>
    public const float REQUEST_TIMEOUT_SECONDS = 3f;

    /// <summary>The game's thrust of the camera drone, m/s squared, in <c>MapRoomCamera.FixedUpdate</c> (a literal there).</summary>
    public const float VANILLA_THRUST = 20f;

    /// <summary>The forward top speed of a Seamoth in this build, m/s (measured: the design notes (not part of this repository)). The speed factor of the drone is a multiple of it.</summary>
    public const float SEAMOTH_TOP_SPEED = 11f;

    /// <summary>The game's battery use of the drone while it moves faster than <see cref="DRAIN_SPEED_THRESHOLD" />, energy per second (<c>MapRoomCamera.HandleInput</c>: delta time times 0.06666).</summary>
    public const float DRAIN_PER_SECOND = 0.06666f;

    /// <summary>Below this speed (m/s) the drone uses no battery.</summary>
    public const float DRAIN_SPEED_THRESHOLD = 2f;

    /// <summary>The capacity of the drone's battery (the game's Battery holds 100).</summary>
    public const float BATTERY_CAPACITY = 100f;

    /// <summary>Energy per second that the bay puts into the drone's battery. The Cyclops does not pay for it (design note, deviation D5).</summary>
    public const float RECHARGE_PER_SECOND = 4f;

    /// <summary>How far past the range the picture goes black, as a fraction of the range.</summary>
    public const float BLACK_SPAN = 0.1f;

    /// <summary>The most that the black fade covers the picture (the game's own cap).</summary>
    public const float MAX_BLACK = 0.99f;

    /// <summary>The drone needs at least this much room in the denominator of the thrust formula (a drag of 50 per second at 50 steps per second would divide by zero).</summary>
    private const float MIN_DAMPING = 0.05f;

    /// <summary>
    /// The thrust (m/s squared, added with the acceleration force mode every physics step) that makes the drone's top speed <paramref name="speedFactor" /> times the Seamoth's. A body that is pushed by an
    /// acceleration a every step and slowed by the rigidbody's drag d settles where the push of a step equals what the drag takes: v = a (1 - d dt) / d, so a = v d / (1 - d dt).
    /// </summary>
    public static float ThrustAcceleration(float speedFactor, float drag, float fixedDeltaTime)
    {
        if (!IsNumber(speedFactor) || !IsNumber(drag) || !IsNumber(fixedDeltaTime) || speedFactor <= 0f || drag <= 0f || fixedDeltaTime <= 0f)
        {
            return VANILLA_THRUST;
        }
        float damping = Math.Max(MIN_DAMPING, 1f - drag * fixedDeltaTime);
        return speedFactor * SEAMOTH_TOP_SPEED * drag / damping;
    }

    /// <summary>
    /// How much static covers the picture (0 to 1): none up to <paramref name="staticStart" /> of the range, then rising evenly to full at the range. The game's own effect takes this number as it is (it
    /// has its distance and 250 m hard coded; the mod puts its own in after the game's).
    /// </summary>
    public static float Noise(float distance, float range, float staticStart)
    {
        if (!IsNumber(distance) || !IsNumber(range) || range <= 0f)
        {
            return 0f;
        }
        float start = range * Math.Min(0.99f, Math.Max(0f, IsNumber(staticStart) ? staticStart : 0f));
        float span = Math.Max(range - start, 1f);
        return Clamp01((distance - start) / span);
    }

    /// <summary>How black the picture is (0 to <see cref="MAX_BLACK" />): nothing up to the range, then fading to black over <see cref="BLACK_SPAN" /> of the range.</summary>
    public static float Fader(float distance, float range)
    {
        if (!IsNumber(distance) || !IsNumber(range) || range <= 0f)
        {
            return 0f;
        }
        float span = Math.Max(range * BLACK_SPAN, 1f);
        return Math.Min(MAX_BLACK, Math.Max(0f, (distance - range) / span));
    }

    /// <summary>Whether the drone is past its range (the signal is lost: the controls only let it come back).</summary>
    public static bool IsPastRange(float distance, float range)
    {
        return IsNumber(distance) && IsNumber(range) && range > 0f && distance > range;
    }

    /// <summary>
    /// The leash: the direction the operator wants to go (world space, at most unit length) with the part that points away from home taken out once the drone is past its range. Home is the unit vector
    /// (<paramref name="ux" />, <paramref name="uy" />, <paramref name="uz" />) from the dock to the drone. Inside the range nothing changes. A direction that leads back or sideways is kept whole.
    /// </summary>
    public static (float X, float Y, float Z) Leash(float distance, float range, float ux, float uy, float uz, float wx, float wy, float wz)
    {
        if (!IsPastRange(distance, range))
        {
            return (wx, wy, wz);
        }
        float outward = wx * ux + wy * uy + wz * uz;
        return outward <= 0f ? (wx, wy, wz) : (wx - outward * ux, wy - outward * uy, wz - outward * uz);
    }

    /// <summary>The battery the drone uses in a step (the game's rule, kept): nothing below <see cref="DRAIN_SPEED_THRESHOLD" />.</summary>
    public static float Drain(float speed, float deltaTime)
    {
        return IsNumber(speed) && IsNumber(deltaTime) && speed > DRAIN_SPEED_THRESHOLD && deltaTime > 0f ? deltaTime * DRAIN_PER_SECOND : 0f;
    }

    /// <summary>The energy the bay puts into a docked drone in a step: <see cref="RECHARGE_PER_SECOND" /> times the step, never more than what is missing.</summary>
    public static float Recharge(float charge, float capacity, float deltaTime)
    {
        if (!IsNumber(charge) || !IsNumber(capacity) || !IsNumber(deltaTime) || deltaTime <= 0f)
        {
            return 0f;
        }
        return Math.Max(0f, Math.Min(capacity - charge, RECHARGE_PER_SECOND * deltaTime));
    }

    /// <summary>
    /// A creature's mouth can bite the drone: the attack is one that bites vehicles, and the drone is out of its dock and alive (a drone in its dock is part of the hull; a dead one is a wreck). The game's list of
    /// what a mouth can bite has no camera drone, so the mod counts the drone as a vehicle by this rule.
    /// </summary>
    public static bool CanBeBitten(bool attackBitesVehicles, bool docked, bool alive)
    {
        return attackBitesVehicles && !docked && alive;
    }

    /// <summary>Hostile creatures hunt the drone (its eco target is on) while it is out of its dock, the way they hunt a Seamoth that is not docked.</summary>
    public static bool IsHunted(bool docked)
    {
        return !docked;
    }

    /// <summary>How far from the drone's last known place (m) the server accepts the centre of a zap: the drone flies at about 22 m/s and its place reaches the server a few times a second.</summary>
    public const float ZAP_POSITION_TOLERANCE = 80f;

    /// <summary>
    /// The shortest time (ms) the server lets pass between two zaps of a Cyclops: 80 percent of the cooldown, so that a client that keeps no cooldown of its own cannot zap as often as it likes, and one that keeps
    /// the exact cooldown is not turned down by the network's jitter.
    /// </summary>
    public static long ZapServerIntervalMilliseconds(float cooldownSeconds)
    {
        return IsNumber(cooldownSeconds) && cooldownSeconds > 0f ? (long)(cooldownSeconds * 800f) : 0L;
    }

    /// <summary>Whether the place named for a zap is where the drone is (within <see cref="ZAP_POSITION_TOLERANCE" />). A place that is not a number is not.</summary>
    public static bool IsNearDrone(float dx, float dy, float dz)
    {
        if (!IsNumber(dx) || !IsNumber(dy) || !IsNumber(dz))
        {
            return false;
        }
        return dx * dx + dy * dy + dz * dz <= ZAP_POSITION_TOLERANCE * ZAP_POSITION_TOLERANCE;
    }

    /// <summary>Whether the drone has the energy for a zap (the game's rule for the controls: a mode that needs no power needs none).</summary>
    public static bool HasZapEnergy(float charge, float cost, bool powerRequired)
    {
        return !powerRequired || (IsNumber(charge) && IsNumber(cost) && charge >= cost);
    }

    private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));

    private static bool IsNumber(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
