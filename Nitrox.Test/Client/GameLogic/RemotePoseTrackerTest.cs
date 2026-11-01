using Nitrox.Model.GameLogic.PlayerAnimation;

namespace NitroxClient.GameLogic;

[TestClass]
public class RemotePoseTrackerTest
{
    [TestMethod]
    public void Swimming_ANewBodyBeforeAnyEvent_IsSwimming()
    {
        // The state AnimationController.Awake gives a body: is_underwater true
        new RemotePoseTracker().Swimming.Should().BeTrue();
    }

    [TestMethod]
    public void Report_UnderwaterOff_StopsTheSwimmingPose()
    {
        RemotePoseTracker pose = new();

        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF).Should().BeTrue();

        pose.Swimming.Should().BeFalse();
        pose.HasReport.Should().BeTrue();
    }

    [TestMethod]
    public void Report_UnderwaterOn_StartsTheSwimmingPose()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);

        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);

        pose.Swimming.Should().BeTrue();
    }

    [TestMethod]
    public void Report_TheInfectionReveal_SaysNothingAboutThePose()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);

        pose.Report(AnimChangeType.INFECTION_REVEAL, AnimChangeState.ON).Should().BeFalse();

        pose.Swimming.Should().BeFalse();
        pose.ReportedUnderwater.Should().BeFalse();
    }

    [TestMethod]
    public void Swimming_APawnWalkingInTheHullWhoseLastEventSaidUnderwater_IsNotSwimming()
    {
        // Bug 6: the copy of a passenger stayed in the swimming pose (a lost or stale event) while his pawn walked on the floor of the Cyclops, its legs about a meter too high
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);

        pose.SetWalkingInHull(true, 100f);

        pose.Swimming.Should().BeFalse();
        pose.ReportedUnderwater.Should().BeTrue("the report itself is kept, only the pose follows the pawn");
    }

    [TestMethod]
    public void Swimming_AfterTheStaleStateOfTheJoinWasNeverCorrected_IsStillNotSwimmingInTheHull()
    {
        // The body of a player who joined is made from the animation the server sent with him (underwater on); no event came because the first one was lost
        RemotePoseTracker pose = new();
        pose.SeedFromJoin(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.Swimming.Should().BeTrue("outside the hull the join state is all there is");

        pose.SetWalkingInHull(true, 100f);

        pose.Swimming.Should().BeFalse();
    }

    [TestMethod]
    public void Swimming_AfterTheWalkerLeftTheHullIntoTheWater_IsSwimmingAgain()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);
        pose.SetWalkingInHull(true, 100f);

        // He swims out: the event comes (before or after the sub root change), then the movement is a world movement again
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.Swimming.Should().BeFalse("still the pawn until the movement says otherwise");
        pose.SetWalkingInHull(false, 130f);

        pose.Swimming.Should().BeTrue();
    }

    [TestMethod]
    public void Swimming_AnEventDuringTheWalkInTheHull_IsRememberedForTheWayOut()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);

        // Climbing the ladder out of a hull and into a dry base: the player reports "not underwater" while the last pawn packets are still to come
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);
        pose.SetWalkingInHull(false, 120f);

        pose.Swimming.Should().BeFalse();
    }

    [TestMethod]
    public void SeedFromJoin_AfterAnEventCame_DoesNotOverwriteIt()
    {
        // The bug of the lost event: the body used to be made from the (old) animation of the join and forgot what the player said after it
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);

        pose.SeedFromJoin(AnimChangeType.UNDERWATER, AnimChangeState.ON);

        pose.ReportedUnderwater.Should().BeFalse();
        pose.Swimming.Should().BeFalse();
    }

    [TestMethod]
    public void SeedFromJoin_WithNothingReportedYet_IsTheStartValue()
    {
        RemotePoseTracker pose = new();

        pose.SeedFromJoin(AnimChangeType.UNDERWATER, AnimChangeState.OFF);

        pose.Swimming.Should().BeFalse();
        pose.HasReport.Should().BeFalse("a start value is not an event: the next seed or event may replace it");
    }

    [TestMethod]
    public void SeedFromJoin_OfAnotherType_ChangesNothing()
    {
        // The server keeps ONE animation per player: after the infection reveal that is what a joiner is sent. It says nothing about the pose.
        RemotePoseTracker pose = new();

        pose.SeedFromJoin(AnimChangeType.INFECTION_REVEAL, AnimChangeState.ON);

        pose.ReportedUnderwater.Should().Be(RemotePoseTracker.DEFAULT_UNDERWATER);
        pose.Swimming.Should().BeTrue();
    }

    [TestMethod]
    public void Swimming_AWorldMovementWithoutAPawn_FollowsTheReportedState()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);
        pose.SetWalkingInHull(false, 100f);

        pose.Swimming.Should().BeFalse("a player walking in a base is reported as not underwater");
    }

    [TestMethod]
    public void WalkingSince_IsTheTimeOfTheFirstPawnPacket_AndKeptWhileHeWalks()
    {
        RemotePoseTracker pose = new();
        float.IsNaN(pose.WalkingSince).Should().BeTrue();

        pose.SetWalkingInHull(true, 100f);
        // every pawn packet repeats the call: the start time must not move
        pose.SetWalkingInHull(true, 101f);
        pose.SetWalkingInHull(true, 150f);

        pose.WalkingSince.Should().Be(100f);
        pose.WalkingFor(160f).Should().BeApproximately(60f, 0.0001f);
    }

    [TestMethod]
    public void WalkingSince_AfterHeLeftTheHull_IsNotSetAndStartsAgainWithTheNextBoarding()
    {
        RemotePoseTracker pose = new();
        pose.SetWalkingInHull(true, 100f);
        pose.SetWalkingInHull(false, 130f);

        float.IsNaN(pose.WalkingSince).Should().BeTrue();
        float.IsNaN(pose.WalkingFor(140f)).Should().BeTrue();

        pose.SetWalkingInHull(true, 200f);

        pose.WalkingSince.Should().Be(200f);
    }

    [TestMethod]
    public void IsStuckUnderwater_TheFirstPawnPacketOfAPlayerWhoClimbsInOutOfTheWater_IsTheBoardingRaceNotEvidence()
    {
        // Review of bug 6: the swimmer reached the hatch (reported ON), the pawn packet arrives before the OFF event of the same client. Nothing is stale: it is a few frames
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);

        pose.SetWalkingInHull(true, 100f);

        pose.IsStuckUnderwater(100f).Should().BeFalse();
        pose.IsStuckUnderwater(100.3f).Should().BeFalse();
        pose.IsStuckUnderwater(103f).Should().BeFalse("the sender has not announced its state again yet");
        pose.IsStuckUnderwater(100f + PoseRefreshRules.REFRESH_INTERVAL).Should().BeFalse("the announcement is still on its way");
    }

    [TestMethod]
    public void IsStuckUnderwater_AfterTheRefreshOfTheSenderHadItsChance_IsEvidence()
    {
        // A state that is still swimming 7 s after the pawn began (the sender repeats it every 5 s): the sender really reports underwater, or his repeats do not arrive
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);

        pose.IsStuckUnderwater(100f + PoseRefreshRules.SETTLED_AFTER).Should().BeTrue();
        pose.IsStuckUnderwater(160f).Should().BeTrue();
    }

    [TestMethod]
    public void IsStuckUnderwater_WhenTheEventCameInTimeEvenLate_IsNotEvidence()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);

        // the correction arrives with the next announcement, 4 s after the pawn began
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);

        pose.IsStuckUnderwater(110f).Should().BeFalse();
    }

    [TestMethod]
    public void IsStuckUnderwater_ForAPlayerWhoDoesNotWalkInTheHull_IsFalse()
    {
        // A swimmer in the open water reports underwater for as long as he likes
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);

        pose.IsStuckUnderwater(1000f).Should().BeFalse();
    }

    [TestMethod]
    public void IsStuckUnderwater_AfterANewBoarding_CountsFromTheNewPawn()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);
        pose.SetWalkingInHull(false, 120f);

        // He gets in again: the clock of the new stretch is young, whatever the old one said
        pose.SetWalkingInHull(true, 500f);

        pose.IsStuckUnderwater(501f).Should().BeFalse();
        pose.IsStuckUnderwater(500f + PoseRefreshRules.SETTLED_AFTER).Should().BeTrue();
    }

    [TestMethod]
    public void SwimmingEndedWhileWalking_TheOffEventOfTheBoardingRace_IsAboutZero()
    {
        // The pawn packet came first, the OFF event 0.1 s after it: below the limit that the log line needs
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);

        float endedAfter = pose.SwimmingEndedWhileWalking(AnimChangeType.UNDERWATER, AnimChangeState.OFF, 100.1f);

        endedAfter.Should().BeApproximately(0.1f, 0.0001f);
        PoseRefreshRules.IsLateCorrection(endedAfter).Should().BeFalse();
    }

    [TestMethod]
    public void SwimmingEndedWhileWalking_TheOffEventOfARefreshThreeSecondsLater_IsALateCorrection()
    {
        // The event of the state change was lost, the announcement 3 s into the walk corrected it: this one is evidence of a stale state
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);

        float endedAfter = pose.SwimmingEndedWhileWalking(AnimChangeType.UNDERWATER, AnimChangeState.OFF, 103f);

        endedAfter.Should().BeApproximately(3f, 0.0001f);
        PoseRefreshRules.IsLateCorrection(endedAfter).Should().BeTrue();
    }

    [TestMethod]
    public void SwimmingEndedWhileWalking_AnythingElse_IsNotANumber()
    {
        RemotePoseTracker walkingOnTheReportedSwim = new();
        walkingOnTheReportedSwim.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        walkingOnTheReportedSwim.SetWalkingInHull(true, 100f);

        // a repeat of the swimming state, another type of event
        float.IsNaN(walkingOnTheReportedSwim.SwimmingEndedWhileWalking(AnimChangeType.UNDERWATER, AnimChangeState.ON, 110f)).Should().BeTrue();
        float.IsNaN(walkingOnTheReportedSwim.SwimmingEndedWhileWalking(AnimChangeType.INFECTION_REVEAL, AnimChangeState.OFF, 110f)).Should().BeTrue();

        // no pawn: an ordinary end of swimming in the open
        RemotePoseTracker notWalking = new();
        notWalking.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        float.IsNaN(notWalking.SwimmingEndedWhileWalking(AnimChangeType.UNDERWATER, AnimChangeState.OFF, 110f)).Should().BeTrue();

        // the state already ended: the repeat of OFF corrects nothing
        RemotePoseTracker alreadyOff = new();
        alreadyOff.Report(AnimChangeType.UNDERWATER, AnimChangeState.OFF);
        alreadyOff.SetWalkingInHull(true, 100f);
        float.IsNaN(alreadyOff.SwimmingEndedWhileWalking(AnimChangeType.UNDERWATER, AnimChangeState.OFF, 110f)).Should().BeTrue();
    }

    [TestMethod]
    public void SwimmingEndedWhileWalking_DoesNotChangeTheTracker()
    {
        RemotePoseTracker pose = new();
        pose.Report(AnimChangeType.UNDERWATER, AnimChangeState.ON);
        pose.SetWalkingInHull(true, 100f);

        pose.SwimmingEndedWhileWalking(AnimChangeType.UNDERWATER, AnimChangeState.OFF, 105f);

        pose.ReportedUnderwater.Should().BeTrue("asking is not reporting");
    }
}
