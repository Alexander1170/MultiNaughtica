namespace NitroxClient.GameLogic.Simulation
{
    /// <summary>The context of the lock request for a scout drone that its operator is about to fly (the bay that asked).</summary>
    public class DroneLock : LockRequestContext
    {
        public DroneLock(NitroxClient.MonoBehaviours.Cyclops.DroneBay bay)
        {
            Bay = bay;
        }

        public NitroxClient.MonoBehaviours.Cyclops.DroneBay Bay { get; }
    }
}
