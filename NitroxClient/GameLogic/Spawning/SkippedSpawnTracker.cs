using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.Spawning;

/// <summary>
/// Counts world entities whose spawn was skipped because the cell they belong to was not awake (<see cref="WorldEntitySpawner"/>), so that such a skip is no longer silent.
/// </summary>
/// <remarks>
/// Nothing is held back for a later flush on purpose. The cell is reported to the server only when <c>EntityCell.AwakeAsync</c> starts, and with Nitrox's patch that coroutine runs
/// to <c>IsAwake</c> in the same step (all its yields sit behind <c>SerialData.Length</c> checks), so an answer to a cell load never finds the cell in <c>InAwakeAsync</c>.
/// What does find a cell not awake is an answer that took longer than the player staying near the cell: the cell went to sleep (and may be queued to wake again) and the
/// server answers the next load of it with the whole cell again, which supersedes anything that was held.
/// The counts by cell state settle whether the first case ever happens: <c>InAwakeAsync</c> (and <c>InAwakeAsyncToSleep</c>, the same with a sleep queued) should stay at zero.
/// </remarks>
internal sealed class SkippedSpawnTracker
{
    public const string STATE_NO_CELL = "NoCell";

    private readonly Dictionary<AbsoluteEntityCell, int> skippedSinceClosed = [];
    private readonly Dictionary<string, int> totalByCellState = [];

    /// <summary>
    /// Every skip since the start of the session
    /// </summary>
    public int Total { get; private set; }

    /// <param name="cell">The cell the entity belongs to</param>
    /// <param name="cellState">What the cell was doing (the name of its <c>EntityCell.State</c>, or <see cref="STATE_NO_CELL"/> when the client has no such cell)</param>
    /// <returns>How many entities of <paramref name="cell"/> were skipped since it was last <see cref="Close"/>d, this one included</returns>
    public int Record(AbsoluteEntityCell cell, string cellState)
    {
        Total++;
        totalByCellState.TryGetValue(cellState, out int stateCount);
        totalByCellState[cellState] = stateCount + 1;

        skippedSinceClosed.TryGetValue(cell, out int cellCount);
        skippedSinceClosed[cell] = cellCount + 1;
        return cellCount + 1;
    }

    /// <summary>
    /// Ends the counting window of a cell, normally when the answer that was being spawned for it is done.
    /// </summary>
    /// <returns>How many entities of <paramref name="cell"/> were skipped in that window</returns>
    public int Close(AbsoluteEntityCell cell)
    {
        if (!skippedSinceClosed.TryGetValue(cell, out int skipped))
        {
            return 0;
        }

        skippedSinceClosed.Remove(cell);
        return skipped;
    }

    public int TotalInState(string cellState)
    {
        totalByCellState.TryGetValue(cellState, out int count);
        return count;
    }

    public string Describe()
    {
        if (Total == 0)
        {
            return "no spawn skipped";
        }

        return $"{Total} spawns skipped, by state of the cell: {string.Join(", ", totalByCellState.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}"))}";
    }
}
