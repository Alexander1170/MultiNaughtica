using System;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.DataStructures.GameLogic;

namespace NitroxClient.GameLogic;

/// <summary>
/// The stats line under the name of every OTHER player in the PDA's "Player List" tab (item c): health, food and water; how far away he is; how deep; the vehicle he is in. Plain data and
/// pure functions, no game type (like <see cref="PdaBadgeRules" />), so that the formatting and the decisions can be tested: <c>PlayerListStatsSource</c> reads the game and builds a
/// <see cref="StatsSnapshot" />, <see cref="Format" /> turns it into short lines, <see cref="ShouldRefresh" /> and <see cref="TextGate" /> keep the work to once a second and to the texts
/// that really changed.
/// <para>
/// Nothing here needs a packet: the vitals come with the <c>PlayerStats</c> packet that every client sends every 3 s and the server relays to all others (the client keeps the last values and
/// the time they came, <see cref="VitalsSample" />), the position, the vehicle, the sub and the pod are on <c>RemotePlayer</c>, the depth is a function of the height and the ocean level.
/// A value that is not known (no packet yet, no body yet) is shown as "?", never as a number that looks real; vitals that are older than <see cref="STALE_AFTER" /> seconds are marked "~".
/// </para>
/// <para>
/// Only net472-safe APIs are used (the client is built for net472 as well: no <c>float.IsFinite</c>, <c>Math.Clamp</c>, <c>string.Contains(char)</c>).
/// </para>
/// </summary>
public static class PlayerListStatsRules
{
    /// <summary>How often the lines are rebuilt while the player list is the shown tab.</summary>
    public const float REFRESH_INTERVAL = 1f;

    /// <summary>The stats come every 3 s: three missed ones plus a margin. Older vitals are marked "~".</summary>
    public const float STALE_AFTER = 10f;

    /// <summary>Closer than this (metres) is "here".</summary>
    public const float HERE_RADIUS = 3f;

    /// <summary>A vital above this is shown as "999+" (the game's ranges are 0 to 100, food 0 to 200).</summary>
    public const int MAX_VITAL = 999;

    /// <summary>A depth above this is shown as this (a position that is not a position).</summary>
    public const int MAX_DEPTH = 9999;

    /// <summary>A distance from this many metres on is "100+ km".</summary>
    public const double FAR_METRES = 99950d;

    public const string SEPARATOR = " | ";

    /// <summary>The language keys (the Nitrox ones exist in en.json; the three vehicle names are the vanilla keys, translated in every language).</summary>
    public static class Keys
    {
        public const string HEALTH = "Nitrox_PlayerStatsHealth";
        public const string FOOD = "Nitrox_PlayerStatsFood";
        public const string WATER = "Nitrox_PlayerStatsWater";
        public const string DISTANCE = "Nitrox_PlayerStatsDistance";
        public const string DEPTH = "Nitrox_PlayerStatsDepth";
        public const string HERE = "Nitrox_PlayerStatsHere";
        public const string SURFACE = "Nitrox_PlayerStatsSurface";
        public const string ABOVE_WATER = "Nitrox_PlayerStatsAboveWater";
        public const string METERS = "Nitrox_PlayerStatsMeters";
        public const string KILOMETERS = "Nitrox_PlayerStatsKilometers";
        public const string BASE = "Nitrox_PlayerStatsBase";
        public const string LIFEPOD = "Nitrox_PlayerStatsLifepod";
        public const string SEAMOTH = "Seamoth";
        public const string PRAWN = "Exosuit";
        public const string CYCLOPS = "Cyclops";
    }

    /// <summary>What kind of place or vehicle a remote player is in. The zero value is "unknown", so that an unfilled field claims nothing.</summary>
    public enum RemoteVehicleKind
    {
        Unknown = 0,
        None,
        Seamoth,
        Prawn,
        Cyclops,
        Base,
        Lifepod
    }

    /// <summary>Which vitals are meaningful for a game mode.</summary>
    [Flags]
    public enum VitalsParts
    {
        None = 0,
        Health = 1,
        Food = 2,
        Water = 4,
        All = Health | Food | Water
    }

    /// <summary>A position that may be unknown, as plain floats. The default value is unknown (a position is only one when it was made with numbers).</summary>
    public readonly struct Point3
    {
        private readonly bool hasValue;

        public Point3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
            hasValue = true;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static Point3 Unknown => default;

        public bool IsKnown => hasValue && IsFinite(X) && IsFinite(Y) && IsFinite(Z);
    }

    /// <summary>The last vitals that came from a player and when (the client's unscaled time). The default value is "nothing came yet".</summary>
    public readonly struct VitalsSample
    {
        public VitalsSample(float health, float food, float water, float receivedAt)
        {
            Health = health;
            Food = food;
            Water = water;
            ReceivedAt = receivedAt;
            HasValue = true;
        }

        public float Health { get; }
        public float Food { get; }
        public float Water { get; }
        public float ReceivedAt { get; }
        public bool HasValue { get; }

        public static VitalsSample None => default;

        /// <summary>Seconds since the vitals came, NaN when none came. Negative when the clock went back.</summary>
        public float AgeAt(float now)
        {
            return HasValue ? now - ReceivedAt : float.NaN;
        }
    }

    /// <summary>Everything the line is made of, for one player. Built through <see cref="Create" /> or <see cref="Unknown" />; a default value reads as an unknown place without vitals (no part is shown).</summary>
    public readonly struct StatsSnapshot
    {
        private StatsSnapshot(float health, float food, float water, float vitalsAge, Point3 remote, Point3 local, float oceanLevel, RemoteVehicleKind vehicle, VitalsParts parts)
        {
            Health = health;
            Food = food;
            Water = water;
            VitalsAge = vitalsAge;
            Remote = remote;
            Local = local;
            OceanLevel = oceanLevel;
            Vehicle = vehicle;
            Parts = parts;
        }

        public float Health { get; }
        public float Food { get; }
        public float Water { get; }

        /// <summary>Seconds since the vitals came; NaN = never; negative = the clock went back.</summary>
        public float VitalsAge { get; }

        public Point3 Remote { get; }
        public Point3 Local { get; }

        /// <summary>The height of the sea surface (0 in the game); NaN is taken as 0.</summary>
        public float OceanLevel { get; }

        public RemoteVehicleKind Vehicle { get; }
        public VitalsParts Parts { get; }

        public static StatsSnapshot Create(VitalsSample vitals, float now, Point3 remote, Point3 local, float oceanLevel, RemoteVehicleKind vehicle, VitalsParts parts)
        {
            return new StatsSnapshot(vitals.HasValue ? vitals.Health : float.NaN, vitals.HasValue ? vitals.Food : float.NaN, vitals.HasValue ? vitals.Water : float.NaN, vitals.AgeAt(now), remote, local, oceanLevel, vehicle, parts);
        }

        public static StatsSnapshot Unknown => new(float.NaN, float.NaN, float.NaN, float.NaN, Point3.Unknown, Point3.Unknown, 0f, RemoteVehicleKind.Unknown, VitalsParts.All);
    }

    /// <summary>
    /// The words of the line. The formats have one <c>{0}</c>. <see cref="English" /> is what is shown when a key is missing in the language file (en.json is loaded first as the fallback, so a
    /// missing key means a broken file); <see cref="Resolve" /> asks a lookup for every key and falls back to the English text for an empty answer or a broken format.
    /// </summary>
    public readonly struct StatsLabels
    {
        public StatsLabels(string health, string food, string water, string distance, string depth, string here, string surface, string aboveWater, string meters, string kilometers, string seamoth, string prawn, string cyclops, string @base, string lifepod)
        {
            Health = health;
            Food = food;
            Water = water;
            Distance = distance;
            Depth = depth;
            Here = here;
            Surface = surface;
            AboveWater = aboveWater;
            Meters = meters;
            Kilometers = kilometers;
            Seamoth = seamoth;
            Prawn = prawn;
            Cyclops = cyclops;
            Base = @base;
            Lifepod = lifepod;
        }

        public string Health { get; }
        public string Food { get; }
        public string Water { get; }
        public string Distance { get; }
        public string Depth { get; }
        public string Here { get; }
        public string Surface { get; }
        public string AboveWater { get; }
        public string Meters { get; }
        public string Kilometers { get; }
        public string Seamoth { get; }
        public string Prawn { get; }
        public string Cyclops { get; }
        public string Base { get; }
        public string Lifepod { get; }

        public static StatsLabels English => new("HP {0}", "Food {0}", "Water {0}", "{0} away", "Depth {0}", "here", "Surface", "Above water", "{0} m", "{0} km", "Seamoth", "Prawn suit", "Cyclops", "In a base", "Lifepod");

        /// <param name="lookup">(key, English fallback) -> the text of the current language; may return null or empty.</param>
        public static StatsLabels Resolve(Func<string, string, string> lookup)
        {
            StatsLabels english = English;
            if (lookup == null)
            {
                return english;
            }

            return new StatsLabels(
                Format1(lookup, Keys.HEALTH, english.Health),
                Format1(lookup, Keys.FOOD, english.Food),
                Format1(lookup, Keys.WATER, english.Water),
                Format1(lookup, Keys.DISTANCE, english.Distance),
                Format1(lookup, Keys.DEPTH, english.Depth),
                Word(lookup, Keys.HERE, english.Here),
                Word(lookup, Keys.SURFACE, english.Surface),
                Word(lookup, Keys.ABOVE_WATER, english.AboveWater),
                Format1(lookup, Keys.METERS, english.Meters),
                Format1(lookup, Keys.KILOMETERS, english.Kilometers),
                Word(lookup, Keys.SEAMOTH, english.Seamoth),
                Word(lookup, Keys.PRAWN, english.Prawn),
                Word(lookup, Keys.CYCLOPS, english.Cyclops),
                Word(lookup, Keys.BASE, english.Base),
                Word(lookup, Keys.LIFEPOD, english.Lifepod));
        }

        private static string Word(Func<string, string, string> lookup, string key, string fallback)
        {
            string text = lookup(key, fallback);
            return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        }

        private static string Format1(Func<string, string, string> lookup, string key, string fallback)
        {
            string text = lookup(key, fallback);
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("{0}", StringComparison.Ordinal) < 0)
            {
                return fallback;
            }

            try
            {
                string.Format(CultureInfo.InvariantCulture, text, "x");
            }
            catch (FormatException)
            {
                return fallback;
            }

            return text;
        }
    }

    /// <summary>
    /// The lines of a player, each possibly empty: the vitals, the place (distance and depth) and the vehicle on a line of its own (the first live run showed that "here | Above water | Lifepod" already
    /// reached the Nitrox buttons next to the row; a line of three parts does not fit). <see cref="Text" /> is the non-empty ones joined by a line break.
    /// </summary>
    public readonly struct StatsLine
    {
        // default(StatsLine) is the "no stats" line (rows are given it for a name only): the parts are null there, so every reader goes through the properties, which turn null into empty
        private readonly string vitals;
        private readonly string place;
        private readonly string vehicle;

        public StatsLine(string vitals, string place, string vehicle)
        {
            this.vitals = vitals ?? string.Empty;
            this.place = place ?? string.Empty;
            this.vehicle = vehicle ?? string.Empty;
        }

        public string Vitals => vitals ?? string.Empty;
        public string Place => place ?? string.Empty;
        public string Vehicle => vehicle ?? string.Empty;

        public string Text
        {
            get
            {
                string text = Vitals;
                if (Place.Length > 0)
                {
                    text = text.Length == 0 ? Place : $"{text}\n{Place}";
                }

                if (Vehicle.Length > 0)
                {
                    text = text.Length == 0 ? Vehicle : $"{text}\n{Vehicle}";
                }

                return text;
            }
        }
    }

    /// <summary>
    /// Remembers the text that is on the screen, so that the label is assigned (and its mesh rebuilt) only when the text changed.
    /// </summary>
    public sealed class TextGate
    {
        private string last;
        private bool hasLast;

        /// <summary>Whether <paramref name="next" /> differs from the last text; remembers it. A null text is the empty text.</summary>
        public bool Changed(string next)
        {
            next ??= string.Empty;
            if (hasLast && string.Equals(last, next, StringComparison.Ordinal))
            {
                return false;
            }

            last = next;
            hasLast = true;
            return true;
        }

        /// <summary>The label was changed by someone else (a new player in a recycled row, a language change): the next text is always assigned.</summary>
        public void Reset()
        {
            last = null;
            hasLast = false;
        }
    }

    public static StatsLine Format(StatsSnapshot snapshot, StatsLabels labels)
    {
        List<string> vitals = new(3);
        bool stale = IsStale(snapshot.VitalsAge);
        if ((snapshot.Parts & VitalsParts.Health) != 0)
        {
            vitals.Add(Fmt(labels.Health, VitalValue(snapshot.Health, stale)));
        }

        if ((snapshot.Parts & VitalsParts.Food) != 0)
        {
            vitals.Add(Fmt(labels.Food, VitalValue(snapshot.Food, stale)));
        }

        if ((snapshot.Parts & VitalsParts.Water) != 0)
        {
            vitals.Add(Fmt(labels.Water, VitalValue(snapshot.Water, stale)));
        }

        string[] place = { DistanceText(snapshot.Remote, snapshot.Local, labels), DepthText(snapshot.Remote, snapshot.OceanLevel, labels) };
        return new StatsLine(string.Join(SEPARATOR, vitals.ToArray()), string.Join(SEPARATOR, place), VehicleText(snapshot.Vehicle, labels));
    }

    /// <summary>
    /// A vital as the HUD's bars show it: rounded UP (the bars use <c>CeilToInt</c>, so a living player never reads 0 and the number is the one on his own screen). Not a number, infinite or
    /// negative is "?" (0 is a real value: dead). No upper limit of 100 (food goes to 200), but "999+" above <see cref="MAX_VITAL" />. "~" in front when the vitals are stale.
    /// </summary>
    public static string VitalValue(float value, bool stale)
    {
        if (!IsFinite(value) || value < 0f)
        {
            return "?";
        }

        double ceiling = Math.Ceiling((double)value);
        string text = ceiling > MAX_VITAL ? string.Format(CultureInfo.InvariantCulture, "{0}+", MAX_VITAL) : ((int)ceiling).ToString(CultureInfo.InvariantCulture);
        return stale ? string.Format(CultureInfo.InvariantCulture, "~{0}", text) : text;
    }

    /// <summary>Vitals older than <see cref="STALE_AFTER" /> seconds, or from the future (the clock went back), are stale; no age at all (NaN) is not stale but unknown.</summary>
    public static bool IsStale(float ageSeconds)
    {
        if (float.IsNaN(ageSeconds))
        {
            return false;
        }

        return ageSeconds < 0f || ageSeconds >= STALE_AFTER;
    }

    public static double Distance(Point3 a, Point3 b)
    {
        double dx = (double)a.X - b.X;
        double dy = (double)a.Y - b.Y;
        double dz = (double)a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>"here" within <see cref="HERE_RADIUS" />, then the nearest metre ("143 m away") up to 999 m, then kilometres with one decimal ("1.2 km away"), "100+ km away" from <see cref="FAR_METRES" />; "? away" when a position is not known.</summary>
    public static string DistanceText(Point3 remote, Point3 local, StatsLabels labels)
    {
        if (!remote.IsKnown || !local.IsKnown)
        {
            return Fmt(labels.Distance, "?");
        }

        double metres = Distance(remote, local);
        if (double.IsNaN(metres) || double.IsInfinity(metres) || metres >= FAR_METRES)
        {
            return Fmt(labels.Distance, Fmt(labels.Kilometers, "100+"));
        }

        if (metres < HERE_RADIUS)
        {
            return labels.Here;
        }

        if (metres < 999.5d)
        {
            long nearest = (long)Math.Round(metres, MidpointRounding.AwayFromZero);
            return Fmt(labels.Distance, Fmt(labels.Meters, nearest.ToString(CultureInfo.InvariantCulture)));
        }

        long tenths = (long)Math.Round(metres / 100d, MidpointRounding.AwayFromZero);
        string kilometres = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", tenths / 10, tenths % 10);
        return Fmt(labels.Distance, Fmt(labels.Kilometers, kilometres));
    }

    /// <summary>The depth as the compass of the HUD shows it (floor of the metres below the sea surface); "Surface" within one metre of it, "Above water" higher; "Depth ?" when the position is not known.</summary>
    public static string DepthText(Point3 remote, float oceanLevel, StatsLabels labels)
    {
        if (!remote.IsKnown)
        {
            return Fmt(labels.Depth, "?");
        }

        double level = IsFinite(oceanLevel) ? oceanLevel : 0d;
        double depth = level - remote.Y;
        if (depth >= 1d)
        {
            double metres = Math.Min(Math.Floor(depth), MAX_DEPTH);
            return Fmt(labels.Depth, Fmt(labels.Meters, ((int)metres).ToString(CultureInfo.InvariantCulture)));
        }

        return depth > -1d ? labels.Surface : labels.AboveWater;
    }

    /// <summary>The name of the vehicle or place; nothing for "no vehicle" and for an unknown one (a line that says "no vehicle" is a line that can be wrong).</summary>
    public static string VehicleText(RemoteVehicleKind kind, StatsLabels labels)
    {
        switch (kind)
        {
            case RemoteVehicleKind.Seamoth:
                return labels.Seamoth;
            case RemoteVehicleKind.Prawn:
                return labels.Prawn;
            case RemoteVehicleKind.Cyclops:
                return labels.Cyclops;
            case RemoteVehicleKind.Base:
                return labels.Base;
            case RemoteVehicleKind.Lifepod:
                return labels.Lifepod;
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Which of the things a player is in counts: a vehicle he drives first (Seamoth, Prawn suit; any other <c>Vehicle</c> is unknown), then the Cyclops (the chair or the hull he stands in), a base,
    /// the lifepod, else no vehicle. The nearest container wins, so a Seamoth that is docked in a base reads "Seamoth".
    /// </summary>
    public static RemoteVehicleKind ClassifyVehicle(bool hasVehicle, bool isSeamoth, bool isPrawn, bool inCyclops, bool inBase, bool inLifepod)
    {
        if (hasVehicle)
        {
            return isSeamoth ? RemoteVehicleKind.Seamoth : isPrawn ? RemoteVehicleKind.Prawn : RemoteVehicleKind.Unknown;
        }

        if (inCyclops)
        {
            return RemoteVehicleKind.Cyclops;
        }

        if (inBase)
        {
            return RemoteVehicleKind.Base;
        }

        return inLifepod ? RemoteVehicleKind.Lifepod : RemoteVehicleKind.None;
    }

    /// <summary>Food and water only mean something where the game requires survival (they stand still in Freedom and Creative); health in every mode but Creative (nothing can hurt a creative player).</summary>
    public static VitalsParts PartsFor(SubnauticaGameMode mode)
    {
        switch (mode)
        {
            case SubnauticaGameMode.CREATIVE:
                return VitalsParts.None;
            case SubnauticaGameMode.FREEDOM:
                return VitalsParts.Health;
            default:
                return VitalsParts.All;
        }
    }

    /// <summary>
    /// Whether the lines are rebuilt now: only while the tab is shown, never for a clock that is not a number, at once when nothing was built yet (<paramref name="lastRefresh" /> is NaN or negative
    /// infinity), when the clock went back, or when <paramref name="interval" /> has passed. A broken interval (not positive, not a number) is <see cref="REFRESH_INTERVAL" />. The caller sets the
    /// last refresh to <paramref name="now" /> (not to last + interval, so that a stall is not followed by a burst).
    /// </summary>
    public static bool ShouldRefresh(float now, float lastRefresh, float interval, bool tabIsShown)
    {
        if (!tabIsShown || !IsFinite(now))
        {
            return false;
        }

        if (float.IsNaN(lastRefresh) || float.IsNegativeInfinity(lastRefresh))
        {
            return true;
        }

        if (!IsFinite(interval) || interval <= 0f)
        {
            interval = REFRESH_INTERVAL;
        }

        float elapsed = now - lastRefresh;
        return elapsed < 0f || elapsed >= interval;
    }

    /// <summary>The height of a row that shows the name and the lines: the height of a one-line row at least, else the height of the text plus a margin above and below.</summary>
    public static float RowHeight(float oneLine, float textHeight, float margin)
    {
        if (!IsFinite(textHeight) || !IsFinite(margin))
        {
            return oneLine;
        }

        return Math.Max(oneLine, textHeight + 2f * margin);
    }

    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string Fmt(string format, string value)
    {
        return string.Format(CultureInfo.InvariantCulture, format, value);
    }
}
