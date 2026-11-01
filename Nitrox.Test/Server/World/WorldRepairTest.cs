using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;
using Nitrox.Server.Subnautica.Models.GameLogic.Players;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

/// <summary>
///     The load-time repair of a world that coop.3 saved after a restart: safety first (a complete copy before the first change, none for a healthy world, a dry run that changes nothing, once per
///     world) and then the repair itself, see <see cref="WorldRepair" />.
/// </summary>
[TestClass]
public class WorldRepairTest
{
    private static readonly DateTime start = new(2026, 10, 26, 9, 15, 0);
    private const string TEST_SEED = "MRZIIKGLHO";

    private sealed class TempWorld : IDisposable
    {
        public TempWorld()
        {
            Root = Path.Combine(Path.GetTempPath(), $"nitrox-test-{Guid.NewGuid():N}");
            SaveDir = Path.Combine(Root, "saves", "Test");
            ReportDir = Path.Combine(Root, "logs");
            Directory.CreateDirectory(SaveDir);
            Directory.CreateDirectory(Path.Combine(SaveDir, "backups"));
            Directory.CreateDirectory(Path.Combine(SaveDir, "other"));
            File.WriteAllText(Path.Combine(SaveDir, "Version.json"), "version");
            File.WriteAllText(Path.Combine(SaveDir, "EntityData.json"), "entities");
            File.WriteAllText(Path.Combine(SaveDir, "other", "note.txt"), "note");
            File.WriteAllText(Path.Combine(SaveDir, "backups", "Backup - 2026-10-24 22.13.02.zip"), "rolling");
        }

        public string Root { get; }
        public string SaveDir { get; }
        public string ReportDir { get; }

        // the copy sits INSIDE the world's backups folder: the launcher lists every folder of the saves folder as a world and would have shown a copy next to it
        public string[] Copies() => Directory.Exists(Path.Combine(SaveDir, "backups")) ? Directory.GetDirectories(Path.Combine(SaveDir, "backups"), "before-heal-*") : [];

        public string[] CopiesNextToTheWorld() => Directory.GetDirectories(Path.GetDirectoryName(SaveDir)!, "Test.before-heal-*");

        public string[] Reports() => Directory.Exists(ReportDir) ? Directory.GetFiles(ReportDir) : [];

        public void Dispose() => Directory.Delete(Root, true);
    }

    /// <summary>
    ///     A world with a twin pair of creatures, a wreck group that exists twice (one copy empty), a batch of 16 generated entities and a record of generated batches that lacks it.
    /// </summary>
    private static PersistedWorldData NewWorld(int repairVersion = 0)
    {
        List<Entity> entities = [];
        for (int i = 0; i < ParsedBatchRepair.MIN_SERVER_ENTITIES; i++)
        {
            entities.Add(Creature(i + 1, "Peeper", 10f + i, -5f, 20f));
        }
        entities.Add(Creature(100, "Spadefish", x: 500f));
        entities.Add(Creature(101, "Spadefish", x: 500f));
        PlaceholderGroupWorldEntity complete = Group(200, "wreck-class", 900f, 0f, 900f);
        Kid(201, complete);
        Kid(202, complete);
        entities.AddRange(Flat(complete, Group(150, "wreck-class", 900f, 0f, 900f)));

        return new PersistedWorldData
        {
            WorldData = new WorldData { ParsedBatchCells = [new NitroxInt3(1, 1, 1)], RepairVersion = repairVersion },
            PlayerData = new PlayerData(),
            GlobalRootData = new GlobalRootData(),
            EntityData = EntityData.From(entities)
        };
    }

    private static int Run(PersistedWorldData data, TempWorld world, bool dryRun = false, DateTime? now = null, string? seed = TEST_SEED)
    {
        return WorldRepair.Run(data, world.SaveDir, dryRun, now ?? start, NullLogger.Instance, seed, world.ReportDir);
    }

    [TestMethod]
    public void TheRepairRemovesTheCopiesAndCompletesTheRecordAfterACompleteCopyOfTheWorld()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        int entitiesBefore = data.EntityData.Entities.Count;

        int version = Run(data, world);

        version.Should().Be(WorldRepair.CURRENT_VERSION);
        data.EntityData.Entities.Select(entity => entity.Id).Should().NotContain([Id(101), Id(150)]);
        data.EntityData.Entities.Should().HaveCount(entitiesBefore - 2);
        data.WorldData.ParsedBatchCells.Should().Contain([new NitroxInt3(1, 1, 1), new NitroxInt3(12, 18, 12)], "the batch the entities prove, behind the one of the save");

        string[] copies = world.Copies();
        copies.Should().ContainSingle().Which.Should().EndWith(Path.Combine("backups", "before-heal-20261026-091500"));
        world.CopiesNextToTheWorld().Should().BeEmpty("a folder next to the world would be listed by the launcher as a second world");
        File.ReadAllText(Path.Combine(copies[0], "EntityData.json")).Should().Be("entities");
        File.ReadAllText(Path.Combine(copies[0], "other", "note.txt")).Should().Be("note");
        Directory.Exists(Path.Combine(copies[0], "backups")).Should().BeFalse("the rolling backups are not part of the copy");

        string report = File.ReadAllText(world.Reports().Should().ContainSingle().Subject);
        report.Should().Contain("REMOVE").And.Contain(Id(101).ToString()).And.Contain(Id(150).ToString()).And.NotContain("DRY RUN");
    }

    [TestMethod]
    public void TheDryRunReportsAndChangesNothingAndMakesNoCopy()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        int entitiesBefore = data.EntityData.Entities.Count;

        int version = Run(data, world, dryRun: true);

        version.Should().Be(0, "the world is not marked as repaired");
        data.EntityData.Entities.Should().HaveCount(entitiesBefore);
        data.WorldData.ParsedBatchCells.Should().Equal(new NitroxInt3(1, 1, 1));
        world.Copies().Should().BeEmpty();
        Directory.GetFiles(world.SaveDir, "*", SearchOption.AllDirectories).Should().HaveCount(4, "no file was added to the world folder");

        string reportPath = world.Reports().Should().ContainSingle().Subject;
        Path.GetFileName(reportPath).Should().EndWith("-dryrun.txt");
        string report = File.ReadAllText(reportPath);
        report.Should().Contain("DRY RUN").And.Contain("REMOVE").And.Contain("KEEP").And.Contain(Id(101).ToString());
    }

    [TestMethod]
    public void TheDryRunWorksOnAWorldThatIsMarkedAsRepairedToo()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld(WorldRepair.CURRENT_VERSION);

        Run(data, world, dryRun: true);

        world.Reports().Should().ContainSingle();
    }

    [TestMethod]
    public void AWorldThatIsMarkedAsRepairedIsNotLookedAtAgain()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld(WorldRepair.CURRENT_VERSION);
        int entitiesBefore = data.EntityData.Entities.Count;

        int version = Run(data, world);

        version.Should().Be(WorldRepair.CURRENT_VERSION);
        data.EntityData.Entities.Should().HaveCount(entitiesBefore);
        world.Copies().Should().BeEmpty();
        world.Reports().Should().BeEmpty();
    }

    [TestMethod]
    public void ARepairedWorldNeedsNothingTheSecondTimeAndMakesNoSecondCopy()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        data.WorldData.RepairVersion = Run(data, world);
        int entitiesAfterTheFirstRepair = data.EntityData.Entities.Count;

        // the marker is lost (a save that was not written yet): the repair finds nothing more to do
        data.WorldData.RepairVersion = 0;
        int version = Run(data, world, now: start.AddMinutes(5));

        version.Should().Be(WorldRepair.CURRENT_VERSION);
        data.EntityData.Entities.Should().HaveCount(entitiesAfterTheFirstRepair);
        world.Copies().Should().ContainSingle("a world with nothing to repair is not copied");
    }

    [TestMethod]
    public void AHealthyWorldIsMarkedWithoutACopy()
    {
        using TempWorld world = new();
        List<Entity> entities = [];
        for (int i = 1; i <= 40; i++)
        {
            entities.Add(Creature(i, "Peeper", i * 40f));
        }
        PersistedWorldData data = new()
        {
            WorldData = new WorldData { ParsedBatchCells = [new NitroxInt3(1, 1, 1)] },
            PlayerData = new PlayerData(),
            GlobalRootData = new GlobalRootData(),
            EntityData = EntityData.From(entities)
        };

        int version = Run(data, world);

        version.Should().Be(WorldRepair.CURRENT_VERSION);
        data.EntityData.Entities.Should().HaveCount(40);
        data.WorldData.ParsedBatchCells.Should().Equal(new NitroxInt3(1, 1, 1));
        world.Copies().Should().BeEmpty();
        world.Reports().Should().BeEmpty();
    }

    [TestMethod]
    public void WhenTheCopyCannotBeMadeNothingIsRepaired()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        int entitiesBefore = data.EntityData.Entities.Count;
        // the folder of the copy exists already: a copy is never merged into another folder
        Directory.CreateDirectory(WorldRepair.BeforeRepairFolder(world.SaveDir, start));

        int version = Run(data, world);

        version.Should().Be(0, "the world stays unmarked, the next start tries again");
        data.EntityData.Entities.Should().HaveCount(entitiesBefore);
        data.WorldData.ParsedBatchCells.Should().Equal(new NitroxInt3(1, 1, 1));
    }

    [TestMethod]
    public void NothingThatAPlayerOwnsIsTouched()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        // a player carries something that hangs below the empty wreck group: the group is not removed
        data.PlayerData.Players.Add(new PersistedPlayerData { Name = "Alice", NitroxId = Id(900), SubRootId = Id(901), EquippedItems = new Dictionary<string, NitroxId> { ["Head"] = Id(150) } });

        Run(data, world);

        data.EntityData.Entities.Select(entity => entity.Id).Should().Contain([Id(150), Id(200)], "both copies of the wreck stay");
        data.EntityData.Entities.Select(entity => entity.Id).Should().NotContain(Id(101), "the creature twin is not referenced");
    }

    [TestMethod]
    public void ABatchProvenByAnIdOfItsOwnGeneratorIsAddedToTheRecordEvenWhenItIsSmall()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        // one creature in a batch far away, with the 9th id of the generator of that batch
        NitroxInt3 smallBatch = new(3, 4, 5);
        NitroxId generated = ParsedBatchRepair.BatchIdStream(TEST_SEED, smallBatch).Skip(8).First();
        data.EntityData.Entities.Add(CreatureWithId(generated, "Peeper", -1488f, -2320f, -1168f));   // the middle of batch (3, 4, 5)

        Run(data, world);

        data.WorldData.ParsedBatchCells.Should().Contain(smallBatch);
    }

    [TestMethod]
    public void WithoutTheSeedOnlyTheOtherEvidenceCounts()
    {
        using TempWorld world = new();
        PersistedWorldData data = NewWorld();
        NitroxInt3 smallBatch = new(3, 4, 5);
        data.EntityData.Entities.Add(CreatureWithId(ParsedBatchRepair.BatchIdStream(TEST_SEED, smallBatch).First(), "Peeper", -1488f, -2320f, -1168f));

        Run(data, world, seed: null);

        data.WorldData.ParsedBatchCells.Should().NotContain(smallBatch);
    }

    private static GlobalRootEntity Container(int id, NitroxId? parent = null)
    {
        return new GlobalRootEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "locker-class", false, Id(id), new NitroxTechType("Locker"), null, parent, []);
    }

    /// <summary>
    ///     A base whose first locker holds a Quartz that has the id of the Salt in the second locker; the entity list holds the Salt.
    /// </summary>
    private static (PersistedWorldData Data, InventoryItemEntity Stale) NewWorldWithClashingItems()
    {
        GlobalRootEntity root = Container(1);
        GlobalRootEntity first = Container(2, root.Id);
        GlobalRootEntity second = Container(3, root.Id);
        root.ChildEntities.Add(first);
        root.ChildEntities.Add(second);
        InventoryItemEntity stale = new(Id(50), "class-Quartz", new NitroxTechType("Quartz"), null, first.Id, []);
        first.ChildEntities.Add(stale);
        second.ChildEntities.Add(new InventoryItemEntity(Id(50), "class-Salt", new NitroxTechType("Salt"), null, second.Id, []));

        PersistedWorldData data = NewWorld(WorldRepair.CURRENT_VERSION - 1);
        data.GlobalRootData = GlobalRootData.From([root]);
        data.EntityData.Entities.Add(new InventoryItemEntity(Id(50), "class-Salt", new NitroxTechType("Salt"), null, second.Id, []));
        return (data, stale);
    }

    [TestMethod]
    public void AnItemThatSharesItsIdWithAnotherEntityGetsANewIdAndNothingIsDeleted()
    {
        using TempWorld world = new();
        (PersistedWorldData data, InventoryItemEntity stale) = NewWorldWithClashingItems();

        int version = Run(data, world);

        version.Should().Be(WorldRepair.CURRENT_VERSION);
        stale.Id.Should().NotBe(Id(50));
        data.GlobalRootData.Entities.Should().ContainSingle();
        data.GlobalRootData.Entities[0].ChildEntities.SelectMany(child => child.ChildEntities).Should().HaveCount(2, "both items are still in their lockers");
        string report = File.ReadAllText(world.Reports().Should().ContainSingle().Subject);
        report.Should().Contain("5. Id fixes: 1").And.Contain(Id(50).ToString()).And.Contain(stale.Id.ToString());
        world.Copies().Should().ContainSingle("the id fix is a change: the copy of the world is made first");
    }

    [TestMethod]
    public void TheDryRunSaysWhichItemWouldGetANewIdAndChangesNone()
    {
        using TempWorld world = new();
        (PersistedWorldData data, InventoryItemEntity stale) = NewWorldWithClashingItems();

        Run(data, world, dryRun: true);

        stale.Id.Should().Be(Id(50));
        File.ReadAllText(world.Reports().Should().ContainSingle().Subject).Should().Contain("5. Id fixes: 1").And.Contain("a new id when it is applied");
    }

    [TestMethod]
    public void TheProtectedIdsAreTheGlobalRootTreeAndEverythingAPlayerPointsAt()
    {
        GlobalRootEntity vehicle = new(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "vehicle-class", false, Id(1), new NitroxTechType("Seamoth"), null, null, []);
        InventoryItemEntity battery = new(Id(2), "battery-class", new NitroxTechType("Battery"), null, vehicle.Id, []);
        vehicle.ChildEntities.Add(battery);
        PersistedWorldData data = new()
        {
            WorldData = new WorldData(),
            PlayerData = new PlayerData
            {
                Players =
                [
                    new PersistedPlayerData
                    {
                        NitroxId = Id(10),
                        SubRootId = Id(11),
                        EquippedItems = new Dictionary<string, NitroxId> { ["Head"] = Id(12) },
                        QuickSlotsBindingIds = [Optional.Of(Id(13)), default]
                    }
                ]
            },
            GlobalRootData = GlobalRootData.From([vehicle]),
            EntityData = EntityData.From([])
        };

        HashSet<NitroxId> ids = WorldRepair.CollectProtectedIds(data);

        ids.Should().BeEquivalentTo([Id(1), Id(2), Id(10), Id(11), Id(12), Id(13)]);
    }

    [TestMethod]
    public void TheCopyIsMadeInsideTheBackupsFolderOfTheWorldWithTheTimeInItsName()
    {
        WorldRepair.BeforeRepairFolder(Path.Combine("saves", "Test"), new DateTime(2026, 10, 26, 9, 15, 7)).Should().Be(Path.Combine("saves", "Test", "backups", "before-heal-20261026-091507"));
        WorldRepair.BeforeRepairFolder($"{Path.Combine("saves", "Test")}{Path.DirectorySeparatorChar}", new DateTime(2026, 10, 26, 9, 15, 7)).Should().Be(Path.Combine("saves", "Test", "backups", "before-heal-20261026-091507"));
    }

    [TestMethod]
    public void TheCopyRefusesAFolderThatExistsAndNeverMergesIntoIt()
    {
        using TempWorld world = new();
        string target = Path.Combine(world.Root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "mine");

        Action copy = () => WorldRepair.CopyWorldFolder(world.SaveDir, target);

        copy.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(target, "keep.txt")).Should().Be("mine");
        File.Exists(Path.Combine(target, "Version.json")).Should().BeFalse();
    }

    [TestMethod]
    public void TheWorldDataKeepsTheFormatOfCoop3AndOnlyGrowsByTheRepairVersion()
    {
        // WorldData.json of coop.3 has no RepairVersion: it loads as 0. A file of this build adds the key; an older server ignores keys it does not know
        const string COOP3_WORLD_DATA = """{"ParsedBatchCells":[{"X":9,"Y":16,"Z":4},{"X":9,"Y":16,"Z":5}],"GameData":null}""";
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);

        WorldData loaded = serializer.Deserialize<WorldData>(new MemoryStream(Encoding.UTF8.GetBytes(COOP3_WORLD_DATA)));

        loaded.RepairVersion.Should().Be(0);
        loaded.ParsedBatchCells.Should().Equal(new NitroxInt3(9, 16, 4), new NitroxInt3(9, 16, 5));

        loaded.RepairVersion = WorldRepair.CURRENT_VERSION;
        using MemoryStream stream = new();
        serializer.Serialize(stream, loaded);
        string written = Encoding.UTF8.GetString(stream.ToArray());
        written.Should().Contain("\"ParsedBatchCells\"").And.Contain("\"GameData\"").And.Contain("\"RepairVersion\":1");
    }
}
