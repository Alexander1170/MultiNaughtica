using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>
/// The arithmetic of the damage-control cutaway of the engineer console (docs/coop/VEHICLE_UPGRADES.md, station 3), as plain numbers: where a point of the hull's frame (z from the bow back, y up) lands on the
/// drawing, how the rooms are called and how the nearest breach is described to the engineer. The drawing is a side view with the bow on the left; the port and starboard side are not told apart in the picture
/// (the hint says it).
/// </summary>
public static class CutawayRules
{
    /// <summary>The hull of a Cyclops: the bow end and the stern end along z, the keel and the deck above the tower's foot along y (the conning tower is left out of the picture).</summary>
    public const float HULL_MIN_Z = -27f;

    public const float HULL_MAX_Z = 27f;
    public const float HULL_MIN_Y = -7.5f;
    public const float HULL_MAX_Y = 7.5f;

    /// <summary>Smaller than this, in metres, along an axis is "level" with the engineer and is not mentioned.</summary>
    public const float LEVEL_METRES = 1.5f;

    /// <summary>The names of the rooms of the game's <c>CyclopsRooms</c>, by its numbers.</summary>
    private static readonly string[] RoomNames = ["Helm", "Build room", "Vehicle bay", "Engine room", "Lower engine room", "Lower vehicle bay", "Lower storage"];

    public static int RoomCount => RoomNames.Length;

    public static string RoomName(int room)
    {
        return room >= 0 && room < RoomNames.Length ? RoomNames[room] : "Hull";
    }

    /// <summary>Where a point of the hull lands on the drawing, as fractions from 0 to 1 (x to the right along the hull, y up), kept on the drawing.</summary>
    public static (float X, float Y) Project(float z, float y)
    {
        float x = (z - HULL_MIN_Z) / (HULL_MAX_Z - HULL_MIN_Z);
        float v = (y - HULL_MIN_Y) / (HULL_MAX_Y - HULL_MIN_Y);
        return (Clamp01(x), Clamp01(v));
    }

    private static float Clamp01(float value)
    {
        return float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
    }

    /// <summary>
    /// Where the second point is as seen from the first, in the hull's frame ((x, y, z) of the difference): "8 m aft, 3 m to port, 4 m below"; each part only when it is more than <see cref="LEVEL_METRES" />.
    /// "right here" when none is.
    /// </summary>
    public static string Describe(float dx, float dy, float dz)
    {
        List<string> parts = [];
        if (Math.Abs(dz) >= LEVEL_METRES)
        {
            parts.Add($"{Math.Round(Math.Abs(dz)):F0} m {(dz > 0f ? "aft" : "forward")}");
        }
        if (Math.Abs(dx) >= LEVEL_METRES)
        {
            parts.Add($"{Math.Round(Math.Abs(dx)):F0} m to {(dx > 0f ? "port" : "starboard")}");
        }
        if (Math.Abs(dy) >= LEVEL_METRES)
        {
            parts.Add($"{Math.Round(Math.Abs(dy)):F0} m {(dy > 0f ? "above" : "below")}");
        }
        return parts.Count == 0 ? "right here" : string.Join(", ", parts);
    }

    /// <summary>The index of the point nearest to <paramref name="from" />, -1 when there is none.</summary>
    public static int Nearest(IReadOnlyList<(float X, float Y, float Z)> points, (float X, float Y, float Z) from)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float dx = points[i].X - from.X;
            float dy = points[i].Y - from.Y;
            float dz = points[i].Z - from.Z;
            float distance = dx * dx + dy * dy + dz * dz;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The repair hint: the nearest breach to the engineer, or that the hull is tight.</summary>
    public static string RepairHint(IReadOnlyList<(float X, float Y, float Z)> breaches, (float X, float Y, float Z) engineer)
    {
        int nearest = Nearest(breaches, engineer);
        if (nearest < 0)
        {
            return "Hull is tight: no breaches";
        }
        (float X, float Y, float Z) point = breaches[nearest];
        return $"Nearest breach: {Describe(point.X - engineer.X, point.Y - engineer.Y, point.Z - engineer.Z)} ({breaches.Count} in all)";
    }
}
