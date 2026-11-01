using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Bases;

namespace NitroxClient.GameLogic;

[TestClass]
public class MapRoomIdRulesTest
{
    private const string GHOST_ID = "6f1e2d3c-4b5a-4968-8776-655443322110";

    [TestMethod]
    public void ARoomBuiltIntoAnExistingBaseKeepsTheIdOfItsGhost()
    {
        NitroxId ghostId = new(GHOST_ID);

        MapRoomIdRules.GetMapRoomId(ghostId, true).Should().Be(ghostId);
    }

    [TestMethod]
    public void AFirstPieceRoomGetsTheNextIdBecauseTheNewBaseTakesTheGhostId()
    {
        NitroxId ghostId = new(GHOST_ID);

        NitroxId roomId = MapRoomIdRules.GetMapRoomId(ghostId, false);

        // The new Base is named with the id of the ghost (BuildingHandler.BuildBase), so the room must not take the same one
        roomId.Should().NotBe(ghostId);
        roomId.Should().Be(ghostId.Increment());
    }

    [TestMethod]
    public void EveryClientGetsTheSameIdFromTheSameGhost()
    {
        // The builder and the receiving clients each ask for the id with the ghost id they know: the same ghost must give the same room id
        NitroxId onTheBuilder = new(GHOST_ID);
        NitroxId onTheReceiver = new(GHOST_ID);

        MapRoomIdRules.GetMapRoomId(onTheBuilder, false).Should().Be(MapRoomIdRules.GetMapRoomId(onTheReceiver, false));
        MapRoomIdRules.GetMapRoomId(onTheBuilder, true).Should().Be(MapRoomIdRules.GetMapRoomId(onTheReceiver, true));
    }
}
