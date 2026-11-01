using System;
using System.Globalization;

namespace NitroxClient.GameLogic;

/// <summary>
///     The arithmetic of the shared game clock as plain numbers (no game types), so that it can be tested. <see cref="TimeManager" /> applies it.
/// </summary>
/// <remarks>
///     <para>
///         The clock is the server's. The server stamps every <c>TimeChange</c> with its game time and with its own UTC moment. A client keeps the latest stamp (<see cref="ServerClockSample" />) and
///         shows <c>stamped game time + (the server UTC moment now - the stamped UTC moment)</c>. Between two packets that runs at one game second per real second: a vanilla day is
///         <see cref="DAY_LENGTH_SECONDS" /> game seconds long at <c>DayNightCycle.dayNightSpeed</c> 1 (the daylight lasts 900 of them, sunrise at 1/8 and sunset at 7/8 of the day).
///     </para>
///     <para>
///         A new packet REPLACES the sample, it is never added to the local clock. A resync therefore cannot count time twice: the step it makes is only the error the old sample had collected
///         (milliseconds when the UTC estimate of the server is good). A time skip (sleeping in a bed, <c>/time</c>) is an absolute step of the server's game time, it never changes the speed.
///     </para>
///     <para>
///         <c>RealTimeElapsed</c> (active play time) works the same way with its own sample.
///     </para>
/// </remarks>
public static class GameClockRules
{
    /// <summary>
    ///     Game seconds that pass per real second (<c>DayNightCycle.dayNightSpeed</c> 1). The server's clock is a stopwatch at this rate; the client projects at the same rate.
    /// </summary>
    public const double GAME_SECONDS_PER_REAL_SECOND = 1d;

    /// <summary>
    ///     Game seconds of a full day (<c>DayNightCycle.kDayLengthSeconds</c>).
    /// </summary>
    public const double DAY_LENGTH_SECONDS = 1200d;

    /// <summary>
    ///     A resync that moves the clock by at most this many seconds is the normal noise of the network and of the NTP estimate.
    /// </summary>
    public const double NORMAL_STEP_SECONDS = 0.25d;

    /// <summary>
    ///     A resync that moves the clock by more than this is a jump: a time skip, a wake-up of the server, or an UTC estimate that was wrong.
    /// </summary>
    public const double JUMP_STEP_SECONDS = 5d;

    public enum StepKind
    {
        Normal,
        Adjusted,
        Jump,
    }

    /// <summary>
    ///     The game clock a sample gives <paramref name="elapsedRealSeconds" /> after it was stamped.
    /// </summary>
    public static double Project(double registeredSeconds, double elapsedRealSeconds)
    {
        return registeredSeconds + elapsedRealSeconds * GAME_SECONDS_PER_REAL_SECOND;
    }

    /// <summary>
    ///     Whether both sides know their offset to real UTC from NTP. Otherwise the clock sync procedure's delta is the only link between the two computers.
    /// </summary>
    public static bool UsesNtp(bool clientOnline, bool serverOnline)
    {
        return clientOnline && serverOnline;
    }

    /// <summary>
    ///     What has to be added to the local UTC clock to get the server's UTC clock. With NTP on both sides: Server UTC + server correction = Client UTC + client correction (a correction is
    ///     real UTC minus the computer's clock). Without: the measured delta (server UTC minus client UTC).
    /// </summary>
    public static TimeSpan ServerUtcOffset(bool clientOnline, bool serverOnline, TimeSpan clientCorrection, TimeSpan serverCorrection, TimeSpan correctionDelta)
    {
        return UsesNtp(clientOnline, serverOnline) ? clientCorrection - serverCorrection : correctionDelta;
    }

    /// <summary>
    ///     Game seconds per real second a clock ran at over a window, <see cref="double.NaN" /> for an empty window.
    /// </summary>
    public static double Rate(double gameSecondsPassed, double realSecondsPassed)
    {
        return realSecondsPassed > 0d ? gameSecondsPassed / realSecondsPassed : double.NaN;
    }

    public static StepKind ClassifyStep(double stepSeconds)
    {
        double size = Math.Abs(stepSeconds);
        if (size <= NORMAL_STEP_SECONDS)
        {
            return StepKind.Normal;
        }
        return size <= JUMP_STEP_SECONDS ? StepKind.Adjusted : StepKind.Jump;
    }

    /// <summary>
    ///     The log line of one resync (one per minute and client). Reading a session's log with it answers whether a clock ran at the vanilla rate between the resyncs (<paramref name="localRate" />),
    ///     how far a resync moved it (<paramref name="stepSeconds" />) and which UTC estimate of the server it used.
    /// </summary>
    /// <param name="number">How many packets this client has applied.</param>
    /// <param name="serverSeconds">The game time the packet carried.</param>
    /// <param name="serverStamp">The server's UTC moment the packet carried. Two lines give the rate of the SERVER clock: game seconds over stamp seconds.</param>
    /// <param name="packetAgeSeconds">The packet's age on arrival: the client's estimate of the server's UTC minus the packet's stamp.</param>
    /// <param name="stepSeconds">What the clock shows after the packet minus what it showed just before (the old sample projected to the same moment).</param>
    /// <param name="localRate">The game seconds per real second the DayNightCycle of this client advanced since the previous resync, <see cref="double.NaN" /> for the first one.</param>
    /// <param name="deltaRate">The same for the sum of <c>DayNightCycle.deltaTime</c> (what the bioreactors, chargers, solar panels and flashlights integrate), <see cref="double.NaN" /> for the first one.</param>
    /// <param name="windowSeconds">The real seconds since the previous resync.</param>
    /// <param name="usesNtp">See <see cref="UsesNtp" />.</param>
    /// <param name="serverUtcOffsetSeconds">See <see cref="ServerUtcOffset" />.</param>
    public static string DescribeResync(int number, double serverSeconds, DateTimeOffset serverStamp, double packetAgeSeconds, double stepSeconds, double localRate, double deltaRate, double windowSeconds, bool usesNtp,
                                        double serverUtcOffsetSeconds)
    {
        string window = windowSeconds.ToString("F1", CultureInfo.InvariantCulture);
        string rate = double.IsNaN(localRate) ? "n/a (first window)" : $"{localRate.ToString("F5", CultureInfo.InvariantCulture)} game s per real s over {window} s";
        string delta = double.IsNaN(deltaRate) ? "n/a" : deltaRate.ToString("F5", CultureInfo.InvariantCulture);
        string step = double.IsNaN(stepSeconds) ? "n/a (first packet)" : $"{stepSeconds.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)} s ({ClassifyStep(stepSeconds)})";
        string age = packetAgeSeconds.ToString("F3", CultureInfo.InvariantCulture);
        string offset = serverUtcOffsetSeconds.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        string method = usesNtp ? "NTP" : "clock sync delta";
        string stamp = serverStamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        return $"[Clock] resync #{number}: server game time {serverSeconds.ToString("F3", CultureInfo.InvariantCulture)} s at server UTC {stamp}, packet age {age} s, clock step {step}, local clock {rate}, delta sum {delta}, server UTC = local UTC {offset} s via {method}";
    }
}
