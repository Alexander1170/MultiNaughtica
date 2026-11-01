using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nitrox.Model.Networking;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.AppEvents.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Services;

/// <summary>
///     Bug 8 (the day/night cycle seemed too fast in two-player play): the server clock is a stopwatch, one game second per real second while a player is connected, frozen while the server
///     sleeps, and a skip is one absolute step. The measured proof from the real logs of 2026-10-23 and 2026-10-24 is in the design notes (not part of this repository) (E3).
/// </summary>
[TestClass]
public class TimeServiceClockTest
{
    private static TimeService NewService(out IPacketSender sender)
    {
        sender = Substitute.For<IPacketSender>();
        return new TimeService(sender, new NtpSyncer(), Substitute.For<ILoggerFactory>(), Substitute.For<ILogger<TimeService>>());
    }

    private static Task Wake(TimeService service) => ((IEvent<IHibernate.WakeArgs>)service).OnEventAsync(new IHibernate.WakeArgs());

    private static Task Sleep(TimeService service) => ((IEvent<IHibernate.SleepArgs>)service).OnEventAsync(new IHibernate.SleepArgs());

    [TestMethod]
    public void AFreshWorldStartsAtTheDefaultTimeOfTheGameInTheMorningOfDayOne()
    {
        TimeService service = NewService(out _);

        service.GameTime.TotalSeconds.Should().Be(480d);
        service.ActiveTime.TotalSeconds.Should().Be(0d);
        service.GameDay.Should().Be(1);
    }

    [TestMethod]
    public void ASavedWorldComesBackWithTheTimeItWasSavedWith()
    {
        // The Test world after its first session (server log of 2026-10-24: play time 5h 10m 55s, day 16, 19135 s): the start offset of 480 s is not counted twice
        TimeService service = NewService(out _);

        service.ActiveTime = TimeSpan.FromSeconds(18655d);
        service.GameTime = TimeSpan.FromSeconds(19135d);

        service.GameTime.TotalSeconds.Should().Be(19135d);
        service.ActiveTime.TotalSeconds.Should().Be(18655d);
        service.GameDay.Should().Be(16);
    }

    [TestMethod]
    public async Task ASleepingServerDoesNotAdvanceTheClock()
    {
        TimeService service = NewService(out _);
        service.GameTime = TimeSpan.FromSeconds(19135d);
        service.ActiveTime = TimeSpan.FromSeconds(18655d);

        // Nobody is connected: the stopwatch has never been started
        await Task.Delay(100);
        service.GameTime.TotalSeconds.Should().Be(19135d);

        await Wake(service);
        await Task.Delay(100);
        await Sleep(service);
        double frozenGame = service.GameTime.TotalSeconds;
        double frozenActive = service.ActiveTime.TotalSeconds;
        await Task.Delay(100);

        service.GameTime.TotalSeconds.Should().Be(frozenGame);
        service.ActiveTime.TotalSeconds.Should().Be(frozenActive);
        frozenGame.Should().BeGreaterThan(19135.05d);
    }

    [TestMethod]
    public async Task AWokenServerRunsOneGameSecondPerRealSecond()
    {
        TimeService service = NewService(out _);
        service.GameTime = TimeSpan.FromSeconds(19135d);
        service.ActiveTime = TimeSpan.FromSeconds(18655d);
        await Wake(service);

        Stopwatch real = Stopwatch.StartNew();
        double gameBefore = service.GameTime.TotalSeconds;
        double activeBefore = service.ActiveTime.TotalSeconds;
        await Task.Delay(500);
        double gameAfter = service.GameTime.TotalSeconds;
        double activeAfter = service.ActiveTime.TotalSeconds;
        double realSeconds = real.Elapsed.TotalSeconds;

        realSeconds.Should().BeGreaterThan(0.4d);
        // The readings sit inside the real interval, so the game interval is never longer than the real one and falls short of it by the time between the calls
        (gameAfter - gameBefore).Should().BeApproximately(realSeconds, 0.05d);
        (activeAfter - activeBefore).Should().BeApproximately(realSeconds, 0.05d);
        // And the two clocks stay 480 s apart, the start offset of the game
        (gameAfter - activeAfter).Should().BeApproximately(480d, 0.05d);
    }

    [TestMethod]
    public async Task TheTimePacketCarriesTheGameTimeThePlayTimeAndTheMomentOfTheSameReading()
    {
        TimeService service = NewService(out _);
        service.GameTime = TimeSpan.FromSeconds(19135d);
        service.ActiveTime = TimeSpan.FromSeconds(18655d);
        await Wake(service);
        await Task.Delay(50);

        TimeChange packet = service.MakeTimePacket();

        packet.CurrentTime.Should().BeGreaterThan(19135.02d).And.BeLessThan(19137d);
        (packet.CurrentTime - packet.RealTimeElapsed).Should().BeApproximately(480d, 0.05d);
        packet.UpdateTime.Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 2000UL);
    }

    [TestMethod]
    public async Task ASkipMovesTheGameTimeOnceByTheSkippedAmountAndNotThePlayTimeAndBroadcastsIt()
    {
        TimeService service = NewService(out IPacketSender sender);
        service.GameTime = TimeSpan.FromSeconds(19135d);
        service.ActiveTime = TimeSpan.FromSeconds(18655d);
        TimeSpan reported = TimeSpan.Zero;
        service.TimeSkipped += skipped => reported = skipped;

        // A bed: 396 game seconds (Bed.kSleepEndTime - Bed.kSleepStartTime)
        await service.SkipTimeAsync(TimeSpan.FromSeconds(396d));

        service.GameTime.TotalSeconds.Should().Be(19135d + 396d);
        service.ActiveTime.TotalSeconds.Should().Be(18655d);
        reported.Should().Be(TimeSpan.FromSeconds(396d));
        await sender.Received(1).SendPacketToAllAsync(Arg.Is<TimeChange>(packet => Math.Abs(packet.CurrentTime - (19135d + 396d)) < 0.001d));
    }

    [TestMethod]
    public async Task ASkipOfNothingChangesNothing()
    {
        TimeService service = NewService(out IPacketSender sender);

        await service.SkipTimeAsync(TimeSpan.Zero);
        await service.SkipTimeAsync(TimeSpan.FromSeconds(-5d));

        service.GameTime.TotalSeconds.Should().Be(480d);
        await sender.DidNotReceiveWithAnyArgs().SendPacketToAllAsync<TimeChange>(default);
    }

    /// <summary>
    ///     19135 s is 1135 s into a day: the next midnight is at 19200 (day 17 starts), noon of that day is 600 s later, /time skip goes to the next multiple of 600 s.
    /// </summary>
    private static async Task<double> GameSecondsAfter(StoryManager.TimeModification modification)
    {
        TimeService service = NewService(out _);
        service.GameTime = TimeSpan.FromSeconds(19135d);

        await service.ChangeTimeAsync(modification);

        return service.GameTime.TotalSeconds;
    }

    [TestMethod]
    public async Task TimeNightGoesToTheNextMidnight()
    {
        (await GameSecondsAfter(StoryManager.TimeModification.NIGHT)).Should().Be(19200d);
    }

    [TestMethod]
    public async Task TimeDayGoesToNoonOfTheNextDay()
    {
        (await GameSecondsAfter(StoryManager.TimeModification.DAY)).Should().Be(19800d);
    }

    [TestMethod]
    public async Task TimeSkipGoesToTheNextMultipleOfSixHundredSeconds()
    {
        (await GameSecondsAfter(StoryManager.TimeModification.SKIP)).Should().Be(19200d);
    }
}
