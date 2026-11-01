using System.Collections.Concurrent;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PickupItemPacketProcessor(EntityRegistry entityRegistry, WorldEntityManager worldEntityManager, SimulationOwnershipData simulationOwnershipData)
    : IAuthPacketProcessor<PickupItem>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    /// <summary>
    ///     A pickup of an item by another player this soon after the first one is a race for the same item (each client picks up locally before
    ///     the server knows). Later pickups of an item the registry still has in someone's inventory are not contested: the registry can be stale.
    /// </summary>
    private const long CONTESTED_PICKUP_WINDOW_MS = 3000;

    private static readonly ConcurrentDictionary<NitroxId, (SessionId Picker, long Timestamp)> recentPickups = new();

    public async Task Process(AuthProcessorContext context, PickupItem packet)
    {
        NitroxId id = packet.Item.Id;

        // The first pickup to reach the server wins. The loser's client already has the item in its inventory: sending it the winner's inventory
        // entity makes it destroy its own copy (forceRespawn) and see the item in the winner's inventory, like every other player does.
        long now = Environment.TickCount64;
        if (recentPickups.TryGetValue(id, out (SessionId Picker, long Timestamp) recent) && recent.Picker != context.Sender.SessionId && now - recent.Timestamp < CONTESTED_PICKUP_WINDOW_MS &&
            entityRegistry.TryGetEntityById(id, out InventoryItemEntity winnerItem))
        {
            await context.ReplyAsync(new SpawnEntities(winnerItem, forceRespawn: true));
            return;
        }
        recentPickups[id] = (context.Sender.SessionId, now);
        if (recentPickups.Count > 512)
        {
            foreach (KeyValuePair<NitroxId, (SessionId Picker, long Timestamp)> entry in recentPickups)
            {
                if (now - entry.Value.Timestamp >= CONTESTED_PICKUP_WINDOW_MS)
                {
                    recentPickups.TryRemove(entry.Key, out _);
                }
            }
        }

        if (simulationOwnershipData.RevokeOwnerOfId(id))
        {
            SimulationOwnershipChange simulationOwnershipChange = new(id, SessionId.SERVER_ID, SimulationLockType.TRANSIENT);
            await context.SendToAllAsync(simulationOwnershipChange);
        }

        StopTrackingExistingWorldEntity(id);

        entityRegistry.AddOrUpdate(packet.Item);

        // Have other players respawn the item inside the inventory.
        await context.SendToOthersAsync(new SpawnEntities(packet.Item, forceRespawn: true));
    }

    private void StopTrackingExistingWorldEntity(NitroxId id)
    {
        Optional<Entity> entity = entityRegistry.GetEntityById(id);

        if (entity is { HasValue: true, Value: WorldEntity worldEntity })
        {
            // Do not track this entity in the open world anymore.
            worldEntityManager.StopTrackingEntity(worldEntity);
        }
    }
}
