using System;
using System.Globalization;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>What a contact on the navigator's map is.</summary>
public enum ContactKind
{
    /// <summary>The Cyclops the console is in; drawn as its outline at the middle of the map.</summary>
    Cyclops,

    /// <summary>Any other Cyclops.</summary>
    OtherCyclops,

    /// <summary>A teammate who is not aboard.</summary>
    Teammate,

    /// <summary>A Seamoth or a Prawn Suit (a teammate's has the teammate's colour).</summary>
    Vehicle,

    /// <summary>A creature that the map warns about (<see cref="CreatureDanger" />).</summary>
    Creature,

    Beacon,
    Base,
    Lifepod,

    /// <summary>A mark that a navigator placed on the map (shared by the crew of the Cyclops).</summary>
    Mark
}

/// <summary>
/// The decisions about the contacts of the navigator's map that need no game objects: which are kept when there are too many for the map, how a distance is written, and what a contact outside the rim of the
/// map looks like. Pure numbers and text, so that they are tested.
/// </summary>
public static class ContactRules
{
    /// <summary>The most contacts the map draws; the rest are the farthest of the least important kinds.</summary>
    public const int MAX_CONTACTS = 64;

    /// <summary>How important a contact is when the map has to leave some out (bigger stays).</summary>
    public static int Priority(ContactKind kind, CreatureDanger danger)
    {
        switch (kind)
        {
            case ContactKind.Cyclops:
                return 1000;
            case ContactKind.Creature:
                return danger switch
                {
                    CreatureDanger.Leviathan => 200,
                    CreatureDanger.Dangerous => 180,
                    _ => 40
                };
            case ContactKind.Mark:
                return 160;
            case ContactKind.Teammate:
                return 150;
            case ContactKind.OtherCyclops:
                return 140;
            case ContactKind.Vehicle:
                return 130;
            case ContactKind.Beacon:
                return 100;
            case ContactKind.Base:
                return 90;
            case ContactKind.Lifepod:
                return 80;
            default:
                return 0;
        }
    }

    /// <summary>A distance for the map: whole metres up to a kilometre, then kilometres with one decimal. A number that is not a distance gives a dash.</summary>
    public static string Distance(float metres)
    {
        if (float.IsNaN(metres) || float.IsInfinity(metres) || metres < 0f)
        {
            return "-";
        }
        if (Math.Round(metres) < 1000.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0} m", metres);
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} km", metres / 1000f);
    }

    /// <summary>
    /// A contact <paramref name="distance" /> from the middle of the map, when the map's rim is at <paramref name="rim" />: drawn at its place when it is inside; one that is outside is drawn on the rim in
    /// the same direction, smaller, so that a leviathan just beyond the edge is not lost (<c>OffMap</c> is true then).
    /// </summary>
    public static (float Scale, bool OffMap) RimClamp(float distance, float rim)
    {
        if (float.IsNaN(distance) || float.IsInfinity(distance) || float.IsNaN(rim) || float.IsInfinity(rim) || rim <= 0f)
        {
            return (0f, true);
        }
        if (distance <= rim || distance <= 0.0001f)
        {
            return (1f, false);
        }
        return (rim / distance, true);
    }

    /// <summary>Whether a contact beyond the map's rim is still drawn, on the rim (the things that move and that matter), or left out (the things that stand still).</summary>
    public static bool ClampsToRim(ContactKind kind)
    {
        return kind == ContactKind.Creature || kind == ContactKind.Teammate || kind == ContactKind.Vehicle || kind == ContactKind.Mark;
    }

    /// <summary>How far above or below the Cyclops a contact is, for the line under the cursor: "35 m above", "20 m below" or "level".</summary>
    public static string Height(float metresUp)
    {
        if (float.IsNaN(metresUp) || float.IsInfinity(metresUp) || Math.Abs(metresUp) < 2f)
        {
            return "level";
        }
        return string.Concat(Distance(Math.Abs(metresUp)), metresUp > 0f ? " above" : " below");
    }

    /// <summary>The icon size factor of a contact: full inside the rim, 0.6 on the rim for one beyond it.</summary>
    public static float IconSize(bool offMap)
    {
        return offMap ? 0.6f : 1f;
    }

    /// <summary>Whether the map keeps a contact at all: it is within the range that the map shows.</summary>
    public static bool WithinRange(float distance, float range)
    {
        return !float.IsNaN(distance) && !float.IsNaN(range) && distance >= 0f && distance <= range;
    }

    /// <summary>The line the map shows for the contact under the cursor: its label and how far it is.</summary>
    public static string Describe(string label, float metres)
    {
        string name = string.IsNullOrWhiteSpace(label) ? "Contact" : label.Trim();
        return string.Concat(name, "  ", Distance(metres));
    }

    /// <summary>The line at the top of the map: its range at this zoom.</summary>
    public static string RangeLine(float rimMetres, float zoom)
    {
        return string.Format(CultureInfo.InvariantCulture, "Range {0} (zoom {1:0.0}x)", Distance(rimMetres), Math.Max(0f, zoom));
    }
}
