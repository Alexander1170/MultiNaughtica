using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.Extensions;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// Keeps the position of a shiny item (scrap metal a Stalker carries off and drops) going to the other players, from the pickup until the
/// item has come to rest after the drop, then hands the item over (transient lock, no more broadcast).
/// Without this the item only moves on the client that simulates the Stalker: the other client and the server keep the spawn position.
/// </summary>
public class ShinySettleWatcher : MonoBehaviour
{
    private const float CHECK_INTERVAL = 1f;
    private const float SETTLED_DISTANCE = 0.05f;
    private const int SETTLED_CHECKS = 3;
    private const float GIVE_UP_AFTER_SECONDS = 60f;

    private NitroxId id;
    private Vector3 lastPosition;
    private int calmChecks;
    private float lastCarriedTime;

    public void Start()
    {
        if (!this.TryGetNitroxId(out NitroxId itemId))
        {
            Destroy(this);
            return;
        }

        id = itemId;
        EntityPositionBroadcaster.WatchEntity(id);
        lastPosition = transform.position;
        lastCarriedTime = Time.time;
        InvokeRepeating(nameof(Check), CHECK_INTERVAL, CHECK_INTERVAL);
    }

    private void Check()
    {
        Vector3 position = transform.position;
        bool carried = GetComponentInParent<Creature>();
        if (carried)
        {
            lastCarriedTime = Time.time;
        }

        calmChecks = !carried && Vector3.Distance(position, lastPosition) < SETTLED_DISTANCE ? calmChecks + 1 : 0;
        lastPosition = position;
        if (calmChecks < SETTLED_CHECKS && Time.time - lastCarriedTime < GIVE_UP_AFTER_SECONDS)
        {
            return;
        }

        EntityPositionBroadcaster.StopWatchingEntity(id);
        if (TryGetComponent(out Rigidbody body))
        {
            // Loose items start out kinematic; this one stays where it came to rest (the other client freezes its copy when the handover below reaches it)
            UWE.Utils.SetIsKinematicAndUpdateInterpolation(body, true, false);
        }
        // A transient lock can be taken over by an exclusive request (propulsion cannon) of the other player
        this.Resolve<SimulationOwnership>().RequestSimulationLock(id, SimulationLockType.TRANSIENT);
        Log.Info($"[ShinySettleWatcher] {name} {id} came to rest at {position.ToString("F2")}, handed over");
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (id != null)
        {
            EntityPositionBroadcaster.StopWatchingEntity(id);
        }
    }
}
