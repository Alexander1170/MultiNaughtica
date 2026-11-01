using System;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The numbers of the third-person camera that shows a player his own emote (docs/coop/PLAYER_MODELS.md, "The emote camera"), as plain floats so that they can be tested: where the orbit starts, how far the camera
/// may go, which way it looks, how it is pulled in at a wall (and eased out again), and how it is kept on the player's side of the water surface. Angles are degrees: yaw 0 looks along +Z and turns towards +X,
/// pitch is positive when the camera looks up.
/// </summary>
public static class EmoteCameraRules
{
    /// <summary>How far from the character the camera starts, in metres: a few metres, the way another player sees him.</summary>
    public const float DEFAULT_DISTANCE = 2.8f;

    public const float MIN_DISTANCE = 0.6f;
    public const float MAX_DISTANCE = 5f;

    /// <summary>Metres of distance for one notch of the mouse wheel.</summary>
    public const float ZOOM_STEP = 0.35f;

    /// <summary>The camera starts a little above the character, looking down at him.</summary>
    public const float START_PITCH = -12f;

    public const float MIN_PITCH = -75f;
    public const float MAX_PITCH = 50f;

    /// <summary>The camera starts in front of the character, this many degrees to one side of straight in front, so that the emote is seen from three quarters.</summary>
    public const float START_SIDE_DEGREES = 28f;

    /// <summary>The camera looks at the character's chest: this far above the hips, in metres.</summary>
    public const float PIVOT_ABOVE_HIPS = 0.25f;

    /// <summary>The radius of the ball that is swept from the character to the camera to find a wall, in metres.</summary>
    public const float SPHERE_RADIUS = 0.22f;

    /// <summary>How much further than the wanted distance the sweep looks, so that a wall just behind the camera still counts.</summary>
    public const float WALL_SKIN = 0.1f;

    /// <summary>
    /// The camera is never closer to the character's chest than this, in metres, whatever is in the way (it would look along nothing): a wall right behind him puts the camera inside his chest (the model is not
    /// drawn from inside) and never on the far side of the wall.
    /// </summary>
    public const float FLOOR_DISTANCE = 0.04f;

    /// <summary>The camera slides round the character when the way the mouse points has less room than this (metres, or the wanted distance when that is less): from so close he cannot be seen.</summary>
    public const float TIGHT_DISTANCE = 1.2f;

    /// <summary>Degrees per second at which the camera slides to a side with room, and back to the way the mouse points when that has room again.</summary>
    public const float SLIDE_DEGREES_PER_SECOND = 270f;

    /// <summary>The ball that is swept is made smaller until it does not touch anything at the character (a cast does not see what it starts inside of), but not smaller than this, in metres.</summary>
    public const float MIN_BALL_RADIUS = 0.03f;

    /// <summary>The camera looks for a side that has at least this much room (metres, or the wanted distance when that is less) when the side it is on is tight: he may stand at a console with his face to a wall.</summary>
    public const float COMFORT_DISTANCE = 2f;

    /// <summary>The turns (degrees, from the wanted side, nearest first) that the camera looks at for a side with room: both ways, up to the opposite side.</summary>
    public static readonly float[] START_OFFSETS = [0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f];

    /// <summary>Metres per second at which the camera drifts back out after a wall let go of it (it is pulled in at once).</summary>
    public const float EASE_OUT_PER_SECOND = 4f;

    /// <summary>How far from the water surface the camera is kept on the character's side of it, in metres.</summary>
    public const float WATER_MARGIN = 0.2f;

    /// <summary>The same angle between -180 and 180.</summary>
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
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    public static float ClampPitch(float pitch)
    {
        return float.IsNaN(pitch) ? START_PITCH : Math.Max(MIN_PITCH, Math.Min(MAX_PITCH, pitch));
    }

    public static float ClampDistance(float distance)
    {
        return float.IsNaN(distance) ? DEFAULT_DISTANCE : Math.Max(MIN_DISTANCE, Math.Min(MAX_DISTANCE, distance));
    }

    /// <summary>The yaw of a horizontal direction (x, z): 0 along +Z, 90 along +X.</summary>
    public static float YawOf(float x, float z)
    {
        return Math.Abs(x) < 1e-6f && Math.Abs(z) < 1e-6f ? 0f : (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
    }

    /// <summary>
    /// The yaw that the camera looks along when it starts: the character faces <paramref name="facingYaw" />, the camera is in front of him (looking back at him, so half a turn from the way he faces) and
    /// <see cref="START_SIDE_DEGREES" /> to one side.
    /// </summary>
    public static float StartYaw(float facingYaw)
    {
        return WrapDegrees(facingYaw + 180f - START_SIDE_DEGREES);
    }

    /// <summary>
    /// The yaw that the camera looks along when it starts on the side of a friend: the camera stands on the line from the character towards the friend, so that the player sees himself from where the friend
    /// sees him. The friend is at (<paramref name="friendX" />, <paramref name="friendZ" />), the character at (<paramref name="characterX" />, <paramref name="characterZ" />). False when they are in the same place.
    /// </summary>
    public static bool TryYawFromFriend(float characterX, float characterZ, float friendX, float friendZ, out float yaw)
    {
        float dx = friendX - characterX;
        float dz = friendZ - characterZ;
        if (dx * dx + dz * dz < 0.25f)
        {
            yaw = 0f;
            return false;
        }
        yaw = WrapDegrees(YawOf(dx, dz) + 180f);
        return true;
    }

    /// <summary>The unit vector that the camera looks along (x, y, z).</summary>
    public static (float X, float Y, float Z) Direction(float yaw, float pitch)
    {
        double yawRadians = yaw * Math.PI / 180.0;
        double pitchRadians = pitch * Math.PI / 180.0;
        double flat = Math.Cos(pitchRadians);
        return ((float)(Math.Sin(yawRadians) * flat), (float)Math.Sin(pitchRadians), (float)(Math.Cos(yawRadians) * flat));
    }

    /// <summary>
    /// How far the camera may be from the character once the sweep has been made: the wanted distance when nothing is in the way, else the distance to the wall less a skin, but not less than
    /// <see cref="FLOOR_DISTANCE" /> and not more than wanted.
    /// </summary>
    public static float AllowedDistance(float wanted, bool hitWall, float hitDistance)
    {
        float distance = ClampDistance(wanted);
        if (!hitWall)
        {
            return distance;
        }
        return Math.Max(FLOOR_DISTANCE, Math.Min(distance, hitDistance - WALL_SKIN));
    }

    /// <summary>
    /// The yaw that the camera starts on: <paramref name="idealYaw" /> when there is room for the camera on that side (<paramref name="roomAt" /> says how far the camera may go along a yaw), else the nearest turn of it
    /// (<see cref="START_OFFSETS" />) that has room, else the one with the most. He may stand at a console with his face to a wall: the camera then starts behind him instead of between him and the wall.
    /// <paramref name="room" /> is the room on the yaw that is returned.
    /// </summary>
    public static float PickStartYaw(float idealYaw, Func<float, float> roomAt, float wanted, out float room)
    {
        float enough = Math.Min(ClampDistance(wanted), COMFORT_DISTANCE);
        float bestYaw = WrapDegrees(idealYaw);
        float bestRoom = float.MinValue;
        foreach (float offset in START_OFFSETS)
        {
            float yaw = WrapDegrees(idealYaw + offset);
            float here = roomAt(yaw);
            if (here >= enough)
            {
                room = here;
                return yaw;
            }
            if (here > bestRoom + 0.01f)
            {
                bestRoom = here;
                bestYaw = yaw;
            }
        }
        room = bestRoom;
        return bestYaw;
    }

    /// <summary>
    /// The yaw that the camera looks along this frame: <paramref name="wantedYaw" /> (where the mouse points) when there is at least <see cref="TIGHT_DISTANCE" /> of room on that side (or the wanted distance, when that is
    /// less), else the one that <see cref="PickStartYaw" /> picks: in a corridor, with his shoulder to the wall, the camera looks along the corridor instead of from inside the wall. <paramref name="room" /> is the room on the yaw that
    /// is returned.
    /// </summary>
    public static float PickViewYaw(float wantedYaw, Func<float, float> roomAt, float wanted, out float room)
    {
        float here = roomAt(wantedYaw);
        if (here >= Math.Min(ClampDistance(wanted), TIGHT_DISTANCE))
        {
            room = here;
            return WrapDegrees(wantedYaw);
        }
        return PickStartYaw(wantedYaw, roomAt, wanted, out room);
    }

    /// <summary>
    /// The turn (degrees, between -180 and 180) that the camera has slid by after <paramref name="seconds" />, from <paramref name="current" /> towards <paramref name="target" /> by the shortest way at
    /// <see cref="SLIDE_DEGREES_PER_SECOND" />: it does not jump when the side it looks along changes.
    /// </summary>
    public static float SlideToward(float current, float target, float seconds)
    {
        float delta = WrapDegrees(target - current);
        float step = SLIDE_DEGREES_PER_SECOND * Math.Max(0f, seconds);
        if (Math.Abs(delta) <= step)
        {
            return WrapDegrees(target);
        }
        return WrapDegrees(current + Math.Sign(delta) * step);
    }

    /// <summary>The distance that is shown this frame: in at once when a wall is closer than it, out at <see cref="EASE_OUT_PER_SECOND" /> when the way is free again.</summary>
    public static float EasedDistance(float shown, float allowed, float seconds)
    {
        if (allowed <= shown)
        {
            return allowed;
        }
        return Math.Min(allowed, shown + EASE_OUT_PER_SECOND * Math.Max(0f, seconds));
    }

    /// <summary>
    /// The direction that a billboard of the mod (the name and the stats that hang above a remote player) is turned to, so that it faces the camera that draws it: <paramref name="headForward" />, the way the game's own camera
    /// looks, or, while the emote camera is out and has drawn a frame, <paramref name="emoteForward" />, the way that one looked. The emote camera is only put where it is for the length of its own rendering, so the game's
    /// camera keeps looking the way of the head: a tag turned to that shows its back (mirrored) to the emote camera when the two look in opposite ways.
    /// </summary>
    public static (float X, float Y, float Z) BillboardForward(bool emoteCameraOut, (float X, float Y, float Z) headForward, (float X, float Y, float Z) emoteForward)
    {
        if (!emoteCameraOut || !IsDirection(emoteForward))
        {
            return headForward;
        }
        return emoteForward;
    }

    private static bool IsDirection((float X, float Y, float Z) vector)
    {
        float lengthSquared = vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z;
        return !float.IsNaN(lengthSquared) && !float.IsInfinity(lengthSquared) && lengthSquared > 1e-6f;
    }

    /// <summary>
    /// The height of the camera, kept on the same side of the water surface as the character's chest: below it for a swimmer, above it for somebody on the surface or on land that the camera could dip under.
    /// Nothing is changed when <paramref name="constrain" /> is false (inside a base or a Cyclops, where the surface is not a thing).
    /// </summary>
    public static float KeepOnWaterSide(float cameraY, float pivotY, float oceanLevel, bool constrain)
    {
        if (!constrain)
        {
            return cameraY;
        }
        return pivotY < oceanLevel ? Math.Min(cameraY, oceanLevel - WATER_MARGIN) : Math.Max(cameraY, oceanLevel + WATER_MARGIN);
    }
}
