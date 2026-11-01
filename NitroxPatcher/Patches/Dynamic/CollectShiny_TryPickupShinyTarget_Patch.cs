using System.Collections.Generic;
using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A Stalker (<see cref="CollectShiny"/>) that carries off a piece of scrap moves a world item on its own client only: the item is not in the
/// movement whitelist, so the other client and the server keep the spawn position (the scrap seemed to sit 34 m apart on the two clients).
/// The pickup therefore waits for an exclusive simulation lock on the item; with the lock the position is broadcast like for a grabbed item
/// (<see cref="EntityPositionBroadcaster"/>), until <see cref="ShinySettleWatcher"/> sees it at rest after the drop.
/// </summary>
public sealed partial class CollectShiny_TryPickupShinyTarget_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CollectShiny t) => t.TryPickupShinyTarget());

    private const float RESEND_REQUEST_AFTER_SECONDS = 3f;
    private const float LOOK_FOR_OTHER_SHINY_AFTER_DENIAL_SECONDS = 6f;

    private static readonly Dictionary<NitroxId, float> requestTimeById = [];

    public static bool Prefix(CollectShiny __instance)
    {
        GameObject target = __instance.shinyTarget;
        if (!target || !target.TryGetNitroxId(out NitroxId id))
        {
            // Not an item the server knows (a local-only spawn): nobody else could have it
            return true;
        }

        SimulationOwnership simulationOwnership = Resolve<SimulationOwnership>();
        if (simulationOwnership.HasExclusiveLock(id))
        {
            requestTimeById.Remove(id);
            return true;
        }

        if (!requestTimeById.TryGetValue(id, out float requestTime) || Time.time - requestTime > RESEND_REQUEST_AFTER_SECONDS)
        {
            requestTimeById[id] = Time.time;
            simulationOwnership.RequestSimulationLock(new LockRequest<ShinyPickup>(id, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, new ShinyPickup(__instance)));
        }

        // The vanilla pickup is tried again with the next Perform
        return false;
    }

    private static void ReceivedSimulationLockResponse(NitroxId id, bool lockAcquired, ShinyPickup context)
    {
        requestTimeById.Remove(id);
        Log.Info($"[Stalker carry] simulation lock for {id}: {(lockAcquired ? "acquired, the Stalker may carry it off" : "denied, the Stalker looks for something else")}");
        if (lockAcquired)
        {
            EntityPositionBroadcaster.WatchEntity(id);
            return;
        }

        // Another player simulates the item (carries it with a propulsion cannon, say): the Stalker looks for something else
        CollectShiny collector = context.Collector;
        if (collector && collector.shinyTarget && !collector.targetPickedUp)
        {
            collector.shinyTarget = null;
            collector.timeNextFindShiny = Time.time + LOOK_FOR_OTHER_SHINY_AFTER_DENIAL_SECONDS;
        }
    }
}
