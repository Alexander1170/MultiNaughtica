using System.Linq;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityMetadataUpdateProcessor(PlayerManager playerManager, EntityRegistry entityRegistry, CyclopsGunnerArbiter cyclopsGunnerArbiter, CyclopsEngineerArbiter cyclopsEngineerArbiter, CyclopsDroneArbiter cyclopsDroneArbiter, CyclopsNavRegistry cyclopsNavRegistry, ILogger<EntityMetadataUpdateProcessor> logger) : IAuthPacketProcessor<EntityMetadataUpdate>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly CyclopsGunnerArbiter cyclopsGunnerArbiter = cyclopsGunnerArbiter;
    private readonly CyclopsEngineerArbiter cyclopsEngineerArbiter = cyclopsEngineerArbiter;
    private readonly CyclopsDroneArbiter cyclopsDroneArbiter = cyclopsDroneArbiter;
    private readonly CyclopsNavRegistry cyclopsNavRegistry = cyclopsNavRegistry;
    private readonly ILogger<EntityMetadataUpdateProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, EntityMetadataUpdate packet)
    {
        if (!entityRegistry.TryGetEntityById(packet.Id, out Entity entity))
        {
            // Who sent it and whether the id left the registry earlier in this run (when, and which code took it out) decide between "the client made it up / the server never had it" and "someone removed it first"
            string history = entityRegistry.DescribeUnknownId(packet.Id);
            logger.ZLogError($"Entity metadata {packet.NewValue.GetType()} updated on an entity unknown to the server {packet.Id} (sent by {context.Sender.Name}; {history})");
            return;
        }

        if (TryProcessMetadata(context.Sender, entity, packet.NewValue))
        {
            // A report of the rocket is one player's whole view of it: it is merged into what the server has, not stored over it (see RocketMetadata.Merge)
            if (packet.NewValue is RocketMetadata reportedRocket)
            {
                RocketMetadata merged = RocketMetadata.Merge(entity.Metadata as RocketMetadata, reportedRocket);
                entity.Metadata = merged;
                if (!merged.HasSameStateAs(reportedRocket))
                {
                    // The reporter lacks what another player did a moment ago: everyone, the reporter included, gets the merged state
                    await SendUpdateToVisiblePlayersAsync(context, new EntityMetadataUpdate(packet.Id, merged), entity, includeSender: true);
                    return;
                }
            }
            else if (packet.NewValue is CyclopsLightingMetadata reportedLighting)
            {
                // A press reports the whole panel, with what it did not change as that client has it: that part is kept as the server has it (see CyclopsLightingMetadata.Merge)
                CyclopsLightingMetadata merged = StoreLighting(entity, reportedLighting);
                // The reporter has an old view of a part: it gets the merged state too, or it keeps showing (and acting on) the old one
                await SendUpdateToVisiblePlayersAsync(context, new EntityMetadataUpdate(packet.Id, merged), entity, includeSender: !merged.HasSameStateAs(reportedLighting));
                return;
            }
            else if (packet.NewValue is ScoutDroneMetadata reportedDrone)
            {
                // The report of a drone is its health; the bay it belongs to is the server's, so a client cannot put a drone into a bay or take it out by a metadata update (see StoreDrone)
                await SendUpdateToVisiblePlayersAsync(context, new EntityMetadataUpdate(packet.Id, StoreDrone(entity, reportedDrone)), entity);
                return;
            }
            else
            {
                entity.Metadata = packet.NewValue;
            }

            if (packet.NewValue is CyclopsMetadata { IsDestroyed: true })
            {
                RemoveEffectEntities(entity);
                // A destroyed Cyclops is a wreck, not an EntityDestroyed: its gunner and its engineer are out of the seat, and the marks and the route of its navigator are gone
                cyclopsNavRegistry.EntityDestroyed(packet.Id);
                await cyclopsGunnerArbiter.BroadcastEndedAsync(cyclopsGunnerArbiter.CyclopsGone(packet.Id), CyclopsGunnerSeatReason.CyclopsDestroyed);
                await cyclopsEngineerArbiter.BroadcastEndedAsync(cyclopsEngineerArbiter.CyclopsGone(packet.Id), CyclopsEngineerSeatReason.CyclopsDestroyed);
                await cyclopsDroneArbiter.BroadcastEndedAsync(cyclopsDroneArbiter.CyclopsGone(packet.Id), CyclopsDroneSeatReason.CyclopsDestroyed);
            }
            await SendUpdateToVisiblePlayersAsync(context, packet, entity);
        }
    }

    /// <summary>
    ///     A destroyed Cyclops (a wreck, not saved) must not give its fires and damage points to the players who join later.
    /// </summary>
    private void RemoveEffectEntities(Entity cyclops)
    {
        foreach (Entity child in cyclops.ChildEntities.Where(child => child is CyclopsFireEntity or CyclopsDamagePointEntity).ToList())
        {
            cyclops.ChildEntities.Remove(child);
            entityRegistry.RemoveEntity(child.Id);
        }
    }

    private async Task SendUpdateToVisiblePlayersAsync(AuthProcessorContext context, EntityMetadataUpdate packet, Entity entity, bool includeSender = false)
    {
        foreach (Player player in playerManager.GetConnectedPlayers())
        {
            bool updateVisibleToPlayer = player.CanSee(entity);
            if ((includeSender || player != context.Sender) && updateVisibleToPlayer)
            {
                await context.SendAsync(packet, player.SessionId);
            }
        }
    }

    private bool TryProcessMetadata(Player sendingPlayer, Entity entity, EntityMetadata metadata)
    {
        return metadata switch
        {
            PlayerMetadata playerMetadata => ProcessPlayerMetadata(sendingPlayer, entity, playerMetadata),

            // temperature is a ratchet (see ThermalPlant.QueryTemperature's Mathf.Max), so drop stale updates instead of
            // relaying them: every client near a thermal plant reports the same rise, and only the first one is news
            ThermalPlantMetadata thermalPlantMetadata => entity.Metadata is not ThermalPlantMetadata currentMetadata || thermalPlantMetadata.Temperature > currentMetadata.Temperature,

            // a leak only gains health: a stale update from a second player who welds at the same time must not make it leak again
            RadiationMetadata radiationMetadata => entity.Metadata is not RadiationMetadata currentRadiation || RadiationMetadata.IsProgress(currentRadiation.Health, radiationMetadata.Health),

            // The water of a base belongs to the base: an update that arrives after the base was replaced by its ghost (same id) must not end up on the ghost
            BaseFloodMetadata => IsBaseEntity(entity),

            // Allow metadata updates from any player by default
            _ => true
        };
    }

    internal static bool IsBaseEntity(Entity entity) => entity is BuildEntity;

    /// <summary>Keeps what a report of a Scout Drone leaves on the entity: its health, as the reporter has it. The bay (the Cyclops and the dock) stays as the server has it. Returns what is kept, which is also what the others get.</summary>
    internal static ScoutDroneMetadata StoreDrone(Entity entity, ScoutDroneMetadata reported)
    {
        ScoutDroneMetadata stored = entity.Metadata as ScoutDroneMetadata;
        // A health that is no number, or beyond what a drone can have, is not taken (the old one stays); a negative one means "not known"
        float health = float.IsNaN(reported.Health) || float.IsInfinity(reported.Health) ? stored?.Health ?? ScoutDroneMetadata.HEALTH_UNKNOWN : reported.Health < 0f ? ScoutDroneMetadata.HEALTH_UNKNOWN : Math.Min(reported.Health, VehicleUpgradeSettingsRules.MAX_DRONE_HEALTH);
        ScoutDroneMetadata kept = new(stored?.CyclopsId, stored != null && stored.Docked, health);
        entity.Metadata = kept;
        return kept;
    }

    /// <summary>Keeps what a report of a lighting panel leaves on the entity: what the press changed, and the rest as the server has it (see <see cref="CyclopsLightingMetadata.Merge" />). Returns what is kept, which is also what the others get.</summary>
    internal static CyclopsLightingMetadata StoreLighting(Entity entity, CyclopsLightingMetadata reported)
    {
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(entity.Metadata as CyclopsLightingMetadata, reported);
        entity.Metadata = merged;
        return merged;
    }

    private bool ProcessPlayerMetadata(Player sendingPlayer, Entity entity, PlayerMetadata metadata)
    {
        if (sendingPlayer.GameObjectId == entity.Id)
        {
            sendingPlayer.EquippedItems.Clear();
            foreach (PlayerMetadata.EquippedItem item in metadata.EquippedItems)
            {
                sendingPlayer.EquippedItems.Add(item.Slot, item.Id);
            }

            return true;
        }

        logger.ZLogWarningOnce($"Player {sendingPlayer.Name} tried updating metadata of another player's entity {entity.Id}");
        return false;
    }
}
