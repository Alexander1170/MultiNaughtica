using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     Where the server puts a player who left before his intro was over when he joins again (bug1-intro-join): the pose it saved is one of the cinematic, which is no place to stand at, and with the
///     lifepod as his parent the game makes him walk there, in the open water. He is put at the spawn of his lifepod instead.
/// </summary>
[TestClass]
public class JoiningManagerIntroPoseTest
{
    private static readonly NitroxVector3 podPosition = new(-60f, 0f, 100f);

    private static Player CreatePlayer(NitroxVector3 position, Optional<NitroxId> subRootId)
    {
        return new Player(1, 1, "Namieen", false, null, position, NitroxQuaternion.FromEuler(0f, 90f, 0f), new NitroxId(), subRootId, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private static EscapePodEntity CreatePod()
    {
        return new EscapePodEntity(podPosition, new NitroxId(), new EscapePodMetadata(false, false));
    }

    private static PlayerEntity CreateEntity(EscapePodEntity pod, NitroxVector3 localPosition)
    {
        NitroxTransform transform = new(localPosition, NitroxQuaternion.Identity, NitroxVector3.One);
        transform.SetParent(pod.Transform, false);
        return new PlayerEntity(transform, 0, null, false, new NitroxId(), NitroxTechType.None, null, pod.Id, []);
    }

    [TestMethod]
    public void APlayerWhoLeftDuringTheIntro_IsPutAtTheSpawnOfHisLifepod()
    {
        EscapePodEntity pod = CreatePod();
        // The pose of the cinematic: 40 m under the lifepod, as the second player of the first live run was found
        PlayerEntity entity = CreateEntity(pod, new NitroxVector3(3f, -40f, 2f));
        Player player = CreatePlayer(podPosition + new NitroxVector3(3f, -40f, 2f), pod.Id);

        IntroCinematicRules.ShouldPlaceAtEscapePod(true, true, player.Position, pod.Transform.Position).Should().BeTrue();
        JoiningManager.PlaceAtEscapePod(player, entity, pod);

        NitroxVector3 spawn = podPosition + new NitroxVector3(0.9f, 2.1f, 0f);
        player.Position.Equals(spawn, 0.001f).Should().BeTrue($"was {player.Position}");
        player.Rotation.Should().Be(NitroxQuaternion.Identity);
        player.SubRootId.Should().Be(Optional.Of(pod.Id));
        // The pose that other players see until his first movement packet
        entity.Transform.Position.Equals(spawn, 0.001f).Should().BeTrue($"was {entity.Transform.Position}");
        entity.Transform.LocalPosition.Equals(new NitroxVector3(0.9f, 2.1f, 0f), 0.001f).Should().BeTrue($"was {entity.Transform.LocalPosition}");
        entity.Transform.Parent.Should().BeSameAs(pod.Transform);
        IntroCinematicRules.IsPoseInEscapePod(player.Position, pod.Transform.Position).Should().BeTrue();
    }

    [TestMethod]
    public void TheNextMovementPacketStillMovesTheEntityRelativeToTheLifepod()
    {
        EscapePodEntity pod = CreatePod();
        PlayerEntity entity = CreateEntity(pod, new NitroxVector3(3f, -40f, 2f));
        Player player = CreatePlayer(NitroxVector3.Zero, Optional.Empty);

        JoiningManager.PlaceAtEscapePod(player, entity, pod);
        // What PlayerMovementProcessor does with the world position of the next packet
        entity.Transform.Position = podPosition + new NitroxVector3(1f, 2f, 0.5f);

        entity.Transform.LocalPosition.Equals(new NitroxVector3(1f, 2f, 0.5f), 0.001f).Should().BeTrue($"was {entity.Transform.LocalPosition}");
    }

    [TestMethod]
    public void AnEntityWhoseParentTransformWasLost_GetsTheLifepodAsParentAgain()
    {
        // After a restart of the server the entity is read from the save: it has the parent id but no parent transform
        EscapePodEntity pod = CreatePod();
        NitroxTransform transform = new(new NitroxVector3(5f, -30f, 0f), NitroxQuaternion.Identity, NitroxVector3.One);
        PlayerEntity entity = new(transform, 0, null, false, new NitroxId(), NitroxTechType.None, null, pod.Id, []);
        Player player = CreatePlayer(new NitroxVector3(5f, -30f, 0f), pod.Id);

        JoiningManager.PlaceAtEscapePod(player, entity, pod);

        entity.Transform.Parent.Should().BeSameAs(pod.Transform);
        entity.Transform.Position.Equals(podPosition + new NitroxVector3(0.9f, 2.1f, 0f), 0.001f).Should().BeTrue($"was {entity.Transform.Position}");
    }
}
