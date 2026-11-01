using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic.Bases;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

// TODO: Refactor this to a QueuingBackgroundService to simplify state tracking.
internal sealed class JoiningManager(
    IPacketSender packetSender,
    PlayerManager playerManager,
    SessionManager sessionManager,
    WorldEntityManager worldEntityManager,
    PdaManager pdaManager,
    StoryManager storyManager,
    StoryScheduler storyScheduler,
    EntitySimulation entitySimulation,
    EscapePodManager escapePodManager,
    EntityRegistry entityRegistry,
    SessionSettings sessionSettings,
    IOptions<SubnauticaServerOptions> options,
    ILogger<JoiningManager> logger)
    : ISessionCleaner
{
    private readonly IPacketSender packetSender = packetSender;
    private readonly PlayerManager playerManager = playerManager;
    private readonly SessionManager sessionManager = sessionManager;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly PdaManager pdaManager = pdaManager;
    private readonly StoryManager storyManager = storyManager;
    private readonly StoryScheduler storyScheduler = storyScheduler;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<JoiningManager> logger = logger;
    private readonly EscapePodManager escapePodManager = escapePodManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly SessionSettings sessionSettings = sessionSettings;

    private readonly ThreadSafeQueue<SessionId> joinQueue = new();
    private readonly Lock queueLocker = new(); // Necessary to avoid race conditions between JoinQueueLoop and AddToJoinQueue
    private bool queueActive;
    public Action? SyncFinishedCallback { get; private set; }

    private async Task JoinQueueLoop()
    {
        while (true)
        {
            lock (queueLocker)
            {
                if (joinQueue.Count == 0)
                {
                    queueActive = false;
                    return;
                }
            }

            try
            {
                SessionId sessionId = joinQueue.Dequeue();
                string? name = playerManager.GetPlayerReservation(sessionId)?.PlayerName;
                if (name == null)
                {
                    continue;
                }

                // Do this after dequeuing because everyone's position shifts forward
                SessionId[] array = [.. joinQueue];
                for (int i = 0; i < array.Length; i++)
                {
                    SessionId s = array[i];
                    await packetSender.SendPacketAsync(new JoinQueueInfo(i + 1, options.Value.InitialSyncTimeout), s);
                }

                logger.ZLogInformation($"Starting sync for player {name}");
                await SendInitialSyncAsync(sessionId);

                using CancellationTokenSource source = new(options.Value.InitialSyncTimeout);
                bool syncFinished = false;

                SyncFinishedCallback = () => { syncFinished = true; };

                while (!syncFinished && sessionManager.IsConnected(sessionId) && !source.IsCancellationRequested)
                {
                    await Task.Delay(10, source.Token);
                }

                if (!sessionManager.IsConnected(sessionId))
                {
                    logger.ZLogInformation($"Player {name} disconnected while syncing");
                }
                else if (source.IsCancellationRequested)
                {
                    logger.ZLogInformation($"Initial sync timed out for player {name}");
                    SyncFinishedCallback = null;

                    if (sessionManager.IsConnected(sessionId))
                    {
                        await packetSender.SendPacketAsync(new PlayerKicked("Initial sync took too long and timed out"), sessionId);
                    }
                }
                else
                {
                    logger.ZLogInformation($"Player {name} joined successfully. Remaining requests: {joinQueue.Count}");
                    if (!playerManager.TryGetPlayerBySessionId(sessionId, out Player? player))
                    {
                        throw new Exception($"Failed to get player object for session #{sessionId}");
                    }
                    BroadcastPlayerJoined(player);
                }
            }
            catch (Exception e)
            {
                logger.ZLogInformation($"Unexpected error during player connection inside the join queue: {e}");
            }
        }
    }

    public void AddToJoinQueue(SessionId sessionId)
    {
        // Necessary to avoid race conditions between JoinQueueLoop and AddToJoinQueue
        lock (queueLocker)
        {
            logger.ZLogInformation($"Added player {playerManager.GetPlayerReservation(sessionId)?.PlayerName} to queue");
            joinQueue.Enqueue(sessionId);

            if (queueActive)
            {
                packetSender.SendPacketAsync(new JoinQueueInfo(joinQueue.Count, options.Value.InitialSyncTimeout), sessionId);
            }
            else
            {
                // It may be possible to use the task's status itself for this,
                // but the ContinueWithHandleError callback might cause issues
                queueActive = true;
                Task.Run(JoinQueueLoop).ContinueWithHandleError();
            }
        }
    }

    private async Task SendInitialSyncAsync(SessionId sessionId)
    {
        Player player = playerManager.CreatePlayerData(sessionId, out bool wasBrandNewPlayer);
        (NitroxId assignedEscapePodId, EscapePodEntity? newlyCreatedEscapePod) = await escapePodManager.AssignPlayerToEscapePodAsync(player.PeerId);

        if (wasBrandNewPlayer)
        {
            player.SubRootId = assignedEscapePodId;
        }

        if (newlyCreatedEscapePod is { } validEscapePod)
        {
            SpawnEntities spawnNewEscapePod = new(validEscapePod);
            await packetSender.SendPacketToOthersAsync(spawnNewEscapePod, sessionId);
        }

        // TODO: Remove this code when security of player login is improved by https://github.com/SubnauticaNitrox/Nitrox/issues/1996
        // We need to reset permissions on join, otherwise players can impersonate an admin easily.
        player.Permissions = options.Value.DefaultPlayerPerm;

        // Make players on localhost admin by default.
        if (options.Value.LocalhostIsAdmin && sessionManager.GetEndPoint(sessionId)?.Address.IsLocalhost() == true)
        {
            logger.ZLogInformation($"Granted admin to '{player.Name}' because they're playing on the host machine");
            player.Permissions = Perms.ADMIN;
        }

        // A new player whose partner is mid-intro or past it cannot be paired: his intro is over (his client skips it cleanly and puts him in the lifepod) instead of leaving him at "waiting for a partner"
        IntroCinematicMode introCinematicMode = IntroCinematicRules.ChooseJoinMode(wasBrandNewPlayer, playerManager.GetConnectedPlayersExcept(player).Select(other => other.PlayerContext?.IntroCinematicMode ?? IntroCinematicMode.NONE));
        if (wasBrandNewPlayer && introCinematicMode == IntroCinematicMode.COMPLETED)
        {
            logger.ZLogInformation($"No partner left for the intro of {player.Name}: another player is mid-intro or past it, so it is skipped for {player.Name}");
        }
        if (player.PlayerContext != null)
        {
            player.PlayerContext.IntroCinematicMode = introCinematicMode;
        }

        List<SimulatedEntity> simulations = entitySimulation.AssignGlobalRootEntitiesAndGetData(player);

        player.Entity = wasBrandNewPlayer ? SetupNewPlayerEntity(player, false) : RespawnExistingEntity(player);
        if (!wasBrandNewPlayer && player.Entity != null)
        {
            // A base (BuildEntity) has no TechType: a player who left inside a base must not throw here, it made every rejoin fail (2026-10-24)
            AlignWithCyclops(player, player.Entity, player.Entity.ParentId != null && entityRegistry.TryGetEntityById(player.Entity.ParentId, out WorldEntity parent) && PlayerParentRules.IsCyclops(parent));
            PlaceAtEscapePodIfNeeded(player, player.Entity, assignedEscapePodId);
        }

        List<GlobalRootEntity> globalRootEntities = worldEntityManager.GetGlobalRootEntities(true);
        bool isFirstPlayer = playerManager.GetConnectedPlayers().Count == 1;

        InitialPlayerSync initialPlayerSync = new(
            player.GameObjectId,
            wasBrandNewPlayer,
            assignedEscapePodId,
            player.EquippedItems,
            player.UsedItems,
            player.QuickSlotsBindingIds,
            pdaManager.GetInitialPDAData(),
            storyManager.GetInitialStoryGoalData(storyScheduler, player),
            player.Position,
            player.Rotation,
            player.SubRootId,
            player.Stats,
            GetOtherPlayers(player),
            globalRootEntities,
            simulations,
            player.GameMode,
            player.Permissions,
            introCinematicMode,
            new(new(player.PingInstancePreferences), player.PinnedRecipePreferences.ToList()),
            storyManager.GetTimeData(),
            isFirstPlayer,
            BuildingManager.GetEntitiesOperations(globalRootEntities),
            options.Value.KeepInventoryOnDeath,
            BuildSessionSettingsAndReport(),
            player.InPrecursor,
            player.DisplaySurfaceWater,
            options.Value.MarkDeathPointsWithBeacon
        );

        await packetSender.SendPacketAsync(initialPlayerSync, player.SessionId);

        IEnumerable<PlayerContext> GetOtherPlayers(Player player)
        {
            return playerManager.GetConnectedPlayers().Where(p => p != player).Select(p => p.PlayerContext);
        }

        PlayerEntity SetupNewPlayerEntity(Player player, bool hasPose)
        {
            WorldEntity? parent = null;
            if (player.SubRootId.HasValue && entityRegistry.TryGetEntityById(player.SubRootId.Value, out WorldEntity foundParent))
            {
                parent = foundParent;
            }

            PlayerEntity playerEntity = CreatePlayerEntity(player, parent, hasPose);
            entityRegistry.AddOrUpdate(playerEntity);
            worldEntityManager.TrackEntityInTheWorld(playerEntity);
            return playerEntity;
        }

        PlayerEntity RespawnExistingEntity(Player player)
        {
            if (entityRegistry.TryGetEntityById(player.PlayerContext.PlayerNitroxId, out PlayerEntity playerWorldEntity))
            {
                return playerWorldEntity;
            }
            logger.ZLogError($"Unable to find player entity for {player.Name}. Re-creating one");
            return SetupNewPlayerEntity(player, true);
        }
    }

    /// <summary>
    ///     The pose of an entity with a parent is kept relative to it: <c>PlayerMovementProcessor</c> assigns the world pose through the setters of <see cref="NitroxTransform" />,
    ///     which convert into the space of its <see cref="NitroxTransform.Parent" />, and clients read the stored pose back as local (<c>RemoteBodySpawnPose</c>). So an entity created
    ///     with a parent id needs that parent on its transform from the start: the clients only report a lifepod or base once the player leaves or enters one
    ///     (<see cref="EntityRegistry.ReparentEntity(Entity?, Entity?)" />), and not during the initial sync.
    /// </summary>
    /// <param name="parent">The entity <see cref="Player.SubRootId" /> names, if the server has it.</param>
    /// <param name="hasPose">
    ///     False for a player who joins for the first time: <see cref="Player.Position" /> is only the default (0, 0, 0), not a place. The entity then starts at the origin of its parent (the inside of the
    ///     lifepod), not at the origin of the world, until the first movement packet.
    /// </param>
    internal static PlayerEntity CreatePlayerEntity(Player player, WorldEntity? parent, bool hasPose)
    {
        NitroxTransform transform = new(player.Position, player.Rotation, NitroxVector3.One);
        if (parent?.Transform != null)
        {
            transform.SetParent(parent.Transform, hasPose);
        }

        return new PlayerEntity(transform, 0, null, false, player.GameObjectId, NitroxTechType.None, null, player.SubRootId.OrNull(), []);
    }

    /// <summary>
    ///     The settings a joining player is told: what is only for this session is the server's own state (<paramref name="session" />), the option about the lockers is the configuration
    ///     (<paramref name="options" />). A copy, so that the server's own settings are not shared with the packet.
    /// </summary>
    internal static SessionSettings BuildSessionSettings(SessionSettings session, SubnauticaServerOptions options, ICollection<string>? messages = null)
    {
        SessionSettings told = new() { FastHatch = session.FastHatch, FastGrow = session.FastGrow, CraftFromLockers = options.CraftFromLockers };
        // The vehicle upgrade options, every number inside its limits (a value outside is clamped and the sentence goes to the caller)
        VehicleUpgradeSettingsRules.Apply(told, options, messages);
        return told;
    }

    private static readonly HashSet<string> reportedOptionProblems = [];

    private SessionSettings BuildSessionSettingsAndReport()
    {
        List<string> messages = [];
        SessionSettings told = BuildSessionSettings(sessionSettings, options.Value, messages);
        foreach (string message in messages)
        {
            // Once per problem and server run, not once per joining player
            bool first;
            lock (reportedOptionProblems)
            {
                first = reportedOptionProblems.Add(message);
            }
            if (first)
            {
                logger.ZLogWarning($"{message}");
            }
        }
        return told;
    }

    /// <summary>
    ///     A player who left while inside a Cyclops that was moved afterwards (another player drove it, or the server kept running): <see cref="Player.Position" /> is the world position
    ///     of his last packet, but his entity keeps the position inside the Cyclops, which follows the Cyclops when it moves. The client puts the player at the position it gets and then
    ///     attaches him to the Cyclops at the local position of that point: with the stale one he was hundreds of meters outside the hull and was thrown out of the sub (V0, PO1).
    ///     The pose of the entity composed with its parent is where he is.
    /// </summary>
    internal static void AlignWithCyclops(Player player, PlayerEntity entity, bool parentIsCyclops)
    {
        if (!parentIsCyclops || entity.Transform.Parent == null)
        {
            return;
        }

        player.Position = entity.Transform.Position;
        player.Rotation = entity.Transform.Rotation;
    }

    /// <summary>
    ///     A player who left before his intro was over is saved at a pose of the cinematic (the seat, the animated pod), which is no place to stand at: the pod flag of the game then puts him in "pod mode"
    ///     (he walks, he does not swim) at a place that is open water, and he falls to the sea floor and walks there "inside a base" (2026-10-23, the second player of a new world, rejoining during the
    ///     intro). One who is saved in his lifepod at a position that is not in it (a server that was restarted forgot the flag) is put back the same way.
    /// </summary>
    private void PlaceAtEscapePodIfNeeded(Player player, PlayerEntity entity, NitroxId escapePodId)
    {
        bool introUnfinished = player.IntroUnfinished;
        player.IntroUnfinished = false;

        if (!entityRegistry.TryGetEntityById(escapePodId, out EscapePodEntity pod) || pod.Transform == null)
        {
            return;
        }

        bool isInEscapePod = player.SubRootId.HasValue && player.SubRootId.Value.Equals(escapePodId);
        if (!IntroCinematicRules.ShouldPlaceAtEscapePod(introUnfinished, isInEscapePod, player.Position, pod.Transform.Position))
        {
            return;
        }

        string reason = introUnfinished ? "he left during his intro" : "that is not in his lifepod";
        logger.ZLogInformation($"{player.Name} joins at {player.Position} ({reason}): putting him at his lifepod {escapePodId} at {pod.Transform.Position}");
        if (entity.ParentId == null || !entity.ParentId.Equals(pod.Id))
        {
            entityRegistry.ReparentEntity(entity, pod);
        }
        PlaceAtEscapePod(player, entity, pod);
    }

    /// <summary>
    ///     Puts the player at the spawn of the lifepod: the position he is sent in his initial sync, his entity (the pose that other players see until his first movement packet) and the lifepod as his parent.
    /// </summary>
    internal static void PlaceAtEscapePod(Player player, PlayerEntity entity, EscapePodEntity pod)
    {
        player.Position = IntroCinematicRules.GetEscapePodSpawn(pod.Transform.Position);
        player.Rotation = NitroxQuaternion.Identity;
        player.SubRootId = pod.Id;

        if (entity.Transform.Parent != pod.Transform)
        {
            entity.Transform.SetParent(pod.Transform, false);
        }
        entity.Transform.LocalPosition = IntroCinematicRules.EscapePodSpawnOffset;
        entity.Transform.LocalRotation = NitroxQuaternion.Identity;
    }

    private void BroadcastPlayerJoined(Player player)
    {
        PlayerJoinedMultiplayerSession playerJoinedPacket = new(player.PlayerContext, player.SubRootId, player.Entity);
        packetSender.SendPacketToOthersAsync(playerJoinedPacket, player.SessionId);
    }

    public Task OnEventAsync(ISessionCleaner.Args args)
    {
        // They may have been queued, so just erase their entry
        joinQueue.RemoveWhere(sessionId => Equals(sessionId, args.Session.Id));
        return Task.CompletedTask;
    }
}
