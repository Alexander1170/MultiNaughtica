using System;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The cable anchors of the tow hooks from the box around a hull (<see cref="HullBounds" />), in plain numbers so that they can be tested: the point at the nose or at the rear of a hull along its
/// forward direction, a little outside it.
/// </summary>
public static class HullGeometry
{
    /// <summary>Metres between the end of the hull and the anchor of the cable, so that the cable does not start inside the hull.</summary>
    public const float ANCHOR_MARGIN = 0.3f;

    /// <summary>A box with a half size above this (in metres) is taken for a mistake (a huge trigger) and not used.</summary>
    public const float MAX_PLAUSIBLE_HALF_SIZE = 80f;

    /// <summary>No vehicle is narrower or shorter than this (a Prawn Suit is about 1 m wide in half size): a smaller box is the colliders of a docked or half-built vehicle, and is measured again later.</summary>
    public const float MIN_PLAUSIBLE_HALF_WIDTH = 0.4f;

    public const float MIN_PLAUSIBLE_HALF_LENGTH = 0.8f;

    /// <summary>How far the center of the box may be from the origin of the vehicle beyond the size of the box: stale bounds of a collider that has not moved with its object yet are far away.</summary>
    public const float MAX_CENTER_MARGIN = 5f;

    /// <summary>Whether a box measured from the colliders can be the hull of a vehicle (the half sizes, in the space of the vehicle).</summary>
    public static bool IsPlausibleBox(float cx, float cy, float cz, float ex, float ey, float ez)
    {
        if (float.IsNaN(cx + cy + cz + ex + ey + ez) || float.IsInfinity(cx + cy + cz + ex + ey + ez))
        {
            return false;
        }
        if (ex < MIN_PLAUSIBLE_HALF_WIDTH || ez < MIN_PLAUSIBLE_HALF_LENGTH || ex > MAX_PLAUSIBLE_HALF_SIZE || ey > MAX_PLAUSIBLE_HALF_SIZE || ez > MAX_PLAUSIBLE_HALF_SIZE)
        {
            return false;
        }
        float center = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
        float size = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
        return center <= size + MAX_CENTER_MARGIN;
    }

    /// <summary>
    /// How far a box with half sizes <c>(ex, ey, ez)</c> reaches from its center along the unit direction <c>(dx, dy, dz)</c>: the support function of the box, |dx|·ex + |dy|·ey + |dz|·ez.
    /// </summary>
    public static float SupportExtent(float dx, float dy, float dz, float ex, float ey, float ez)
    {
        return Math.Abs(dx) * ex + Math.Abs(dy) * ey + Math.Abs(dz) * ez;
    }

    /// <summary>
    /// The anchor at one end of the hull along the direction: the center of the box moved along the direction (or against it) by the reach of the box and the margin. The direction is made a unit
    /// vector here (a zero direction gives the center).
    /// </summary>
    /// <param name="forward">True for the nose (along the direction), false for the rear (against it).</param>
    public static (float X, float Y, float Z) EndAnchor(float cx, float cy, float cz, float ex, float ey, float ez, float dx, float dy, float dz, bool forward)
    {
        float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!(length > 0f))
        {
            return (cx, cy, cz);
        }
        dx /= length;
        dy /= length;
        dz /= length;
        float reach = SupportExtent(dx, dy, dz, ex, ey, ez) + ANCHOR_MARGIN;
        float sign = forward ? 1f : -1f;
        return (cx + sign * dx * reach, cy + sign * dy * reach, cz + sign * dz * reach);
    }

    /// <summary>The length of the cable between two anchors.</summary>
    public static float Distance(float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float dz = bz - az;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// The sag of the cable for drawing: a slack cable hangs below the straight line by up to <paramref name="maxSag" />, a taut one does not. <paramref name="t" /> is the position along the cable (0 to 1).
    /// </summary>
    public static float Sag(float length, float restLength, float t, float maxSag)
    {
        float slack = restLength - length;
        if (!(slack > 0f) || maxSag <= 0f)
        {
            return 0f;
        }
        float depth = Math.Min(maxSag, slack * 0.35f);
        return depth * 4f * t * (1f - t);
    }
}
