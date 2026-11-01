using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.Serialization;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     The sulfur plants of a loaded world must agree with the Crashfish the world holds for them (<see cref="CrashHomeStates" />). coop.3 lost the fish of nearly every home (1 fish for the 66 homes of
///     the world "Test"): a client told the server that the fish was destroyed whenever the player who held the lock of a home walked away.
/// </summary>
[TestClass]
public class CrashHomeStatesTest
{
    private static PlaceholderGroupWorldEntity Home(int id, float? spawnTime)
    {
        PlaceholderGroupWorldEntity home = Group(id, "crash-home-class", tech: "CrashHome");
        home.Metadata = spawnTime.HasValue ? new CrashHomeMetadata(spawnTime.Value) : null;
        return home;
    }

    /// <summary>
    ///     A fish as a running server holds it: a world entity with the home as its parent id, which the server did not spawn, and a transform with the position in the world and no parent (the client
    ///     introduces it with <c>ToWorldDto</c>). The child list of the home is rebuilt by the loader from the parent id, so it is only filled when a test asks for it. With
    ///     <paramref name="hangsUnderTheHome" /> the transform is hung under the one of the home, as the loader of a save does.
    /// </summary>
    private static WorldEntity Fish(int id, Entity home, bool listedInTheHome = false, bool hangsUnderTheHome = false)
    {
        WorldEntity fish = new(new NitroxVector3(1f, 2f, 3f), NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("Crash"), 1, "crash-class", false, Id(id), home);
        if (!hangsUnderTheHome)
        {
            fish.Transform.SetParent(null, false);
        }
        if (listedInTheHome)
        {
            home.ChildEntities.Add(fish);
        }
        return fish;
    }

    private static float? SpawnTimeOf(Entity entity) => (entity.Metadata as CrashHomeMetadata)?.SpawnTime;

    // ---- a home without a fish

    [TestMethod]
    public void AHomeAtMinusOneWithoutAFishIsMadeFresh()
    {
        PlaceholderGroupWorldEntity home = Home(1, -1f);

        CrashHomeStates.Result result = CrashHomeStates.Normalize([home]);

        SpawnTimeOf(home).Should().Be(CrashHomeStates.PRISTINE, "the first player who holds the home makes its fish at once");
        result.FishlessHomes.Should().Be(1);
        result.Changed.Should().Be(1);
    }

    [TestMethod]
    public void AHomeWithoutAnyStateAndWithoutAFishIsMadeFresh()
    {
        PlaceholderGroupWorldEntity home = Home(1, null);

        CrashHomeStates.Normalize([home]).FishlessHomes.Should().Be(1);

        SpawnTimeOf(home).Should().Be(CrashHomeStates.PRISTINE);
    }

    [TestMethod]
    public void AHomeThatNobodyMetStaysAsItIs()
    {
        PlaceholderGroupWorldEntity home = Home(1, 0f);
        EntityMetadata? before = home.Metadata;

        CrashHomeStates.Normalize([home]).Changed.Should().Be(0);

        home.Metadata.Should().BeSameAs(before);
    }

    [TestMethod]
    public void AHomeWaitingForItsNextFishKeepsItsTimer()
    {
        // 36 of the 66 homes of the world "Test" are in this state: the fish is gone and the timer runs. The timer is the truth, not something to repair
        PlaceholderGroupWorldEntity home = Home(1, 30483.223f);
        EntityMetadata? before = home.Metadata;

        CrashHomeStates.Normalize([home]).Changed.Should().Be(0);

        home.Metadata.Should().BeSameAs(before);
        SpawnTimeOf(home).Should().Be(30483.223f);
    }

    // ---- a home with a fish

    [TestMethod]
    public void AHomeAtMinusOneWithItsFishIsLeftAlone()
    {
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        EntityMetadata? before = home.Metadata;
        WorldEntity fish = Fish(2, home);

        CrashHomeStates.Normalize([home, fish]).Changed.Should().Be(0);

        home.Metadata.Should().BeSameAs(before);
    }

    [TestMethod]
    public void AHomeWithAFishAndATimerLosesTheTimer()
    {
        // the one fish that is left in the world "Test" belongs to a home with the timer 30483.223: the lock holder made the timer before the fish reached the home. When it ran out, a second fish was made
        PlaceholderGroupWorldEntity home = Home(1, 30483.223f);
        WorldEntity fish = Fish(2, home);

        CrashHomeStates.Result result = CrashHomeStates.Normalize([home, fish]);

        SpawnTimeOf(home).Should().Be(CrashHomeStates.FISH_ALIVE);
        result.HomesWithFishAndTimer.Should().Be(1);
        result.FishlessHomes.Should().Be(0);
    }

    [TestMethod]
    public void AHomeAtZeroWithAFishIsAHomeWithAFish()
    {
        // the second copy of a home that a batch made again carries the number of a home nobody met, and may get the fish of the first
        PlaceholderGroupWorldEntity home = Home(1, 0f);
        WorldEntity fish = Fish(2, home);

        CrashHomeStates.Normalize([home, fish]).HomesWithFishAndTimer.Should().Be(1);

        SpawnTimeOf(home).Should().Be(CrashHomeStates.FISH_ALIVE);
    }

    [TestMethod]
    public void TheFishIsFoundByItsParentIdOrByTheChildListOfTheHome()
    {
        // a save keeps the parent ids and the loader rebuilds the child lists; a registry holds both. Either one is enough
        PlaceholderGroupWorldEntity byParentId = Home(1, 100f);
        WorldEntity fishOfFirst = Fish(2, byParentId);
        PlaceholderGroupWorldEntity byChildList = Home(3, 100f);
        // this fish is not part of the list that is given in, only of the child list of its home
        Fish(4, byChildList, listedInTheHome: true);

        CrashHomeStates.Result result = CrashHomeStates.Normalize([byParentId, fishOfFirst, byChildList]);

        SpawnTimeOf(byParentId).Should().Be(CrashHomeStates.FISH_ALIVE);
        SpawnTimeOf(byChildList).Should().Be(CrashHomeStates.FISH_ALIVE);
        result.HomesWithFishAndTimer.Should().Be(2);
    }

    [TestMethod]
    public void AFishOfAnotherHomeIsNoFishOfThisOne()
    {
        PlaceholderGroupWorldEntity lonely = Home(1, -1f);
        PlaceholderGroupWorldEntity other = Home(2, -1f);
        WorldEntity fish = Fish(3, other);

        CrashHomeStates.Result result = CrashHomeStates.Normalize([lonely, other, fish]);

        SpawnTimeOf(lonely).Should().Be(CrashHomeStates.PRISTINE);
        SpawnTimeOf(other).Should().Be(CrashHomeStates.FISH_ALIVE);
        result.FishlessHomes.Should().Be(1);
    }

    // ---- the fish after a restart

    [TestMethod]
    public void ACrashfishThatHangsUnderTheTransformOfItsHomeIsTakenOutAgain()
    {
        // the loader of a save hangs every child under its parent without converting the values, and a fish is saved with its world position
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        home.Transform.LocalPosition = new NitroxVector3(100f, -50f, 300f);
        WorldEntity fish = Fish(2, home, hangsUnderTheHome: true);
        fish.Transform.LocalPosition = new NitroxVector3(100f, -50f, 300f);
        fish.Transform.Position.Should().NotBe(new NitroxVector3(100f, -50f, 300f), "under the home the fish stands somewhere else");

        CrashHomeStates.Result result = CrashHomeStates.Normalize([home, fish]);

        fish.Transform.Parent.Should().BeNull();
        fish.Transform.LocalPosition.Should().Be(new NitroxVector3(100f, -50f, 300f), "the values are the world position, they stay");
        fish.Transform.Position.Should().Be(new NitroxVector3(100f, -50f, 300f));
        result.FishTransforms.Should().Be(1);
    }

    // ---- a second fish under one home

    [TestMethod]
    public void AHomeWithOneFishOrNoneHasNoSurplusFish()
    {
        PlaceholderGroupWorldEntity withFish = Home(1, -1f);
        WorldEntity fish = Fish(2, withFish);
        PlaceholderGroupWorldEntity withoutFish = Home(3, 0f);

        CrashHomeStates.FindSurplusFish([withFish, fish, withoutFish]).Should().BeEmpty();
    }

    [TestMethod]
    public void AHomeWithTwoFishKeepsTheOneWithTheLowestId()
    {
        // a second fish is a ghost on every client that loads the home: the client destroys only the fish its home points to
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        WorldEntity first = Fish(2, home);
        WorldEntity second = Fish(3, home);

        List<Entity> surplus = CrashHomeStates.FindSurplusFish([home, second, first]);

        surplus.Should().ContainSingle().Which.Should().BeSameAs(second);
    }

    [TestMethod]
    public void TheFishThatStaysDoesNotDependOnTheOrderOfTheWorld()
    {
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        WorldEntity first = Fish(2, home);
        WorldEntity second = Fish(3, home);
        WorldEntity third = Fish(4, home);

        CrashHomeStates.FindSurplusFish([home, first, second, third]).Select(fish => fish.Id).Should().Equal(Id(3), Id(4));
        CrashHomeStates.FindSurplusFish([third, second, first, home]).Select(fish => fish.Id).Should().Equal(Id(3), Id(4));
        CrashHomeStates.FindSurplusFish([second, home, third, first]).Select(fish => fish.Id).Should().Equal(Id(3), Id(4));
    }

    [TestMethod]
    public void TheFishOfTwoHomesAreNotCountedTogether()
    {
        PlaceholderGroupWorldEntity homeA = Home(1, -1f);
        PlaceholderGroupWorldEntity homeB = Home(2, -1f);
        WorldEntity fishOfA = Fish(3, homeA);
        WorldEntity fishOfB = Fish(4, homeB);

        CrashHomeStates.FindSurplusFish([homeA, homeB, fishOfA, fishOfB]).Should().BeEmpty();
    }

    [TestMethod]
    public void TwoFishUnderSomethingThatIsNoHomeAreNotTouched()
    {
        // the ids of homes were given to other entities by a batch that was generated again: a Crash entity under such an id is not a second fish of a home
        WorldEntity peeper = Creature(1, "Peeper");
        WorldEntity fishA = Fish(2, peeper);
        WorldEntity fishB = Fish(3, peeper);

        CrashHomeStates.FindSurplusFish([peeper, fishA, fishB]).Should().BeEmpty();
    }

    [TestMethod]
    public void AFishSeenByItsParentIdAndByTheChildListOfItsHomeIsOneFish()
    {
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        WorldEntity fish = Fish(2, home, listedInTheHome: true);

        CrashHomeStates.FindSurplusFish([home, fish]).Should().BeEmpty();
    }

    [TestMethod]
    public void ASecondFishThatOnlyTheChildListOfTheHomeKnowsIsFound()
    {
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        WorldEntity first = Fish(2, home);
        // not part of the list that is given in
        WorldEntity second = Fish(3, home, listedInTheHome: true);

        CrashHomeStates.FindSurplusFish([home, first]).Should().ContainSingle().Which.Id.Should().Be(second.Id);
    }

    [TestMethod]
    public void TheSurplusFishLeavesTheRegistryAndTheChildListOfItsHome()
    {
        // what the loader of the server does (WorldService.NormalizeCrashHomes): the surplus fish is taken out of the registry, the home keeps one fish and its number
        EntityRegistry registry = new(NullLogger<EntityRegistry>.Instance);
        PlaceholderGroupWorldEntity home = Home(1, -1f);
        WorldEntity first = Fish(2, home, listedInTheHome: true);
        WorldEntity second = Fish(3, home, listedInTheHome: true);
        registry.AddEntities([home, first, second]);

        foreach (Entity surplus in CrashHomeStates.FindSurplusFish(registry.GetAllEntities()))
        {
            registry.RemoveEntity(surplus.Id);
        }

        registry.TryGetEntityById(first.Id, out Entity _).Should().BeTrue();
        registry.TryGetEntityById(second.Id, out Entity _).Should().BeFalse();
        home.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(first);
        CrashHomeStates.FindSurplusFish(registry.GetAllEntities()).Should().BeEmpty("the second start of the server has nothing left to do");
        CrashHomeStates.Normalize(registry.GetAllEntities()).Changed.Should().Be(0);
    }

    // ---- what is not a home

    [TestMethod]
    public void AnEntityThatIsNoHomeLosesTheStateOfAHome()
    {
        // 12 entities of the world "Test" (Peepers, RabbitRays, a Boomerang, a Bladderfish) carry it: a batch that was generated again gave them the ids of homes, and the client of the home wrote its state to them
        WorldEntity peeper = Creature(1, "Peeper");
        peeper.Metadata = new CrashHomeMetadata(-1f);

        CrashHomeStates.Result result = CrashHomeStates.Normalize([peeper]);

        peeper.Metadata.Should().BeNull();
        result.OtherEntities.Should().Be(1);
    }

    [TestMethod]
    public void TheMetadataOfOtherKindsIsNotTouched()
    {
        WorldEntity crate = Creature(1, "Locker");
        SupplyCrateMetadata crateMetadata = new(true);
        crate.Metadata = crateMetadata;
        PlaceholderGroupWorldEntity homeWithOtherState = Home(2, null);
        homeWithOtherState.Metadata = crateMetadata;

        CrashHomeStates.Normalize([crate, homeWithOtherState]).Changed.Should().Be(0);

        crate.Metadata.Should().BeSameAs(crateMetadata);
        homeWithOtherState.Metadata.Should().BeSameAs(crateMetadata);
    }

    [TestMethod]
    public void AnEntityWithoutATechTypeDoesNotMakeItThrow()
    {
        // a base (BuildEntity) has no tech type
        Entity baseEntity = BuildEntity.MakeEmpty();
        baseEntity.TechType.Should().BeNull();

        CrashHomeStates.Normalize([baseEntity]).Changed.Should().Be(0);
    }

    [TestMethod]
    public void NormalizingTwiceChangesNothingTheSecondTime()
    {
        PlaceholderGroupWorldEntity fishless = Home(1, -1f);
        PlaceholderGroupWorldEntity withTimer = Home(2, 5000f);
        WorldEntity fish = Fish(3, withTimer);
        WorldEntity peeper = Creature(4, "Peeper");
        peeper.Metadata = new CrashHomeMetadata(-1f);
        List<Entity> world = [fishless, withTimer, fish, peeper];

        CrashHomeStates.Normalize(world).Changed.Should().Be(3);
        CrashHomeStates.Normalize(world).Changed.Should().Be(0);
    }

    [TestMethod]
    public void TheClientAndTheServerMeanTheSameNumberByAFishThatLives()
    {
        CrashHomeStates.FISH_ALIVE.Should().Be(NitroxClient.GameLogic.CrashHomeRules.FISH_ALIVE);
    }

    // ---- the world "Test" of coop.3

    /// <summary>Real entities of the world "Test" (EntityData.json after the second session): a home at -1 without a fish, a home with a timer and its one fish (the fish has a world position and the home as parent), a home nobody met, and a Peeper with the state of a home.</summary>
    private const string COOP3_CRASH_HOMES = """
        {"Entities":[
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":116.8008,"Y":-51.98046,"Z":-249.1109},"LocalRotation":{"X":0.545907,"Y":-0.4250918,"Z":0.39245754,"W":0.6060195},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"05fb77b6-bd94-5581-46bf-b1b169cd558a","Level":1,"TechType":"CrashHome","ClassId":"eb38de5d-c3df-4446-a37a-d770fb0f92bb","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica","SpawnTime":-1.0},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":-150.27078,"Y":-48.292652,"Z":-334.71762},"LocalRotation":{"X":0.40574256,"Y":0.43090516,"Z":-0.23275597,"W":0.7716984},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"6006e879-e82b-30ca-750f-337f38d8b181","Level":1,"TechType":"CrashHome","ClassId":"eb38de5d-c3df-4446-a37a-d770fb0f92bb","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica","SpawnTime":30483.223},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.WorldEntity, Nitrox.Model.Subnautica","Transform":{"LocalPosition":{"X":-150.27078,"Y":-48.292652,"Z":-334.71762},"LocalRotation":{"X":-0.25876987,"Y":0.46927935,"Z":0.14011265,"W":0.8325765},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"9f2c1705-2245-41bf-b8e9-4e3c9fe56148","Level":1,"TechType":"Crash","ClassId":"7d307502-46b7-4f86-afb0-65fe8867f893","Metadata":null,"SpawnedByServer":false,"ParentId":"6006e879-e82b-30ca-750f-337f38d8b181","ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlaceholderGroupWorldEntity, Nitrox.Model.Subnautica","ComponentIndex":0,"Transform":{"LocalPosition":{"X":-775.0486,"Y":-60.109493,"Z":134.79709},"LocalRotation":{"X":0.7809336,"Y":-0.18032472,"Z":0.26194662,"W":0.53759634},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"a7d14ee1-80b2-0c92-47e1-52bd2ca920d7","Level":1,"TechType":"CrashHome","ClassId":"eb38de5d-c3df-4446-a37a-d770fb0f92bb","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica","SpawnTime":0.0},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]},
        {"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.WorldEntity, Nitrox.Model.Subnautica","Transform":{"LocalPosition":{"X":55.824844,"Y":-14.253814,"Z":-110.24146},"LocalRotation":{"X":0.70595855,"Y":0.6927666,"Z":0.0037177943,"W":-0.14725213},"LocalScale":{"X":1.0,"Y":1.0,"Z":1.0}},"Id":"20dbf5fb-a262-7cdf-116d-f429bc1f0455","Level":0,"TechType":"Peeper","ClassId":"3fcd548b-781f-46ba-b076-7412608deeef","Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica","SpawnTime":-1.0},"SpawnedByServer":true,"ParentId":null,"ChildEntities":[]}
        ]}
        """;

    private static (ServerJsonSerializer Serializer, EntityData Data) LoadCoop3CrashHomes()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        return (serializer, serializer.Deserialize<EntityData>(new MemoryStream(Encoding.UTF8.GetBytes(COOP3_CRASH_HOMES))));
    }

    [TestMethod]
    public void TheCrashHomesOfTheWorldTestAreSetRightAsDecided()
    {
        var (_, data) = LoadCoop3CrashHomes();
        data.Entities.Should().HaveCount(5);
        Entity Find(string id) => data.Entities.Single(entity => entity.Id.ToString() == id);

        // what the loader of a save does (WorldService, EntityDataPostProcessing): the children are hung under their parents without converting the values
        foreach (Entity entity in data.Entities)
        {
            if (entity.ParentId != null && Find(entity.ParentId.ToString()) is WorldEntity parent && entity is WorldEntity child)
            {
                child.Transform.SetParent(parent.Transform, false);
            }
        }
        WorldEntity fish = (WorldEntity)Find("9f2c1705-2245-41bf-b8e9-4e3c9fe56148");
        WorldEntity homeOfTheFish = (WorldEntity)Find("6006e879-e82b-30ca-750f-337f38d8b181");
        fish.AbsoluteEntityCell.Should().NotBe(homeOfTheFish.AbsoluteEntityCell, "hung under the home, the fish is in another cell than the one it stands in");

        CrashHomeStates.Result result = CrashHomeStates.Normalize(data.Entities);

        // a home at -1 without a fish: fresh, so that the first player who holds it gets a fish at once
        SpawnTimeOf(Find("05fb77b6-bd94-5581-46bf-b1b169cd558a")).Should().Be(CrashHomeStates.PRISTINE);
        // the home of the one fish that is left: the fish wins over the timer
        SpawnTimeOf(Find("6006e879-e82b-30ca-750f-337f38d8b181")).Should().Be(CrashHomeStates.FISH_ALIVE);
        // a home nobody met is a home nobody met
        SpawnTimeOf(Find("a7d14ee1-80b2-0c92-47e1-52bd2ca920d7")).Should().Be(CrashHomeStates.PRISTINE);
        // the Peeper that got the id of a home carries nothing of it any more
        Find("20dbf5fb-a262-7cdf-116d-f429bc1f0455").Metadata.Should().BeNull();
        // the fish keeps no state, and stands in the cell of its home again
        fish.Metadata.Should().BeNull();
        fish.AbsoluteEntityCell.Should().Be(homeOfTheFish.AbsoluteEntityCell);
        fish.Transform.LocalPosition.X.Should().BeApproximately(-150.27078f, 0.0001f);

        result.FishlessHomes.Should().Be(1);
        result.HomesWithFishAndTimer.Should().Be(1);
        result.OtherEntities.Should().Be(1);
        result.FishTransforms.Should().Be(1);
        CrashHomeStates.Normalize(data.Entities).Changed.Should().Be(0, "the second start of the server has nothing left to do");
    }

    [TestMethod]
    public void TheMetadataOfAHomeIsWrittenInTheFormatOfCoop3()
    {
        // no change of the format of the save: the repaired home is written exactly as the coop.3 server writes a home that nobody met
        (ServerJsonSerializer serializer, EntityData data) = LoadCoop3CrashHomes();
        CrashHomeStates.Normalize(data.Entities);

        using MemoryStream stream = new();
        serializer.Serialize(stream, data);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        json.Should().Contain("\"Id\":\"05fb77b6-bd94-5581-46bf-b1b169cd558a\",\"Level\":1,\"TechType\":\"CrashHome\",\"ClassId\":\"eb38de5d-c3df-4446-a37a-d770fb0f92bb\",\"Metadata\":{\"$type\":\"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CrashHomeMetadata, Nitrox.Model.Subnautica\",\"SpawnTime\":0.0}");
        json.Should().Contain("\"Id\":\"20dbf5fb-a262-7cdf-116d-f429bc1f0455\",\"Level\":0,\"TechType\":\"Peeper\",\"ClassId\":\"3fcd548b-781f-46ba-b076-7412608deeef\",\"Metadata\":null");
    }
}
