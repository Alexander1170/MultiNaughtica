using System;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The limits and the checks of the marks of a Cyclops' navigator (docs/coop/VEHICLE_UPGRADES.md, station 1), shared by the server, which enforces them, and the client, which stays inside them so that the server
/// has nothing to refuse. Pure numbers and text, so that they are tested.
/// </summary>
public static class CyclopsNavRules
{
    /// <summary>The most marks one Cyclops keeps; one more is refused.</summary>
    public const int MAX_MARKS = 24;

    /// <summary>The longest label of a mark; a longer one is cut.</summary>
    public const int MAX_LABEL_LENGTH = 24;

    /// <summary>A plain mark (cyan).</summary>
    public const byte MARK_POINT = 0;

    /// <summary>A hazard to keep clear of (orange).</summary>
    public const byte MARK_HAZARD = 1;

    /// <summary>Where to go (yellow).</summary>
    public const byte MARK_TARGET = 2;

    /// <summary>A resource that the operator of the Scout Drone marked (teal). Only the seated operator of that Cyclops' drone places these.</summary>
    public const byte MARK_RESOURCE = 3;

    /// <summary>A threat (a creature) that the operator of the Scout Drone marked (red). Only the seated operator of that Cyclops' drone places these.</summary>
    public const byte MARK_THREAT = 4;

    public const byte MARK_KIND_COUNT = 5;

    /// <summary>The most marks of the Scout Drone one Cyclops keeps, besides the <see cref="MAX_MARKS" /> of the navigator: the two caps are separate, so that a drone cannot fill the map of the navigator or the other way round.</summary>
    public const int MAX_DRONE_MARKS = 16;

    /// <summary>The world is about 4 km wide; nothing beyond this is a place in it.</summary>
    public const float MAX_HORIZONTAL = 6000f;

    public const float MIN_DEPTH_Y = -3500f;
    public const float MAX_HEIGHT_Y = 600f;

    public static bool IsKnownKind(byte kind) => kind < MARK_KIND_COUNT;

    /// <summary>The marks of the Scout Drone's operator (a resource, a threat): the server takes them from the seated operator only, and every player sees them, aboard or not.</summary>
    public static bool IsDroneKind(byte kind) => kind == MARK_RESOURCE || kind == MARK_THREAT;

    /// <summary>How many marks of this kind's class one Cyclops keeps: <see cref="MAX_DRONE_MARKS" /> for the drone's, <see cref="MAX_MARKS" /> for the navigator's.</summary>
    public static int CapOf(byte kind) => IsDroneKind(kind) ? MAX_DRONE_MARKS : MAX_MARKS;

    /// <summary>
    /// The index into the game's own ping colours (<c>PingManager.colorOptions</c>: 0 cyan, 1 orange, 2 red, 3 teal, 4 yellow) that the HUD ping of a mark of this kind uses; the game's own list is used
    /// as it is, so nothing has to be added to it.
    /// </summary>
    public static int HudColorIndex(byte kind)
    {
        return kind switch
        {
            MARK_HAZARD => 1,
            MARK_TARGET => 4,
            MARK_THREAT => 2,
            MARK_RESOURCE => 3,
            _ => 0
        };
    }

    /// <summary>Whether a position can be a place in the world (a number, not far outside it).</summary>
    public static bool IsValidPosition(float x, float y, float z)
    {
        return IsFinite(x) && IsFinite(y) && IsFinite(z) && Math.Abs(x) <= MAX_HORIZONTAL && Math.Abs(z) <= MAX_HORIZONTAL && y >= MIN_DEPTH_Y && y <= MAX_HEIGHT_Y;
    }

    /// <summary>
    /// A label that is safe to keep and to show: control, format (zero width, right-to-left override), surrogate, private use and unassigned characters are dropped, runs of blanks become one, the ends are trimmed
    /// and it is cut to <see cref="MAX_LABEL_LENGTH" />. Null gives an empty label.
    /// </summary>
    public static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return string.Empty;
        }
        System.Text.StringBuilder builder = new(Math.Min(label.Length, MAX_LABEL_LENGTH + 8));
        bool lastWasBlank = true;
        foreach (char c in label)
        {
            if (builder.Length > MAX_LABEL_LENGTH)
            {
                break;
            }
            bool blank = char.IsWhiteSpace(c);
            if (!blank && IsUnsafe(c))
            {
                continue;
            }
            if (blank && lastWasBlank)
            {
                continue;
            }
            builder.Append(blank ? ' ' : c);
            lastWasBlank = blank;
        }
        string text = builder.ToString().TrimEnd();
        return text.Length <= MAX_LABEL_LENGTH ? text : text.Substring(0, MAX_LABEL_LENGTH).TrimEnd();
    }

    /// <summary>The label a mark gets when its player gave none: "Mark 3".</summary>
    public static string DefaultLabel(int number) => $"Mark {Math.Max(1, number)}";

    /// <summary>
    /// The text of a mark's ping on the HUD: the player who placed it and the label ("Alice: Mark 3"), or just the label when the player is unknown. Cut to a length the HUD can draw.
    /// </summary>
    public static string PingText(string? ownerName, string? label)
    {
        string owner = SanitizeLabel(ownerName);
        string text = SanitizeLabel(label);
        if (text.Length == 0)
        {
            text = "Mark";
        }
        return owner.Length == 0 ? text : $"{owner}: {text}";
    }

    private static bool IsUnsafe(char c)
    {
        if (char.IsControl(c))
        {
            return true;
        }
        switch (char.GetUnicodeCategory(c))
        {
            case System.Globalization.UnicodeCategory.Format:
            case System.Globalization.UnicodeCategory.Surrogate:
            case System.Globalization.UnicodeCategory.PrivateUse:
            case System.Globalization.UnicodeCategory.OtherNotAssigned:
                return true;
            default:
                return false;
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
