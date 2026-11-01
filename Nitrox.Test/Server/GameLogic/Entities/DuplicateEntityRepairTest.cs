using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.Serialization;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     The rule that finds the second copies a batch generated twice left in the entity data of a save, and decides which copy stays. See <see cref="DuplicateEntityRepair" />.
/// </summary>
[TestClass]
public class DuplicateEntityRepairTest
{
    private static DuplicateEntityRepair.RepairPlan Plan(List<Entity> entities, params NitroxId[] protectedIds)
    {
        return DuplicateEntityRepair.Plan(entities, [.. protectedIds], IsCreature);
    }

    // ---- creatures

    [TestMethod]
    public void TwoCreaturesOfTheSameKindAtTheSameSpotAreOneGhostCopy()
    {
        List<Entity> entities = [Creature(7), Creature(3)];

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].KeptId.Should().Be(Id(3), "no copy carries state: the lowest id stays");
        plan.Sets[0].RemovedIds.Should().Equal(Id(7));
        plan.RemovedIds.Should().BeEquivalentTo([Id(7)]);
    }

    [TestMethod]
    public void ThreeCopiesLeaveOne()
    {
        List<Entity> entities = [Creature(9), Creature(2), Creature(5)];

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle().Which.KeptId.Should().Be(Id(2));
        plan.RemovedIds.Should().BeEquivalentTo([Id(5), Id(9)]);
        plan.CopiesToRemove.Should().Be(2);
    }

    [TestMethod]
    public void CreaturesThatOnlyLookAlikeAreNotTwins()
    {
        List<Entity> entities =
        [
            Creature(1),
            Creature(2, x: 10.5f),                          // half a metre aside
            Creature(3, tech: "Spadefish"),                 // another kind
            Creature(4, classId: "other-class"),            // another prefab
            new WorldEntity(new NitroxVector3(10f, -5f, 20f), NitroxQuaternion.FromEuler(0f, 90f, 0f), NitroxVector3.One, new NitroxTechType("Boomerang"), 0, "class-Boomerang", true, Id(5), null),
            new WorldEntity(new NitroxVector3(10f, -5f, 20f), NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("Boomerang"), 1, "class-Boomerang", true, Id(6), null) // another cell level
        ];

        Plan(entities).Sets.Should().BeEmpty();
    }

    [TestMethod]
    public void PositionsWithinTheEpsilonAreTheSameSpot()
    {
        List<Entity> entities = [Creature(1, x: 10f), Creature(2, x: 10f + (DuplicateEntityRepair.POSITION_EPSILON / 2f))];

        Plan(entities).Sets.Should().ContainSingle();
    }

    [TestMethod]
    public void ACreatureThatAPlayerDroppedIsNeverATwin()
    {
        // SpawnedByServer is false for what players put into the world
        List<Entity> entities = [Creature(1, spawnedByServer: false), Creature(2, spawnedByServer: false)];

        Plan(entities).Sets.Should().BeEmpty();
    }

    [TestMethod]
    public void AResourceThatExistsTwiceAtOneSpotIsACopyOfASecondRunToo()
    {
        // measured on the world "Test": in the batches that were generated once there are 2 such pairs among 97632 entities, in the batches that were generated twice 339
        List<Entity> entities = [Creature(5, "LimestoneChunk"), Creature(4, "LimestoneChunk"), Creature(9, "LimestoneChunk", x: 99f)];

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].Kind.Should().Be(DuplicateEntityRepair.TwinKind.StaticResource);
        plan.Sets[0].KeptId.Should().Be(Id(4));
        plan.RemovedIds.Should().BeEquivalentTo([Id(5)]);
    }

    [TestMethod]
    public void AResourceThatAPlayerDroppedIsNeverATwin()
    {
        // the three Titanium pieces of the world "Test" that were dropped at one spot: SpawnedByServer is false
        List<Entity> entities = [Creature(1, "Titanium", spawnedByServer: false), Creature(2, "Titanium", spawnedByServer: false), Creature(3, "Titanium", spawnedByServer: false)];

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().BeEmpty();
        plan.TwinsLeftAlone.Should().Be(0);
    }

    // ---- what is left alone

    [TestMethod]
    public void DecorThatIsIdenticalIsCountedAndNeverTouched()
    {
        // the baked game data holds a few objects twice: 462 identical pairs of decor in the 139 batches of the world "Test" that were generated once, so a pair says nothing
        List<Entity> entities = [Decor(3, 5, 5, 5), Decor(4, 5, 5, 5), Decor(5, 5, 5, 5)];

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().BeEmpty();
        plan.Review.Should().BeEmpty();
        plan.RemovedIds.Should().BeEmpty();
        plan.TwinsLeftAlone.Should().Be(2, "two extra decor objects");
    }

    [TestMethod]
    public void AHealthyWorldHasNothingToRepair()
    {
        List<Entity> entities = [];
        for (int i = 1; i <= 2000; i++)
        {
            entities.Add(Creature(i, i % 2 == 0 ? "Boomerang" : "LimestoneChunk", x: i * 3f));
            entities.Add(Group(10000 + i, x: i * 3f + 1f));
        }

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().BeEmpty();
        plan.Review.Should().BeEmpty();
        plan.RemovedIds.Should().BeEmpty();
        plan.TwinsLeftAlone.Should().Be(0);
    }

    [TestMethod]
    public void AChildIsNeverATwinOfAnythingByItself()
    {
        PlaceholderGroupWorldEntity group = Group(1);
        Kid(2, group);
        Kid(3, group);
        List<Entity> entities = Flat(group);

        // two kids with the same class at the same offset under one parent are not top-level entities: the rule only looks at the top level
        Plan(entities).Sets.Should().BeEmpty();
    }

    // ---- groups

    [TestMethod]
    public void TheGroupThatLostItsChildrenIsTheCopyToDrop()
    {
        // the second run took the ids of the children of the first, which stay below the first copy in the list; the new group has none
        PlaceholderGroupWorldEntity complete = Group(10);
        Kid(11, complete);
        Kid(12, complete);
        Kid(13, complete);
        PlaceholderGroupWorldEntity empty = Group(5);
        List<Entity> entities = Flat(complete, empty);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].KeptId.Should().Be(Id(10), "the complete copy stays although its id is the higher one");
        plan.Sets[0].RemovedIds.Should().Equal(Id(5));
        plan.RemovedIds.Should().BeEquivalentTo([Id(5)]);
    }

    [TestMethod]
    public void TheChildrenOfARemovedCopyGoWithIt()
    {
        PlaceholderGroupWorldEntity keep = Group(1);
        Kid(2, keep);
        Kid(3, keep);
        Kid(4, keep);
        PlaceholderGroupWorldEntity drop = Group(20);
        PrefabPlaceholderEntity child = Kid(21, drop);
        Kid(22, child);
        List<Entity> entities = Flat(keep, drop);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].RemovedDescendants.Should().Be(2);
        plan.RemovedIds.Should().BeEquivalentTo([Id(20), Id(21), Id(22)]);
    }

    [TestMethod]
    public void AnOpenedCrateBeatsThePristineTwinWithItsLootStillInside()
    {
        // the copy a player opened carries the state; the other one is what the generator makes, loot included
        PlaceholderGroupWorldEntity opened = Group(100, "crate-class");
        opened.Metadata = new SupplyCrateMetadata(true);
        PlaceholderGroupWorldEntity pristine = Group(50, "crate-class");
        Kid(51, pristine);
        List<Entity> entities = Flat(opened, pristine);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].KeptId.Should().Be(Id(100));
        plan.RemovedIds.Should().BeEquivalentTo([Id(50), Id(51)]);
    }

    [TestMethod]
    public void ACopyWithATouchedChildKeepsItsPlace()
    {
        PlaceholderGroupWorldEntity cut = Group(30);
        PrefabPlaceholderEntity door = Kid(31, cut);
        door.Metadata = new SupplyCrateMetadata(true);
        PlaceholderGroupWorldEntity untouched = Group(40);
        Kid(41, untouched);
        Kid(42, untouched);
        List<Entity> entities = Flat(cut, untouched);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].KeptId.Should().Be(Id(30));
        plan.RemovedIds.Should().BeEquivalentTo([Id(40), Id(41), Id(42)]);
    }

    [TestMethod]
    public void ACopyWithAChildThatIsNotAGeneratedOneIsTouched()
    {
        // an item a player left in a wreck locker is no plain generated child
        PlaceholderGroupWorldEntity withItem = Group(30);
        InventoryItemEntity item = new(Id(31), "item-class", new NitroxTechType("Titanium"), null, withItem.Id, []);
        withItem.ChildEntities.Add(item);
        PlaceholderGroupWorldEntity plain = Group(40);
        List<Entity> entities = Flat(withItem, plain);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle().Which.KeptId.Should().Be(Id(30));
        plan.RemovedIds.Should().BeEquivalentTo([Id(40)]);
    }

    [TestMethod]
    public void TwoCopiesThatBothCarryStateWhichDiffersAreLeftForReview()
    {
        PlaceholderGroupWorldEntity first = Group(1, "crate-class");
        first.Metadata = new SupplyCrateMetadata(true);
        PlaceholderGroupWorldEntity second = Group(2, "crate-class");
        second.Metadata = new SupplyCrateMetadata(false);
        List<Entity> entities = Flat(first, second);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().BeEmpty();
        plan.RemovedIds.Should().BeEmpty();
        plan.Review.Should().ContainSingle().Which.Ids.Should().BeEquivalentTo([Id(1), Id(2)]);
    }

    [TestMethod]
    public void ACopyThatAPlayerOrABaseReferencesIsNeverRemovedAndTheSetIsLeftForReview()
    {
        PlaceholderGroupWorldEntity referenced = Group(1);
        PlaceholderGroupWorldEntity other = Group(2);
        List<Entity> entities = Flat(referenced, other);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities, Id(1));

        plan.RemovedIds.Should().BeEmpty();
        plan.Review.Should().ContainSingle();
    }

    [TestMethod]
    public void AReferenceToSomethingBelowACopyProtectsTheWholeSet()
    {
        PlaceholderGroupWorldEntity first = Group(1);
        Kid(11, first);
        PlaceholderGroupWorldEntity second = Group(2);
        List<Entity> entities = Flat(first, second);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities, Id(11));

        plan.RemovedIds.Should().BeEmpty();
        plan.Review.Should().ContainSingle();
    }

    // ---- the crash fish homes of the world "Test"

    [TestMethod]
    public void TwoCrashHomesWithTheSameCrashTimeAreOneHome()
    {
        // both copies were written by a client (the bootstrapper writes 0): game seconds 24546.87 and 24546.85
        PlaceholderGroupWorldEntity first = Group(1, "crash-home-class", tech: "CrashHome");
        first.Metadata = new CrashHomeMetadata(24546.87f);
        PlaceholderGroupWorldEntity second = Group(2, "crash-home-class", tech: "CrashHome");
        second.Metadata = new CrashHomeMetadata(24546.85f);
        List<Entity> entities = Flat(second, first);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle();
        plan.Sets[0].KeptId.Should().Be(Id(1), "the later crash time stays");
        plan.Sets[0].RemovedIds.Should().Equal(Id(2));
    }

    [TestMethod]
    public void TwoCrashHomesWhoseCrashTimesAreFarApartAreLeftForReview()
    {
        PlaceholderGroupWorldEntity first = Group(1, "crash-home-class", tech: "CrashHome");
        first.Metadata = new CrashHomeMetadata(24546.87f);
        PlaceholderGroupWorldEntity second = Group(2, "crash-home-class", tech: "CrashHome");
        second.Metadata = new CrashHomeMetadata(30663.15f);
        List<Entity> entities = Flat(first, second);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.RemovedIds.Should().BeEmpty();
        plan.Review.Should().ContainSingle();
    }

    [TestMethod]
    public void ACrashHomeThatNobodyTouchedHasTheMetadataOfTheBootstrapperAndIsPristine()
    {
        PlaceholderGroupWorldEntity first = Group(1, "crash-home-class", tech: "CrashHome");
        first.Metadata = new CrashHomeMetadata(0f);
        PlaceholderGroupWorldEntity second = Group(2, "crash-home-class", tech: "CrashHome");
        second.Metadata = new CrashHomeMetadata(0f);
        List<Entity> entities = Flat(first, second);

        DuplicateEntityRepair.RepairPlan plan = Plan(entities);

        plan.Sets.Should().ContainSingle().Which.KeptId.Should().Be(Id(1));
        plan.RemovedIds.Should().BeEquivalentTo([Id(2)]);
    }

    // ---- time

    [TestMethod]
    public void AWorldTheSizeOfTheWorldTestIsPlannedInSeconds()
    {
        // 186341 entities in the entity data of "Test" (98 MB): groups with children, creatures, resources, and about 1400 pairs of copies
        List<Entity> entities = new(190_000);
        int id = 1;
        for (int i = 0; i < 20_000; i++)
        {
            PlaceholderGroupWorldEntity group = Group(id++, $"group-{i % 40}", i * 3f, 0f, 0f);
            Kid(id++, group);
            Kid(id++, group);
            Kid(id++, group);
            entities.AddRange(Flat(group));
            entities.Add(Creature(id++, "Boomerang", i * 3f, 10f, 0f));
            entities.Add(Creature(id++, "LimestoneChunk", i * 3f, 20f, 0f));
            if (i % 14 == 0)
            {
                entities.Add(Creature(id++, "Boomerang", i * 3f, 10f, 0f));
            }
        }
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        DuplicateEntityRepair.RepairPlan plan = DuplicateEntityRepair.Plan(entities, [], IsCreature);

        stopwatch.Stop();
        plan.Sets.Should().HaveCount(1429, "one extra Boomerang for every 14th position");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    // ---- idempotence

    [TestMethod]
    public void RepairingTwiceDoesNothingTheSecondTime()
    {
        PlaceholderGroupWorldEntity complete = Group(10);
        Kid(11, complete);
        PlaceholderGroupWorldEntity empty = Group(5);
        List<Entity> entities = Flat(complete, empty);
        entities.Add(Creature(100));
        entities.Add(Creature(101));
        entities.Add(Creature(102, "Spadefish"));

        DuplicateEntityRepair.RepairPlan first = Plan(entities);
        first.RemovedIds.Should().NotBeEmpty();
        List<Entity> repaired = entities.Where(entity => !first.RemovedIds.Contains(entity.Id)).ToList();

        DuplicateEntityRepair.RepairPlan second = Plan(repaired);

        second.Sets.Should().BeEmpty();
        second.Review.Should().BeEmpty();
        second.RemovedIds.Should().BeEmpty();
    }

    [TestMethod]
    public void TheResultDoesNotDependOnTheOrderOfTheList()
    {
        List<Entity> entities = [Creature(9), Creature(2), Creature(5), Creature(30, "Spadefish"), Creature(31, "Spadefish")];
        List<Entity> reversed = Enumerable.Reverse(entities).ToList();

        DuplicateEntityRepair.RepairPlan forward = Plan(entities);
        DuplicateEntityRepair.RepairPlan backward = Plan(reversed);

        forward.RemovedIds.Should().BeEquivalentTo(backward.RemovedIds);
        forward.Sets.Select(set => set.KeptId).Should().BeEquivalentTo(backward.Sets.Select(set => set.KeptId));
    }

    // ---- the layout of coop.3 (entity data of the world "Test", read for this change; ids and numbers are the real ones)

    private const string COOP3_ENTITY_DATA = """
        {"Entities":[
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":126.91042,"Y":-80.18683,"Z":100.70262},"LocalRotation":{"X":-0.5141516,"Y":-0.48089027,"Z":0.54945153,"W":-0.44999522},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"60541f7a-af82-796c-f8cb-111423b68af8","Level":1,"TechType":"CrashHome","ClassId":"eb38de5d-c3df-4446-a37a-d770fb0f92bb","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica","SpawnTime":24546.87},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":126.91042,"Y":-80.18683,"Z":100.70262},"LocalRotation":{"X":-0.5141516,"Y":-0.48089027,"Z":0.54945153,"W":-0.44999522},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"c69c6e04-7295-99d9-594f-723cdd692c3c","Level":1,"TechType":"CrashHome","ClassId":"eb38de5d-c3df-4446-a37a-d770fb0f92bb","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica","SpawnTime":24546.85},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":509.1504,"Y":-86.43945,"Z":-410.4187},"LocalRotation":{"X":-0.21339978,"Y":0.21970841,"Z":0.022996545,"W":0.95166165},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"ea1a5610-cf86-ed3b-19d2-6d732177c933","Level":0,"TechType":"None","ClassId":"9ba6541e-193c-4744-9d4f-43c6e9f44d72","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.SupplyCrateMetadata, Nitrox.Model.Subnautica","Open":true},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":509.1504,"Y":-86.43945,"Z":-410.4187},"LocalRotation":{"X":-0.21339978,"Y":0.21970841,"Z":0.022996545,"W":0.95166165},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"733a949d-b692-d960-b01a-f1b1893c639a","Level":0,"TechType":"None","ClassId":"9ba6541e-193c-4744-9d4f-43c6e9f44d72","Metadata":null,"SpawnedByServer":true,"ParentId":null,"ChildEntities":[{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PrefabPlaceholderEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":0.0,"Y":0.31799316,"Z":0.0},"LocalRotation":{"X":0.0,"Y":0.0,"Z":0.0,"W":1.0},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"a19f8b75-2750-7b2b-1761-d80dfbdb5481","Level":0,"TechType":"DisinfectedWater","ClassId":"f7fb4077-b4d7-443c-b367-349cc1d39cc8","Metadata":null,"SpawnedByServer":true,"ParentId":"733a949d-b692-d960-b01a-f1b1893c639a","ChildEntities":[]}]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PrefabPlaceholderEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":0.0,"Y":0.31799316,"Z":0.0},"LocalRotation":{"X":0.0,"Y":0.0,"Z":0.0,"W":1.0},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"a19f8b75-2750-7b2b-1761-d80dfbdb5481","Level":0,"TechType":"DisinfectedWater","ClassId":"f7fb4077-b4d7-443c-b367-349cc1d39cc8","Metadata":null,"SpawnedByServer":true,"ParentId":"733a949d-b692-d960-b01a-f1b1893c639a","ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.WorldEntity, Nitrox.Model.Subnautica","Transform":{"LocalPosition":{"X":-774.3104,"Y":-15.3365965,"Z":-136.11162},"LocalRotation":{"X":0.0,"Y":0.95105654,"Z":0.0,"W":0.30901697},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"d6535e47-c1db-50f8-9341-a94057e8b18f","Level":0,"TechType":"Boomerang","ClassId":"fa4cfe65-4eaf-4d51-ba0d-e8cc9632fd47","Metadata":null,"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.WorldEntity, Nitrox.Model.Subnautica","Transform":{"LocalPosition":{"X":-774.3104,"Y":-15.3365965,"Z":-136.11162},"LocalRotation":{"X":0.0,"Y":0.95105654,"Z":0.0,"W":0.30901697},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"3c56cd26-c764-cc2a-fd93-816ce5e9994f","Level":0,"TechType":"Boomerang","ClassId":"fa4cfe65-4eaf-4d51-ba0d-e8cc9632fd47","Metadata":null,"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":-34.03,"Y":-128.22,"Z":-618.28},"LocalRotation":{"X":-0.122323714,"Y":-0.12800656,"Z":-0.0300337,"W":0.9837425},"LocalScale":{"X":0.99999994,"Y":0.9999999,"Z":1.0}},"Id":"85890469-0017-45d9-24d3-7d66ea49108b","Level":3,"TechType":"None","ClassId":"237347f9-36ce-4dd4-8a34-f75ccc00fc09","Metadata":null,"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":-34.03,"Y":-128.22,"Z":-618.28},"LocalRotation":{"X":-0.122323714,"Y":-0.12800656,"Z":-0.0300337,"W":0.9837425},"LocalScale":{"X":0.99999994,"Y":0.9999999,"Z":1.0}},"Id":"b4c21c89-ab69-447f-56e8-73b9b078fabe","Level":3,"TechType":"None","ClassId":"237347f9-36ce-4dd4-8a34-f75ccc00fc09","Metadata":null,"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PrefabPlaceholderEntity, Nitrox.Model.Subnautica","ComponentIndex":34,"Transform":{"LocalPosition":{"X":13.573945,"Y":20.968979,"Z":20.634037},"LocalRotation":{"X":-5.5879354e-09,"Y":9.429641e-07,"Z":-3.7252903e-08,"W":1.0},"LocalScale":{"X":1.056,"Y":1.0,"Z":1.0}},"Id":"5b079097-4ffd-042c-dce2-63f0140fb030","Level":1,"TechType":"None","ClassId":"9b5fd318-bb6d-4c76-b37d-b12a68ee715c","Metadata":null,"SpawnedByServer":true,"ParentId":"85890469-0017-45d9-24d3-7d66ea49108b","ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PrefabPlaceholderEntity, Nitrox.Model.Subnautica","ComponentIndex":36,"Transform":{"LocalPosition":{"X":15.532428,"Y":20.968903,"Z":14.372338},"LocalRotation":{"X":0.0,"Y":0.70710665,"Z":-7.45058e-09,"W":0.7071069},"LocalScale":{"X":1.1179997,"Y":1.0,"Z":1.0}},"Id":"37058a35-0b38-2a25-f670-3a486ca8da2a","Level":1,"TechType":"None","ClassId":"9b5fd318-bb6d-4c76-b37d-b12a68ee715c","Metadata":null,"SpawnedByServer":true,"ParentId":"85890469-0017-45d9-24d3-7d66ea49108b","ChildEntities":[]}
        ]}
        """;

    private static List<Entity> LoadCoop3Entities()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        EntityData data = serializer.Deserialize<EntityData>(new MemoryStream(Encoding.UTF8.GetBytes(COOP3_ENTITY_DATA)));
        return data.Entities;
    }

    [TestMethod]
    public void TheEntityDataOfCoop3Loads()
    {
        List<Entity> entities = LoadCoop3Entities();

        entities.Should().HaveCount(11);
        entities.OfType<PlaceholderGroupWorldEntity>().Should().HaveCount(6);
        entities.OfType<PrefabPlaceholderEntity>().Should().HaveCount(3);
        entities.Where(entity => entity.GetType() == typeof(WorldEntity)).Should().HaveCount(2);

        PlaceholderGroupWorldEntity crashHome = entities.OfType<PlaceholderGroupWorldEntity>().First(entity => entity.Id.ToString() == "60541f7a-af82-796c-f8cb-111423b68af8");
        crashHome.TechType.Name.Should().Be("CrashHome");
        crashHome.Level.Should().Be(1);
        crashHome.SpawnedByServer.Should().BeTrue();
        crashHome.ParentId.Should().BeNull();
        crashHome.Transform.LocalPosition.X.Should().BeApproximately(126.91042f, 0.0001f);
        crashHome.Metadata.Should().BeOfType<CrashHomeMetadata>().Which.SpawnTime.Should().BeApproximately(24546.87f, 0.01f);

        PlaceholderGroupWorldEntity crate = entities.OfType<PlaceholderGroupWorldEntity>().First(entity => entity.Id.ToString() == "ea1a5610-cf86-ed3b-19d2-6d732177c933");
        crate.Metadata.Should().BeOfType<SupplyCrateMetadata>().Which.Open.Should().BeTrue();

        PlaceholderGroupWorldEntity unopened = entities.OfType<PlaceholderGroupWorldEntity>().First(entity => entity.Id.ToString() == "733a949d-b692-d960-b01a-f1b1893c639a");
        unopened.ChildEntities.Should().ContainSingle().Which.Should().BeOfType<PrefabPlaceholderEntity>().Which.TechType.Name.Should().Be("DisinfectedWater");

        PrefabPlaceholderEntity kid = entities.OfType<PrefabPlaceholderEntity>().First(entity => entity.Id.ToString() == "5b079097-4ffd-042c-dce2-63f0140fb030");
        kid.ComponentIndex.Should().Be(34);
        kid.ParentId.ToString().Should().Be("85890469-0017-45d9-24d3-7d66ea49108b");
    }

    [TestMethod]
    public void TheDuplicatesOfTheWorldTestAreResolvedAsDecided()
    {
        List<Entity> entities = LoadCoop3Entities();

        DuplicateEntityRepair.RepairPlan plan = DuplicateEntityRepair.Plan(entities, [], DuplicateEntityRepair.IsMobileCreature);

        plan.Sets.Should().HaveCount(4);
        plan.Review.Should().BeEmpty();

        // the crash home: both copies were written by a client, the later crash time stays
        DuplicateEntityRepair.TwinSet crashHome = plan.Sets.Single(set => set.TechType == "CrashHome");
        crashHome.KeptId.ToString().Should().Be("60541f7a-af82-796c-f8cb-111423b68af8");
        crashHome.RemovedIds.Select(id => id.ToString()).Should().Equal("c69c6e04-7295-99d9-594f-723cdd692c3c");

        // the supply crate: the opened copy stays, the unopened one and the loot inside it go
        DuplicateEntityRepair.TwinSet crate = plan.Sets.Single(set => set.ClassId == "9ba6541e-193c-4744-9d4f-43c6e9f44d72");
        crate.KeptId.ToString().Should().Be("ea1a5610-cf86-ed3b-19d2-6d732177c933");
        crate.RemovedIds.Select(id => id.ToString()).Should().Equal("733a949d-b692-d960-b01a-f1b1893c639a");
        crate.RemovedDescendants.Should().Be(1);

        // the Boomerang: two fish at one spot, the lower id stays
        DuplicateEntityRepair.TwinSet fish = plan.Sets.Single(set => set.TechType == "Boomerang");
        fish.KeptId.ToString().Should().Be("3c56cd26-c764-cc2a-fd93-816ce5e9994f");
        fish.RemovedIds.Select(id => id.ToString()).Should().Equal("d6535e47-c1db-50f8-9341-a94057e8b18f");

        // the wreck group: the copy that still has its children stays, the empty one goes
        DuplicateEntityRepair.TwinSet wreck = plan.Sets.Single(set => set.ClassId == "237347f9-36ce-4dd4-8a34-f75ccc00fc09");
        wreck.KeptId.ToString().Should().Be("85890469-0017-45d9-24d3-7d66ea49108b");
        wreck.RemovedIds.Select(id => id.ToString()).Should().Equal("b4c21c89-ab69-447f-56e8-73b9b078fabe");

        plan.RemovedIds.Should().HaveCount(5);
    }

    [TestMethod]
    public void TheRepairedEntityDataOfCoop3NeedsNoSecondRepair()
    {
        List<Entity> entities = LoadCoop3Entities();
        DuplicateEntityRepair.RepairPlan plan = DuplicateEntityRepair.Plan(entities, [], DuplicateEntityRepair.IsMobileCreature);
        List<Entity> repaired = entities.Where(entity => !plan.RemovedIds.Contains(entity.Id)).ToList();

        repaired.Should().HaveCount(entities.Count - 5);
        DuplicateEntityRepair.Plan(repaired, [], DuplicateEntityRepair.IsMobileCreature).RemovedIds.Should().BeEmpty();
    }
}
