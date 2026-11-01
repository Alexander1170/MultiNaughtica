namespace NitroxClient.GameLogic;

[TestClass]
public class SpawnerCreaturesTest
{
    [TestMethod]
    public void ACreatureNobodyKnowsIsIntroducedToTheServer()
    {
        SpawnerCreatures.Decide(someCreatureHasTheId: false).Should().Be(SpawnerCreatures.Outcome.INTRODUCE);
    }

    [TestMethod]
    public void ACreatureThatTheServerAlreadyGaveUsIsKeptInsteadOfAPrivateCopy()
    {
        SpawnerCreatures.Decide(someCreatureHasTheId: true).Should().Be(SpawnerCreatures.Outcome.ADOPT_KNOWN);
    }
}
