using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

[TestClass]
public class StoryRelayRulesTest
{
    [TestMethod]
    public void AGoalTheServerLearnsFromThisPacketIsPassedOn()
    {
        StoryRelayRules.ShouldRelay("Goal_Builder", StoryGoalExecuted.EventType.PDA, addedToCompletedGoals: true).Should().BeTrue();
        StoryRelayRules.ShouldRelay("RadioGrassy25", StoryGoalExecuted.EventType.RADIO, addedToCompletedGoals: true).Should().BeTrue();
        StoryRelayRules.ShouldRelay("OnPlayRadioGrassy25", StoryGoalExecuted.EventType.STORY, addedToCompletedGoals: true).Should().BeTrue();
    }

    [TestMethod]
    public void ARepeatedGoalIsNotPassedOnAgain()
    {
        // both players run the same scheduled goal: the second report adds nothing (the server log of the first play session shows RadioBounceBack twice)
        StoryRelayRules.ShouldRelay("RadioBounceBack", StoryGoalExecuted.EventType.RADIO, addedToCompletedGoals: false).Should().BeFalse();
        StoryRelayRules.ShouldRelay("Story_AuroraWarning1", StoryGoalExecuted.EventType.PDA, addedToCompletedGoals: false).Should().BeFalse();
        StoryRelayRules.ShouldRelay("OnCraftBattery", StoryGoalExecuted.EventType.STORY, addedToCompletedGoals: false).Should().BeFalse();
    }

    [TestMethod]
    public void TheGoalOfAPlayedRadioMessageIsPassedOnEvenThoughThePlayCompletedItOnTheServerFirst()
    {
        // RadioPlayPendingMessage reaches the server before the player's own report of OnPlay<key> (the client sends it first), and StoryManager.RemovedLatestRadioMessage completes the goal there;
        // the report is then a repeat. The other client must still get it: if its own queue ran differently it never completes the goal itself, and the beacons hang on it
        StoryRelayRules.ShouldRelay("OnPlayRadioGrassy25", StoryGoalExecuted.EventType.STORY, addedToCompletedGoals: false).Should().BeTrue();
        StoryRelayRules.ShouldRelay("OnPlayRadioSunbeam4", StoryGoalExecuted.EventType.STORY, addedToCompletedGoals: false).Should().BeTrue();
    }

    [TestMethod]
    public void OnlyTheStoryTypeCarriesThePlayedGoal()
    {
        StoryRelayRules.ShouldRelay("OnPlayRadioGrassy25", StoryGoalExecuted.EventType.RADIO, addedToCompletedGoals: false).Should().BeFalse();
        StoryRelayRules.ShouldRelay("OnPlayRadioGrassy25", StoryGoalExecuted.EventType.PDA, addedToCompletedGoals: false).Should().BeFalse();
    }
}
