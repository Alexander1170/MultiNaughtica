namespace NitroxClient.GameLogic;

[TestClass]
public class WeldablePanelRulesTest
{
    private const float MAX = 100f;

    [TestMethod]
    public void ARestoredRepairedPanelGetsItsFullHealthBeforeStartReadsIt()
    {
        // joiner or restart: WeldableWallPanelGeneric.Start then sees full health and unlocks the door at once, LiveMixin.Start spawns no sparks
        WeldablePanelRules.Result result = WeldablePanelRules.Apply(freshlySpawned: true, currentHealth: 0f, incomingHealth: 100f, MAX);

        result.Health.Should().Be(100f);
        result.HealBy.Should().Be(0f);
    }

    [TestMethod]
    public void ARepairByAnotherPlayerGoesThroughAddHealthSoTheSparksStop()
    {
        WeldablePanelRules.Result result = WeldablePanelRules.Apply(freshlySpawned: false, currentHealth: 0f, incomingHealth: 100f, MAX);

        result.HealBy.Should().Be(100f);
        result.Health.Should().Be(0f);
    }

    [TestMethod]
    public void AReportOfAHealthThatThePanelHasAlreadyDoesNothing()
    {
        WeldablePanelRules.Result result = WeldablePanelRules.Apply(freshlySpawned: false, currentHealth: 100f, incomingHealth: 100f, MAX);

        result.HealBy.Should().Be(0f);
        result.Health.Should().Be(100f);
    }

    [TestMethod]
    public void ALateLowerReportNeverDamagesARepairedPanel()
    {
        WeldablePanelRules.Result result = WeldablePanelRules.Apply(freshlySpawned: false, currentHealth: 100f, incomingHealth: 30f, MAX);

        result.HealBy.Should().Be(0f);
        result.Health.Should().Be(100f);
    }

    [TestMethod]
    public void TheHealthIsCappedAtTheMaximum()
    {
        WeldablePanelRules.Apply(freshlySpawned: true, 0f, 400f, MAX).Health.Should().Be(100f);
        WeldablePanelRules.Apply(freshlySpawned: false, 40f, 400f, MAX).HealBy.Should().Be(60f);
    }

    [TestMethod]
    public void GarbageHealthCountsAsDamaged()
    {
        WeldablePanelRules.Apply(freshlySpawned: true, 0f, float.NaN, MAX).Health.Should().Be(0f);
        WeldablePanelRules.Apply(freshlySpawned: true, 0f, -3f, MAX).Health.Should().Be(0f);
    }
}
