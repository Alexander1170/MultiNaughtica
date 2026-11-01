using System;

namespace NitroxClient.GameLogic.Gunner;

/// <summary>
/// The arithmetic of the Cyclops gunner station (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 2): how the turret turns and where it may point, how a shot is charged and paid for, how long to wait
/// for the next one and how a range becomes the flight of a stasis sphere. Plain floats and tuples, no Unity types, NaN-safe (a number that is not a number counts as 0 or as "no"), so that it is tested.
/// </summary>
/// <remarks>
/// Directions are written in the hull's own frame, as the cockpit numbers are: the bow is at -z (the hull's own forward points to the stern), the pilot's right (starboard) is -x, up is +y. The turret's
/// yaw is counted from the bow, positive to starboard; its pitch is positive up.
/// </remarks>
public static class GunnerRules
{
    /// <summary>Seconds of holding the button for a full charge (the Stasis Rifle's <c>chargeDuration</c>).</summary>
    public const float CHARGE_SECONDS = 3f;

    /// <summary>A release with less charge than this is a click, not a shot (nothing is paid, nothing flies).</summary>
    public const float MIN_CHARGE = 0.1f;

    /// <summary>The speed of the rifle's sphere; a shorter range is flown at this speed for a shorter time.</summary>
    public const float MIN_SPHERE_SPEED = 25f;

    /// <summary>The fastest sphere of the gunner (a long range is flown a little faster and a little longer than the rifle's).</summary>
    public const float MAX_SPHERE_SPEED = 60f;

    /// <summary>Metres in front of the turret's pivot where the sphere starts: clear of the antennas and of the hull.</summary>
    public const float MUZZLE_OFFSET = 1.2f;

    /// <summary>How far up the turret can point.</summary>
    public const float MAX_PITCH = 75f;

    /// <summary>Degrees of turret per unit of mouse movement (slower than looking around freely).</summary>
    public const float DEGREES_PER_UNIT = 0.6f;

    /// <summary>The most mouse movement taken from one frame: the cursor being locked or a hitch must not spin the turret.</summary>
    public const float MAX_FRAME_DELTA = 120f;

    /// <summary>Seconds that the gunner waits for the server to answer a seat or a shot before it gives up (the server waits less for a forwarded shot).</summary>
    public const float REQUEST_TIMEOUT_SECONDS = 3f;

    /// <summary>The shortest range that <see cref="SphereFlight" /> works with (the setting never goes below 20).</summary>
    public const float MIN_RANGE_METRES = 1f;

    // How far down the turret can point, by the angle from the bow: the hull's silhouette as seen from the mount (the vanilla "conning tower" camera anchor, 9.93 m above the keel line), less the sphere's
    // shell. Read from the skin points of the Cyclops' scene (docs/coop research for station 2); symmetric, linear in between.
    private static readonly (float Yaw, float Pitch)[] MinPitchNodes =
    [
        (0f, -14f), (15f, -26f), (30f, -35f), (45f, -50f), (60f, -56f), (75f, -58f), (120f, -58f), (135f, -58f), (150f, -44f), (165f, -30f), (180f, -13f)
    ];

    // ---- the turret

    /// <summary>The yaw wrapped into (-180, 180]; a number that is not a number gives 0.</summary>
    public static float WrapYaw(float degrees)
    {
        if (!IsNumber(degrees))
        {
            return 0f;
        }
        float wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped <= -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    /// <summary>The lowest pitch (a negative number: down) that the hull allows at this yaw.</summary>
    public static float MinPitch(float yaw)
    {
        float angle = Math.Abs(WrapYaw(yaw));
        for (int i = 1; i < MinPitchNodes.Length; i++)
        {
            (float yawB, float pitchB) = MinPitchNodes[i];
            if (angle <= yawB)
            {
                (float yawA, float pitchA) = MinPitchNodes[i - 1];
                float t = yawB > yawA ? (angle - yawA) / (yawB - yawA) : 1f;
                return pitchA + (pitchB - pitchA) * t;
            }
        }
        return MinPitchNodes[MinPitchNodes.Length - 1].Pitch;
    }

    /// <summary>Degrees between the sectors of a scanned horizon (<see cref="SCAN_SECTORS" /> of them round the turret, the first centred on -180).</summary>
    public const float SCAN_STEP_DEGREES = 5f;

    public const int SCAN_SECTORS = 72;

    /// <summary>
    /// The lowest pitch at this yaw when the hull has been scanned as well (<paramref name="scan" /> holds the lowest clear pitch of each sector, centred on -180, -175 ... 175 degrees from the bow, NaN for a sector
    /// not scanned yet): the higher of the table's and the scan's, linear between the two nearest sectors. Without a scan, the table's.
    /// </summary>
    public static float MinPitch(float yaw, float[] scan)
    {
        float table = MinPitch(yaw);
        if (scan == null || scan.Length != SCAN_SECTORS)
        {
            return table;
        }
        float position = (WrapYaw(yaw) + 180f) / SCAN_STEP_DEGREES;
        int low = (int)Math.Floor(position) % SCAN_SECTORS;
        int high = (low + 1) % SCAN_SECTORS;
        float t = position - (float)Math.Floor(position);
        float a = scan[low];
        float b = scan[high];
        if (!IsNumber(a) && !IsNumber(b))
        {
            return table;
        }
        float scanned = !IsNumber(a) ? b : !IsNumber(b) ? a : a + (b - a) * t;
        return Math.Max(table, scanned);
    }

    /// <summary>The pitch kept between what the hull allows at this yaw and <see cref="MAX_PITCH" />; a number that is not a number gives level.</summary>
    public static float ClampPitch(float yaw, float pitch)
    {
        return ClampPitch(yaw, pitch, null);
    }

    /// <summary><see cref="ClampPitch(float, float)" /> with the horizon that was scanned (<see cref="MinPitch(float, float[])" />).</summary>
    public static float ClampPitch(float yaw, float pitch, float[] scan)
    {
        if (!IsNumber(pitch))
        {
            return 0f;
        }
        return Math.Max(MinPitch(yaw, scan), Math.Min(MAX_PITCH, pitch));
    }

    /// <summary>
    /// The turret after the mouse moved by (<paramref name="dx" />, <paramref name="dy" />) units: right turns it to starboard, up raises it. One frame's movement is capped; a movement that is not a number is
    /// ignored; the pitch is kept inside what the hull allows at the new yaw.
    /// </summary>
    public static (float Yaw, float Pitch) Step(float yaw, float pitch, float dx, float dy)
    {
        return Step(yaw, pitch, dx, dy, null);
    }

    /// <summary><see cref="Step(float, float, float, float)" /> inside the hull's scanned horizon as well (<see cref="MinPitch(float, float[])" />).</summary>
    public static (float Yaw, float Pitch) Step(float yaw, float pitch, float dx, float dy, float[] scan)
    {
        float turnX = IsNumber(dx) ? Math.Max(-MAX_FRAME_DELTA, Math.Min(MAX_FRAME_DELTA, dx)) : 0f;
        float turnY = IsNumber(dy) ? Math.Max(-MAX_FRAME_DELTA, Math.Min(MAX_FRAME_DELTA, dy)) : 0f;
        float newYaw = WrapYaw((IsNumber(yaw) ? yaw : 0f) + turnX * DEGREES_PER_UNIT);
        float newPitch = ClampPitch(newYaw, (IsNumber(pitch) ? pitch : 0f) + turnY * DEGREES_PER_UNIT, scan);
        return (newYaw, newPitch);
    }

    /// <summary>The direction the turret points in, in the hull's frame: yaw 0 pitch 0 is the bow (0, 0, -1), yaw 90 is starboard (-1, 0, 0), pitch 90 is up (0, 1, 0).</summary>
    public static (float X, float Y, float Z) Direction(float yaw, float pitch)
    {
        double y = (IsNumber(yaw) ? yaw : 0f) * Math.PI / 180.0;
        double p = (IsNumber(pitch) ? pitch : 0f) * Math.PI / 180.0;
        double flat = Math.Cos(p);
        return ((float)(-Math.Sin(y) * flat), (float)Math.Sin(p), (float)(-Math.Cos(y) * flat));
    }

    /// <summary>Where a sphere starts: the pivot moved along the aim by <see cref="MUZZLE_OFFSET" />.</summary>
    public static (float X, float Y, float Z) Muzzle((float X, float Y, float Z) pivot, (float X, float Y, float Z) direction)
    {
        return (pivot.X + direction.X * MUZZLE_OFFSET, pivot.Y + direction.Y * MUZZLE_OFFSET, pivot.Z + direction.Z * MUZZLE_OFFSET);
    }

    // ---- the shot

    /// <summary>The charge (0 to 1) after the button was held for <paramref name="seconds" /> more; full after <see cref="CHARGE_SECONDS" />.</summary>
    public static float ChargeAfter(float charge, float seconds)
    {
        float start = IsNumber(charge) ? Math.Max(0f, Math.Min(1f, charge)) : 0f;
        float held = IsNumber(seconds) ? Math.Max(0f, seconds) : 0f;
        return Math.Min(1f, start + held / CHARGE_SECONDS);
    }

    /// <summary>Whether a release at this charge is a shot (and not a click).</summary>
    public static bool IsShot(float charge)
    {
        return IsNumber(charge) && charge >= MIN_CHARGE;
    }

    /// <summary>The energy that a shot at this charge takes from the Cyclops: the energy of a full charge times the charge. Nothing for a click.</summary>
    public static float ShotEnergy(float charge, float fullChargeEnergy)
    {
        if (!IsShot(charge) || !IsNumber(fullChargeEnergy) || fullChargeEnergy <= 0f)
        {
            return 0f;
        }
        return fullChargeEnergy * Math.Min(1f, charge);
    }

    /// <summary>Seconds still to wait for the next shot, counted from the time of the last one; 0 when the wait is over (or there was no last shot).</summary>
    public static float CooldownRemaining(float lastShotAt, float now, float cooldownSeconds)
    {
        if (!IsNumber(lastShotAt) || !IsNumber(now) || !IsNumber(cooldownSeconds) || cooldownSeconds <= 0f)
        {
            return 0f;
        }
        return Math.Max(0f, cooldownSeconds - (now - lastShotAt));
    }

    /// <summary>
    /// The flight of a stasis sphere that reaches <paramref name="range" /> metres: the sphere slows down on the way, linearly to a stop, so it flies speed x time / 2. The speed is 2/3 of the range per
    /// second (never below the rifle's 25 m/s, never above <see cref="MAX_SPHERE_SPEED" />) and the time is what is needed for the reach to be the range. 37.5 m is the rifle's own: 25 m/s for 3 s.
    /// </summary>
    public static (float Speed, float LifeTime) SphereFlight(float range)
    {
        float reach = IsNumber(range) ? Math.Max(MIN_RANGE_METRES, range) : MIN_RANGE_METRES;
        float speed = Math.Max(MIN_SPHERE_SPEED, Math.Min(MAX_SPHERE_SPEED, 2f * reach / 3f));
        return (speed, 2f * reach / speed);
    }

    /// <summary>How far a sphere that is shot at this speed for this time flies before it stops.</summary>
    public static float Reach(float speed, float lifeTime)
    {
        return IsNumber(speed) && IsNumber(lifeTime) && speed > 0f && lifeTime > 0f ? speed * lifeTime / 2f : 0f;
    }

    private static bool IsNumber(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
