using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityTransformUpdatesProcessor(PlayerManager playerManager, WorldEntityManager worldEntityManager, SimulationOwnershipData simulationOwnershipData, EntitySimulation entitySimulation, ILogger<EntityTransformUpdatesProcessor> logger) : IAuthPacketProcessor<EntityTransformUpdates>
{
    /// <summary>
    ///     How long after a creature left a cell the players who had that cell loaded keep receiving its position.
    /// </summary>
    private const long LEFT_CELL_FORWARD_MS = 3000;

    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly ILogger<EntityTransformUpdatesProcessor> logger = logger;
    private readonly PlayerManager playerManager = playerManager;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;

    public async Task Process(AuthProcessorContext context, EntityTransformUpdates packet)
    {
        Dictionary<Player, List<EntityTransformUpdates.EntityTransformUpdate>> visibleUpdatesByPlayer = InitializeVisibleUpdateMapWithOtherPlayers(context.Sender);
        List<(Player Player, NitroxId EntityId)> ghostCopies = [];
        List<SimulatedEntity> ownershipChanges = AssignVisibleUpdatesToPlayers(context.Sender, packet.Updates, visibleUpdatesByPlayer, ghostCopies);
        await SendUpdatesToPlayersAsync(context, visibleUpdatesByPlayer);
        foreach ((Player holder, NitroxId entityId) in ghostCopies)
        {
            // Not a destruction of the entity: the copy this player still has stands in a cell they have loaded while the entity is somewhere they cannot see
            logger.ZLogDebug($"K4e: removed the copy of {entityId} from {holder.Name}, it left every cell they have loaded");
            await context.SendAsync(new EntityDestroyed(entityId), holder.SessionId);
        }
        if (ownershipChanges.Count > 0)
        {
            await context.SendToAllAsync(new SimulationOwnershipChange(ownershipChanges));
        }
    }

    private Dictionary<Player, List<EntityTransformUpdates.EntityTransformUpdate>> InitializeVisibleUpdateMapWithOtherPlayers(Player simulatingPlayer)
    {
        Dictionary<Player, List<EntityTransformUpdates.EntityTransformUpdate>> visibleUpdatesByPlayer = [];
        foreach (Player player in playerManager.GetConnectedPlayers())
        {
            if (!player.Equals(simulatingPlayer))
            {
                visibleUpdatesByPlayer[player] = [];
            }
        }
        return visibleUpdatesByPlayer;
    }

    private List<SimulatedEntity> AssignVisibleUpdatesToPlayers(Player sendingPlayer, List<EntityTransformUpdates.EntityTransformUpdate> updates, Dictionary<Player, List<EntityTransformUpdates.EntityTransformUpdate>> visibleUpdatesByPlayer, List<(Player Player, NitroxId EntityId)> ghostCopies)
    {
        List<SimulatedEntity> ownershipChanges = [];
        foreach (EntityTransformUpdates.EntityTransformUpdate update in updates)
        {
            if (!simulationOwnershipData.TryGetLock(update.Id, out SimulationOwnershipData.PlayerLock playerLock) || playerLock.Player != sendingPlayer)
            {
                // This will happen pretty frequently when a player moves very fast (swimfast or maybe some more edge cases) so we can just ignore this
                continue;
            }

            if (!worldEntityManager.TryUpdateEntityPosition(update.Id, update.Position, update.Rotation, out AbsoluteEntityCell currentCell, out WorldEntity worldEntity))
            {
                // Normal behaviour if the entity was removed at the same time as someone trying to simulate a postion update.
                // we log an info inside entityManager.UpdateEntityPosition just in case.
                continue;
            }

            // The server tracks an entity by the cell it is in, but a player who loaded the cell the entity just left still has it and has to keep following it:
            // its copy only reports leaving the loaded area (OutOfCellEntity) once it has moved there, which it does not do without position updates, so it drifted off alone
            AbsoluteEntityCell? recentlyLeftCell = worldEntityManager.GetRecentlyLeftCell(update.Id, LEFT_CELL_FORWARD_MS);
            foreach (KeyValuePair<Player, List<EntityTransformUpdates.EntityTransformUpdate>> playerUpdates in visibleUpdatesByPlayer)
            {
                if (playerUpdates.Key.CanSee(worldEntity) || (recentlyLeftCell != null && playerUpdates.Key.HasCellLoaded(recentlyLeftCell)))
                {
                    playerUpdates.Value.Add(update);
                }
            }

            if (entitySimulation.TryRebalanceCreatureLock(worldEntity, sendingPlayer, out SimulatedEntity? ownershipChange))
            {
                ownershipChanges.Add(ownershipChange);
            }

            // K4e: players whose copy of this entity was left standing in a loaded cell when it moved out of their view and has not come back into it since
            foreach (SessionId holderId in worldEntityManager.TakeDueGhostHolders(update.Id))
            {
                if (holderId != sendingPlayer.SessionId && playerManager.TryGetPlayerBySessionId(holderId, out Player? holder) && !holder.CanSee(worldEntity) &&
                    simulationOwnershipData.GetPlayerForLock(update.Id)?.SessionId != holderId)
                {
                    ghostCopies.Add((holder, update.Id));
                }
            }
        }
        return ownershipChanges;
    }

    private async Task SendUpdatesToPlayersAsync(AuthProcessorContext context, Dictionary<Player, List<EntityTransformUpdates.EntityTransformUpdate>> visibleUpdatesByPlayer)
    {
        foreach (KeyValuePair<Player, List<EntityTransformUpdates.EntityTransformUpdate>> playerUpdates in visibleUpdatesByPlayer)
        {
            Player player = playerUpdates.Key;
            List<EntityTransformUpdates.EntityTransformUpdate> updates = playerUpdates.Value;

            if (updates.Count > 0)
            {
                await context.SendAsync(new EntityTransformUpdates(updates), player.SessionId);
            }
        }
    }
}
