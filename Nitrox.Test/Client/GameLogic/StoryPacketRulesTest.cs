namespace NitroxClient.GameLogic;

/// <summary>
///     When a story packet (a goal executed, a goal scheduled) may be applied (<see cref="StoryPacketRules" />). A client that is still joining has no <c>StoryGoalScheduler</c> yet: the first batch
///     with the emperor fixes showed a NullReferenceException in <c>StoryGoalExecutedClientProcessor</c> on the client that was loading after a world restart.
/// </summary>
[TestClass]
public class StoryPacketRulesTest
{
    [TestMethod]
    public void AClientThatHasFinishedJoiningAppliesTheGoalAtOnce()
    {
        StoryPacketRules.ShouldWait(initialSyncCompleted: true, storySystemsLoaded: true).Should().BeFalse();
    }

    [TestMethod]
    public void AClientThatIsStillJoiningWaits()
    {
        StoryPacketRules.ShouldWait(initialSyncCompleted: false, storySystemsLoaded: true).Should().BeTrue();
        StoryPacketRules.ShouldWait(initialSyncCompleted: false, storySystemsLoaded: false).Should().BeTrue();
    }

    [TestMethod]
    public void AClientWithoutTheStorySystemsWaitsEvenAfterTheSync()
    {
        // the scene can be unloaded (back to the menu) after the flag was set
        StoryPacketRules.ShouldWait(initialSyncCompleted: true, storySystemsLoaded: false).Should().BeTrue();
    }

    [TestMethod]
    public void APacketThatWaitedInAnEarlierSessionIsDropped()
    {
        // leave while joining, join again within the wait: the old packet must not be applied to the new session
        StoryPacketRules.SessionEnded(generationAtArrival: 3, generationNow: 4).Should().BeTrue();
        StoryPacketRules.SessionEnded(generationAtArrival: 3, generationNow: 9).Should().BeTrue();
        StoryPacketRules.SessionEnded(generationAtArrival: 3, generationNow: 3).Should().BeFalse();
    }

    [TestMethod]
    public void APacketIsGivenUpAfterFiveMinutes()
    {
        StoryPacketRules.GiveUp(0f).Should().BeFalse();
        StoryPacketRules.GiveUp(StoryPacketRules.WAIT_SECONDS - 0.25f).Should().BeFalse();
        StoryPacketRules.GiveUp(StoryPacketRules.WAIT_SECONDS).Should().BeTrue();
        StoryPacketRules.WAIT_SECONDS.Should().Be(300f);
    }
}
