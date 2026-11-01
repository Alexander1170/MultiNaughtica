using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
/// What the server keeps as the pose of a <see cref="PlayerEntity" />, which the client reads when it spawns the body of a remote player (<c>RemoteBodySpawnPose</c>): parent-relative while the
/// entity has a parent, world space while it has none. That holds from the creation of the entity (a player who joins in a lifepod) to the removal of the parent (a base or Cyclops destroyed
/// around the player), not only after a reparent by the packet processors.
/// </summary>
[TestClass]
public class PlayerEntityPoseStorageTest
{
    private const float TOLERANCE = 0.001f;

    // A base that is neither at the origin nor unrotated: the case in which a world pose stored as if it were local (#2895) would show
    private static readonly NitroxVector3 baseWorldPosition = new(100f, -20f, 300f);
    private static readonly NitroxQuaternion baseWorldRotation = NitroxQuaternion.FromEuler(0f, 90f, 0f);
    private static readonly NitroxVector3 localPosition = new(2f, 1f, 3f);
    private static readonly NitroxQuaternion localRotation = NitroxQuaternion.FromEuler(0f, 30f, 0f);

    private EntityRegistry entityRegistry;
    private GlobalRootEntity parent;
    private PlayerEntity playerEntity;
    private Player player;
    private AuthProcessorContext context;

    [TestInitialize]
    public void Init()
    {
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        parent = new GlobalRootEntity(new NitroxTransform(baseWorldPosition, baseWorldRotation, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, null, false, new NitroxId(), NitroxTechType.None, null, null, []);
        NitroxId playerId = new();
        playerEntity = new PlayerEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, null, false, playerId, NitroxTechType.None, null, null, []);
        entityRegistry.AddOrUpdate(parent);
        entityRegistry.AddOrUpdate(playerEntity);

        PlayerContext playerContext = new("Alice", 1, playerId, false, null, false, SubnauticaGameMode.SURVIVAL, null, IntroCinematicMode.COMPLETED, null);
        player = new Player(1, 1, "Alice", false, playerContext, NitroxVector3.Zero, NitroxQuaternion.Identity, playerId, Optional.Empty, Perms.PLAYER,
                            new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                            new Dictionary<string, PingInstancePreference>(), [], false, true);
        context = new AuthProcessorContext(player, new IgnoringPacketSender());
    }

    [TestMethod]
    public async Task WorldMovementInsideAParent_IsStoredRelativeToTheParent()
    {
        await EnterParent();

        // The two assignments of PlayerMovementProcessor: the packet carries the world pose of the player (PL2)
        playerEntity.Transform.Position = ToWorld(localPosition);
        playerEntity.Transform.Rotation = baseWorldRotation * localRotation;

        playerEntity.ParentId.Should().Be(parent.Id);
        playerEntity.Transform.LocalPosition.Equals(localPosition, TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalPosition}");
        playerEntity.Transform.LocalRotation.Equals(localRotation, TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalRotation}");
    }

    [TestMethod]
    public async Task CyclopsMovement_IsStoredAsSent()
    {
        await EnterParent();

        PlayerInCyclopsMovement movement = new(player.SessionId, localPosition, NitroxVector3.Zero, localRotation, 1f);
        await new PlayerInCyclopsMovementProcessor(entityRegistry, NullLogger<PlayerInCyclopsMovementProcessor>.Instance).Process(context, movement);

        // The pose of the pawn is in the space of the Cyclops already: the entity keeps it as it is, whatever the Cyclops does
        playerEntity.Transform.LocalPosition.Equals(localPosition, TOLERANCE).Should().BeTrue();
        playerEntity.Transform.LocalRotation.Equals(localRotation, TOLERANCE).Should().BeTrue();
        // and the world pose the server derives from it (the sender's position, used for simulation and warps) is the one the client's TransformPoint gives
        player.Position.Equals(ToWorld(localPosition), TOLERANCE).Should().BeTrue($"was {player.Position}");
    }

    [TestMethod]
    public async Task LeavingTheParent_TheStoredPoseIsTheWorldPose()
    {
        await EnterParent();
        playerEntity.Transform.Position = ToWorld(localPosition);
        playerEntity.Transform.Rotation = baseWorldRotation * localRotation;

        await new SubRootChangedPacketProcessor(entityRegistry, new CyclopsGunnerArbiter(new IgnoringPacketSender()), new CyclopsEngineerArbiter(new IgnoringPacketSender()), new CyclopsDroneArbiter(new IgnoringPacketSender())).Process(context, new SubRootChanged(player.SessionId, Optional.Empty));

        // Without a parent the client takes the stored pose for a world pose
        playerEntity.ParentId.Should().BeNull();
        playerEntity.Transform.LocalPosition.Equals(ToWorld(localPosition), TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalPosition}");
        playerEntity.Transform.LocalRotation.Equals(baseWorldRotation * localRotation, TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalRotation}");
    }

    [TestMethod]
    public void NewPlayerInALifepod_StartsAtTheOriginOfTheLifepod()
    {
        // A player who joins for the first time: the position of the Player is only the default and the lifepod is assigned before the entity is created (JoiningManager)
        player.SubRootId = Optional.Of(parent.Id);

        PlayerEntity created = JoiningManager.CreatePlayerEntity(player, parent, false);

        created.ParentId.Should().Be(parent.Id);
        created.Transform.Parent.Should().BeSameAs(parent.Transform);
        created.Transform.LocalPosition.Equals(NitroxVector3.Zero, TOLERANCE).Should().BeTrue($"was {created.Transform.LocalPosition}");
        created.Transform.Position.Equals(baseWorldPosition, TOLERANCE).Should().BeTrue($"was {created.Transform.Position}");
    }

    [TestMethod]
    public void NewPlayerInALifepod_FirstWorldMovementIsStoredRelativeToTheLifepod()
    {
        player.SubRootId = Optional.Of(parent.Id);
        playerEntity = JoiningManager.CreatePlayerEntity(player, parent, false);
        entityRegistry.AddOrUpdate(playerEntity);

        // Nothing reparents the entity in between: the client reports the lifepod only when the player leaves it (Player_SetCurrentEscapePod_Patch is silent during the initial sync)
        playerEntity.Transform.Position = ToWorld(localPosition);
        playerEntity.Transform.Rotation = baseWorldRotation * localRotation;

        playerEntity.ParentId.Should().Be(parent.Id);
        playerEntity.Transform.LocalPosition.Equals(localPosition, TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalPosition}");
        playerEntity.Transform.LocalRotation.Equals(localRotation, TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalRotation}");
    }

    [TestMethod]
    public async Task NewPlayerInALifepod_LeavingItStoresTheWorldPose()
    {
        player.SubRootId = Optional.Of(parent.Id);
        playerEntity = JoiningManager.CreatePlayerEntity(player, parent, false);
        entityRegistry.AddOrUpdate(playerEntity);
        playerEntity.Transform.Position = ToWorld(localPosition);
        playerEntity.Transform.Rotation = baseWorldRotation * localRotation;

        await new SubRootChangedPacketProcessor(entityRegistry, new CyclopsGunnerArbiter(new IgnoringPacketSender()), new CyclopsEngineerArbiter(new IgnoringPacketSender()), new CyclopsDroneArbiter(new IgnoringPacketSender())).Process(context, new SubRootChanged(player.SessionId, Optional.Empty));

        playerEntity.ParentId.Should().BeNull();
        playerEntity.Transform.Parent.Should().BeNull();
        playerEntity.Transform.LocalPosition.Equals(ToWorld(localPosition), TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalPosition}");
        playerEntity.Transform.LocalRotation.Equals(baseWorldRotation * localRotation, TOLERANCE).Should().BeTrue($"was {playerEntity.Transform.LocalRotation}");
    }

    [TestMethod]
    public void RecreatedEntityOfAPlayerWithAKnownPose_KeepsThatWorldPoseUnderItsParent()
    {
        // The fallback of RespawnExistingEntity: the position of the Player is the last world pose it reported
        player.Position = ToWorld(localPosition);
        player.Rotation = baseWorldRotation * localRotation;
        player.SubRootId = Optional.Of(parent.Id);

        PlayerEntity created = JoiningManager.CreatePlayerEntity(player, parent, true);

        created.Transform.LocalPosition.Equals(localPosition, TOLERANCE).Should().BeTrue($"was {created.Transform.LocalPosition}");
        created.Transform.LocalRotation.Equals(localRotation, TOLERANCE).Should().BeTrue($"was {created.Transform.LocalRotation}");
        created.Transform.Position.Equals(player.Position, TOLERANCE).Should().BeTrue($"was {created.Transform.Position}");
    }

    [TestMethod]
    public void EntityOfAPlayerWithoutAParent_KeepsTheWorldPose()
    {
        player.Position = new NitroxVector3(5f, -10f, 20f);

        PlayerEntity created = JoiningManager.CreatePlayerEntity(player, null, true);

        created.ParentId.Should().BeNull();
        created.Transform.Parent.Should().BeNull();
        created.Transform.LocalPosition.Equals(player.Position, TOLERANCE).Should().BeTrue($"was {created.Transform.LocalPosition}");
    }

    [TestMethod]
    public async Task ParentRemovedWithThePlayerInside_TheStoredPoseBecomesTheWorldPose()
    {
        await EnterParent();
        playerEntity.Transform.Position = ToWorld(localPosition);
        playerEntity.Transform.Rotation = baseWorldRotation * localRotation;
        WorldEntityManager manager = new(new IgnoringPacketSender(), entityRegistry, null, null, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);

        // A base or a Cyclops destroyed around the player: the entity is kept on top of GlobalRoot
        manager.RemoveGlobalRootEntity(parent.Id);

        entityRegistry.TryGetEntityById(playerEntity.Id, out PlayerEntity kept).Should().BeTrue();
        kept.ParentId.Should().BeNull();
        kept.Transform.Parent.Should().BeNull();
        kept.Transform.LocalPosition.Equals(ToWorld(localPosition), TOLERANCE).Should().BeTrue($"was {kept.Transform.LocalPosition}");
        kept.Transform.LocalRotation.Equals(baseWorldRotation * localRotation, TOLERANCE).Should().BeTrue($"was {kept.Transform.LocalRotation}");
    }

    private async Task EnterParent()
    {
        await new SubRootChangedPacketProcessor(entityRegistry, new CyclopsGunnerArbiter(new IgnoringPacketSender()), new CyclopsEngineerArbiter(new IgnoringPacketSender()), new CyclopsDroneArbiter(new IgnoringPacketSender())).Process(context, new SubRootChanged(player.SessionId, Optional.Of(parent.Id)));
    }

    /// <summary>
    /// What the client does with a pose relative to the parent: <c>parent.TransformPoint(local)</c>, a rotation of the parent and its position (scale 1).
    /// </summary>
    private static NitroxVector3 ToWorld(NitroxVector3 local)
    {
        Vector3 world = Vector3.Transform(new Vector3(local.X, local.Y, local.Z), (Quaternion)baseWorldRotation) + new Vector3(baseWorldPosition.X, baseWorldPosition.Y, baseWorldPosition.Z);
        return new NitroxVector3(world.X, world.Y, world.Z);
    }

    private sealed class IgnoringPacketSender : IPacketSender
    {
        public ValueTask SendPacketAsync<T>(T packet, SessionId sessionId) where T : Packet => ValueTask.CompletedTask;

        public ValueTask SendPacketToAllAsync<T>(T packet) where T : Packet => ValueTask.CompletedTask;

        public ValueTask SendPacketToOthersAsync<T>(T packet, SessionId excludedSessionId) where T : Packet => ValueTask.CompletedTask;
    }
}
