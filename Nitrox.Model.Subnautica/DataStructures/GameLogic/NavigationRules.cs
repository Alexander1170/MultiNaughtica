using System;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>Why a Cyclops is off course (the verdict keeps both, each with its own hysteresis).</summary>
[Flags]
public enum OffCourseReason : byte
{
    None = 0,

    /// <summary>Too far from the line to the next waypoint.</summary>
    Far = 1,

    /// <summary>Moving, and not towards the next waypoint.</summary>
    WrongWay = 2
}

/// <summary>
/// The arithmetic of the route that a navigator sets for a Cyclops (docs/coop/VEHICLE_UPGRADES.md, station 1): which waypoints can be added, when the Cyclops has reached one, how far it is off the line between two
/// of them, which way the pilot has to turn. Shared by the server, which validates the route and the advance, and the client, which guides the pilot. Plain floats, no Unity types, NaN-safe, so that it is tested.
/// </summary>
/// <remarks>
/// Directions are written in the Cyclops' own frame as the guidance sees it: x to starboard, y up, z ahead. The hull's own axes are the other way round (its bow is at -z), which the client turns around when it
/// builds the numbers; nothing here knows about that.
/// </remarks>
public static class NavigationRules
{
    /// <summary>The most waypoints a route has.</summary>
    public const int MAX_WAYPOINTS = 16;

    /// <summary>A waypoint closer than this to the last one adds nothing (a double click).</summary>
    public const float MIN_WAYPOINT_SPACING = 5f;

    /// <summary>A waypoint is reached when the hull is this close to it, in metres to the hull's box, not to its middle (the Cyclops is 54 m long).</summary>
    public const float REACHED_DISTANCE = 12f;

    /// <summary>The Cyclops is off course when it is this far from the line to the next waypoint...</summary>
    public const float OFF_COURSE_METRES = 35f;

    /// <summary>... or when it moves and its way is this far from the way to the next waypoint.</summary>
    public const float OFF_COURSE_DEGREES = 70f;

    /// <summary>Leaving the "off course" state needs the numbers to fall below this fraction of the limits, so that it does not flicker.</summary>
    public const float HYSTERESIS = 0.7f;

    /// <summary>Slower than this (m/s) the Cyclops has no heading worth comparing.</summary>
    public const float MIN_HEADING_SPEED = 1f;

    public static bool IsValidWaypoint(float x, float y, float z) => CyclopsNavRules.IsValidPosition(x, y, z);

    /// <summary>A waypoint can be appended: the route has room, and it is not on top of the last one.</summary>
    public static bool CanAppend(int count, bool hasLast, float lastX, float lastY, float lastZ, float x, float y, float z)
    {
        if (count < 0 || count >= MAX_WAYPOINTS || !IsValidWaypoint(x, y, z))
        {
            return false;
        }
        return !hasLast || Distance(lastX, lastY, lastZ, x, y, z) >= MIN_WAYPOINT_SPACING;
    }

    /// <summary>The server's check of an advance: forward only, by one, and not beyond the end of the route ("count" means finished).</summary>
    public static bool IsValidAdvance(int count, int current, int proposed)
    {
        return count > 0 && current >= 0 && current < count && proposed == current + 1 && proposed <= count;
    }

    /// <summary>The index of the next waypoint after the waypoint at <paramref name="removedIndex" /> was taken out of a route whose next waypoint was <paramref name="next" />.</summary>
    public static int NextAfterRemoval(int next, int removedIndex, int countAfter)
    {
        int result = removedIndex < next ? next - 1 : next;
        return Math.Max(0, Math.Min(result, countAfter));
    }

    public static bool IsFinished(int count, int next) => count > 0 && next >= count;

    public static float Distance(float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float dz = bz - az;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// The point on the leg A to B that is nearest to P: how far along it (T, 0 at A, 1 at B, held to that), where, and how far P is from it. A leg of no length is the point A.
    /// </summary>
    public static (float T, float X, float Y, float Z, float Distance) NearestOnSegment(float px, float py, float pz, float ax, float ay, float az, float bx, float by, float bz)
    {
        float abx = bx - ax;
        float aby = by - ay;
        float abz = bz - az;
        float lengthSquared = abx * abx + aby * aby + abz * abz;
        float t = 0f;
        if (lengthSquared > 0.000001f)
        {
            t = ((px - ax) * abx + (py - ay) * aby + (pz - az) * abz) / lengthSquared;
            t = Math.Max(0f, Math.Min(1f, t));
        }
        float x = ax + abx * t;
        float y = ay + aby * t;
        float z = az + abz * t;
        return (t, x, y, z, Distance(px, py, pz, x, y, z));
    }

    /// <summary>How far P is from the leg A to B (the off-course distance).</summary>
    public static float CrossTrack(float px, float py, float pz, float ax, float ay, float az, float bx, float by, float bz)
    {
        return NearestOnSegment(px, py, pz, ax, ay, az, bx, by, bz).Distance;
    }

    /// <summary>The waypoint is reached: the hull is within <see cref="REACHED_DISTANCE" /> of it. A distance that is not a number is not reached.</summary>
    public static bool HasReached(float distanceToHull) => distanceToHull >= 0f && distanceToHull <= REACHED_DISTANCE;

    /// <summary>
    /// The Cyclops has gone past the waypoint without coming within reach (it slid by in a current, or the leg was cut): it has covered the leg and the hull is not far from the point either. Such a waypoint
    /// still advances the route, so that the guide does not turn the pilot around for a point behind.
    /// </summary>
    public static bool HasPassed(float alongTrack, float legLength, float distanceToHull)
    {
        return legLength > 0f && alongTrack >= legLength && distanceToHull >= 0f && distanceToHull <= REACHED_DISTANCE * 3f;
    }

    /// <summary>Degrees wrapped into (-180, 180].</summary>
    public static float WrapDegrees(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees))
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

    /// <summary>
    /// The turn from a heading (degrees, 0 along +z, growing towards +x) to the direction (dx, dz) on the horizontal plane: positive to starboard, negative to port. The Cyclops' own frame, as described at the top.
    /// </summary>
    public static float RelativeBearing(float headingDegrees, float dx, float dz)
    {
        if (Math.Abs(dx) < 0.0001f && Math.Abs(dz) < 0.0001f)
        {
            return 0f;
        }
        float target = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
        return WrapDegrees(target - headingDegrees);
    }

    /// <summary>The direction of a target in the Cyclops' frame (x starboard, y up, z ahead) as the angles of an arrow: the yaw (positive to starboard) and the pitch (positive up), in degrees.</summary>
    public static (float Yaw, float Pitch) ArrowAngles(float localX, float localY, float localZ)
    {
        float horizontal = (float)Math.Sqrt(localX * localX + localZ * localZ);
        float yaw = (float)(Math.Atan2(localX, localZ) * 180.0 / Math.PI);
        float pitch = (float)(Math.Atan2(localY, horizontal) * 180.0 / Math.PI);
        return (float.IsNaN(yaw) ? 0f : yaw, float.IsNaN(pitch) ? 0f : pitch);
    }

    /// <summary>
    /// Whether the Cyclops is off course: too far from the line to the next waypoint (<paramref name="crossTrack" />, NaN when there is no line yet, the first leg), or moving the wrong way
    /// (<paramref name="bearingErrorDegrees" /> between its way and the way to the waypoint, counted only above <see cref="MIN_HEADING_SPEED" />). Once off course the numbers have to fall below
    /// <see cref="HYSTERESIS" /> of the limits to be on course again.
    /// </summary>
    public static bool IsOffCourse(bool wasOff, float crossTrack, float bearingErrorDegrees, float speed)
    {
        return OffCourse(wasOff ? OffCourseReason.Far | OffCourseReason.WrongWay : OffCourseReason.None, crossTrack, bearingErrorDegrees, speed) != OffCourseReason.None;
    }

    /// <summary>
    /// The same verdict with the reason kept: each limit has its own hysteresis, and a heading that cannot be judged (the Cyclops hardly moves) keeps the verdict that was given before, so that stopping the Cyclops
    /// does not clear "wrong way" and starting it again does not bring it back.
    /// </summary>
    public static OffCourseReason OffCourse(OffCourseReason previous, float crossTrack, float bearingErrorDegrees, float speed)
    {
        float farFactor = (previous & OffCourseReason.Far) != 0 ? HYSTERESIS : 1f;
        float wayFactor = (previous & OffCourseReason.WrongWay) != 0 ? HYSTERESIS : 1f;
        bool far = !float.IsNaN(crossTrack) && crossTrack > OFF_COURSE_METRES * farFactor;
        bool headingKnown = !float.IsNaN(bearingErrorDegrees) && speed >= MIN_HEADING_SPEED;
        bool wrongWay = headingKnown ? Math.Abs(bearingErrorDegrees) > OFF_COURSE_DEGREES * wayFactor : (previous & OffCourseReason.WrongWay) != 0;
        return (far ? OffCourseReason.Far : OffCourseReason.None) | (wrongWay ? OffCourseReason.WrongWay : OffCourseReason.None);
    }

    /// <summary>Seconds to a place at this distance at this speed; NaN when the Cyclops is too slow for the number to mean anything.</summary>
    public static float EtaSeconds(float distance, float speed)
    {
        return speed >= MIN_HEADING_SPEED && distance >= 0f ? distance / speed : float.NaN;
    }

    /// <summary>The width of a line in the water at this distance from the camera: a cable's 7 cm is under a pixel at 100 m, so it grows with the distance, within limits.</summary>
    public static float LineWidth(float cameraDistance)
    {
        if (float.IsNaN(cameraDistance) || cameraDistance < 0f)
        {
            return 0.12f;
        }
        return Math.Max(0.12f, Math.Min(2f, cameraDistance * 0.004f));
    }
}
