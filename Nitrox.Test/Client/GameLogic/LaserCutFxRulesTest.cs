namespace NitroxClient.GameLogic;

[TestClass]
public class LaserCutFxRulesTest
{
    [TestMethod]
    public void NothingStoredYetIsNotStale()
    {
        // OnEnable stores the first position: SetCutFxPosition takes zero as "no position yet" and uses the node as it is
        LaserCutFxRules.IsStaleNodePosition(nodePositionIsZero: true, squaredDistanceToDoor: 1_000_000f).Should().BeFalse();
    }

    [TestMethod]
    public void ANodeOfTheDoorItselfIsNotStale()
    {
        // a node is a few metres from the root of the door, the lerp has to stay as it is
        LaserCutFxRules.IsStaleNodePosition(nodePositionIsZero: false, squaredDistanceToDoor: 2.5f * 2.5f).Should().BeFalse();
    }

    [TestMethod]
    public void ANodeStoredWhileTheDoorStoodAtTheWorldOriginIsStale()
    {
        // the door of the Aurora at (947, -4, -24), the node stored at OnEnable near (0, 0, 0)
        float squared = 947f * 947f + 4f * 4f + 24f * 24f;

        LaserCutFxRules.IsStaleNodePosition(nodePositionIsZero: false, squared).Should().BeTrue();
    }

    [TestMethod]
    public void TheLimitIsThirtyMetres()
    {
        LaserCutFxRules.IsStaleNodePosition(false, 29.9f * 29.9f).Should().BeFalse();
        LaserCutFxRules.IsStaleNodePosition(false, 30.1f * 30.1f).Should().BeTrue();
    }
}
