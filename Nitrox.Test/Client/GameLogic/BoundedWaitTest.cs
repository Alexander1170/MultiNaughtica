namespace NitroxClient.GameLogic;

[TestClass]
public class BoundedWaitTest
{
    private sealed class FakeClock
    {
        public float Now { get; set; }

        public float Read() => Now;
    }

    /// <summary>
    /// Runs the loop that the callers run (<c>while (wait.KeepWaiting(isDone)) yield return null;</c>) with a clock that advances by <paramref name="secondsPerFrame" /> each frame
    /// </summary>
    private static int FramesWaited(BoundedWait wait, FakeClock clock, Func<bool> isDone, float secondsPerFrame)
    {
        int frames = 0;
        while (wait.KeepWaiting(isDone))
        {
            frames++;
            clock.Now += secondsPerFrame;
            frames.Should().BeLessThan(10_000, "the wait has to end by itself");
        }
        return frames;
    }

    [TestMethod]
    public void ConditionAlreadyMet_DoesNotWait()
    {
        FakeClock clock = new();
        BoundedWait wait = new(10f, clock.Read);

        FramesWaited(wait, clock, () => true, 0.1f).Should().Be(0);

        wait.TimedOut.Should().BeFalse();
        wait.Waited.Should().Be(0f);
    }

    [TestMethod]
    public void ConditionMetBeforeTheTimeout_WaitsForExactlyThatLong()
    {
        FakeClock clock = new();
        BoundedWait wait = new(10f, clock.Read);

        int frames = FramesWaited(wait, clock, () => clock.Now >= 2.5f, 0.5f);

        frames.Should().Be(5);
        wait.TimedOut.Should().BeFalse();
        wait.Waited.Should().BeApproximately(2.5f, 0.001f);
    }

    [TestMethod]
    public void ConditionNeverMet_StopsAtTheTimeoutAndSaysSo()
    {
        FakeClock clock = new();
        BoundedWait wait = new(10f, clock.Read);

        int frames = FramesWaited(wait, clock, () => false, 0.5f);

        frames.Should().Be(20);
        wait.TimedOut.Should().BeTrue();
        wait.Waited.Should().BeApproximately(10f, 0.001f);
    }

    [TestMethod]
    public void ConditionMetOnTheFrameThatPassesTheDeadline_IsNotATimeout()
    {
        // A hitch while the world loads: the frame is longer than the whole timeout, but when it ends the world is settled
        FakeClock clock = new();
        BoundedWait wait = new(10f, clock.Read);
        bool settled = false;

        wait.KeepWaiting(() => settled).Should().BeTrue();
        clock.Now = 25f;
        settled = true;

        wait.KeepWaiting(() => settled).Should().BeFalse();
        wait.TimedOut.Should().BeFalse();
        wait.Waited.Should().Be(25f);
    }

    [TestMethod]
    public void DeadlinePassedWithoutTheCondition_TimesOutOnTheNextCheck()
    {
        FakeClock clock = new();
        BoundedWait wait = new(10f, clock.Read);

        wait.KeepWaiting(() => false).Should().BeTrue();
        clock.Now = 25f;

        wait.KeepWaiting(() => false).Should().BeFalse();
        wait.TimedOut.Should().BeTrue();
    }

    [TestMethod]
    public void TheTimeoutIsMeasuredFromTheCreationOfTheWait()
    {
        // The unscaled game time is long past zero when a teleport starts
        FakeClock clock = new() { Now = 3600f };
        BoundedWait wait = new(10f, clock.Read);

        wait.KeepWaiting(() => false).Should().BeTrue();
        clock.Now = 3609.5f;
        wait.KeepWaiting(() => false).Should().BeTrue();
        wait.Waited.Should().BeApproximately(9.5f, 0.001f);
        clock.Now = 3610f;

        wait.KeepWaiting(() => false).Should().BeFalse();
        wait.TimedOut.Should().BeTrue();
    }

    [TestMethod]
    public void WaitForAPodThatAppearsLate_EndsWhenItAppears()
    {
        // The shape of the escape pod wait of the initial sync: the pod is registered on the 4th frame
        FakeClock clock = new();
        BoundedWait wait = new(5f, clock.Read);
        int frame = 0;
        bool podRegistered = false;

        bool PodIsRegistered()
        {
            podRegistered = frame >= 4;
            return podRegistered;
        }

        while (wait.KeepWaiting(PodIsRegistered))
        {
            frame++;
            clock.Now += 0.016f;
        }

        podRegistered.Should().BeTrue();
        wait.TimedOut.Should().BeFalse();
        frame.Should().Be(4);
    }

    /// <summary>
    /// Like the callers: the player is released by the finally of the iterator that holds him, so also when the condition throws
    /// </summary>
    private static IEnumerator FrozenWhileWaiting(FakeClock clock, Func<bool> isDone, List<bool> frozenHistory, BoundedWait[] waitOut)
    {
        frozenHistory.Add(true);
        try
        {
            BoundedWait wait = new(10f, clock.Read);
            waitOut[0] = wait;
            while (wait.KeepWaiting(isDone))
            {
                yield return null;
            }
        }
        finally
        {
            frozenHistory.Add(false);
        }
    }

    private static void RunToTheEnd(IEnumerator routine, FakeClock clock, float secondsPerFrame)
    {
        while (routine.MoveNext())
        {
            clock.Now += secondsPerFrame;
        }
    }

    [TestMethod]
    public void PlayerIsReleasedWhenTheWorldSettles()
    {
        FakeClock clock = new();
        List<bool> frozen = [];

        RunToTheEnd(FrozenWhileWaiting(clock, () => clock.Now >= 3f, frozen, new BoundedWait[1]), clock, 1f);

        frozen.Should().Equal(true, false);
    }

    [TestMethod]
    public void PlayerIsReleasedWhenTheWorldNeverSettles()
    {
        FakeClock clock = new();
        List<bool> frozen = [];
        BoundedWait[] wait = new BoundedWait[1];

        RunToTheEnd(FrozenWhileWaiting(clock, () => false, frozen, wait), clock, 1f);

        frozen.Should().Equal(true, false);
        wait[0].TimedOut.Should().BeTrue();
    }

    [TestMethod]
    public void PlayerIsReleasedWhenTheConditionThrows()
    {
        FakeClock clock = new();
        List<bool> frozen = [];

        Action run = () => RunToTheEnd(FrozenWhileWaiting(clock, () => throw new InvalidOperationException("streamer is gone"), frozen, new BoundedWait[1]), clock, 1f);

        run.Should().Throw<InvalidOperationException>();
        frozen.Should().Equal(true, false);
    }
}
