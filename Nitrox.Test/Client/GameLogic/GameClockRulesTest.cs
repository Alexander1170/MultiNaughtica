using System.Globalization;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic;

/// <summary>
///     Bug 8 (the day/night cycle seemed too fast in two-player play): the arithmetic of the shared game clock must run at one game second per real second between the resyncs and a resync must
///     replace the sample, never add to it. The simulation below plays the packets of <c>TimeService</c> against the projection <c>TimeManager</c> does (the same <see cref="ServerClockSample" />
///     and <see cref="GameClockRules" />), with two computers whose UTC clocks are wrong by different amounts and a network that needs 40 ms.
/// </summary>
[TestClass]
public class GameClockRulesTest
{
    private static readonly DateTimeOffset epoch = new(2026, 10, 25, 22, 0, 0, TimeSpan.Zero);

    private const double GAME_START = 480d;
    private const double FRAME_SECONDS = 1d / 60d;

    private static DateTimeOffset Utc(double trueSeconds, double clockError)
    {
        return epoch.AddTicks((long)Math.Round((trueSeconds + clockError) * TimeSpan.TicksPerSecond));
    }

    private static TimeSpan Secs(double seconds)
    {
        return TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    }

    private readonly record struct Frame(double T, double Clock, double Active);

    /// <summary>
    ///     Five minutes of play at 60 frames per second. The server's game time is <c>GAME_START + true seconds + skips</c>, its UTC clock is wrong by <see cref="ServerClockError" /> and the
    ///     client's by <see cref="ClientClockError" />. The client's idea of the server's UTC clock is the real offset plus <see cref="UtcEstimateError" />.
    /// </summary>
    private sealed class Run
    {
        public double ServerClockError = -1.25d;
        public double ClientClockError = 37.5d;
        public double UtcEstimateError;
        public double Duration = 300d;
        public double ResyncSeconds = 60d;
        public double LatencySeconds = 0.040d;
        public double SkipAt = -1d;
        public double SkipSeconds;

        public readonly ServerClockSample Game = new();
        public readonly ServerClockSample ActivePlay = new();
        public readonly List<double> Steps = [];
        public readonly List<Frame> Frames = [];

        public Run Play()
        {
            // Both computers know their offset to real UTC from NTP (a correction is real UTC minus the clock of the computer)
            TimeSpan offset = GameClockRules.ServerUtcOffset(true, true, Secs(-ClientClockError), Secs(-ServerClockError), TimeSpan.Zero) + Secs(UtcEstimateError);

            double skipped = 0d;
            bool skipDone = false;
            double nextSend = 0d;
            Queue<(double Arrives, TimeChange Packet)> inFlight = new();

            for (long index = 0; index * FRAME_SECONDS <= Duration; index++)
            {
                double t = index * FRAME_SECONDS;

                if (SkipAt >= 0d && !skipDone && t >= SkipAt)
                {
                    // /time or a bed: an absolute step of the server's game time, broadcast at once
                    skipped += SkipSeconds;
                    skipDone = true;
                    inFlight.Enqueue((t + LatencySeconds, MakePacket(t, skipped)));
                }

                while (nextSend <= t)
                {
                    inFlight.Enqueue((nextSend + LatencySeconds, MakePacket(nextSend, skipped)));
                    nextSend += ResyncSeconds;
                }

                while (inFlight.Count > 0 && inFlight.Peek().Arrives <= t)
                {
                    (double arrives, TimeChange packet) = inFlight.Dequeue();
                    DateTimeOffset stamp = DateTimeOffset.FromUnixTimeMilliseconds(packet.UpdateTime);
                    DateTimeOffset estimateAtArrival = Utc(arrives, ClientClockError) + offset;
                    double before = Game.IsSet ? Game.Project(estimateAtArrival) : double.NaN;
                    Game.Register(packet.CurrentTime, stamp);
                    ActivePlay.Register(packet.RealTimeElapsed, stamp);
                    Steps.Add(Game.Project(estimateAtArrival) - before);
                }

                if (Game.IsSet)
                {
                    DateTimeOffset estimate = Utc(t, ClientClockError) + offset;
                    Frames.Add(new Frame(t, Game.Project(estimate), ActivePlay.Project(estimate)));
                }
            }

            return this;
        }

        private TimeChange MakePacket(double t, double skipped)
        {
            // What TimeService.MakeTimePacket stamps: the game time, the server's UTC moment (milliseconds) and the active play time (a skip is not play time)
            return new TimeChange(GAME_START + t + skipped, Utc(t, ServerClockError).ToUnixTimeMilliseconds(), t, true, Secs(-ServerClockError).Ticks);
        }

        public double RateBetween(double fromT, double toT)
        {
            Frame start = Frames.First(frame => frame.T >= fromT);
            Frame end = Frames.Last(frame => frame.T <= toT);
            return GameClockRules.Rate(end.Clock - start.Clock, end.T - start.T);
        }
    }

    [TestMethod]
    public void TheClockRunsAtOneGameSecondPerRealSecondOverFiveMinutesWithAResyncEveryMinute()
    {
        Run run = new Run().Play();

        // The whole run, from the first packet to the last frame
        Frame first = run.Frames[0];
        Frame last = run.Frames[run.Frames.Count - 1];
        GameClockRules.Rate(last.Clock - first.Clock, last.T - first.T).Should().BeApproximately(1d, 0.001);
        // And every minute of it, resyncs included
        for (double minute = 0.1d; minute < 240d; minute += 60d)
        {
            run.RateBetween(minute, minute + 60d).Should().BeApproximately(1d, 0.001, $"the minute starting at {minute} s");
        }
    }

    [TestMethod]
    public void TheClockNeverLeavesTheServersGameTimeByMoreThanTheMillisecondOfTheStamp()
    {
        Run run = new Run().Play();

        run.Frames.Should().NotBeEmpty();
        foreach (Frame frame in run.Frames)
        {
            frame.Clock.Should().BeApproximately(GAME_START + frame.T, 0.002, $"at {frame.T} s");
            frame.Active.Should().BeApproximately(frame.T, 0.002, $"at {frame.T} s");
        }
    }

    [TestMethod]
    public void AResyncMakesNoStepWhenTheServersUtcClockIsKnownAndOnlyTheFirstPacketHasNothingToCompareWith()
    {
        Run run = new Run().Play();

        // Packets sent at 0, 60, 120, 180 and 240 s arrive inside the run (the one of 300 s is still on its way)
        run.Steps.Should().HaveCount(5);
        double.IsNaN(run.Steps[0]).Should().BeTrue();
        foreach (double step in run.Steps.Skip(1))
        {
            Math.Abs(step).Should().BeLessThan(0.002);
            GameClockRules.ClassifyStep(step).Should().Be(GameClockRules.StepKind.Normal);
        }
    }

    [TestMethod]
    public void AWrongUtcEstimateShiftsTheClockByAConstantAmountAndNeverChangesItsRate()
    {
        // The clock sync procedure (or NTP) was 3 seconds off: the client reads the server clock 3 seconds late, at the same rate
        Run run = new Run { UtcEstimateError = -3d }.Play();

        foreach (Frame frame in run.Frames)
        {
            frame.Clock.Should().BeApproximately(GAME_START + frame.T - 3d, 0.002, $"at {frame.T} s");
        }
        foreach (double step in run.Steps.Skip(1))
        {
            Math.Abs(step).Should().BeLessThan(0.002, "a constant error does not move at a resync");
        }
        run.RateBetween(0.1d, 300d).Should().BeApproximately(1d, 0.001);
    }

    [TestMethod]
    public void ATimeSkipIsOneAbsoluteStepAndTheRateBeforeAndAfterIsStillOne()
    {
        // A bed (396 game seconds) 150 seconds into the run, announced by an extra TimeChange
        Run run = new Run { SkipAt = 150d, SkipSeconds = 396d }.Play();

        run.Steps.Should().HaveCount(6);
        double[] big = run.Steps.Skip(1).Where(step => Math.Abs(step) > 1d).ToArray();
        big.Should().ContainSingle();
        big[0].Should().BeApproximately(396d, 0.002);
        GameClockRules.ClassifyStep(big[0]).Should().Be(GameClockRules.StepKind.Jump);

        run.RateBetween(0.1d, 149d).Should().BeApproximately(1d, 0.001);
        run.RateBetween(151d, 300d).Should().BeApproximately(1d, 0.001);
        // Afterwards the clock is the server's, 396 seconds on, and the active play time does not count the skip
        Frame last = run.Frames[run.Frames.Count - 1];
        last.Clock.Should().BeApproximately(GAME_START + last.T + 396d, 0.002);
        last.Active.Should().BeApproximately(last.T, 0.002);
    }

    [TestMethod]
    public void ASlowNetworkDoesNotMakeTheClockLagBecauseThePacketCarriesItsStamp()
    {
        // 400 ms of latency: the stamp makes the age of the packet irrelevant, the clock does not lag behind by the latency
        Run run = new Run { LatencySeconds = 0.4d }.Play();

        foreach (Frame frame in run.Frames)
        {
            frame.Clock.Should().BeApproximately(GAME_START + frame.T, 0.002, $"at {frame.T} s");
        }
        foreach (double step in run.Steps.Skip(1))
        {
            Math.Abs(step).Should().BeLessThan(0.002);
        }
    }

    [TestMethod]
    public void ARepeatedPacketLeavesTheClockWhereItWas()
    {
        ServerClockSample sample = new();
        DateTimeOffset stamp = epoch;
        sample.Register(1000d, stamp);
        double first = sample.Project(stamp.AddSeconds(30));

        for (int i = 0; i < 10; i++)
        {
            sample.Register(1000d, stamp);
        }

        first.Should().Be(1030d);
        sample.Project(stamp.AddSeconds(30)).Should().Be(first);
    }

    [TestMethod]
    public void AnOlderPacketOfTheSameClockGivesTheSameTimeBecauseItReplacesTheSample()
    {
        ServerClockSample sample = new();
        sample.Register(1000d, epoch);
        // A packet that was stamped 1 s earlier, arriving late: the server's clock read 999 then
        sample.Register(999d, epoch.AddSeconds(-1));

        sample.Project(epoch.AddSeconds(30)).Should().BeApproximately(1030d, 1e-9);
    }

    [TestMethod]
    public void ASampleThatWasNeverRegisteredIsNotSet()
    {
        new ServerClockSample().IsSet.Should().BeFalse();
    }

    [TestMethod]
    public void ProjectionAddsRealSecondsOneForOne()
    {
        GameClockRules.Project(480d, 0d).Should().Be(480d);
        GameClockRules.Project(480d, 1200d).Should().Be(1680d);
        GameClockRules.Project(19135d, 0.5d).Should().Be(19135.5d);
        GameClockRules.GAME_SECONDS_PER_REAL_SECOND.Should().Be(1d);
        GameClockRules.DAY_LENGTH_SECONDS.Should().Be(1200d);
    }

    [TestMethod]
    public void BothSidesNeedNtpForTheNtpFormula()
    {
        TimeSpan clientCorrection = TimeSpan.FromSeconds(1.2765);
        TimeSpan serverCorrection = TimeSpan.FromSeconds(1.2537);
        TimeSpan delta = TimeSpan.FromSeconds(0.5);

        GameClockRules.ServerUtcOffset(true, true, clientCorrection, serverCorrection, delta).Should().Be(clientCorrection - serverCorrection);
        GameClockRules.ServerUtcOffset(true, false, clientCorrection, serverCorrection, delta).Should().Be(delta);
        GameClockRules.ServerUtcOffset(false, true, clientCorrection, serverCorrection, delta).Should().Be(delta);
        GameClockRules.ServerUtcOffset(false, false, clientCorrection, serverCorrection, delta).Should().Be(delta);
        GameClockRules.UsesNtp(true, true).Should().BeTrue();
        GameClockRules.UsesNtp(true, false).Should().BeFalse();
    }

    [TestMethod]
    public void TheNtpFormulaMatchesTheEquationServerUtcPlusServerCorrectionEqualsClientUtcPlusClientCorrection()
    {
        // The host's log of 2026-10-24: server correction 1.2537 s, client correction 1.2765 s (the same computer, 23 ms apart)
        double realSeconds = 1000d;
        double serverClockError = -1.2537d;
        double clientClockError = -1.2765d;
        TimeSpan offset = GameClockRules.ServerUtcOffset(true, true, Secs(-clientClockError), Secs(-serverClockError), TimeSpan.Zero);

        DateTimeOffset serverUtc = Utc(realSeconds, serverClockError);
        DateTimeOffset clientUtc = Utc(realSeconds, clientClockError);

        (clientUtc + offset).Should().BeCloseTo(serverUtc, TimeSpan.FromTicks(2));
    }

    [TestMethod]
    public void TheRateOfAWindowIsGameSecondsOverRealSecondsAndNotANumberWhenEmpty()
    {
        GameClockRules.Rate(60d, 60d).Should().Be(1d);
        GameClockRules.Rate(120d, 60d).Should().Be(2d);
        GameClockRules.Rate(30d, 60d).Should().Be(0.5d);
        double.IsNaN(GameClockRules.Rate(5d, 0d)).Should().BeTrue();
        double.IsNaN(GameClockRules.Rate(5d, -1d)).Should().BeTrue();
    }

    [TestMethod]
    public void AStepIsNormalAdjustedOrAJumpBySize()
    {
        GameClockRules.ClassifyStep(0d).Should().Be(GameClockRules.StepKind.Normal);
        GameClockRules.ClassifyStep(0.25d).Should().Be(GameClockRules.StepKind.Normal);
        GameClockRules.ClassifyStep(-0.25d).Should().Be(GameClockRules.StepKind.Normal);
        GameClockRules.ClassifyStep(0.26d).Should().Be(GameClockRules.StepKind.Adjusted);
        GameClockRules.ClassifyStep(-5d).Should().Be(GameClockRules.StepKind.Adjusted);
        GameClockRules.ClassifyStep(5.01d).Should().Be(GameClockRules.StepKind.Jump);
        GameClockRules.ClassifyStep(-396d).Should().Be(GameClockRules.StepKind.Jump);
    }

    [TestMethod]
    public void TheResyncLogLineNamesTheRateTheStepAndTheUtcEstimate()
    {
        DateTimeOffset stamp = new(2026, 10, 24, 22, 20, 37, 123, TimeSpan.Zero);

        string line = GameClockRules.DescribeResync(7, 19235.1234d, stamp, 0.0412d, 0.0021d, 1.000021d, 0.99998d, 60.04d, true, -0.023d);

        line.Should().Be("[Clock] resync #7: server game time 19235.123 s at server UTC 2026-10-24T22:20:37.123Z, packet age 0.041 s, clock step +0.002 s (Normal), local clock 1.00002 game s per real s over 60.0 s, delta sum 0.99998, server UTC = local UTC -0.023 s via NTP");
    }

    [TestMethod]
    public void TheFirstResyncLogLineHasNoStepAndNoRateYet()
    {
        string line = GameClockRules.DescribeResync(1, 480d, epoch, 0.5d, double.NaN, double.NaN, double.NaN, 0d, false, 0d);

        line.Should().Contain("clock step n/a (first packet)");
        line.Should().Contain("local clock n/a (first window)");
        line.Should().Contain("via clock sync delta");
    }

    [TestMethod]
    public void TheResyncLogLineIsTheSameInEveryCulture()
    {
        CultureInfo german;
        try
        {
            german = new CultureInfo("de-DE");
        }
        catch (CultureNotFoundException)
        {
            Assert.Inconclusive("This machine has no German culture to check the decimal comma with");
            return;
        }

        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = german;
            string line = GameClockRules.DescribeResync(2, 1234.5d, epoch, 0.04d, -0.001d, 1.00001d, 1d, 60d, true, 1.5d);

            line.Should().Contain("server game time 1234.500 s");
            line.Should().Contain("clock step -0.001 s");
            line.Should().NotContain("1234,500");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
