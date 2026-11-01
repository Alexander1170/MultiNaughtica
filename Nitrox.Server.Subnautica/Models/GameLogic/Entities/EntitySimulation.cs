using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

internal sealed class EntitySimulation : ISessionCleaner
{
    private const SimulationLockType DEFAULT_ENTITY_SIMULATION_LOCKTYPE = SimulationLockType.TRANSIENT;

    /// <summary>
    ///     A creature is only handed to another player when that player is this many meters closer than its current simulating player.
    /// </summary>
    private const float REBALANCE_HYSTERESIS = 20f;

    private const long REBALANCE_INTERVAL_MS = 2000;

    /// <summary>
    ///     A base keeps its simulating player as long as that player is this close to it (meters), whoever else is inside.
    /// </summary>
    private const float BASE_HOLDER_NEAR_RADIUS = 100f;

    /// <summary>
    ///     A base is only handed to a player outside it when that player is this many meters closer than the current simulating player.
    /// </summary>
    private const float BASE_REBALANCE_HYSTERESIS = 150f;

    private const long BASE_REBALANCE_INTERVAL_MS = 2000;
    private const long BASE_MIN_HOLD_MS = 30000;
    private readonly Dictionary<NitroxId, long> lastRebalanceCheckById = [];
    private readonly Dictionary<NitroxId, long> lastBaseHandoverById = [];
    private long lastBaseRebalanceCheck;
    private readonly EntityRegistry entityRegistry;
    private readonly ILogger<EntitySimulation> logger;

    private readonly IPacketSender packetSender;
    private readonly PlayerManager playerManager;
    private readonly SimulationOwnershipData simulationOwnershipData;
    private readonly WorldEntityManager worldEntityManager;

    public EntitySimulation(IPacketSender packetSender, EntityRegistry entityRegistry, WorldEntityManager worldEntityManager, SimulationOwnershipData simulationOwnershipData, PlayerManager playerManager, ILogger<EntitySimulation> logger)
    {
        this.packetSender = packetSender;
        this.entityRegistry = entityRegistry;
        this.worldEntityManager = worldEntityManager;
        this.simulationOwnershipData = simulationOwnershipData;
        this.playerManager = playerManager;
        this.logger = logger;
    }

    public IEnumerable<SimulatedEntity> GetSimulationChangesForCell(Player player, AbsoluteEntityCell cell)
    {
        foreach (WorldEntity entity in GetPlayerSimulatedEntities(player, cell))
        {
            bool doesEntityMove = ShouldSimulateEntityMovement(entity);
            yield return new SimulatedEntity(entity.Id, player.SessionId, doesEntityMove, DEFAULT_ENTITY_SIMULATION_LOCKTYPE);
        }
    }

    public void FillWithRemovedCells(Player player, AbsoluteEntityCell removedCell, List<SimulatedEntity> ownershipChanges)
    {
        AssignEntitiesToOtherPlayers(player.SessionId, GetEntitiesToRevoke(player, removedCell), ownershipChanges);
        return;

        IEnumerable<WorldEntity> GetEntitiesOfCell(AbsoluteEntityCell cell)
        {
            foreach (WorldEntity entity in worldEntityManager.GetEntities(cell))
            {
                yield return entity;
                foreach (WorldEntity child in GetSimulatableChildren(entity))
                {
                    yield return child;
                }
            }
        }

        IEnumerable<WorldEntity> GetEntitiesToRevoke(Player simulatingPlayer, AbsoluteEntityCell cell)
        {
            foreach (WorldEntity entity in GetEntitiesOfCell(cell))
            {
                if (player.CanSee(entity))
                {
                    continue;
                }
                if (!simulationOwnershipData.RevokeIfOwner(entity.Id, simulatingPlayer))
                {
                    continue;
                }

                yield return entity;
            }
        }
    }

    private IEnumerable<WorldEntity> GetSimulatableChildren(WorldEntity entity)
    {
        return entity.ChildEntities.OfType<WorldEntity>().Where(ShouldSimulateEntity);
    }

    private IEnumerable<WorldEntity> GetPlayerSimulatedEntities(Player simulatingPlayer, AbsoluteEntityCell cell)
    {
        foreach (WorldEntity entity in worldEntityManager.GetEntities(cell))
        {
            if (!simulatingPlayer.CanSee(entity))
            {
                continue;
            }
            if (!ShouldSimulateEntity(entity))
            {
                continue;
            }
            if (!simulationOwnershipData.TryToAcquire(entity.Id, simulatingPlayer, DEFAULT_ENTITY_SIMULATION_LOCKTYPE))
            {
                continue;
            }

            yield return entity;
            foreach (WorldEntity child in GetSimulatableChildren(entity))
            {
                if (simulationOwnershipData.TryToAcquire(child.Id, simulatingPlayer, DEFAULT_ENTITY_SIMULATION_LOCKTYPE))
                {
                    yield return child;
                }
            }
        }
    }

    public void BroadcastSimulationChanges(List<SimulatedEntity> ownershipChanges)
    {
        if (ownershipChanges.Count > 0)
        {
            SimulationOwnershipChange ownershipChange = new(ownershipChanges);
            packetSender.SendPacketToAllAsync(ownershipChange);
        }
    }

    public bool TryAssignEntityToPlayer(Entity entity, Player player, bool shouldEntityMove, [NotNullWhen(true)] out SimulatedEntity? simulatedEntity)
    {
        if (simulationOwnershipData.TryToAcquire(entity.Id, player, DEFAULT_ENTITY_SIMULATION_LOCKTYPE))
        {
            bool doesEntityMove = shouldEntityMove && entity is WorldEntity worldEntity && ShouldSimulateEntityMovement(worldEntity);
            simulatedEntity = new(entity.Id, player.SessionId, doesEntityMove, DEFAULT_ENTITY_SIMULATION_LOCKTYPE);
            return true;
        }

        simulatedEntity = null;
        return false;
    }

    public List<SimulatedEntity> AssignGlobalRootEntitiesAndGetData(Player player)
    {
        List<SimulatedEntity> simulatedEntities = new();
        foreach (GlobalRootEntity entity in worldEntityManager.GetGlobalRootEntities())
        {
            simulationOwnershipData.TryToAcquire(entity.Id, player, SimulationLockType.TRANSIENT);
            if (!simulationOwnershipData.TryGetLock(entity.Id, out SimulationOwnershipData.PlayerLock playerLock))
            {
                continue;
            }
            bool doesEntityMove = ShouldSimulateEntityMovement(entity);
            SimulatedEntity simulatedEntity = new(entity.Id, playerLock.Player.SessionId, doesEntityMove, playerLock.LockType);
            simulatedEntities.Add(simulatedEntity);
        }
        return simulatedEntities;
    }

    /// <summary>
    ///     Who holds the lock of every global root entity (vehicles, bases, ...) right now, without taking any lock. The snapshot that a joining player gets with his initial sync is
    ///     made when the sync starts; what changes during the seconds of his loading screen (the holder leaves, a base is handed to the player who is inside it) only reaches him as a
    ///     change for entities that his client has not spawned yet, and the snapshot then overwrites it. The same list at the end of the sync puts him right (V0, PO6).
    /// </summary>
    public List<SimulatedEntity> GetGlobalRootSimulationSnapshot()
    {
        List<SimulatedEntity> simulatedEntities = [];
        foreach (GlobalRootEntity entity in worldEntityManager.GetGlobalRootEntities())
        {
            if (!simulationOwnershipData.TryGetLock(entity.Id, out SimulationOwnershipData.PlayerLock playerLock))
            {
                continue;
            }

            simulatedEntities.Add(new SimulatedEntity(entity.Id, playerLock.Player.SessionId, ShouldSimulateEntityMovement(entity), playerLock.LockType));
        }

        return simulatedEntities;
    }

    public bool TryAssignEntityToPlayers(List<Player> players, Entity entity, [NotNullWhen(true)] out SimulatedEntity? simulatedEntity)
    {
        NitroxId id = entity.Id;

        foreach (Player player in players)
        {
            if (player.CanSee(entity) && simulationOwnershipData.TryToAcquire(id, player, DEFAULT_ENTITY_SIMULATION_LOCKTYPE))
            {
                bool doesEntityMove = entity is WorldEntity worldEntity && ShouldSimulateEntityMovement(worldEntity);

                logger.ZLogTrace($"Player {player.Name} has taken over simulating {id}");
                simulatedEntity = new(id, player.SessionId, doesEntityMove, DEFAULT_ENTITY_SIMULATION_LOCKTYPE);
                return true;
            }
        }

        simulatedEntity = null;
        return false;
    }

    public bool ShouldSimulateEntity(WorldEntity entity)
    {
        return SimulationWhitelist.UtilityWhitelist.Contains(entity.TechType) || ShouldSimulateEntityMovement(entity);
    }

    public bool ShouldSimulateEntityMovement(WorldEntity entity)
    {
        return !entity.SpawnedByServer || SimulationWhitelist.MovementWhitelist.Contains(entity.TechType);
    }

    public bool ShouldSimulateEntityMovement(NitroxId entityId)
    {
        return entityRegistry.TryGetEntityById(entityId, out WorldEntity worldEntity) && ShouldSimulateEntityMovement(worldEntity);
    }

    public void EntityDestroyed(NitroxId id)
    {
        simulationOwnershipData.RevokeOwnerOfId(id);
        lock (lastRebalanceCheckById)
        {
            lastRebalanceCheckById.Remove(id);
        }
    }

    /// <summary>
    ///     Locks are handed out first-come per cell, so whoever loaded a cell first would simulate every creature in it for as long as they
    ///     keep it loaded. Called with the position updates the simulating player sends: moves a creature's transient lock to the closest
    ///     player who has the creature loaded, once that player is clearly closer than the current owner (<see cref="REBALANCE_HYSTERESIS" />).
    /// </summary>
    /// <param name="entity">A creature, with the position its owner just reported.</param>
    /// <param name="owner">The player who sent the position update.</param>
    /// <param name="change">The ownership change to broadcast when the lock moved.</param>
    public bool TryRebalanceCreatureLock(WorldEntity entity, Player owner, [NotNullWhen(true)] out SimulatedEntity? change)
    {
        change = null;
        if (!entity.SpawnedByServer || entity is GlobalRootEntity || !SimulationWhitelist.MovementWhitelist.Contains(entity.TechType))
        {
            return false;
        }

        long now = Environment.TickCount64;
        lock (lastRebalanceCheckById)
        {
            if (lastRebalanceCheckById.TryGetValue(entity.Id, out long lastCheck) && now - lastCheck < REBALANCE_INTERVAL_MS)
            {
                return false;
            }
            lastRebalanceCheckById[entity.Id] = now;
        }

        if (!simulationOwnershipData.TryGetLock(entity.Id, out SimulationOwnershipData.PlayerLock currentLock) ||
            currentLock.LockType != SimulationLockType.TRANSIENT || currentLock.Player != owner)
        {
            return false;
        }

        NitroxVector3 position = entity.Transform.Position;
        Player? closest = null;
        float closestDistance = NitroxVector3.Distance(owner.Position, position) - REBALANCE_HYSTERESIS;
        foreach (Player candidate in playerManager.GetConnectedPlayersExcept(owner.SessionId))
        {
            if (!candidate.CanSee(entity))
            {
                continue;
            }
            float distance = NitroxVector3.Distance(candidate.Position, position);
            if (distance < closestDistance)
            {
                closest = candidate;
                closestDistance = distance;
            }
        }
        if (closest == null || !simulationOwnershipData.RevokeIfOwner(entity.Id, owner))
        {
            return false;
        }

        if (!simulationOwnershipData.TryToAcquire(entity.Id, closest, DEFAULT_ENTITY_SIMULATION_LOCKTYPE))
        {
            // Someone else took it in between, give it back
            simulationOwnershipData.TryToAcquire(entity.Id, owner, DEFAULT_ENTITY_SIMULATION_LOCKTYPE);
            return false;
        }

        logger.ZLogDebug($"Moved simulation of {entity.TechType} {entity.Id} from {owner.Name} to closer player {closest.Name}");
        change = new SimulatedEntity(entity.Id, closest.SessionId, true, DEFAULT_ENTITY_SIMULATION_LOCKTYPE);
        return true;
    }

    /// <summary>
    ///     A player as a candidate to simulate a base.
    /// </summary>
    /// <param name="IsInside">The player is inside this base (their SubRoot is the base).</param>
    /// <param name="Distance">Meters between the player and the base.</param>
    internal readonly record struct BaseSimulationCandidate(bool IsInside, float Distance);

    /// <summary>
    ///     Which of the players should simulate a base (power, water filtration, hull damage, ... run only for the holder of its lock).
    ///     A base is loaded and running on every client wherever its players are, but what a player does in it (crafting, using power)
    ///     only counts in the simulation of the holder, so the lock should follow the players: the holder keeps it while it is inside or
    ///     within <see cref="BASE_HOLDER_NEAR_RADIUS" /> of the base; once it is farther away a player inside the base gets it, otherwise
    ///     a player who is <see cref="BASE_REBALANCE_HYSTERESIS" /> meters closer than the holder.
    /// </summary>
    /// <returns>The index in <paramref name="candidates" /> of the player who should hold the lock (<paramref name="holderIndex" /> to keep it).</returns>
    internal static int ChooseBaseSimulator(int holderIndex, IReadOnlyList<BaseSimulationCandidate> candidates)
    {
        BaseSimulationCandidate holder = candidates[holderIndex];
        if (holder.IsInside || holder.Distance <= BASE_HOLDER_NEAR_RADIUS)
        {
            return holderIndex;
        }

        int best = holderIndex;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (i != holderIndex && candidates[i].IsInside && (best == holderIndex || candidates[i].Distance < candidates[best].Distance))
            {
                best = i;
            }
        }
        if (best != holderIndex)
        {
            return best;
        }

        float bestDistance = holder.Distance - BASE_REBALANCE_HYSTERESIS;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (i != holderIndex && candidates[i].Distance < bestDistance)
            {
                best = i;
                bestDistance = candidates[i].Distance;
            }
        }
        return best;
    }

    /// <summary>
    ///     Moves the simulation lock of bases to the players who are in or near them (see <see cref="ChooseBaseSimulator" />). Bases get their lock when
    ///     they are built or when the first player joins and keep it until that player leaves, so without this a player far away from the base would
    ///     keep simulating it while another player is standing in it. Called with every player movement, runs at most every
    ///     <see cref="BASE_REBALANCE_INTERVAL_MS" /> and keeps a handover for at least <see cref="BASE_MIN_HOLD_MS" />.
    /// </summary>
    /// <returns>The ownership changes to broadcast, empty most of the time.</returns>
    public List<SimulatedEntity> RebalanceBaseLocks()
    {
        long now = Environment.TickCount64;
        lock (lastBaseHandoverById)
        {
            if (now - lastBaseRebalanceCheck < BASE_REBALANCE_INTERVAL_MS)
            {
                return [];
            }
            lastBaseRebalanceCheck = now;
        }

        List<SimulatedEntity> changes = [];
        List<Player> players = playerManager.GetConnectedPlayers();
        if (players.Count < 2)
        {
            return changes;
        }

        foreach (GlobalRootEntity globalRootEntity in worldEntityManager.GetGlobalRootEntities())
        {
            if (globalRootEntity is not BuildEntity baseEntity ||
                !simulationOwnershipData.TryGetLock(baseEntity.Id, out SimulationOwnershipData.PlayerLock currentLock) ||
                currentLock.LockType != SimulationLockType.TRANSIENT)
            {
                continue;
            }
            int holderIndex = players.FindIndex(player => player.SessionId == currentLock.Player.SessionId);
            if (holderIndex < 0)
            {
                continue;
            }
            lock (lastBaseHandoverById)
            {
                if (lastBaseHandoverById.TryGetValue(baseEntity.Id, out long lastHandover) && now - lastHandover < BASE_MIN_HOLD_MS)
                {
                    continue;
                }
            }

            List<BaseSimulationCandidate> candidates = players
                .Select(player => new BaseSimulationCandidate(player.SubRootId.HasValue && player.SubRootId.Value.Equals(baseEntity.Id), NitroxVector3.Distance(player.Position, baseEntity.Transform.Position)))
                .ToList();
            int chosenIndex = ChooseBaseSimulator(holderIndex, candidates);
            if (chosenIndex == holderIndex)
            {
                continue;
            }

            Player holder = players[holderIndex];
            Player chosen = players[chosenIndex];
            if (!simulationOwnershipData.RevokeIfOwner(baseEntity.Id, holder))
            {
                continue;
            }
            if (!simulationOwnershipData.TryToAcquire(baseEntity.Id, chosen, DEFAULT_ENTITY_SIMULATION_LOCKTYPE))
            {
                simulationOwnershipData.TryToAcquire(baseEntity.Id, holder, DEFAULT_ENTITY_SIMULATION_LOCKTYPE);
                continue;
            }

            lock (lastBaseHandoverById)
            {
                lastBaseHandoverById[baseEntity.Id] = now;
            }
            logger.ZLogInformation($"Moved simulation of base {baseEntity.Id} from {holder.Name} ({candidates[holderIndex].Distance:F0} m away) to {chosen.Name} ({(candidates[chosenIndex].IsInside ? "inside" : $"{candidates[chosenIndex].Distance:F0} m away")})");
            changes.Add(new SimulatedEntity(baseEntity.Id, chosen.SessionId, false, DEFAULT_ENTITY_SIMULATION_LOCKTYPE));
        }
        return changes;
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        List<SimulatedEntity> ownershipChanges = CalculateSimulationChangesFromPlayerDisconnect(args.Session.Id);
        if (ownershipChanges.Count > 0)
        {
            SimulationOwnershipChange ownershipChange = new(ownershipChanges);
            await packetSender.SendPacketToAllAsync(ownershipChange);
        }
    }

    private List<SimulatedEntity> CalculateSimulationChangesFromPlayerDisconnect(SessionId sessionId)
    {
        List<SimulatedEntity> ownershipChanges = new();

        List<NitroxId> revokedEntityIds = simulationOwnershipData.RevokeAllForOwner(sessionId);
        List<Entity> revokedEntities = entityRegistry.GetEntities(revokedEntityIds);

        AssignEntitiesToOtherPlayers(sessionId, revokedEntities, ownershipChanges);

        return ownershipChanges;
    }

    private void AssignEntitiesToOtherPlayers(SessionId oldSessionId, IEnumerable<Entity> entities, List<SimulatedEntity> ownershipChanges)
    {
        List<Player> otherPlayers = playerManager.GetConnectedPlayersExcept(oldSessionId);
        foreach (Entity entity in entities)
        {
            if (TryAssignEntityToPlayers(otherPlayers, entity, out SimulatedEntity simulatedEntity))
            {
                ownershipChanges.Add(simulatedEntity);
            }
        }
    }
}
