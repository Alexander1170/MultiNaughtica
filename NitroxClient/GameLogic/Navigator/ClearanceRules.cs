using System;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>Where the terrain is that the clearance warning is about, in the Cyclops' own frame (x starboard, y up, z ahead).</summary>
public enum ClearanceSide
{
    None,
    Ahead,
    Astern,
    Port,
    Starboard,
    Above,
    Below
}

/// <summary>How tight the space is (<see cref="ClearanceRules.LevelFor" />).</summary>
public enum ClearanceLevel
{
    Clear,
    Caution,
    Danger
}

/// <summary>
/// The decisions of the clearance warnings of a Cyclops with the Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md, station 1, "Clearance 4 m, port"): which side a direction is, how big the gap between the hull
/// and the terrain is, how tight is tight, how far ahead to look, what the warning says and when it is said aloud. Pure numbers and text, so that they are tested. The casts themselves are
/// <c>CyclopsClearance</c>.
/// </summary>
public static class ClearanceRules
{
    /// <summary>Below this fraction of the warning distance the space is no longer tight but dangerous.</summary>
    public const float DANGER_FRACTION = 0.35f;

    /// <summary>Back to a lower level only when the gap is this much bigger than the limit, so that a gap at the limit does not flicker.</summary>
    public const float HYSTERESIS = 1.15f;

    /// <summary>The longest look ahead in metres, whatever the speed.</summary>
    public const float MAX_LOOK_AHEAD = 100f;

    /// <summary>Seconds of travel that the look ahead covers.</summary>
    public const float LOOK_AHEAD_SECONDS = 5f;

    /// <summary>The warning is said aloud (in the message list of the game) no more often than this.</summary>
    public const float ANNOUNCE_SECONDS = 4f;

    /// <summary>The side that a direction in the Cyclops' frame mostly points to (the axis with the longest component); None for a direction of no length or not a number.</summary>
    public static ClearanceSide SideOfDirection(float x, float y, float z)
    {
        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
        {
            return ClearanceSide.None;
        }
        float ax = Math.Abs(x);
        float ay = Math.Abs(y);
        float az = Math.Abs(z);
        if (ax < 0.0001f && ay < 0.0001f && az < 0.0001f)
        {
            return ClearanceSide.None;
        }
        if (az >= ax && az >= ay)
        {
            return z > 0f ? ClearanceSide.Ahead : ClearanceSide.Astern;
        }
        if (ax >= ay)
        {
            return x > 0f ? ClearanceSide.Starboard : ClearanceSide.Port;
        }
        return y > 0f ? ClearanceSide.Above : ClearanceSide.Below;
    }

    /// <summary>
    /// The gap between the hull and the terrain for a sphere that was cast from the hull's face and hit at <paramref name="hitDistance" />: the sphere's own radius is in front of its centre, so the terrain is
    /// that much further than the centre went; <paramref name="startOffset" /> is how far outside (positive) or inside the face the centre started. Never negative.
    /// </summary>
    public static float GapFromSphereHit(float hitDistance, float radius, float startOffset)
    {
        return Math.Max(0f, startOffset + hitDistance + radius);
    }

    /// <summary>How far ahead the Cyclops looks: five seconds of travel at its speed, but at least the warning distance and at most <see cref="MAX_LOOK_AHEAD" />.</summary>
    public static float LookAheadDistance(float speed, float warningDistance)
    {
        float warning = Math.Max(0f, warningDistance);
        float travel = float.IsNaN(speed) ? 0f : Math.Max(0f, speed) * LOOK_AHEAD_SECONDS;
        return Math.Min(MAX_LOOK_AHEAD, Math.Max(warning, travel));
    }

    /// <summary>
    /// The level of a gap: Caution below the warning distance, Danger below 35 percent of it; once at a level the gap has to grow 15 percent past the limit to fall to the one below. A gap that is not a number
    /// (nothing known) is Clear.
    /// </summary>
    public static ClearanceLevel LevelFor(ClearanceLevel previous, float gap, float warningDistance)
    {
        if (float.IsNaN(gap) || warningDistance <= 0f)
        {
            return ClearanceLevel.Clear;
        }
        float warning = warningDistance;
        float danger = warning * DANGER_FRACTION;
        float dangerLimit = previous == ClearanceLevel.Danger ? danger * HYSTERESIS : danger;
        float cautionLimit = previous != ClearanceLevel.Clear ? warning * HYSTERESIS : warning;
        if (gap < dangerLimit)
        {
            return ClearanceLevel.Danger;
        }
        return gap < cautionLimit ? ClearanceLevel.Caution : ClearanceLevel.Clear;
    }

    public static string SideText(ClearanceSide side)
    {
        return side switch
        {
            ClearanceSide.Ahead => "ahead",
            ClearanceSide.Astern => "astern",
            ClearanceSide.Port => "port",
            ClearanceSide.Starboard => "starboard",
            ClearanceSide.Above => "above",
            ClearanceSide.Below => "below",
            _ => string.Empty
        };
    }

    /// <summary>What the warning says: "Clearance 4 m, port", "Clearance under 1 m, starboard"; ahead the terrain is an obstacle: "Obstacle ahead, 12 m". Nothing for no side.</summary>
    public static string WarningText(ClearanceSide side, float gap)
    {
        if (side == ClearanceSide.None || float.IsNaN(gap))
        {
            return string.Empty;
        }
        string distance = gap < 1f ? "under 1 m" : $"{(int)Math.Round(gap)} m";
        return side == ClearanceSide.Ahead ? $"Obstacle ahead, {distance}" : $"Clearance {distance}, {SideText(side)}";
    }

    /// <summary>The warning is said aloud when the level got worse, and at most every four seconds; a level that stays or gets better is shown on the screen but not repeated.</summary>
    public static bool ShouldAnnounce(ClearanceLevel previous, ClearanceLevel now, float secondsSinceLast)
    {
        return now > previous && now != ClearanceLevel.Clear && secondsSinceLast >= ANNOUNCE_SECONDS;
    }
}
