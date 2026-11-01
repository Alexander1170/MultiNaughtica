using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

/// <summary>
///     The load-time repair of a record of generated batches that is shorter than the entity data (a save that coop.3 wrote after a restart).
/// </summary>
[TestClass]
public class ParsedBatchRepairTest
{
    // creatures at x 10..26, y -5, z 20 stand in batch (12, 18, 12)
    private static readonly NitroxInt3 batchOfTheSpot = new(12, 18, 12);

    private static List<Entity> CreaturesInTheBatch(int count, int firstId, bool spawnedByServer = true)
    {
        List<Entity> entities = [];
        for (int i = 0; i < count; i++)
        {
            entities.Add(Creature(firstId + i, "Peeper", 10f + i, -5f, 20f, spawnedByServer));
        }
        return entities;
    }

    [TestMethod]
    public void TheBatchOfAPositionIsTheOneTheGameCalculates()
    {
        // a real entity of the world "Test", an AcidMushroom at (-60.78, -106.07, -633.53), came from batch (12, 18, 8)
        List<Entity> entities = [];
        for (int i = 0; i < ParsedBatchRepair.MIN_SERVER_ENTITIES; i++)
        {
            entities.Add(Creature(i + 1, "Peeper", -60.78f + (i * 0.5f), -106.07f, -633.53f));
        }

        ParsedBatchRepair.Derive(entities, null).Added.Should().Equal(new NitroxInt3(12, 18, 8));
    }

    [TestMethod]
    public void ABatchWithEnoughEntitiesTheServerSpawnedIsGenerated()
    {
        ParsedBatchRepair.Derivation derived = ParsedBatchRepair.Derive(CreaturesInTheBatch(ParsedBatchRepair.MIN_SERVER_ENTITIES, 1), []);

        derived.Added.Should().Equal(batchOfTheSpot);
        derived.All.Should().Equal(batchOfTheSpot);
    }

    [TestMethod]
    public void AFewStraysDoNotMakeABatchGenerated()
    {
        // creatures swim over the border of a batch nobody generated: counting such a batch would leave it empty for good
        ParsedBatchRepair.Derivation derived = ParsedBatchRepair.Derive(CreaturesInTheBatch(ParsedBatchRepair.MIN_SERVER_ENTITIES - 1, 1), []);

        derived.Added.Should().BeEmpty();
    }

    [TestMethod]
    public void WhatAPlayerDroppedAndChildrenDoNotCount()
    {
        List<Entity> entities = CreaturesInTheBatch(ParsedBatchRepair.MIN_SERVER_ENTITIES + 5, 1, spawnedByServer: false);
        PlaceholderGroupWorldEntity group = Group(500, x: 0f, y: 0f, z: 0f);
        for (int i = 0; i < ParsedBatchRepair.MIN_SERVER_ENTITIES + 5; i++)
        {
            Kid(501 + i, group);
        }
        entities.AddRange(Flat(group).Skip(1));

        ParsedBatchRepair.Derive(entities, []).Added.Should().BeEmpty();
    }

    [TestMethod]
    public void TheGlobalRootDoesNotCount()
    {
        List<Entity> entities = [];
        for (int i = 0; i < ParsedBatchRepair.MIN_SERVER_ENTITIES + 5; i++)
        {
            entities.Add(new GlobalRootEntity(new NitroxTransform(new NitroxVector3(10f + i, -5f, 20f), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "root-class", true, Id(i + 1), new NitroxTechType("Beacon"), null, null, []));
        }

        ParsedBatchRepair.Derive(entities, []).Added.Should().BeEmpty();
    }

    [TestMethod]
    public void AnEntityOfTheBakedGameDataNamesTheBatchItWasGeneratedFor()
    {
        // one is enough: it is fixed to the batch it belongs to and never moves
        List<Entity> entities = [Decor(1, 3, 4, 5, x: 100f, y: 100f, z: 100f)];

        ParsedBatchRepair.Derive(entities, []).Added.Should().Equal(new NitroxInt3(3, 4, 5));
    }

    [TestMethod]
    public void TheBatchesOfTheSaveStayFirstInTheirOrderAndTheAddedOnesFollowSorted()
    {
        List<Entity> entities = [Decor(1, 9, 9, 9), Decor(2, 2, 9, 9), Decor(3, 2, 1, 9), Decor(4, 2, 1, 3)];
        List<NitroxInt3> saved = [new(9, 9, 9), new(1, 1, 1), new(9, 9, 9)];

        ParsedBatchRepair.Derivation derived = ParsedBatchRepair.Derive(entities, saved);

        derived.All.Should().Equal(new NitroxInt3(9, 9, 9), new NitroxInt3(1, 1, 1), new NitroxInt3(2, 1, 3), new NitroxInt3(2, 1, 9), new NitroxInt3(2, 9, 9));
        derived.Added.Should().Equal(new NitroxInt3(2, 1, 3), new NitroxInt3(2, 1, 9), new NitroxInt3(2, 9, 9));
    }

    [TestMethod]
    public void ARecordThatLacksEveryBatchGetsThemBackAndANewRepairAddsNothing()
    {
        // the save of coop.3 after a restart: the entity data holds three generated batches, the record none of them
        List<Entity> entities = [];
        entities.AddRange(CreaturesInTheBatch(ParsedBatchRepair.MIN_SERVER_ENTITIES, 1));
        entities.Add(Decor(100, 3, 4, 5));
        entities.Add(Decor(101, 6, 7, 8));

        ParsedBatchRepair.Derivation first = ParsedBatchRepair.Derive(entities, []);
        ParsedBatchRepair.Derivation second = ParsedBatchRepair.Derive(entities, first.All);

        first.Added.Should().HaveCount(3);
        second.Added.Should().BeEmpty();
        second.All.Should().Equal(first.All);
    }

    // ---- the ids of the generator of a batch

    private const string SEED = "MRZIIKGLHO";

    private static Func<NitroxInt3, IEnumerable<NitroxId>> Stream => batchId => ParsedBatchRepair.BatchIdStream(SEED, batchId);

    [TestMethod]
    public void TheIdsOfABatchAreTheSameEveryTimeAndDifferPerBatchAndSeed()
    {
        List<NitroxId> first = ParsedBatchRepair.BatchIdStream(SEED, batchOfTheSpot).Take(50).ToList();
        List<NitroxId> again = ParsedBatchRepair.BatchIdStream(SEED, batchOfTheSpot).Take(50).ToList();

        again.Should().Equal(first);
        first.Distinct().Should().HaveCount(50);
        ParsedBatchRepair.BatchIdStream(SEED, new NitroxInt3(12, 18, 13)).Take(50).Should().NotIntersectWith(first);
        ParsedBatchRepair.BatchIdStream("OTHERSEED", batchOfTheSpot).Take(50).Should().NotIntersectWith(first);
    }

    [TestMethod]
    public void AnIdOfTheGeneratorOfABatchProvesTheBatchEvenForOneEntity()
    {
        NitroxId generated = ParsedBatchRepair.BatchIdStream(SEED, batchOfTheSpot).Skip(40).First();
        List<Entity> entities = [CreatureWithId(generated, "Peeper", 10f, -5f, 20f)];

        ParsedBatchRepair.Derive(entities, null, Stream).Added.Should().Equal(batchOfTheSpot);
        ParsedBatchRepair.Derive(entities, null).Added.Should().BeEmpty("without the generator one creature is a stray");
    }

    [TestMethod]
    public void AStrayWhoseIdIsNotOfTheGeneratorOfTheBatchItStandsInDoesNotCount()
    {
        List<Entity> entities = [Creature(77, "Peeper", 10f, -5f, 20f)];

        ParsedBatchRepair.Derive(entities, null, Stream).Added.Should().BeEmpty();
    }

    [TestMethod]
    public void ACreatureThatSwamIntoAnotherBatchProvesItsOwnBatchOnlyWhenSomethingStandsThere()
    {
        // generated for batch (12, 18, 13) and standing in batch (12, 18, 12) now: the stray does not make its own batch count, and the batch it came from has nothing in it
        NitroxId generated = ParsedBatchRepair.BatchIdStream(SEED, new NitroxInt3(12, 18, 13)).Skip(3).First();
        List<Entity> entities = [CreatureWithId(generated, "Peeper", 10f, -5f, 20f)];

        ParsedBatchRepair.Derive(entities, null, Stream).Added.Should().BeEmpty();
    }

    [TestMethod]
    public void OnlyTheFirstIdsOfTheGeneratorAreLookedAt()
    {
        NitroxId last = ParsedBatchRepair.BatchIdStream(SEED, batchOfTheSpot).Skip(ParsedBatchRepair.ID_STREAM_DEPTH - 1).First();
        NitroxId beyond = ParsedBatchRepair.BatchIdStream(SEED, batchOfTheSpot).Skip(ParsedBatchRepair.ID_STREAM_DEPTH).First();

        ParsedBatchRepair.Derive([CreatureWithId(last, "Peeper", 10f, -5f, 20f)], null, Stream).Added.Should().Equal(batchOfTheSpot);
        ParsedBatchRepair.Derive([CreatureWithId(beyond, "Peeper", 10f, -5f, 20f)], null, Stream).Added.Should().BeEmpty();
    }

    [TestMethod]
    public void AWorldWithoutEntitiesNeedsNoBatches()
    {
        ParsedBatchRepair.Derive([], null).All.Should().BeEmpty();
    }
}
