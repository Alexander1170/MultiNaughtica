using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.Spawning;

[TestClass]
public class SkippedSpawnTrackerTest
{
    private static AbsoluteEntityCell Cell(int batchX, int cellX, int level = 0) => new(new NitroxInt3(batchX, 0, 0), new NitroxInt3(cellX, 0, 0), level);

    [TestMethod]
    public void NothingRecorded_NothingIsSkipped()
    {
        SkippedSpawnTracker tracker = new();

        tracker.Total.Should().Be(0);
        tracker.TotalInState("IsAsleep").Should().Be(0);
        tracker.Close(Cell(1, 1)).Should().Be(0);
        tracker.Describe().Should().Be("no spawn skipped");
    }

    [TestMethod]
    public void Record_CountsPerCellAndReturnsTheRunningCountOfTheCell()
    {
        SkippedSpawnTracker tracker = new();

        tracker.Record(Cell(1, 1), "IsAsleep").Should().Be(1);
        tracker.Record(Cell(1, 1), "IsAsleep").Should().Be(2);
        tracker.Record(Cell(1, 2), "IsAsleep").Should().Be(1);

        tracker.Total.Should().Be(3);
    }

    [TestMethod]
    public void Record_CellsOfTheSamePlaceButAnotherLevelAreSeparate()
    {
        SkippedSpawnTracker tracker = new();

        tracker.Record(Cell(1, 1, 0), "IsAsleep").Should().Be(1);
        tracker.Record(Cell(1, 1, 1), "IsAsleep").Should().Be(1);
        tracker.Record(Cell(1, 1, 0), "IsAsleep").Should().Be(2);
    }

    [TestMethod]
    public void Record_CountsByStateOfTheCell()
    {
        SkippedSpawnTracker tracker = new();

        tracker.Record(Cell(1, 1), "IsAsleep");
        tracker.Record(Cell(1, 2), "IsAsleep");
        tracker.Record(Cell(1, 3), SkippedSpawnTracker.STATE_NO_CELL);

        tracker.TotalInState("IsAsleep").Should().Be(2);
        tracker.TotalInState(SkippedSpawnTracker.STATE_NO_CELL).Should().Be(1);
        // A state nothing was recorded in is a zero, not an error (R4 checks that InAwakeAsync stays at zero)
        tracker.TotalInState("InAwakeAsync").Should().Be(0);
    }

    [TestMethod]
    public void Close_ReturnsTheSkipsOfThatCellOnlyAndStartsANewWindow()
    {
        SkippedSpawnTracker tracker = new();
        tracker.Record(Cell(1, 1), "IsAsleep");
        tracker.Record(Cell(1, 1), "InSleepAsync");
        tracker.Record(Cell(1, 2), "IsAsleep");

        tracker.Close(Cell(1, 1)).Should().Be(2);
        tracker.Close(Cell(1, 1)).Should().Be(0);
        // The other cell keeps counting, and the first skip of a new window is reported as the first again (it is what gets logged)
        tracker.Record(Cell(1, 2), "IsAsleep").Should().Be(2);
        tracker.Record(Cell(1, 1), "IsAsleep").Should().Be(1);
    }

    [TestMethod]
    public void Close_DoesNotTakeAwayFromTheTotals()
    {
        SkippedSpawnTracker tracker = new();
        tracker.Record(Cell(1, 1), "IsAsleep");

        tracker.Close(Cell(1, 1));

        tracker.Total.Should().Be(1);
        tracker.TotalInState("IsAsleep").Should().Be(1);
    }

    [TestMethod]
    public void Describe_ListsTheStatesInOrder()
    {
        SkippedSpawnTracker tracker = new();
        tracker.Record(Cell(1, 1), "IsAsleep");
        tracker.Record(Cell(1, 1), "InSleepAsync");
        tracker.Record(Cell(1, 2), "IsAsleep");

        tracker.Describe().Should().Be("3 spawns skipped, by state of the cell: InSleepAsync 1, IsAsleep 2");
    }
}
