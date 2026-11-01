#if DEBUG
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using Nitrox.Model.Core;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "clock" , bug 8: the day/night cycle seemed too fast in two-player play. Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs
// (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command> [arg] [arg]" and log
// "[SyncHarness] <name>: key=value ..." lines (no spaces inside a value).
//
//   clockprobe [seconds] [samples]   every <seconds> (default 30) real seconds, <samples> times (default 11, that is 5 minutes), logs ONE line of this client's clocks:
//                                    n=       number of the sample
//                                    utc=     this computer's UTC clock
//                                    mono=    real seconds since the probe started (Stopwatch, no UTC clock)
//                                    local=   DayNightCycle.timePassedAsDouble (what the sun, the PDA log and every scheduled goal of this client use)
//                                    server=  TimeManager.CurrentTime (the server's clock as this client projects it now)
//                                    err=     local - server (one frame at most)
//                                    pkt=     the game time of the latest TimeChange and pktutc= the server's UTC moment it was stamped (two lines give the SERVER clock's rate)
//                                    rate=    local game seconds per mono second since the previous sample, rate_total= since the first one, delta_rate= the same for the sum of
//                                             DayNightCycle.deltaTime (what bioreactors, chargers, solar panels and flashlights integrate)
//                                    fps=     frames per second over the interval (the old delta ran fast above about 64 fps)
//                                    day=     DayNightCycle.GetDayScalar() (sunrise at 0.125, sunset at 0.875), isday= IsDay()
//                                    offset=  the seconds the server's UTC clock is ahead of this computer's, as this client estimates it
//                                    PASS = every rate, rate_total and delta_rate within 1.00 +- 0.02 on every participant, |err| < 0.1
//   clockprobe stop                  ends a running probe
//   clockstate                       one sample line now (n=0, no rates)
public partial class HarnessConsoleCommands
{
    private Coroutine clockProbeRoutine;

    private struct ClockProbeSample
    {
        public double Mono;
        public double Local;
        public double DeltaSum;
        public int Frame;
    }

    private static string ClockNum(double value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string ClockRate(double gameSeconds, double realSeconds)
    {
        double rate = GameClockRules.Rate(gameSeconds, realSeconds);
        return double.IsNaN(rate) ? "n/a" : ClockNum(rate, "F5");
    }

    private static ClockProbeSample ClockLogSample(int number, TimeManager timeManager, Stopwatch watch, ClockProbeSample first, ClockProbeSample previous)
    {
        ClockProbeSample sample = new()
        {
            Mono = watch.Elapsed.TotalSeconds,
            Local = DayNightCycle.main.timePassedAsDouble,
            DeltaSum = timeManager.DeltaTimeSum,
            Frame = Time.frameCount,
        };
        double server = timeManager.CurrentTime;
        string utc = DateTimeOffset.UtcNow.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string packetUtc = timeManager.LatestServerStamp.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string sinceLast = number > 1 ? ClockRate(sample.Local - previous.Local, sample.Mono - previous.Mono) : "n/a";
        string sinceFirst = number > 1 ? ClockRate(sample.Local - first.Local, sample.Mono - first.Mono) : "n/a";
        string deltaRate = number > 1 ? ClockRate(sample.DeltaSum - previous.DeltaSum, sample.Mono - previous.Mono) : "n/a";
        string fps = number > 1 ? ClockNum((sample.Frame - previous.Frame) / Math.Max(sample.Mono - previous.Mono, 0.001d), "F1") : "n/a";
        string clocks = $"mono={ClockNum(sample.Mono, "F3")} local={ClockNum(sample.Local, "F3")} server={ClockNum(server, "F3")} err={ClockNum(sample.Local - server, "F3")}";
        string packet = $"pkt={ClockNum(timeManager.LatestServerSeconds, "F3")} pktutc={packetUtc}";
        string rates = $"rate={sinceLast} rate_total={sinceFirst} delta_rate={deltaRate} fps={fps}";
        string sky = $"day={ClockNum(DayNightCycle.main.GetDayScalar(), "F3")} isday={DayNightCycle.main.IsDay()} offset={ClockNum(timeManager.ServerUtcOffset.TotalSeconds, "F3")}";

        Log.Info($"[SyncHarness] clockprobe: n={number} utc={utc} {clocks} {packet} {rates} {sky}");
        return sample;
    }

    private void LiveCommand_clockstate(NotificationCenter.Notification n)
    {
        if (!DayNightCycle.main)
        {
            Log.Info("[SyncHarness] clockstate: failed reason=no-daynightcycle");
            return;
        }

        ClockLogSample(0, NitroxServiceLocator.LocateService<TimeManager>(), Stopwatch.StartNew(), default, default);
    }

    private void LiveCommand_clockprobe(NotificationCenter.Notification n)
    {
        if (LiveArgString(n, 0, "") == "stop")
        {
            if (clockProbeRoutine != null)
            {
                StopCoroutine(clockProbeRoutine);
                clockProbeRoutine = null;
            }
            Log.Info("[SyncHarness] clockprobe: stopped");
            return;
        }

        if (!DayNightCycle.main)
        {
            Log.Info("[SyncHarness] clockprobe: failed reason=no-daynightcycle");
            return;
        }

        float interval = Mathf.Clamp(LiveArgFloat(n, 0, 30f), 1f, 600f);
        int samples = Mathf.Clamp((int)LiveArgFloat(n, 1, 11f), 2, 400);
        if (clockProbeRoutine != null)
        {
            StopCoroutine(clockProbeRoutine);
        }

        Log.Info($"[SyncHarness] clockprobe: started interval={ClockNum(interval, "F1")} samples={samples}");
        clockProbeRoutine = StartCoroutine(ClockProbeLoop(interval, samples));
    }

    private IEnumerator ClockProbeLoop(float interval, int samples)
    {
        TimeManager timeManager = NitroxServiceLocator.LocateService<TimeManager>();
        Stopwatch watch = Stopwatch.StartNew();
        ClockProbeSample first = default;
        ClockProbeSample previous = default;

        for (int number = 1; number <= samples; number++)
        {
            if (!DayNightCycle.main)
            {
                Log.Info($"[SyncHarness] clockprobe: failed reason=no-daynightcycle n={number}");
                break;
            }

            ClockProbeSample sample = ClockLogSample(number, timeManager, watch, first, previous);
            if (number == 1)
            {
                first = sample;
            }
            previous = sample;

            if (number < samples)
            {
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        Log.Info("[SyncHarness] clockprobe: done");
        clockProbeRoutine = null;
    }
}
#endif
