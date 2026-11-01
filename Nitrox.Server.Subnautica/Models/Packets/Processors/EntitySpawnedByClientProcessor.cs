using System.Diagnostics.CodeAnalysis;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntitySpawnedByClientProcessor(PlayerManager playerManager, EntityRegistry entityRegistry, WorldEntityManager worldEntityManager, EntitySimulation entitySimulation, ModuleSlotArbiter moduleSlotArbiter, ILogger<EntitySpawnedByClientProcessor> logger)
    : IAuthPacketProcessor<EntitySpawnedByClient>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly ModuleSlotArbiter moduleSlotArbiter = moduleSlotArbiter;
    private readonly ILogger<EntitySpawnedByClientProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, EntitySpawnedByClient packet)
    {
        Entity entity = packet.Entity;

        // Two players that load the same spawner introduce its creature under the same id at nearly the same time: the first one counts, the lock stays with it
        if (IsRepeatedIntroduction(packet, entityRegistry.TryGetEntityById(entity.Id, out Entity _)))
        {
            return;
        }

        // Two players that put a module into the same slot at the same moment: the first one counts, the other one is sent back (V0, U4)
        if (IsArbitratedModule(packet, out InstalledModuleEntity? module) && await IsModuleSlotLostAsync(context, module))
        {
            return;
        }
        MarkSpawnerCreature(packet);

        // An entity which is already in the open world (a dropped item picked up and dropped somewhere else) is still tracked in the cell it was in:
        // untrack it there, else players loading that cell get it back at the old position.
        if (entityRegistry.TryGetEntityById(entity.Id, out WorldEntity previousWorldEntity))
        {
            worldEntityManager.StopTrackingEntity(previousWorldEntity);
        }

        // If the entity already exists in the registry, it is fine to update.  This is a normal case as the player
        // may have an item in their inventory (that the registry knows about) then wants to spawn it into the world.
        entityRegistry.AddOrUpdate(entity);

        SimulatedEntity simulatedEntity = null;
        if (entity is WorldEntity worldEntity)
        {
            worldEntityManager.TrackEntityInTheWorld(worldEntity);
        }

        if (packet.RequireSimulation && entitySimulation.TryAssignEntityToPlayer(entity, context.Sender, true, out simulatedEntity))
        {
            SimulationOwnershipChange ownershipChangePacket = new(simulatedEntity);
            await context.SendToAllAsync(ownershipChangePacket);
        }

        SpawnEntities spawnEntities = new(entity, simulatedEntity, packet.RequireRespawn);
        foreach (Player player in playerManager.GetConnectedPlayers())
        {
            bool isOtherPlayer = player != context.Sender;
            if ((isOtherPlayer || IsSentBackToSender(packet)) && player.CanSee(entity))
            {
                await context.SendAsync(spawnEntities, player.SessionId);
            }
        }
    }

    /// <summary>
    /// A module that a player put into a slot (<see cref="InstalledModuleEntity"/> with its owner and slot) is the only thing the module slot arbiter decides about: a creature
    /// of a spawner (<see cref="EntitySpawnedByClient.OnlyIfNew"/>) and every other kind of entity is left alone.
    /// </summary>
    internal static bool IsArbitratedModule(EntitySpawnedByClient packet, [NotNullWhen(true)] out InstalledModuleEntity? module)
    {
        module = packet.Entity as InstalledModuleEntity;
        return !packet.OnlyIfNew && module is { ParentId: not null, Slot: not null };
    }

    /// <summary>
    /// Asks the arbiter about an install. When another player was first, the sender is told to take the module back and the install is dropped (true): it is not registered,
    /// takes no simulation lock and is not forwarded. When the sender wins, the module that the registry still holds in that slot (it is stale, the sender's own slot was
    /// empty) is removed for everybody before the newcomer is forwarded, so the other clients have an empty slot when it arrives (false).
    /// </summary>
    private async Task<bool> IsModuleSlotLostAsync(AuthProcessorContext context, InstalledModuleEntity module)
    {
        ModuleSlotDecision decision = moduleSlotArbiter.Decide(context.Sender.SessionId, module, entityRegistry);
        if (decision.Rejected)
        {
            logger.ZLogWarning($"{context.Sender.Name} put {module.TechType} {module.Id} into slot {module.Slot} of {module.ParentId}, {decision.WinnerId} was first: sending it back");

            // A module that was moved from another slot is not known to the server any more (its old slot's destroy came first) while the sender's client puts it into his inventory
            // without telling anybody: register it there, else the server would have lost it. The other players are not told: a remote player has no container of the sender's to spawn
            // an inventory item into (InventoryItemEntitySpawner would log an error), the registry entry is all that is needed (it is sent with the sender's inventory to whoever joins)
            if (!entityRegistry.TryGetEntityById(module.Id, out Entity _))
            {
                InventoryItemEntity item = new(module.Id, module.ClassId, module.TechType, module.Metadata, context.Sender.GameObjectId, module.ChildEntities ?? []);
                entityRegistry.AddOrUpdate(item);
            }

            await context.ReplyAsync(new ModuleSlotRejected(module.Id, module.ParentId, module.Slot));
            return true;
        }

        if (decision.StaleOccupantId != null && worldEntityManager.TryDestroyEntity(decision.StaleOccupantId, out Entity? _))
        {
            entitySimulation.EntityDestroyed(decision.StaleOccupantId);
            logger.ZLogInformation($"Slot {module.Slot} of {module.ParentId}: stale module {decision.StaleOccupantId} removed, {module.Id} takes the slot");
            // The sender is not told, the slot was empty on his side
            await context.SendToOthersAsync(new EntityDestroyed(decision.StaleOccupantId));
        }
        return false;
    }

    /// <summary>
    /// An introduction that may be sent by several players for the same id (<see cref="EntitySpawnedByClient.OnlyIfNew"/>) and that the server already knows.
    /// </summary>
    internal static bool IsRepeatedIntroduction(EntitySpawnedByClient packet, bool entityAlreadyKnown)
    {
        return packet.OnlyIfNew && entityAlreadyKnown;
    }

    /// <summary>
    /// The creature of a spawner is made by the server's entity on every client, the sender's included: the sender destroyed the creature that the game made (it was only the seed of the
    /// introduction), so it gets the entity like everybody else (K15).
    /// </summary>
    internal static bool IsSentBackToSender(EntitySpawnedByClient packet) => packet.OnlyIfNew;

    /// <summary>
    /// The creature of a spawner stands for the world's own population: of a kind that the server moves and rebalances (the whitelist), it counts as spawned by the server, so that
    /// its lock follows the closest player like the lock of any other fish. Others stay with the player who made them, as everything a client spawns does.
    /// </summary>
    internal static void MarkSpawnerCreature(EntitySpawnedByClient packet)
    {
        if (packet.OnlyIfNew && packet.Entity is WorldEntity worldEntity && SimulationWhitelist.MovementWhitelist.Contains(worldEntity.TechType))
        {
            worldEntity.SpawnedByServer = true;
        }
    }
}
