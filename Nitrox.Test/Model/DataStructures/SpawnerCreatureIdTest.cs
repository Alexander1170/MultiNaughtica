namespace Nitrox.Model.DataStructures;

[TestClass]
public class SpawnerCreatureIdTest
{
    private const string SPAWNER_ID = "5f3c9b0e-1d7a-4c52-9b41-6a2f8e0d3c71";

    [TestMethod]
    public void TwoClientsComputeTheSameId()
    {
        // Every client has the id of the spawner entity from the server and the same prefab: the id of its creature must not depend on anything else
        SpawnerCreatureId.For(new NitroxId(SPAWNER_ID), "Spawner#0|0").Should().Be(SpawnerCreatureId.For(new NitroxId(SPAWNER_ID), "Spawner#0|0"));
    }

    [TestMethod]
    public void TheIdIsStableAcrossRuns()
    {
        // A fixed expectation (MD5 of "spawner-creature:<spawner id>:<slot key>" as a guid): a change of the scheme would give every client of an older build another id for the same creature
        NitroxId spawnerId = new(SPAWNER_ID);

        SpawnerCreatureId.For(spawnerId, "Spawner#0|0").ToString().Should().Be("1ef21d12-2ce0-7ce2-1006-34b0ecef093b");
        SpawnerCreatureId.For(spawnerId, "Group#1/Spawner#2|1").ToString().Should().Be("b1abd7b4-21ef-023e-4b7b-6a4a836f6ee2");
    }

    [TestMethod]
    public void EverySlotOfASpawnerHasItsOwnId()
    {
        NitroxId spawnerId = new();

        SpawnerCreatureId.For(spawnerId, "Spawner#0|0").Should().NotBe(SpawnerCreatureId.For(spawnerId, "Spawner#0|1"));
        SpawnerCreatureId.For(spawnerId, "Spawner#0|0").Should().NotBe(SpawnerCreatureId.For(spawnerId, "Spawner#1|0"));
    }

    [TestMethod]
    public void EverySpawnerHasItsOwnIds()
    {
        SpawnerCreatureId.For(new NitroxId(), "Spawner#0|0").Should().NotBe(SpawnerCreatureId.For(new NitroxId(), "Spawner#0|0"));
    }
}
