using System.Collections.Generic;
using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic;

/// <summary>
/// A hit of a creature on a vehicle that the local player simulates can reach it twice. The player that simulates the creature relays every hit it makes on a vehicle someone
/// else simulates (<c>CreatureAttackedVehicle</c>), and the local copy of a creature whose attacks are synced bites the vehicle on its own as well (when it is loaded here and
/// close enough, which is not the case for a far away holder, for the Sea Dragon's shove or for a Crashfish). Which of the two arrives is not knowable, so each hit is accepted from
/// whichever comes first and the other one, if it comes within <see cref="MATCH_WINDOW" />, is the same hit: one-to-one, so that a lost copy never swallows the next relayed hit.
/// </summary>
public sealed class CreatureVehicleHitMatcher
{
    /// <summary>
    /// Seconds within which a local bite and a relayed hit of the same creature on the same vehicle are the same hit. Longer than the delay of a relayed hit and shorter than the
    /// interval of the attacks that matter (a Reaper bites every 1.5 s or more, the damage over time of its grip ticks every second).
    /// </summary>
    public const float MATCH_WINDOW = 0.6f;

    /// <summary>Hits remembered per creature and vehicle while no counterpart shows up (the oldest are forgotten first).</summary>
    private const int MAX_PENDING = 8;

    /// <summary>Above this many creature and vehicle pairs the expired ones are removed.</summary>
    private const int PRUNE_PAIR_COUNT = 32;

    public enum HitOrigin
    {
        /// <summary>The creature's copy on this client bit the vehicle.</summary>
        Local,
        /// <summary>The simulator of the creature told us about the hit.</summary>
        Relayed
    }

    private readonly Dictionary<(NitroxId VehicleId, NitroxId CreatureId), Queue<float>> pendingLocal = [];
    private readonly Dictionary<(NitroxId VehicleId, NitroxId CreatureId), Queue<float>> pendingRelayed = [];

    /// <summary>
    /// True while a relayed hit is applied to the vehicle, so that the damage patch does not take it for a bite of the local copy.
    /// </summary>
    public bool IsApplyingRelayedHit { get; set; }

    /// <summary>
    /// Creature and vehicle pairs that still wait for the other report of a hit (for the tests: the pairs of expired hits are removed once there are many).
    /// </summary>
    public int PendingPairCount => pendingLocal.Count + pendingRelayed.Count;

    /// <returns>True if the hit must change the vehicle's health, false if it is the second report of a hit that was applied already</returns>
    public bool ShouldApply(NitroxId vehicleId, NitroxId creatureId, HitOrigin origin, float now)
    {
        (NitroxId, NitroxId) key = (vehicleId, creatureId);
        Dictionary<(NitroxId VehicleId, NitroxId CreatureId), Queue<float>> counterparts = origin == HitOrigin.Local ? pendingRelayed : pendingLocal;
        Dictionary<(NitroxId VehicleId, NitroxId CreatureId), Queue<float>> own = origin == HitOrigin.Local ? pendingLocal : pendingRelayed;

        if (counterparts.TryGetValue(key, out Queue<float> waiting))
        {
            DiscardExpired(waiting, now);
            if (waiting.Count > 0)
            {
                waiting.Dequeue();
                if (waiting.Count == 0)
                {
                    counterparts.Remove(key);
                }
                return false;
            }
            counterparts.Remove(key);
        }

        if (!own.TryGetValue(key, out Queue<float> pending))
        {
            pending = new Queue<float>();
            own[key] = pending;
        }
        DiscardExpired(pending, now);
        pending.Enqueue(now);
        while (pending.Count > MAX_PENDING)
        {
            pending.Dequeue();
        }

        if (own.Count > PRUNE_PAIR_COUNT)
        {
            Prune(own, now);
        }
        return true;
    }

    private static void DiscardExpired(Queue<float> times, float now)
    {
        while (times.Count > 0 && now - times.Peek() > MATCH_WINDOW)
        {
            times.Dequeue();
        }
    }

    private static void Prune(Dictionary<(NitroxId VehicleId, NitroxId CreatureId), Queue<float>> pending, float now)
    {
        List<(NitroxId, NitroxId)> expired = [];
        foreach (KeyValuePair<(NitroxId VehicleId, NitroxId CreatureId), Queue<float>> entry in pending)
        {
            DiscardExpired(entry.Value, now);
            if (entry.Value.Count == 0)
            {
                expired.Add(entry.Key);
            }
        }
        foreach ((NitroxId, NitroxId) key in expired)
        {
            pending.Remove(key);
        }
    }
}
