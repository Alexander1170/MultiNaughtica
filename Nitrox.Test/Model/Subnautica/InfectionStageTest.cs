using System.Linq;
using Nitrox.Model.Subnautica.Helper;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class InfectionStageTest
{
    private static float CatchUp(float saved, params string[] goals) => InfectionStage.CatchUp(saved, goals);

    [TestMethod]
    public void ANewPlayerAfterTheCureIsCured()
    {
        CatchUp(0.1f, "Infection_Progress1", "Infection_Progress2", "Infection_Progress3", "Infection_Progress4", "Infection_Progress5").Should().Be(0f);
    }

    [TestMethod]
    public void APlayerWhoWasOfflineForTheCureIsCuredWhateverHisStage()
    {
        CatchUp(1f, "Infection_Progress5").Should().Be(0f);
        CatchUp(0.75f, "Infection_Progress5").Should().Be(0f);
    }

    [TestMethod]
    public void ANewPlayerIsBroughtToTheStageOfTheTeam()
    {
        CatchUp(0.1f, "Infection_Progress1", "Infection_Progress2", "Infection_Progress3").Should().Be(0.75f);
        CatchUp(0.1f, "Infection_Progress1").Should().Be(0.25f);
    }

    [TestMethod]
    public void TheInfectionNeverGoesDown()
    {
        CatchUp(0.9f, "Infection_Progress1", "Infection_Progress2").Should().Be(0.9f);
    }

    [TestMethod]
    public void ACuredPlayerIsNotMadeSickAgain()
    {
        CatchUp(0f, "Infection_Progress1", "Infection_Progress2", "Infection_Progress3").Should().Be(0f);
    }

    [TestMethod]
    public void TheSelfScanRevealSetsTheFullInfection()
    {
        CatchUp(0.1f, "Infection_Progress1", "SelfScan4").Should().Be(1f);
    }

    [TestMethod]
    public void TheCureWinsOverTheSelfScan()
    {
        CatchUp(0.1f, "SelfScan4", "Infection_Progress5").Should().Be(0f);
    }

    [TestMethod]
    public void NoGoalsLeaveTheAmountAlone()
    {
        CatchUp(0.1f).Should().Be(0.1f);
    }

    [TestMethod]
    public void TheKeysAreComparedWithoutRegardToCase()
    {
        CatchUp(0.1f, "infection_progress5").Should().Be(0f);
    }

    [TestMethod]
    [DataRow(0.1f, true)]
    [DataRow(0.75f, true)]
    [DataRow(1f, true)]
    [DataRow(0f, false)]
    public void OnlyAnInfectedPlayerHasAnInfectionToReveal(float amount, bool expected)
    {
        InfectionStage.CanReveal(amount).Should().Be(expected);
    }

    [TestMethod]
    public void TheRevealHasHappenedOnceTheSelfScanGoalIsDoneAndNobodyIsCured()
    {
        InfectionStage.IsRevealed(goal => goal == "SelfScan4").Should().BeTrue();
        InfectionStage.IsRevealed(goal => goal == "Infection_Progress4").Should().BeFalse();
        InfectionStage.IsRevealed(goal => goal is "SelfScan4" or "Infection_Progress5").Should().BeFalse();
    }

    [TestMethod]
    public void TheSetOverloadReadsALiveGoalSet()
    {
        InfectionStage.CatchUp(0.1f, goal => goal == "Infection_Progress2").Should().Be(0.5f);
    }
}
