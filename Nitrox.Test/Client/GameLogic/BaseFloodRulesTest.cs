using NitroxClient.GameLogic.Bases;

namespace NitroxClient.GameLogic;

[TestClass]
public class BaseFloodRulesTest
{
    private static readonly float[] twoCellsWet = [0.5f, 1f];

    [TestMethod]
    public void AGridOfAnotherSizeIsNeverApplied()
    {
        // Restoring a grid of another size would throw inside the simulation and stop its Update loop for good
        BaseFloodRules.ShouldApply(3, twoCellsWet, 0f, false).Should().BeFalse();
        BaseFloodRules.ShouldApply(2, null, 0f, false).Should().BeFalse();
    }

    [TestMethod]
    public void TheHolderOfTheBaseKeepsItsOwnWater()
    {
        // What the others send must not set back the simulation of the player who runs it
        BaseFloodRules.ShouldApply(2, twoCellsWet, 3f, true).Should().BeFalse();
    }

    [TestMethod]
    public void ADryBaseOfTheHolderTakesTheStoredWater()
    {
        // A base that was just spawned from the stored state starts without water and gets what the server has
        BaseFloodRules.ShouldApply(2, twoCellsWet, 0f, true).Should().BeTrue();
    }

    [TestMethod]
    public void EveryoneElseTakesWhatTheHolderSends()
    {
        // Even less water than the local copy has: the water drains after a repair, it does not only rise
        BaseFloodRules.ShouldApply(2, [0f, 0.25f], 5f, false).Should().BeTrue();
        BaseFloodRules.ShouldApply(2, twoCellsWet, 0f, false).Should().BeTrue();
    }

    [TestMethod]
    public void ADryBaseThatNeverHadWaterSendsNothing()
    {
        BaseFloodRules.ShouldSend(0f, null).Should().BeFalse();
    }

    [TestMethod]
    public void TheFirstWaterIsSent()
    {
        BaseFloodRules.ShouldSend(0.25f, null).Should().BeTrue();
    }

    [TestMethod]
    public void OnlyAChangeIsSentAgain()
    {
        BaseFloodRules.ShouldSend(4f, 4f).Should().BeFalse();
        BaseFloodRules.ShouldSend(4.5f, 4f).Should().BeTrue();
    }

    [TestMethod]
    public void WaterThatStaysIsRepeatedAfterAWhile()
    {
        // For a player who joined while the base was not ready and ignored the water that came with it
        BaseFloodRules.ShouldSend(4f, 4f, BaseFloodRules.REPEAT_INTERVAL_SECONDS - 1f).Should().BeFalse();
        BaseFloodRules.ShouldSend(4f, 4f, BaseFloodRules.REPEAT_INTERVAL_SECONDS).Should().BeTrue();
    }

    [TestMethod]
    public void AGridOfAnotherSizeIsSentAtOnceEvenWithTheSameTotal()
    {
        // The base grew: what the server holds (the old size) no longer fits, a player who joins now would get a dry base
        BaseFloodRules.ShouldSend(4f, 4f, 1f, gridLength: 12, lastSentLength: 8).Should().BeTrue();
        BaseFloodRules.ShouldSend(4f, 4f, 1f, gridLength: 8, lastSentLength: 8).Should().BeFalse();
    }

    [TestMethod]
    public void ADryBaseIsNotRepeated()
    {
        BaseFloodRules.ShouldSend(0f, 0f, BaseFloodRules.REPEAT_INTERVAL_SECONDS * 10f).Should().BeFalse();
    }

    [TestMethod]
    public void TheEndOfTheDrainingIsSent()
    {
        // The last water is gone: the others must hear that the base is dry again
        BaseFloodRules.ShouldSend(0f, 1.5f).Should().BeTrue();
    }

    [TestMethod]
    public void TheSumOfNothingIsZero()
    {
        BaseFloodRules.Sum(null).Should().Be(0f);
        BaseFloodRules.Sum([]).Should().Be(0f);
        BaseFloodRules.Sum(twoCellsWet).Should().Be(1.5f);
    }
}
