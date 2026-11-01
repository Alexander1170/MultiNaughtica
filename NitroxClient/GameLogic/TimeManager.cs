using System;
using System.Diagnostics;
using Nitrox.Model.Networking;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

public partial class TimeManager
{
    /// <summary>
    ///     When first player connects to the server, time will resume when time will be resumed on server-side.
    ///     According to this, we need to freeze time on first player connecting before it has fully loaded.
    /// </summary>
    private bool freezeTime = true;

    /// <summary>
    ///     Raised once, by the first time packet after the join of the FIRST player has completed (the moment <see cref="freezeTime" /> ends and <see cref="RealTimeElapsed" /> runs again). What a client
    ///     computed from the frozen clock while it was joining can be computed again with the clock that runs (the radius of the Aurora's radiation field, bug of the first live run of story_radiation:
    ///     the first player's radius was 0.66 m too high after a world restart). Never raised for a player who joins a world that is already running.
    /// </summary>
    public event Action ClockReleased;

    /// <summary>
    ///     The latest game time the server reported (its value and the server UTC moment it was read). Every <see cref="TimeChange"/> REPLACES it, nothing is ever added to it
    ///     (see <see cref="GameClockRules"/>).
    /// </summary>
    private readonly ServerClockSample gameClock = new();

    /// <summary>
    ///     The latest real (active play) time the server reported, same moment as <see cref="gameClock"/> except right after <see cref="InitRealTimeElapsed"/>.
    /// </summary>
    private readonly ServerClockSample activeClock = new();

    public float AuroraRealExplosionTime { get; set; }

    private const double DEFAULT_REAL_TIME = 0;

    /// <summary>
    ///     Calculates the server's real time elapsed from the latest reading (<see cref="activeClock"/>) and the time between
    ///     <see cref="ServerUtcNow"/> and the moment of that reading.
    /// </summary>
    public double RealTimeElapsed
    {
        get
        {
            // Unitialized state
            if (!activeClock.IsSet)
            {
                return DEFAULT_REAL_TIME;
            }
            if (freezeTime)
            {
                return activeClock.RegisteredSeconds;
            }

            return activeClock.Project(ServerUtcNow());
        }
    }

    private const double DEFAULT_SUBNAUTICA_TIME = 480;

    /// <summary>
    ///     Calculates the server's exact time from the latest reading (<see cref="gameClock"/>) and the time between
    ///     <see cref="ServerUtcNow"/> and the moment of that reading. One game second per real second in between (<see cref="GameClockRules"/>).
    /// </summary>
    /// <remarks>
    ///     This should only be used for DayNigthCycle internal calculations so that we don't use different times during the same frame.
    ///     Use <see cref="DayNightCycle.timePassed"/> instead to get the current frame's time.
    /// </remarks>
    public double CurrentTime
    {
        get
        {
            // Unitialized state
            if (!gameClock.IsSet)
            {
                return DEFAULT_SUBNAUTICA_TIME;
            }
            if (freezeTime)
            {
                return gameClock.RegisteredSeconds;
            }
            return gameClock.Project(ServerUtcNow());
        }
    }

    /// <summary>
    ///     Real deltaTime between two updates of local time through <see cref="DayNightCycle_Update_Patch"/>
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Replaces <see cref="Time.deltaTime"/> because it is capped by <see cref="Time.maximumDeltaTime"/>
    ///     and may not reflect the real time which has passed between two frames once it's higher than the said maximum
    ///     <br/>See <a href="https://docs.unity3d.com/ScriptReference/Time-maximumDeltaTime.html">Time.maximumDeltaTime</a>
    /// </para>
    /// <para>
    ///     It is measured on a monotonic high resolution timestamp (<see cref="FrameDeltaMeter"/>), not as the difference of two UTC clock readings: the UTC clock moves in steps of its resolution
    ///     and the sum of the deltas then ran up to 1.5 times faster than real time. A time skip does not touch it (the clock jumps, the frame does not take longer), so there is no
    ///     day worth of energy consumed just when 24 in-game hours were skipped.
    /// </para>
    /// </remarks>
    public float DeltaTime = 0;

    private readonly FrameDeltaMeter frameDeltaMeter = new();

    /// <summary>
    ///     Sum of every <see cref="DeltaTime"/> handed out, only to measure its rate (<see cref="ReportResync"/>, the clock probe of the harness).
    /// </summary>
    public double DeltaTimeSum { get; private set; }

    /// <summary>
    ///     The game time of the latest <see cref="TimeChange"/> and the server's UTC moment it was read at (for the harness: the server's clock rate is their difference quotient).
    /// </summary>
    public double LatestServerSeconds => gameClock.RegisteredSeconds;
    public DateTimeOffset LatestServerStamp => gameClock.RegisteredAt;

    /// <summary>
    ///     The monotonic timestamp of the latest <see cref="CalculateCurrentTime"/> (the latest frame).
    /// </summary>
    private long lastFrameTimestamp;

    /// <summary>
    ///     State of the previous resync, to measure the rate of this client's clock between two resyncs (<see cref="GameClockRules.DescribeResync"/>).
    /// </summary>
    private long lastResyncTimestamp;
    private double lastResyncLocalClock = double.NaN;
    private double lastResyncDeltaSum;
    private int resyncCount;

    /// <summary>
    /// Delay during which we consider that effects from a recent <see cref="TimeChange"/> packet might affect some systems.
    /// </summary>
    public static readonly TimeSpan RECENTLY_PROCESSED_DELAY = TimeSpan.FromSeconds(3);

    public TimeManager(NtpSyncer ntpSyncer)
    {
        this.ntpSyncer = ntpSyncer;
    }

    public void ProcessUpdate(TimeChange packet)
    {
        // What the clock shows right now, before this packet replaces the sample: what the clock shows after it, minus this, is the step the resync makes (milliseconds when all is well)
        bool hadSample = gameClock.IsSet;
        double clockBefore = CurrentTime;
        long nowTimestamp = Stopwatch.GetTimestamp();

        bool clockReleased = false;
        if (freezeTime && Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted)
        {
            freezeTime = false;
            clockReleased = true;
        }
        DateTimeOffset registrationTime = DateTimeOffset.FromUnixTimeMilliseconds(packet.UpdateTime);
        activeClock.Register(packet.RealTimeElapsed, registrationTime);
        gameClock.Register(packet.CurrentTime, registrationTime);
        if (clockReleased)
        {
            RaiseClockReleased();
        }

        // No need to re-initialize the fields with the same data
        if (!serverOnlineMode)
        {
            SetServerCorrectionData(packet.OnlineMode, packet.UtcCorrectionTicks);
        }

        // If the server is in online mode, the client should try to get to online mode too
        if (!clientOnlineMode && serverOnlineMode)
        {
            AttemptNtpSync();
        }

        // The packet also reaches a client that is connected but still in the main menu (waiting for a name): the world with its clock does not exist yet and the initial
        // sync sets the time. Everything above is kept for that.
        if (!DayNightCycle.main)
        {
            return;
        }

        // The day/night clock is SET to the server's clock, never advanced by a difference: the packet replaced the sample above, so there is nothing to count twice.
        // The frame delta (DeltaTime) is not touched: a time skip moves the clock but the frame did not take longer.
        double localBefore = DayNightCycle.main.timePassedAsDouble;
        DayNightCycle.main.timePassedAsDouble = CurrentTime;

        DayNightCycle.main.StopSkipTimeMode();

        ReportResync(packet, registrationTime, hadSample ? CurrentTime - clockBefore : double.NaN, localBefore, nowTimestamp);
    }

    private void RaiseClockReleased()
    {
        try
        {
            ClockReleased?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "The clock of the first player was released, but something that waited for it failed");
        }
    }

    /// <summary>
    ///     One log line per resync (one per minute): the step it made, the rate this client's clock and its delta sum had since the previous resync, the UTC estimate in use. Reading the log
    ///     of a session with it answers whether a clock ran at one game second per real second and how far each resync moved it.
    /// </summary>
    private void ReportResync(TimeChange packet, DateTimeOffset registrationTime, double stepSeconds, double localBefore, long nowTimestamp)
    {
        resyncCount++;

        // The window runs from the previous resync to the last frame that was drawn before this packet (that is the frame localBefore belongs to)
        double windowSeconds = lastResyncTimestamp != 0 && lastFrameTimestamp > lastResyncTimestamp ? (lastFrameTimestamp - lastResyncTimestamp) / (double)Stopwatch.Frequency : double.NaN;
        double localRate = double.NaN;
        double deltaRate = double.NaN;
        if (!double.IsNaN(windowSeconds) && !double.IsNaN(lastResyncLocalClock))
        {
            localRate = GameClockRules.Rate(localBefore - lastResyncLocalClock, windowSeconds);
            deltaRate = GameClockRules.Rate(DeltaTimeSum - lastResyncDeltaSum, windowSeconds);
        }

        double packetAgeSeconds = (ServerUtcNow() - registrationTime).TotalSeconds;
        Log.Info(GameClockRules.DescribeResync(resyncCount, packet.CurrentTime, registrationTime, packetAgeSeconds, stepSeconds, localRate, deltaRate, double.IsNaN(windowSeconds) ? 0d : windowSeconds,
                                               GameClockRules.UsesNtp(clientOnlineMode, serverOnlineMode), ServerUtcOffset.TotalSeconds));

        lastResyncTimestamp = nowTimestamp;
        lastResyncLocalClock = DayNightCycle.main.timePassedAsDouble;
        lastResyncDeltaSum = DeltaTimeSum;
    }

    /// <remarks>
    /// Sets <see cref="DeltaTime"/> accordingly
    /// </remarks>
    /// <returns>The newly calculated time from <see cref="CurrentTime"/></returns>
    public double CalculateCurrentTime()
    {
        long timestamp = Stopwatch.GetTimestamp();
        // The first frame has nothing to measure from; later ones are the real time since the previous frame (never zero: a delta of 0 ends up in a division by zero => NaN in some scripts)
        DeltaTime = frameDeltaMeter.Next(timestamp, Stopwatch.Frequency, Time.deltaTime);
        DeltaTimeSum += DeltaTime;
        lastFrameTimestamp = timestamp;
        return CurrentTime;
    }

    public void InitRealTimeElapsed(double realTimeElapsed, long registrationTime, bool isFirstPlayer)
    {
        activeClock.Register(realTimeElapsed, DateTimeOffset.FromUnixTimeMilliseconds(registrationTime));
        freezeTime = isFirstPlayer;
    }
}
