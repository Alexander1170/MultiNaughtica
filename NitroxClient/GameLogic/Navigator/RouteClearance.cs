using System;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// How far above a route point the helm aims. A route point is stored, shown on the hologram and sent to everybody where it was clicked: on the terrain, like a mark (a click that crossed no terrain, or an Alt+click, puts
/// it at the Cyclops' own depth, see <see cref="PinYFor" />). The Cyclops cannot dive into the rock, so the guidance (<c>RouteGuide</c>, <c>RouteHud</c>) aims <see cref="AimClearance" /> above that point instead,
/// never above the sea's surface. Pure, so that the numbers are tested; a point is a y (world, the sea's surface is 0) and a depth is its negative.
/// </summary>
public static class RouteClearance
{
    /// <summary>The most that the guidance aims above a route point, in metres: where the floor is 40 m deep or deeper.</summary>
    public const float CLEARANCE_METRES = 20f;

    /// <summary>The least that the guidance aims above a route point, in metres (a point on a floor 2 m deep or less).</summary>
    public const float MIN_CLEARANCE_METRES = 1f;

    /// <summary>How much of the depth the clearance is, where the water is shallow: a floor 12 m deep is aimed 6 m above.</summary>
    public const float SHALLOW_FRACTION = 0.5f;

    // (the floor depths where the clearance changes its rule: up to 2 m it is the least, up to 40 m it is half the depth, below that it is the most)
    private const float LEAST_LIMIT_DEPTH = MIN_CLEARANCE_METRES / SHALLOW_FRACTION;
    private const float MOST_LIMIT_DEPTH = CLEARANCE_METRES / SHALLOW_FRACTION;

    /// <summary>The height above a point on a floor <paramref name="depth" /> metres deep that the guidance aims at: half the depth, but between 1 and 20 m.</summary>
    public static float AimClearance(float depth)
    {
        return Math.Min(CLEARANCE_METRES, Math.Max(MIN_CLEARANCE_METRES, depth * SHALLOW_FRACTION));
    }

    /// <summary>The y that the guidance aims at for a route point at <paramref name="pinY" />: the clearance above it, but not above the surface. Not a number is left as it is.</summary>
    public static float AimY(float pinY)
    {
        if (float.IsNaN(pinY) || float.IsInfinity(pinY))
        {
            return pinY;
        }
        return Math.Min(0f, pinY + AimClearance(-pinY));
    }

    /// <summary>
    /// The y that a route point is stored at so that the guidance aims at <paramref name="aimY" />: the inverse of <see cref="AimY" />. It is for a route point that is not on the terrain (an Alt+click, or a click that
    /// crossed no terrain), which means "go to this depth": its pin is that much lower, on the hologram, and nobody can tell on a map that is a few centimetres deep.
    /// </summary>
    public static float PinYFor(float aimY)
    {
        if (float.IsNaN(aimY) || float.IsInfinity(aimY))
        {
            return aimY;
        }
        float aimDepth = Math.Max(0f, -aimY);
        // (the depth of the aim is d - 1 for a pin d = 1 to 2 m deep, d / 2 for 2 to 40 m, and d - 20 below that; a pin less than 1 m deep is aimed at the surface)
        float leastAim = LEAST_LIMIT_DEPTH - MIN_CLEARANCE_METRES;
        float mostAim = MOST_LIMIT_DEPTH * (1f - SHALLOW_FRACTION);
        float depth = aimDepth < leastAim ? aimDepth + MIN_CLEARANCE_METRES : aimDepth <= mostAim ? aimDepth / (1f - SHALLOW_FRACTION) : aimDepth + CLEARANCE_METRES;
        return -depth;
    }

    /// <summary>What the message says about a route point whose pin is at <paramref name="pinY" /> on the terrain: where the floor is and where the helm aims.</summary>
    public static string Describe(float pinY)
    {
        float aimY = AimY(pinY);
        if (aimY >= 0f)
        {
            return $"floor at {-pinY:F0} m, the helm aims at the surface";
        }
        return $"floor at {-pinY:F0} m, the helm aims {aimY - pinY:F0} m above it at {-aimY:F0} m";
    }
}
