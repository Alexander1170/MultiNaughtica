using System;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>The bubbles of the engineer's PDA screen: four routes of the power (one is chosen), the overdrive, the vent and the fire suppression.</summary>
public enum EngineerBubble
{
    Shields,
    Systems,
    Engines,
    Balanced,
    Overdrive,
    Vent,
    FireSuppression
}

/// <summary>
/// Where things are on the engineer's PDA screen (docs/coop/VEHICLE_UPGRADES.md, station 3), as plain numbers: a side-view picture of the Cyclops
/// (a side view, bow on the left, 2046 x 768) with the spots chosen for the bubbles, the heat bar, and the map from the hull's frame onto the picture for the fires and the breaches. Everything on
/// the picture is a fraction of it: x from the left, y from the top.
/// </summary>
public static class EngineerPictureLayout
{
    /// <summary>The width over the height of the picture.</summary>
    public const float PICTURE_ASPECT = 2046f / 768f;

    /// <summary>
    /// A bubble's diameter as a share of the picture's height: 0.156 of the 583 high picture is 91 units of the page's sheet, about 72 pixels on a 1080p screen (an earlier draft had 0.085; after the first look the
    /// user wanted bubbles like the upgrade console's module slots, 70 to 90 pixels).
    /// </summary>
    public const float BUBBLE_DIAMETER = 0.156f;

    /// <summary>How far a bubble's name plate (the name and the state under it) reaches below the bubble's edge, as a share of the picture's height.</summary>
    public const float LABEL_BELOW = 0.125f;

    /// <summary>The spots chosen: which bubble, its name, x and y.</summary>
    public static readonly (EngineerBubble Bubble, string Name, float X, float Y)[] Bubbles =
    [
        (EngineerBubble.Shields, "SHIELDS", 0.118f, 0.383f),
        (EngineerBubble.Systems, "SYSTEMS", 0.473f, 0.387f),
        (EngineerBubble.Engines, "ENGINES", 0.685f, 0.387f),
        (EngineerBubble.Balanced, "BALANCED", 0.425f, 0.616f),
        (EngineerBubble.Overdrive, "OVERDRIVE", 0.573f, 0.616f),
        (EngineerBubble.Vent, "VENT", 0.744f, 0.589f),
        (EngineerBubble.FireSuppression, "FIRE SUPPRESSION", 0.253f, 0.626f)
    ];

    /// <summary>The heat bar behind the stern, a vertical bar filled from the bottom: x from, x to, y from the top, y to.</summary>
    public static readonly (float X0, float X1, float Y0, float Y1) HeatBar = (0.845f, 0.856f, 0.322f, 0.564f);

    /// <summary>Where a spot of the picture is on a rectangle of <paramref name="width" /> by <paramref name="height" /> whose middle is the origin (x to the right, y up).</summary>
    public static (float X, float Y) PanelPoint(float fx, float fy, float width, float height)
    {
        return ((fx - 0.5f) * width, (0.5f - fy) * height);
    }

    /// <summary>The size of the biggest picture that fits an area, with the picture's own proportions.</summary>
    public static (float Width, float Height) FitPicture(float areaWidth, float areaHeight)
    {
        float width = Math.Max(0f, areaWidth);
        float height = width / PICTURE_ASPECT;
        if (areaHeight > 0f && height > areaHeight)
        {
            height = areaHeight;
            width = height * PICTURE_ASPECT;
        }
        return (width, Math.Max(0f, height));
    }

    // ---- the hull's frame on the picture

    // The seven rooms' fire spawn points (the scene of the Cyclops, hull frame) lie within about 0.025 of the picture's width of one straight map: x grows with z (aft), y falls with the hull's height.
    private const float MAP_X_AT_ZERO = 0.531f;
    private const float MAP_X_PER_METRE = 0.0206f;
    private const float MAP_Y_AT_ZERO = 0.38f;
    private const float MAP_Y_PER_METRE = 0.054f;

    /// <summary>Where a point of the hull's frame (z from the bow back, y up) is on the picture, not kept on it (a breach on the skin is above or below the rooms).</summary>
    public static (float X, float Y) Project(float z, float y)
    {
        return (MAP_X_AT_ZERO + MAP_X_PER_METRE * z, MAP_Y_AT_ZERO - MAP_Y_PER_METRE * y);
    }

    /// <summary>The rooms of the game's <c>CyclopsRooms</c> by their numbers (0 Helm, 1 Build room, 2 Vehicle bay, 3 Engine room, 4 Lower engine room, 5 Lower vehicle bay, 6 Lower storage) and their rectangles on the picture.</summary>
    public static readonly (float X0, float X1, float Y0, float Y1)[] Rooms =
    [
        (0.080f, 0.225f, 0.270f, 0.490f),
        (0.230f, 0.294f, 0.270f, 0.490f),
        (0.302f, 0.534f, 0.270f, 0.490f),
        (0.540f, 0.810f, 0.270f, 0.490f),
        (0.521f, 0.807f, 0.510f, 0.750f),
        (0.323f, 0.511f, 0.510f, 0.750f),
        (0.098f, 0.315f, 0.510f, 0.750f)
    ];

    public static int RoomCount => Rooms.Length;

    /// <summary>The rectangle of a room on the picture (x from, x to, y from the top, y to); the whole picture for a number that is not a room.</summary>
    public static (float X0, float X1, float Y0, float Y1) RoomRect(int room)
    {
        return room >= 0 && room < Rooms.Length ? Rooms[room] : (0f, 1f, 0f, 1f);
    }

    /// <summary>
    /// The room that a point of the hull belongs to, for the badges of the breaches: the room whose rectangle holds the point on the picture, or the nearest room when none does (a breach on the skin is
    /// above or below the rooms).
    /// </summary>
    public static int RoomOf(float z, float y)
    {
        (float px, float py) = Project(z, y);
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int room = 0; room < Rooms.Length; room++)
        {
            (float x0, float x1, float y0, float y1) = Rooms[room];
            float dx = Math.Max(Math.Max(x0 - px, 0f), px - x1);
            float dy = Math.Max(Math.Max(y0 - py, 0f), py - y1);
            float distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = room;
            }
        }
        return best;
    }

    /// <summary>How much of the heat bar is filled, from 0 to 1: the engine's heat over the most it can be.</summary>
    public static float HeatFraction(int heat, int maxHeat)
    {
        return maxHeat <= 0 ? 0f : Math.Max(0f, Math.Min(1f, (float)heat / maxHeat));
    }
}
