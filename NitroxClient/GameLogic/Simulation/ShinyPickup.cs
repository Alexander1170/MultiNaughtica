namespace NitroxClient.GameLogic.Simulation;

/// <summary>
/// Remembers the creature that waits for the simulation lock of the piece of scrap (or other shiny item) it wants to carry off.
/// </summary>
public class ShinyPickup : LockRequestContext
{
    public CollectShiny Collector { get; }

    public ShinyPickup(CollectShiny collector)
    {
        Collector = collector;
    }
}
