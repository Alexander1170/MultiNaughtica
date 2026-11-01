namespace NitroxClient.GameLogic.Spawning;

/// <summary>
///     The verdicts of the ghost probe (bug 9): a creature copy is a ghost when nothing of the sync reaches it any more.
/// </summary>
[TestClass]
public class CreatureCopyRulesTest
{
    [TestMethod]
    public void AnObjectWithoutAnIdIsUnknownToTheServer()
    {
        CreatureCopyRules.Judge(hasId: false, registeredToThisObject: false, simulatedHere: false, everUpdated: false, secondsSinceUpdate: 0f).Should().Be(CreatureCopyRules.Verdict.NoId);
        CreatureCopyRules.IsGhost(CreatureCopyRules.Verdict.NoId).Should().BeTrue();
    }

    [TestMethod]
    public void AnObjectWhoseIdIsRegisteredToAnotherObjectIsShadowedWhateverElseIsTrue()
    {
        // The dropped fish in the base with the id of a wild fish somewhere else: the lock, the updates and the destruction of the id all go to the other object
        CreatureCopyRules.Judge(hasId: true, registeredToThisObject: false, simulatedHere: true, everUpdated: true, secondsSinceUpdate: 1f).Should().Be(CreatureCopyRules.Verdict.Shadowed);
        CreatureCopyRules.IsGhost(CreatureCopyRules.Verdict.Shadowed).Should().BeTrue();
    }

    [TestMethod]
    public void ACreatureThisClientSimulatesIsNotAGhost()
    {
        CreatureCopyRules.Judge(hasId: true, registeredToThisObject: true, simulatedHere: true, everUpdated: false, secondsSinceUpdate: 0f).Should().Be(CreatureCopyRules.Verdict.SimulatedHere);
        CreatureCopyRules.IsGhost(CreatureCopyRules.Verdict.SimulatedHere).Should().BeFalse();
    }

    [TestMethod]
    public void ACopyThatWasUpdatedLatelyIsFollowed()
    {
        CreatureCopyRules.Judge(hasId: true, registeredToThisObject: true, simulatedHere: false, everUpdated: true, secondsSinceUpdate: 0.3f).Should().Be(CreatureCopyRules.Verdict.Followed);
        CreatureCopyRules.Judge(hasId: true, registeredToThisObject: true, simulatedHere: false, everUpdated: true, secondsSinceUpdate: CreatureCopyRules.GONE_QUIET_AFTER_SECONDS).Should().Be(CreatureCopyRules.Verdict.Followed, "the limit itself is still fine");
        CreatureCopyRules.IsGhost(CreatureCopyRules.Verdict.Followed).Should().BeFalse();
    }

    [TestMethod]
    public void ACopyThatWasFollowedAndWentQuietIsAGhost()
    {
        CreatureCopyRules.Judge(hasId: true, registeredToThisObject: true, simulatedHere: false, everUpdated: true, secondsSinceUpdate: CreatureCopyRules.GONE_QUIET_AFTER_SECONDS + 0.1f).Should().Be(CreatureCopyRules.Verdict.GoneQuiet);
        CreatureCopyRules.IsGhost(CreatureCopyRules.Verdict.GoneQuiet).Should().BeTrue();
    }

    [TestMethod]
    public void ACopyThatNeverReceivedAnUpdateIsNotJudgedByTime()
    {
        // A creature that sits still reports nothing: only the other client's list can say that this copy is a ghost
        CreatureCopyRules.Judge(hasId: true, registeredToThisObject: true, simulatedHere: false, everUpdated: false, secondsSinceUpdate: 9999f).Should().Be(CreatureCopyRules.Verdict.NeverUpdated);
        CreatureCopyRules.IsGhost(CreatureCopyRules.Verdict.NeverUpdated).Should().BeFalse();
    }
}
