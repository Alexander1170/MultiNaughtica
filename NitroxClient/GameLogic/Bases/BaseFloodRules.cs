namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     When the water of a base that another client sent is applied to the local flood simulation, and when the local simulation is worth sending. Plain data, no game types.
/// </summary>
/// <remarks>
///     Every client simulates the water of the bases it has loaded (leaks, flow, draining); the player who simulates a base (the holder of its lock) sends the result, the others
///     take it over. The water only ever moved forward in the first version of this sync (an incoming grid was only applied when it was more flooded than the local one); that
///     kept a client from being set back by a delayed packet but also kept the water from draining after a repair, so the holder's grid is simply applied by everybody else now.
///     The holder keeps its own simulation: what the others send reaches it only while it has no water at all (a base that was just spawned from the stored state).
/// </remarks>
public static class BaseFloodRules
{
    /// <summary>
    ///     How often the holder sends the grid of a base.
    /// </summary>
    public const float SEND_INTERVAL_SECONDS = 2f;

    /// <param name="expectedSize">The number of cells of the local simulation. A grid of another size belongs to another geometry of the base (it changed or is still being built) and cannot be applied: restoring it would break the update loop of the simulation.</param>
    /// <param name="incoming">The grid that was sent.</param>
    /// <param name="localSum">The total water of the local simulation.</param>
    /// <param name="localIsHolder">This client simulates the base.</param>
    public static bool ShouldApply(int expectedSize, float[] incoming, float localSum, bool localIsHolder)
    {
        if (incoming == null || incoming.Length != expectedSize)
        {
            return false;
        }

        return !localIsHolder || localSum <= 0f;
    }

    /// <summary>
    ///     How long a base that holds water and does not change is silent before the holder sends it again. A player who joins while the geometry of the base is not ready
    ///     ignores the water he is given with the base (the grid has another size, see <see cref="ShouldApply" />) and would otherwise wait for a change.
    /// </summary>
    public const float REPEAT_INTERVAL_SECONDS = 30f;

    /// <summary>
    ///     A base that never had water does not send anything; once it had some, every change is sent, and water that stays is repeated now and then.
    /// </summary>
    /// <param name="gridLength">The number of cells of the grid now. A base that grew or shrank has a grid of another size with possibly the same total: what the server holds no longer fits and is sent again at once.</param>
    /// <param name="lastSentLength">The number of cells of the grid that was sent last.</param>
    public static bool ShouldSend(float currentSum, float? lastSentSum, float secondsSinceLastSend = 0f, int gridLength = 0, int? lastSentLength = null)
    {
        if (lastSentSum == null)
        {
            return currentSum > 0f;
        }

        if (lastSentLength != null && gridLength != lastSentLength.Value)
        {
            return true;
        }

        return currentSum != lastSentSum.Value || (currentSum > 0f && secondsSinceLastSend >= REPEAT_INTERVAL_SECONDS);
    }

    public static float Sum(float[] values)
    {
        float total = 0f;
        if (values != null)
        {
            foreach (float value in values)
            {
                total += value;
            }
        }

        return total;
    }
}
