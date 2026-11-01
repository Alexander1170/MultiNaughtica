using System;
using System.Globalization;

namespace NitroxClient.GameLogic;

/// <summary>
/// The clock that stamps and reads the movement snapshots (<c>TimeManager.RealTimeElapsed</c>, the server's active play time), with a test setting for Debug builds only
/// (a harness script that is not part of this repository, bug 7). A harness world is a few hundred seconds old, while the problem of float timestamps grows with the age of the world (the step of a float is 3.9 ms from 9.1 h of
/// play), so a run could never show it. <c>NITROX_TIME_TEST=offset=30000,float=1</c> on the clients makes every movement stamp and every movement "now" read <c>real + 30000 s</c> (an 8.3 h old world) and,
/// with <c>float=1</c>, rounds it to a float, which is exactly what the replicators did before the change: the same build runs both behaviours, at any age. In a Release build none of this exists:
/// <see cref="Now" /> returns its argument.
/// </summary>
public static class MovementClock
{
    public readonly record struct Setting(double OffsetSeconds, bool FloatTimes);

    /// <summary>
    /// Reads "offset=30000,float=1" (keys in any order, separated by commas; unknown keys and unreadable values are ignored). Nothing set: no offset, no rounding.
    /// </summary>
    public static Setting Parse(string? text)
    {
        double offset = 0;
        bool floatTimes = false;
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string part in text!.Split(','))
            {
                string[] pair = part.Split('=');
                if (pair.Length != 2)
                {
                    continue;
                }

                string key = pair[0].Trim();
                string value = pair[1].Trim();
                if (key.Equals("offset", StringComparison.OrdinalIgnoreCase) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                {
                    offset = seconds;
                }
                else if (key.Equals("float", StringComparison.OrdinalIgnoreCase))
                {
                    floatTimes = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return new Setting(offset, floatTimes);
    }

    /// <summary>
    /// The reading of the clock under a setting: the real time plus the offset, rounded to a float when asked to.
    /// </summary>
    public static double Apply(Setting setting, double realTimeElapsed)
    {
        double time = realTimeElapsed + setting.OffsetSeconds;
        return setting.FloatTimes ? (float)time : time;
    }

    /// <summary>
    /// <paramref name="time" /> plus <paramref name="period" /> the way the code before bug 7 added them: all operands were floats, so the sum was rounded to the float grid (the sender's threshold
    /// "now is not before the last broadcast plus one period"). Under the float setting the emulation rounds the sum too; otherwise it is the exact sum.
    /// </summary>
    public static double Plus(Setting setting, double time, float period)
    {
        double sum = time + period;
        return setting.FloatTimes ? (float)sum : sum;
    }

    public static string Describe(Setting setting)
    {
        return setting.OffsetSeconds == 0 && !setting.FloatTimes ? "real" : $"offset={setting.OffsetSeconds.ToString("F0", CultureInfo.InvariantCulture)}{(setting.FloatTimes ? "+float" : "")}";
    }

#if DEBUG
    private static readonly Setting setting = Parse(Environment.GetEnvironmentVariable("NITROX_TIME_TEST"));

    public static double Now(double realTimeElapsed) => Apply(setting, realTimeElapsed);

    public static double Plus(double time, float period) => Plus(setting, time, period);

    public static string Describe() => Describe(setting);
#else
    public static double Now(double realTimeElapsed) => realTimeElapsed;

    public static double Plus(double time, float period) => time + period;

    public static string Describe() => "real";
#endif
}
