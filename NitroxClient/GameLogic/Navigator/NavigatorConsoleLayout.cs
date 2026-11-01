using System;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// Where the navigator's hologram is in the Cyclops, in the hull's own frame (x to the pilot's right is negative, y up, z from the bow back is positive: the pilot at z -24.9 faces the bow at -z), and how big it
/// may be. The numbers are read from the game's own files of the Cyclops (the "cyclops" scene bundle, its collision boxes, read offline), so they hold for every Cyclops of this game version. Pure numbers (tuples),
/// so that the placement is tested.
/// </summary>
/// <remarks>
/// <para>
/// <b>The build room</b> (the game's <c>CyclopsRooms.BuildRoom</c>, "Build room" on the engineer's map) is the room between the helm's bulkhead and the vehicle bay's, empty and of no use before the navigator moved
/// in. Its collision boxes (<c>secondRoomGroup</c>): the side walls' inner faces at x -2.88 and 2.59, the floor's top at y -0.79, the ceiling's underside at y 2.20 (<c>zOuterGroup/bottom_deck</c>), and the two
/// bulkheads' faces at z -15.41 (the helm's, a doorway at x -1.04 to 0.95, 2.25 m high) and z -11.29 (the vehicle bay's, a doorway at x -0.83 to 0.87, 2.14 m high). Walking from the helm to the vehicle bay goes
/// along x 0 through the middle of the room, and the hologram is there: it has no collider, so nothing blocks (only a trigger for the hand, on the hologram's own volume), and the player walks through it.
/// </para>
/// <para>
/// <b>The hologram</b> floats in the middle of the room, a disc of terrain tilted towards whoever looks at it (<see cref="FrameYawToward" />) with the Cyclops at its middle. Its size is the player's to choose
/// (<see cref="ClampRadius" />, <see cref="StepRadius" />), up to nearly the room's half-length. The game fades the terrain by its distance in all three directions from the middle (a sphere, not a flat disc), so
/// the volume of the hologram is a sphere of the disc's radius: it hangs at waist height while it is small, and rises as it grows (<see cref="CenterY" />) until the sphere fills the room's height (a radius of
/// <see cref="FullHeightRadius" />, 2.9 m across). A bigger one is cut by the floor and the ceiling, which hide what is beyond them like any other wall, and never reaches a wall or a bulkhead (<see cref="MAX_RADIUS" />
/// is less than the room's half-length).
/// </para>
/// </remarks>
public static class NavigatorConsoleLayout
{
    /// <summary>The inside of the build room: the faces of its walls, floor, ceiling and bulkheads.</summary>
    public const float ROOM_MIN_X = -2.88f;

    public const float ROOM_MAX_X = 2.59f;
    public const float ROOM_MIN_Y = -0.79f;
    public const float ROOM_MAX_Y = 2.20f;
    public const float ROOM_MIN_Z = -15.41f;
    public const float ROOM_MAX_Z = -11.29f;

    /// <summary>The hologram's middle is this high above the floor: waist height, so that a standing player looks down on it and their eyes (about 1.65 m up) stay above it.</summary>
    public const float HOLOGRAM_HEIGHT_ABOVE_FLOOR = 1.0f;

    /// <summary>The middle of the hologram: the middle of the room, at waist height. One constant to move it (the walking line from the helm to the vehicle bay is x 0).</summary>
    public static readonly (float X, float Y, float Z) HologramCenter = ((ROOM_MIN_X + ROOM_MAX_X) / 2f, ROOM_MIN_Y + HOLOGRAM_HEIGHT_ABOVE_FLOOR, (ROOM_MIN_Z + ROOM_MAX_Z) / 2f);

    /// <summary>The radius in metres of the disc of terrain that the hologram is built with, and the size a player starts with (2.8 m across, about two thirds of the room's length).</summary>
    public const float DEFAULT_RADIUS = 1.4f;

    /// <summary>The smallest size a player can choose (30 cm across) and the biggest (3.9 m across, a little less than the 4.12 m between the bulkheads), see <see cref="MaxRadiusInRoom" />.</summary>
    public const float MIN_RADIUS = 0.15f;

    public const float MAX_RADIUS = 1.95f;

    /// <summary>The gap that the hologram keeps from the walls and the bulkheads, and from the floor and the ceiling while its volume fits between them.</summary>
    public const float ROOM_MARGIN = 0.05f;

    /// <summary>The biggest radius whose sphere fits between the floor and the ceiling (half the room's height, less the margin): up to it nothing of the hologram is cut by either.</summary>
    public const float FullHeightRadius = (ROOM_MAX_Y - ROOM_MIN_Y) / 2f - ROOM_MARGIN;

    /// <summary>Each notch of the size control (a wheel notch with Shift, a +/- key press, a button click) changes the radius by this factor.</summary>
    public const float RADIUS_NOTCH = 1.1f;

    /// <summary>
    /// The trigger for the player's hand is a slab on the disc: as wide as the disc (not under <see cref="MIN_TARGET_DIAMETER" />, so that a very small hologram can be aimed at) and <see cref="TARGET_THICKNESS" />
    /// thick. Not a sphere: a big hologram would have the player's eyes inside it, and a ray that starts inside a collider does not find it.
    /// </summary>
    public const float MIN_TARGET_DIAMETER = 0.6f;

    public const float TARGET_THICKNESS = 0.5f;

    /// <summary>The map is shown while the local player is within this many metres of it (the build room and its neighbours), and kept until they are farther than <see cref="MAP_HIDE_DISTANCE" />.</summary>
    public const float MAP_SHOW_DISTANCE = 9f;

    public const float MAP_HIDE_DISTANCE = 12f;

    /// <summary>The console closes when the player is farther than this from the hologram's rim (they were moved away).</summary>
    public const float MAX_USE_DISTANCE = 4.5f;

    /// <summary>
    /// The biggest radius that keeps a sphere of that radius, centred on <paramref name="center" />, inside the build room by <paramref name="margin" />: the smallest distance to the six faces, less the margin.
    /// </summary>
    public static float MaxRadiusInRoom((float X, float Y, float Z) center, float margin)
    {
        float nearest = Math.Min(
            Math.Min(Math.Min(center.X - ROOM_MIN_X, ROOM_MAX_X - center.X), Math.Min(center.Y - ROOM_MIN_Y, ROOM_MAX_Y - center.Y)),
            Math.Min(center.Z - ROOM_MIN_Z, ROOM_MAX_Z - center.Z));
        return nearest - margin;
    }

    /// <summary>
    /// The height of the hologram's middle for a size: waist height while its sphere fits below the ceiling, higher as it grows (its lowest point stays above the floor by the margin) until it is as tall as the
    /// room, and from there at the middle of the room's height, so that the floor and the ceiling cut an equal share of it.
    /// </summary>
    public static float CenterY(float radius)
    {
        float r = ClampRadius(radius);
        float waist = ROOM_MIN_Y + HOLOGRAM_HEIGHT_ABOVE_FLOOR;
        float lowest = ROOM_MIN_Y + ROOM_MARGIN + r;
        float highest = ROOM_MAX_Y - ROOM_MARGIN - r;
        if (lowest > highest)
        {
            return (ROOM_MIN_Y + ROOM_MAX_Y) / 2f;
        }
        return Math.Max(lowest, Math.Min(highest, waist));
    }

    /// <summary>The size in metres, along the disc and across it, of the slab that the hand finds, for a hologram of this radius.</summary>
    public static (float Width, float Thickness) TargetSize(float radius)
    {
        return (Math.Max(2f * ClampRadius(radius), MIN_TARGET_DIAMETER), TARGET_THICKNESS);
    }

    /// <summary>A size kept between the smallest and the biggest; not a number gives the default.</summary>
    public static float ClampRadius(float radius)
    {
        if (float.IsNaN(radius) || float.IsInfinity(radius))
        {
            return DEFAULT_RADIUS;
        }
        return Math.Max(MIN_RADIUS, Math.Min(MAX_RADIUS, radius));
    }

    /// <summary>The size after <paramref name="notches" /> notches of the size control (positive: bigger), kept between the limits.</summary>
    public static float StepRadius(float radius, float notches)
    {
        if (float.IsNaN(notches) || float.IsInfinity(notches))
        {
            return ClampRadius(radius);
        }
        return ClampRadius(ClampRadius(radius) * (float)Math.Pow(RADIUS_NOTCH, notches));
    }

    /// <summary>How far the player may be from the hologram's middle with the console open: <see cref="MAX_USE_DISTANCE" /> from its rim.</summary>
    public static float MaxUseDistance(float radius)
    {
        return MAX_USE_DISTANCE + ClampRadius(radius);
    }

    /// <summary>
    /// The turn about the vertical, in degrees as Unity counts them (a turn of the hull's +x towards -z is positive), that makes the hologram's frame tilt towards the viewer: the frame is tilted about its own z axis
    /// towards its +x, so it is turned until its +x points at the viewer. When the viewer is straight above or below it (or not a number) the <paramref name="previous" /> turn is kept.
    /// </summary>
    public static float FrameYawToward((float X, float Y, float Z) viewer, (float X, float Y, float Z) anchor, float previous)
    {
        if (!IsNumber(viewer) || !IsNumber(anchor))
        {
            return previous;
        }
        float dx = viewer.X - anchor.X;
        float dz = viewer.Z - anchor.Z;
        if (dx * dx + dz * dz < 0.04f * 0.04f)
        {
            return previous;
        }
        return (float)(Math.Atan2(-dz, dx) * 180.0 / Math.PI);
    }

    /// <summary>
    /// The turn that makes a camera at <paramref name="from" /> look at <paramref name="to" />, both in the same space: the yaw (degrees about the vertical, 0 along +z and 90 along +x, as Unity counts it) and
    /// the pitch (degrees, positive looking up). NaN or the same point gives 0, 0.
    /// </summary>
    public static (float Yaw, float Pitch) AimAngles((float X, float Y, float Z) from, (float X, float Y, float Z) to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dz = to.Z - from.Z;
        double length = Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);
        if (!IsNumber(from) || !IsNumber(to) || length < 1e-6)
        {
            return (0f, 0f);
        }
        float yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
        float pitch = (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, dy / length))) * 180.0 / Math.PI);
        return (yaw, pitch);
    }

    /// <summary>Whether the map is shown, given how far the local player is from it and whether it is shown now (the hysteresis keeps it from flickering at the edge).</summary>
    public static bool ShouldShowMap(float distance, bool shownNow)
    {
        if (float.IsNaN(distance))
        {
            return false;
        }
        return shownNow ? distance <= MAP_HIDE_DISTANCE : distance <= MAP_SHOW_DISTANCE;
    }

    private static bool IsNumber((float X, float Y, float Z) value)
    {
        return !float.IsNaN(value.X) && !float.IsInfinity(value.X) && !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) && !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);
    }
}
