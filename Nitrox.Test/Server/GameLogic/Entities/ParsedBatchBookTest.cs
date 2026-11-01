using System.Collections.Concurrent;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

/// <summary>
///     The record of generated batches that the save keeps (<c>WorldData.ParsedBatchCells</c>). coop.3 wrote a record without every batch that nobody visited since the server started
///     (the server saves as soon as it hibernates, 4 seconds after a start with nobody connected), and the next visitor made the server generate those batches a second time.
/// </summary>
[TestClass]
public class ParsedBatchBookTest
{
    private static readonly NitroxInt3 batchA = new(12, 18, 12);
    private static readonly NitroxInt3 batchB = new(12, 18, 13);
    private static readonly NitroxInt3 batchC = new(7, 17, 8);

    private static ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> NewTasks() => new();

    [TestMethod]
    public void BatchesThatAreRestoredAreListedAgainWithoutAnyoneAskingForThem()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();

        ParsedBatchBook.Restore(tasks, [batchA, batchB]);

        ParsedBatchBook.ListParsed(tasks, []).Should().BeEquivalentTo([batchA, batchB]);
    }

    [TestMethod]
    public void AServerThatIsStartedAndSavedAtOnceKeepsEveryBatchOfTheSave()
    {
        // the case of the world "Test": start, nobody joins, the hibernate save runs
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> firstRun = NewTasks();
        ParsedBatchBook.Restore(firstRun, [batchA, batchB, batchC]);
        List<NitroxInt3> firstSave = ParsedBatchBook.ListParsed(firstRun, []);

        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> secondRun = NewTasks();
        ParsedBatchBook.Restore(secondRun, firstSave);

        firstSave.Should().BeEquivalentTo([batchA, batchB, batchC]);
        ParsedBatchBook.ListParsed(secondRun, []).Should().BeEquivalentTo([batchA, batchB, batchC]);
    }

    [TestMethod]
    public void ARestoredBatchIsNotGeneratedAgain()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        ParsedBatchBook.Restore(tasks, [batchA]);

        Task<List<Entity>> task = tasks[batchA].Value;

        task.IsCompletedSuccessfully.Should().BeTrue();
        task.Result.Should().BeEmpty("its entities are in the registry, loaded with the save");
    }

    [TestMethod]
    public void ALazyThatNobodyHasReadIsNotListed()
    {
        // what the setter used to put in: a factory nobody ran. A batch whose generation has not started is not generated, so it must not be listed
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        tasks[batchA] = new Lazy<Task<List<Entity>>>(() => Task.FromResult(new List<Entity>()));

        ParsedBatchBook.ListParsed(tasks, []).Should().BeEmpty();
    }

    [TestMethod]
    public void ABatchThatIsStillBeingGeneratedOrFailedIsNotListed()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        TaskCompletionSource<List<Entity>> running = new();
        TaskCompletionSource<List<Entity>> failed = new();
        failed.SetException(new InvalidOperationException("generation failed"));
        tasks[batchA] = new Lazy<Task<List<Entity>>>(() => running.Task);
        tasks[batchB] = new Lazy<Task<List<Entity>>>(() => failed.Task);
        tasks[batchC] = new Lazy<Task<List<Entity>>>(() => Task.FromResult(new List<Entity>()));
        foreach (Lazy<Task<List<Entity>>> lazy in tasks.Values)
        {
            _ = lazy.Value;
        }

        ParsedBatchBook.ListParsed(tasks, []).Should().BeEquivalentTo([batchC]);
    }

    [TestMethod]
    public void AnEmptyBatchIsNotListed()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        ParsedBatchBook.Restore(tasks, [batchA, batchB]);

        ParsedBatchBook.ListParsed(tasks, [batchB]).Should().BeEquivalentTo([batchA]);
    }

    [TestMethod]
    public void RestoringReplacesWhatWasThere()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        ParsedBatchBook.Restore(tasks, [batchA]);

        ParsedBatchBook.Restore(tasks, [batchB]);

        ParsedBatchBook.ListParsed(tasks, []).Should().BeEquivalentTo([batchB]);
    }

    [TestMethod]
    public void MarkingBatchesParsedOnlyAddsTheMissingOnes()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        TaskCompletionSource<List<Entity>> running = new();
        Lazy<Task<List<Entity>>> generating = new(() => running.Task);
        tasks[batchA] = generating;

        int added = ParsedBatchBook.MarkParsed(tasks, [batchA, batchB, batchB]);

        added.Should().Be(1);
        tasks[batchA].Should().BeSameAs(generating, "a batch that is being generated is not replaced");
        ParsedBatchBook.MarkParsed(tasks, null).Should().Be(0);
    }

    [TestMethod]
    public void TheListIsAPlainListOfBatchIds()
    {
        // the save keeps the batches as a plain list of NitroxInt3 (WorldData.ParsedBatchCells): the same list as before, an older reader reads it as before
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = NewTasks();
        ParsedBatchBook.Restore(tasks, [batchA, batchC]);

        List<NitroxInt3> listed = ParsedBatchBook.ListParsed(tasks, []);

        listed.OrderBy(batch => batch.X).Should().Equal(batchC, batchA);
    }
}
