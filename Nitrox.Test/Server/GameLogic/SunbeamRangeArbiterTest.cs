using System.Threading.Tasks;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

[TestClass]
public class SunbeamRangeArbiterTest
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Decide_FirstProposal_IsTheNewDecision(bool proposedInRange)
    {
        SunbeamRangeArbiter arbiter = new();

        bool outcome = arbiter.Decide(proposedInRange, false, false, out bool isNewDecision);

        outcome.Should().Be(proposedInRange);
        isNewDecision.Should().BeTrue();
        arbiter.DecidedInRange.Should().Be(proposedInRange);
    }

    [TestMethod]
    public void Decide_NothingProposedYet_HasNoDecision()
    {
        new SunbeamRangeArbiter().DecidedInRange.Should().BeNull();
    }

    [TestMethod]
    public void Decide_LaterProposalThatDisagrees_GetsTheFirstOutcome()
    {
        // Alice stands at the landing site, Bob does not and Bob's client proposes second: nobody gets both PDA goals
        SunbeamRangeArbiter arbiter = new();
        arbiter.Decide(true, false, false, out _);

        bool outcome = arbiter.Decide(false, false, false, out bool isNewDecision);

        outcome.Should().BeTrue();
        isNewDecision.Should().BeFalse();
    }

    [TestMethod]
    public void Decide_LaterProposalThatDisagrees_FirstOutOfRangeStays()
    {
        SunbeamRangeArbiter arbiter = new();
        arbiter.Decide(false, false, false, out _);

        bool outcome = arbiter.Decide(true, false, false, out bool isNewDecision);

        outcome.Should().BeFalse();
        isNewDecision.Should().BeFalse();
    }

    [TestMethod]
    public void Decide_SameProposalTwice_OnlyTheFirstIsNew()
    {
        SunbeamRangeArbiter arbiter = new();

        arbiter.Decide(true, false, false, out bool firstIsNew);
        arbiter.Decide(true, false, false, out bool secondIsNew);

        firstIsNew.Should().BeTrue();
        secondIsNew.Should().BeFalse();
    }

    [TestMethod]
    public void Decide_ProposerDisconnectsAfterwards_DecisionStandsForTheOthers()
    {
        // The arbiter does not know who proposed, so the proposing player leaving changes nothing: the remaining client asks later and gets the same answer
        SunbeamRangeArbiter arbiter = new();
        arbiter.Decide(true, false, false, out _);

        bool answerForTheRemainingPlayer = arbiter.Decide(false, false, false, out bool isNewDecision);

        answerForTheRemainingPlayer.Should().BeTrue();
        isNewDecision.Should().BeFalse();
        arbiter.DecidedInRange.Should().BeTrue();
    }

    [TestMethod]
    public void Decide_ProposerDisconnectsBeforeProposing_TheNextProposalDecides()
    {
        // Nothing was proposed by the player who left, so nothing was decided: whoever proposes next decides, there is no waiting for the missing one
        SunbeamRangeArbiter arbiter = new();
        arbiter.DecidedInRange.Should().BeNull();

        bool outcome = arbiter.Decide(false, false, false, out bool isNewDecision);

        outcome.Should().BeFalse();
        isNewDecision.Should().BeTrue();
    }

    [TestMethod]
    public void Decide_InRangeGoalAlreadyCompleted_OverrulesAnOutOfRangeProposal()
    {
        // After a server restart the decision is gone but the goal that ran is not
        SunbeamRangeArbiter arbiter = new();

        bool outcome = arbiter.Decide(false, true, false, out bool isNewDecision);

        outcome.Should().BeTrue();
        isNewDecision.Should().BeFalse();
        arbiter.DecidedInRange.Should().BeTrue();
    }

    [TestMethod]
    public void Decide_OutOfRangeGoalAlreadyCompleted_OverrulesAnInRangeProposal()
    {
        SunbeamRangeArbiter arbiter = new();

        bool outcome = arbiter.Decide(true, false, true, out bool isNewDecision);

        outcome.Should().BeFalse();
        isNewDecision.Should().BeFalse();
    }

    [TestMethod]
    public void Decide_BothGoalsCompleted_InRangeWinsConsistently()
    {
        // Both can only be completed by hand; the answer just has to be the same for everybody
        SunbeamRangeArbiter arbiter = new();

        arbiter.Decide(false, true, true, out _).Should().BeTrue();
        arbiter.Decide(false, true, true, out _).Should().BeTrue();
    }

    [TestMethod]
    public void Decide_DecisionAlreadyMade_CompletedGoalDoesNotChangeIt()
    {
        // What the clients were told stays, even if the other goal gets completed by hand afterwards
        SunbeamRangeArbiter arbiter = new();
        arbiter.Decide(false, false, false, out _);

        arbiter.Decide(true, true, false, out bool isNewDecision).Should().BeFalse();
        isNewDecision.Should().BeFalse();
    }

    [TestMethod]
    public void Reset_AfterADecision_TheNextProposalDecidesAgain()
    {
        // The Sunbeam event restarted (/sunbeam), the range check runs again
        SunbeamRangeArbiter arbiter = new();
        arbiter.Decide(true, false, false, out _);

        arbiter.Reset();
        bool outcome = arbiter.Decide(false, false, false, out bool isNewDecision);

        arbiter.DecidedInRange.Should().BeFalse();
        outcome.Should().BeFalse();
        isNewDecision.Should().BeTrue();
    }

    [TestMethod]
    public void Reset_WithoutDecision_ChangesNothing()
    {
        SunbeamRangeArbiter arbiter = new();

        arbiter.Reset();

        arbiter.DecidedInRange.Should().BeNull();
    }

    [TestMethod]
    public void Decide_ProposalsFromManyThreads_OneDecisionEverybodyAgreesOn()
    {
        // The packet processors of the server run concurrently: all clients propose at the same moment and disagree
        SunbeamRangeArbiter arbiter = new();
        int newDecisions = 0;
        bool[] outcomes = new bool[200];

        Parallel.For(0, outcomes.Length, i =>
        {
            outcomes[i] = arbiter.Decide(i % 2 == 0, false, false, out bool isNewDecision);
            if (isNewDecision)
            {
                Interlocked.Increment(ref newDecisions);
            }
        });

        newDecisions.Should().Be(1);
        outcomes.Should().AllSatisfy(outcome => outcome.Should().Be(arbiter.DecidedInRange!.Value));
    }
}
