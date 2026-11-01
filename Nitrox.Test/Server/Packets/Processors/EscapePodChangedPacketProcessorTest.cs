using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NSubstitute;
using static Nitrox.Server.Subnautica.Models.Packets.Processors.PlayerParentScene;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

[TestClass]
public class EscapePodChangedPacketProcessorTest
{
    [TestMethod]
    public async Task Process_NoLifepodWhileInLifepod_ClearsParent()
    {
        PlayerParentScene scene = new(PlayerStart.IN_POD);
        EscapePodChangedPacketProcessor processor = new(scene.EntityRegistry);

        await processor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().BeNull();
        scene.Pod.ChildEntities.Should().NotContain(scene.PlayerEntity);
        scene.Player.SubRootId.HasValue.Should().BeFalse();
    }

    [TestMethod]
    public async Task Process_NoLifepodWhileInSubRoot_KeepsSubRootParent()
    {
        // A stale lifepod (the player died in it and respawned in a base) is dropped by the client once the player is far from it, with the player already in the base
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        EscapePodChangedPacketProcessor processor = new(scene.EntityRegistry);

        await processor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().Be(scene.SubRoot.Id);
        scene.SubRoot.ChildEntities.Should().Contain(scene.PlayerEntity);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.SubRoot.Id);
    }

    [TestMethod]
    public async Task Process_NoLifepodWhileInSubRoot_StillTellsTheOthers()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        EscapePodChangedPacketProcessor processor = new(scene.EntityRegistry);
        EscapePodChanged packet = new(scene.Player.SessionId, Optional.Empty);

        await processor.Process(scene.Context, packet);

        await scene.PacketSender.Received(1).SendPacketToOthersAsync(packet, scene.Player.SessionId);
    }

    [TestMethod]
    public async Task Process_NoLifepodWhileInOpenWater_StaysInOpenWater()
    {
        PlayerParentScene scene = new(PlayerStart.IN_OPEN_WATER);
        EscapePodChangedPacketProcessor processor = new(scene.EntityRegistry);

        await processor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().BeNull();
        scene.Player.SubRootId.HasValue.Should().BeFalse();
    }

    [TestMethod]
    public async Task Process_LifepodWhileInOpenWater_MovesToTheLifepod()
    {
        PlayerParentScene scene = new(PlayerStart.IN_OPEN_WATER);
        EscapePodChangedPacketProcessor processor = new(scene.EntityRegistry);

        await processor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, scene.Pod.Id));

        scene.PlayerEntity.ParentId.Should().Be(scene.Pod.Id);
        scene.Pod.ChildEntities.Should().Contain(scene.PlayerEntity);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.Pod.Id);
    }

    [TestMethod]
    public async Task Process_LifepodWhileInSubRoot_MovesToTheLifepod()
    {
        // Entering the lifepod's trigger is a real change of place whatever the player stood in before
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        EscapePodChangedPacketProcessor processor = new(scene.EntityRegistry);

        await processor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, scene.Pod.Id));

        scene.PlayerEntity.ParentId.Should().Be(scene.Pod.Id);
        scene.SubRoot.ChildEntities.Should().NotContain(scene.PlayerEntity);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.Pod.Id);
    }
}
