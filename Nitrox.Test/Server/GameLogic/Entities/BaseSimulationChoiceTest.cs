using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.EntitySimulation;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

[TestClass]
public class BaseSimulationChoiceTest
{
    private static BaseSimulationCandidate Inside(float distance = 5f) => new(true, distance);

    private static BaseSimulationCandidate Outside(float distance) => new(false, distance);

    [TestMethod]
    public void HolderInsideTheBase_KeepsIt()
    {
        BaseSimulationCandidate[] players = [Inside(), Inside(3f)];

        ChooseBaseSimulator(0, players).Should().Be(0);
        ChooseBaseSimulator(1, players).Should().Be(1);
    }

    [TestMethod]
    public void HolderNearTheBase_KeepsItWhoeverIsInside()
    {
        // Alice stands outside, 60 m from the base; Bob is inside: no reason to move the base around
        BaseSimulationCandidate[] players = [Outside(60f), Inside()];

        ChooseBaseSimulator(0, players).Should().Be(0);
    }

    [TestMethod]
    public void HolderFarAway_PlayerInsideTheBaseTakesOver()
    {
        // Alice 1 km away exploring, Bob in the base
        BaseSimulationCandidate[] players = [Outside(1000f), Inside()];

        ChooseBaseSimulator(0, players).Should().Be(1);
    }

    [TestMethod]
    public void HolderFarAway_PlayerInsideWinsOverACloserPlayerOutside()
    {
        BaseSimulationCandidate[] players = [Outside(1000f), Outside(120f), Inside(10f)];

        ChooseBaseSimulator(0, players).Should().Be(2);
    }

    [TestMethod]
    public void HolderFarAway_NobodyInside_ClearlyCloserPlayerTakesOver()
    {
        BaseSimulationCandidate[] players = [Outside(1000f), Outside(130f)];

        ChooseBaseSimulator(0, players).Should().Be(1);
    }

    [TestMethod]
    public void HolderFarAway_NobodyInside_SlightlyCloserPlayerDoesNotTakeOver()
    {
        // 300 m against 200 m: closer, but not by the 150 m that justify a handover
        BaseSimulationCandidate[] players = [Outside(300f), Outside(200f)];

        ChooseBaseSimulator(0, players).Should().Be(0);
    }

    [TestMethod]
    public void HolderFarAway_TwoPlayersInside_TheCloserOneTakesOver()
    {
        BaseSimulationCandidate[] players = [Outside(1000f), Inside(30f), Inside(8f)];

        ChooseBaseSimulator(0, players).Should().Be(2);
    }

    [TestMethod]
    public void AfterTheHandover_TheReturningPlayerDoesNotTakeTheBaseBack()
    {
        // Bob holds the base and is inside; Alice comes back and stands next to it, then walks in
        BaseSimulationCandidate[] players = [Outside(40f), Inside()];
        ChooseBaseSimulator(1, players).Should().Be(1);

        players = [Inside(), Inside()];
        ChooseBaseSimulator(1, players).Should().Be(1);
    }

    [TestMethod]
    public void HolderLeavesTheBase_ThePlayerWhoStaysTakesOverOnlyOnceTheHolderIsFarAway()
    {
        // Bob (the holder) walks out; Alice is inside
        ChooseBaseSimulator(1, [Inside(), Outside(60f)]).Should().Be(1);
        ChooseBaseSimulator(1, [Inside(), Outside(400f)]).Should().Be(0);
    }

    [TestMethod]
    public void SinglePlayer_KeepsTheBase()
    {
        ChooseBaseSimulator(0, [Outside(5000f)]).Should().Be(0);
    }
}
