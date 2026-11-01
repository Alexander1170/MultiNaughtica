using System;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// How far a vehicle is from a Cyclops for the power share: from the hull, not from the middle of a 65 metre boat. The hull is the box around the Cyclops' colliders in the Cyclops' own space, and
/// the distance is the distance from the point to that box (0 inside it). Plain numbers, so that it can be tested.
/// </summary>
public static class PowerShareGeometry
{
    /// <summary>
    /// Metres added to the range when the giver checks a request. The two clients see the vehicles a little apart (a copy is drawn about half a metre off while it moves, and a request is up to a
    /// second old), and a vehicle that was in range when it asked must not be refused for it.
    /// </summary>
    public const float RANGE_TOLERANCE = 5f;

    /// <summary>Metres from the middle of the Cyclops that count as its hull when its colliders cannot be found (a Cyclops is about 65 metres long).</summary>
    public const float FALLBACK_HULL_RADIUS = 20f;

    /// <summary>The distance from the point to the box around <c>center</c> with half sizes <c>extents</c>; 0 when the point is inside.</summary>
    public static float DistanceToBox(float pointX, float pointY, float pointZ, float centerX, float centerY, float centerZ, float extentX, float extentY, float extentZ)
    {
        float dx = Math.Max(0f, Math.Abs(pointX - centerX) - extentX);
        float dy = Math.Max(0f, Math.Abs(pointY - centerY) - extentY);
        float dz = Math.Max(0f, Math.Abs(pointZ - centerZ) - extentZ);
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>The distance to the hull when only the middle is known: the distance to the middle less <see cref="FALLBACK_HULL_RADIUS" />, but not below 0.</summary>
    public static float DistanceToHullFromCenter(float distanceToCenter) => Math.Max(0f, distanceToCenter - FALLBACK_HULL_RADIUS);

    /// <summary>True when the vehicle is in range: <paramref name="distance" /> is the distance to the hull, <paramref name="tolerance" /> is 0 for the vehicle's own test and <see cref="RANGE_TOLERANCE" /> for the giver's.</summary>
    public static bool InRange(float distance, float range, float tolerance) => distance <= range + tolerance;
}
