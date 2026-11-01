using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using NSubstitute;
using static Nitrox.Server.Subnautica.Models.Packets.Processors.PlayerParentScene;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

[TestClass]
public class SubRootChangedPacketProcessorTest
{
    [TestMethod]
    public async Task Process_NoSubRootWhileInLifepod_KeepsLifepodParent()
    {
        // A lifepod respawn ends with SetCurrentSub(null): the lifepod is no sub root, so this does not mean open water
        PlayerParentScene scene = new(PlayerStart.IN_POD);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().Be(scene.Pod.Id);
        scene.Pod.ChildEntities.Should().Contain(scene.PlayerEntity);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.Pod.Id);
    }

    [TestMethod]
    public async Task Process_LeavingTheCyclops_EndsTheGunnersSeatAndTellsEverybody()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        CyclopsGunnerArbiter arbiter = new(scene.PacketSender);
        arbiter.TrySit(scene.SubRoot.Id, scene.Player.SessionId, scene.Player.Name, out _);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, arbiter, new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        arbiter.Count.Should().Be(0);
        await scene.PacketSender.Received(1).SendPacketToAllAsync(Arg.Is<CyclopsGunnerSeat>(seat => seat.Message == CyclopsGunnerSeatMessage.Released && seat.Reason == CyclopsGunnerSeatReason.LeftCyclops && seat.SessionId == scene.Player.SessionId));
    }

    [TestMethod]
    public async Task Process_ChangesWithinTheSameCyclops_KeepTheGunnersSeat()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        CyclopsGunnerArbiter arbiter = new(scene.PacketSender);
        arbiter.TrySit(scene.SubRoot.Id, scene.Player.SessionId, scene.Player.Name, out _);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, arbiter, new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Of(scene.SubRoot.Id)));

        arbiter.Count.Should().Be(1);
        await scene.PacketSender.DidNotReceive().SendPacketToAllAsync(Arg.Any<CyclopsGunnerSeat>());
    }

    [TestMethod]
    public async Task Process_NoSubRootWhileInLifepod_StillTellsTheOthers()
    {
        PlayerParentScene scene = new(PlayerStart.IN_POD);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));
        SubRootChanged packet = new(scene.Player.SessionId, Optional.Empty);

        await processor.Process(scene.Context, packet);

        await scene.PacketSender.Received(1).SendPacketToOthersAsync(packet, scene.Player.SessionId);
    }

    [TestMethod]
    public async Task Process_NoSubRootWhileInSubRoot_ClearsParent()
    {
        PlayerParentScene scene = new(PlayerStart.IN_SUB);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().BeNull();
        scene.SubRoot.ChildEntities.Should().NotContain(scene.PlayerEntity);
        scene.Player.SubRootId.HasValue.Should().BeFalse();
    }

    [TestMethod]
    public async Task Process_NoSubRootWhileInOpenWater_StaysInOpenWater()
    {
        PlayerParentScene scene = new(PlayerStart.IN_OPEN_WATER);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().BeNull();
        scene.Player.SubRootId.HasValue.Should().BeFalse();
    }

    [TestMethod]
    public async Task Process_SubRootWhileInLifepod_MovesToTheSubRoot()
    {
        PlayerParentScene scene = new(PlayerStart.IN_POD);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, scene.SubRoot.Id));

        scene.PlayerEntity.ParentId.Should().Be(scene.SubRoot.Id);
        scene.Pod.ChildEntities.Should().NotContain(scene.PlayerEntity);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.SubRoot.Id);
    }

    [TestMethod]
    public async Task Process_SubRootWhileInOpenWater_MovesToTheSubRoot()
    {
        PlayerParentScene scene = new(PlayerStart.IN_OPEN_WATER);
        SubRootChangedPacketProcessor processor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await processor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, scene.SubRoot.Id));

        scene.PlayerEntity.ParentId.Should().Be(scene.SubRoot.Id);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.SubRoot.Id);
    }

    [TestMethod]
    public async Task Process_LifepodRespawnPackets_LeaveThePlayerInTheLifepod()
    {
        // What the client sends for a respawn in the lifepod, in the order it sends it: EscapePod.RespawnPlayer, set_currentEscapePod, and (before PL6) the postfix of MovePlayerToRespawnPoint
        PlayerParentScene scene = new(PlayerStart.IN_OPEN_WATER);
        EscapePodChangedPacketProcessor podProcessor = new(scene.EntityRegistry);
        SubRootChangedPacketProcessor subRootProcessor = new(scene.EntityRegistry, new CyclopsGunnerArbiter(scene.PacketSender), new CyclopsEngineerArbiter(scene.PacketSender), new CyclopsDroneArbiter(scene.PacketSender));

        await podProcessor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, scene.Pod.Id));
        await podProcessor.Process(scene.Context, new EscapePodChanged(scene.Player.SessionId, scene.Pod.Id));
        await subRootProcessor.Process(scene.Context, new SubRootChanged(scene.Player.SessionId, Optional.Empty));

        scene.PlayerEntity.ParentId.Should().Be(scene.Pod.Id);
        scene.Pod.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(scene.PlayerEntity);
        scene.Player.SubRootId.Should().Be((Optional<NitroxId>)scene.Pod.Id);
    }
}
