using Nitrox.Model.GameLogic.PlayerAnimation;

namespace NitroxClient.GameLogic;

/// <summary>
///     The copy of a player who holds on to a Seamoth's rider bar is drawn swimming: the game's locked mode makes the rider report "not underwater" (the water does not count for a pilot), but he hangs in
///     the water.
/// </summary>
[TestClass]
public class RiderPoseTest
{
    [TestMethod]
    public void ARiderIsDrawnSwimmingWhateverHeReported()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);
        pose.Swimming.Should().BeFalse("the report of the locked mode alone says he does not swim");

        pose.SetHoldingBar(true);

        pose.Swimming.Should().BeTrue();
        pose.HoldingBar.Should().BeTrue();
    }

    [TestMethod]
    public void WhenTheRiderLetsGoTheLastReportDecidesAgain()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);
        pose.SetHoldingBar(true);

        pose.SetHoldingBar(false);

        pose.Swimming.Should().BeFalse();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.Swimming.Should().BeTrue();
    }

    [TestMethod]
    public void ARiderWhoWalksNowhereIsStillSwimming()
    {
        RemotePoseTracker pose = new();
        pose.SetWalkingInHull(true, 10f);

        pose.SetHoldingBar(true);

        pose.Swimming.Should().BeTrue("holding on to a bar outside wins over a stale pawn");
    }

    [TestMethod]
    public void ABodyThatDoesNotHoldOnIsAsBefore()
    {
        new RemotePoseTracker().HoldingBar.Should().BeFalse();
        new RemotePoseTracker().Swimming.Should().BeTrue();
    }
}
