using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     The registry remembers which ids left it, when and which code took them out (<see cref="EntityRegistry.TryGetRemoval" />, <see cref="EntityRegistry.DescribeUnknownId" />), only so that a packet
///     about an id the server does not know can say whether the id was known once. On 2026-10-23 the update of a sulfur plant that the server did not know was logged, one packet in five hours, and
///     nothing in the log said whether the server had ever held it: the memory is bounded by the number of removals, not by a few minutes.
/// </summary>
[TestClass]
public class EntityRegistryRemovalAgeTest
{
    private const long HOUR_MS = 3600000;

    private static EntityRegistry NewRegistry() => new(NullLogger<EntityRegistry>.Instance);

    private static void RemoveCreatures(EntityRegistry registry, int first, int last)
    {
        for (int i = first; i <= last; i++)
        {
            registry.AddEntity(Creature(i));
            registry.RemoveEntity(Id(i));
        }
    }

    [TestMethod]
    public void AnIdThatLeftTheRegistryIsRemembered()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(1));

        registry.RemoveEntity(Id(1));

        registry.TryGetRemovalAge(Id(1), out long ageMs).Should().BeTrue();
        ageMs.Should().BeInRange(0L, 10000L);
    }

    [TestMethod]
    public void AnIdThatWasNeverInTheRegistryIsNotRemembered()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(1));

        registry.RemoveEntity(Id(2));

        registry.TryGetRemovalAge(Id(2), out long _).Should().BeFalse();
        registry.TryGetRemovalAge(Id(1), out long _).Should().BeFalse("it is still registered");
    }

    [TestMethod]
    public void TheChildrenThatLeaveWithTheirParentAreRememberedToo()
    {
        EntityRegistry registry = NewRegistry();
        PlaceholderGroupWorldEntity group = Group(1);
        PrefabPlaceholderEntity kid = Kid(2, group);
        registry.AddEntitiesIgnoringDuplicate([group]);

        registry.RemoveEntity(Id(1));

        registry.TryGetRemovalAge(Id(1), out long _).Should().BeTrue();
        registry.TryGetRemoval(kid.Id, out long _, out string remover).Should().BeTrue();
        remover.Should().Contain(Id(1).ToString(), "the note of a child names the parent that took it along");
    }

    [TestMethod]
    public void AnOldRemovalIsStillOnRecordHoursLater()
    {
        // the event of 2026-10-23 was one packet in five hours: a memory of two minutes could not have answered
        EntityRegistry registry = NewRegistry();
        long now = 1000;
        registry.TickSource = () => now;
        registry.AddEntity(Creature(1));
        registry.RemoveEntity(Id(1));

        now += (5 * HOUR_MS) + 1;

        registry.TryGetRemovalAge(Id(1), out long ageMs).Should().BeTrue();
        ageMs.Should().Be((5 * HOUR_MS) + 1);
    }

    [TestMethod]
    public void TheRemoverIsTheMethodThatAskedForTheRemoval()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(1));

        registry.RemoveEntity(Id(1));

        registry.TryGetRemoval(Id(1), out long _, out string remover).Should().BeTrue();
        remover.Should().Be($"{nameof(EntityRegistryRemovalAgeTest)}.{nameof(TheRemoverIsTheMethodThatAskedForTheRemoval)}");
    }

    [TestMethod]
    public void ACrashHomeThatLeavesIsRecordedWithTheCodeThatTookItOut()
    {
        // a sulfur plant is never taken out of the world: when one leaves, the note and the log line name the chain of calls
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Group(1, "crash-home-class", tech: "CrashHome"));

        registry.RemoveEntity(Id(1));

        registry.TryGetRemoval(Id(1), out long _, out string remover).Should().BeTrue();
        remover.Should().StartWith($"{nameof(EntityRegistryRemovalAgeTest)}.{nameof(ACrashHomeThatLeavesIsRecordedWithTheCodeThatTookItOut)}");
        remover.Should().NotContain("RemoveEntity", "the frames of the registry itself say nothing");
    }

    [TestMethod]
    public void TheMemoryIsBoundedAndForgetsTheOldestRemovalsFirst()
    {
        EntityRegistry registry = NewRegistry();
        const int EXTRA = 1000;
        const int COUNT = EntityRegistry.MAX_REMEMBERED_REMOVALS + EXTRA;

        RemoveCreatures(registry, 1, COUNT);

        registry.TryGetRemovalAge(Id(COUNT), out long _).Should().BeTrue("the latest removal is always remembered");
        registry.TryGetRemovalAge(Id(EXTRA + 1), out long _).Should().BeTrue("the oldest one that still fits");
        registry.TryGetRemovalAge(Id(EXTRA), out long _).Should().BeFalse("forgotten: the newest EntityRegistry.MAX_REMEMBERED_REMOVALS removals are on record");
        registry.TryGetRemovalAge(Id(1), out long _).Should().BeFalse();
    }

    [TestMethod]
    public void AnIdThatWasRemovedAgainKeepsItsNewerNoteWhenTheOlderOneIsForgotten()
    {
        EntityRegistry registry = NewRegistry();
        long now = 0;
        registry.TickSource = () => now;
        registry.AddEntity(Creature(1));
        registry.RemoveEntity(Id(1));
        now = 500;
        registry.AddEntity(Creature(1));
        registry.RemoveEntity(Id(1));

        // exactly enough other removals that the first note of the id is the one that is forgotten
        RemoveCreatures(registry, 2, EntityRegistry.MAX_REMEMBERED_REMOVALS);

        registry.TryGetRemoval(Id(1), out long ageMs, out string _).Should().BeTrue("the second removal of the id is still within the memory");
        ageMs.Should().Be(0L);
        registry.DescribeUnknownId(Id(1)).Should().Contain("it left the registry");
    }

    [TestMethod]
    public void AnUnknownIdThatNeverLeftSaysSoWhileNothingWasForgotten()
    {
        EntityRegistry registry = NewRegistry();
        registry.AddEntity(Creature(1));
        registry.RemoveEntity(Id(1));

        string text = registry.DescribeUnknownId(Id(99));

        text.Should().Contain("no removal of it is on record since this server started");
        text.Should().Contain("all 1 removals");
        text.Should().NotContain("forgotten");
    }

    [TestMethod]
    public void AnUnknownIdSaysWhatWasForgottenOnceSomethingWas()
    {
        EntityRegistry registry = NewRegistry();
        RemoveCreatures(registry, 1, EntityRegistry.MAX_REMEMBERED_REMOVALS + 5);

        string text = registry.DescribeUnknownId(Id(1));

        text.Should().Contain("no removal of it is on record");
        text.Should().Contain($"the last {EntityRegistry.MAX_REMEMBERED_REMOVALS} removals");
        text.Should().Contain("5 older ones are forgotten");
        text.Should().NotContain("since this server started", "once something is forgotten, 'no record' no longer means 'never removed'");
    }

    [TestMethod]
    public void AnUnknownIdThatLeftHoursAgoNamesTheTimeAndTheRemover()
    {
        EntityRegistry registry = NewRegistry();
        long now = 0;
        registry.TickSource = () => now;
        registry.AddEntity(Creature(1));
        registry.RemoveEntity(Id(1));
        now = (3 * HOUR_MS) + (HOUR_MS / 2);

        string text = registry.DescribeUnknownId(Id(1));

        text.Should().Be($"it left the registry 3.5 h ago, taken out by {nameof(EntityRegistryRemovalAgeTest)}.{nameof(AnUnknownIdThatLeftHoursAgoNamesTheTimeAndTheRemover)}");
    }

    [TestMethod]
    public void TheTextOfARemovalWithoutARemoverHasNoDanglingComma()
    {
        EntityRegistry.DescribeRemoval(true, 1500, "", 1, 0, 0).Should().Be("it left the registry 1.5 s ago");
        EntityRegistry.DescribeRemoval(true, 1500, "Code.Method", 1, 0, 0).Should().Be("it left the registry 1.5 s ago, taken out by Code.Method");
    }

    [TestMethod]
    public void AgesAreWrittenInTheUnitThatReadsBest()
    {
        EntityRegistry.FormatAge(0).Should().Be("0 ms");
        EntityRegistry.FormatAge(999).Should().Be("999 ms");
        EntityRegistry.FormatAge(1500).Should().Be("1.5 s");
        EntityRegistry.FormatAge(59999).Should().Be("60.0 s");
        EntityRegistry.FormatAge(90000).Should().Be("1.5 min");
        EntityRegistry.FormatAge(5 * HOUR_MS).Should().Be("5.0 h");
    }

    [TestMethod]
    public void AgesDoNotDependOnTheCultureOfTheServer()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            EntityRegistry.FormatAge(1500).Should().Be("1.5 s");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    public void TheCallerIsWrittenAsFileAndMethod()
    {
        EntityRegistry.DescribeCaller("Process", @"C:\build\Nitrox.Server.Subnautica\Models\Packets\Processors\EntityDestroyedPacketProcessor.cs").Should().Be("EntityDestroyedPacketProcessor.Process");
        EntityRegistry.DescribeCaller("Process", "/src/Models/EntityDestroyedPacketProcessor.cs").Should().Be("EntityDestroyedPacketProcessor.Process");
        EntityRegistry.DescribeCaller("Process", null).Should().Be("Process");
        EntityRegistry.DescribeCaller(null, null).Should().Be("unknown code");
    }

    private static async Task<string> CallersOfAnAsyncMethod()
    {
        await Task.CompletedTask;
        return EntityRegistry.DescribeCallers(new StackTrace(false), 4);
    }

    [TestMethod]
    public async Task TheCallersNameAnAsyncMethodByItsRealName()
    {
        // the packet processors are async methods: their frames are the MoveNext of a compiler state machine called <Process>d__3
        string callers = await CallersOfAnAsyncMethod();

        callers.Should().StartWith($"{nameof(EntityRegistryRemovalAgeTest)}.{nameof(CallersOfAnAsyncMethod)}");
        callers.Should().NotContain("MoveNext");
        // the frames are joined with " <- ": none of the names themselves may contain the angle brackets of a compiler-made name like <Process>d__3
        callers.Split(new[] { " <- " }, System.StringSplitOptions.None).Should().NotContain(name => name.Contains("<") || name.Contains(">"));
    }

    [TestMethod]
    public void TheCallersLeaveOutTheFramesOfTheRuntimeAndOfTheRegistry()
    {
        string callers = EntityRegistry.DescribeCallers(new StackTrace(false), 20);

        callers.Should().StartWith($"{nameof(EntityRegistryRemovalAgeTest)}.{nameof(TheCallersLeaveOutTheFramesOfTheRuntimeAndOfTheRegistry)}");
        callers.Should().NotContain("RuntimeMethodInfo", "the reflection call that started the test is a frame of the runtime");
    }
}
