namespace NitroxClient.GameLogic;

[TestClass]
public class LatestPendingTest
{
    [TestMethod]
    public void NothingPendingGivesNothing()
    {
        LatestPending<string> pending = new();

        pending.TryTake(out string taken).Should().BeFalse();
        taken.Should().BeNull();
    }

    [TestMethod]
    public void TheNewestUpdateReplacesTheOlderOne()
    {
        LatestPending<string> pending = new();
        pending.Set("first");
        pending.Set("second");

        pending.TryTake(out string taken).Should().BeTrue();
        taken.Should().Be("second");
    }

    [TestMethod]
    public void AnUpdateIsTakenOnlyOnce()
    {
        LatestPending<string> pending = new();
        pending.Set("only");
        pending.TryTake(out _);

        pending.HasValue.Should().BeFalse();
        pending.TryTake(out _).Should().BeFalse();
    }

    [TestMethod]
    public void ClearDropsWhatWasPending()
    {
        LatestPending<string> pending = new();
        pending.Set("stale");
        pending.Clear();

        pending.TryTake(out _).Should().BeFalse();
    }
}
