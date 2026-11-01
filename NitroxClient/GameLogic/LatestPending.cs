namespace NitroxClient.GameLogic;

/// <summary>
/// Keeps the newest of the updates that arrived too early to be applied (before the world they change exists), so that it can be applied once, after the state it must not
/// be overwritten by. An older update that was never applied is of no use: the newer one replaces it.
/// </summary>
public sealed class LatestPending<T> where T : class
{
    private T? value;

    public bool HasValue => value != null;

    public void Set(T newValue)
    {
        value = newValue;
    }

    /// <returns>True and the update if there was one; it is not kept after that</returns>
    public bool TryTake(out T? taken)
    {
        taken = value;
        value = null;
        return taken != null;
    }

    public void Clear()
    {
        value = null;
    }
}
