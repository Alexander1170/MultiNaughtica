using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

[TestClass]
public class JoiningManagerCyclopsPoseTest
{
    private static Player CreatePlayer(NitroxVector3 position)
    {
        return new Player(1, 1, "Alice", false, null, position, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }

    private static PlayerEntity CreateEntityInside(NitroxTransform cyclops, NitroxVector3 localPosition)
    {
        NitroxTransform transform = new(localPosition, NitroxQuaternion.Identity, NitroxVector3.One);
        transform.SetParent(cyclops, false);
        return new PlayerEntity(transform, 0, null, false, new NitroxId(), NitroxTechType.None, null, new NitroxId(), []);
    }

    [TestMethod]
    public void APlayerWhoLeftInACyclopsThatMovedIsPutWhereTheCyclopsIsNow()
    {
        // His last packet put him at (10, 0, 0) while the Cyclops was at the origin; the Cyclops has been driven to (500, -20, 300) since
        NitroxTransform cyclops = new(new NitroxVector3(500, -20, 300), NitroxQuaternion.Identity, NitroxVector3.One);
        PlayerEntity entity = CreateEntityInside(cyclops, new NitroxVector3(10, 0, 0));
        Player player = CreatePlayer(new NitroxVector3(10, 0, 0));

        JoiningManager.AlignWithCyclops(player, entity, parentIsCyclops: true);

        player.Position.Should().Be(new NitroxVector3(510, -20, 300));
    }

    [TestMethod]
    public void AParentThatIsNoCyclopsLeavesThePositionAlone()
    {
        // A base or a lifepod: the pose round-trips to the same value, nothing to gain
        NitroxTransform parent = new(new NitroxVector3(500, 0, 0), NitroxQuaternion.Identity, NitroxVector3.One);
        PlayerEntity entity = CreateEntityInside(parent, new NitroxVector3(10, 0, 0));
        Player player = CreatePlayer(new NitroxVector3(7, 7, 7));

        JoiningManager.AlignWithCyclops(player, entity, parentIsCyclops: false);

        player.Position.Should().Be(new NitroxVector3(7, 7, 7));
    }

    [TestMethod]
    public void AnEntityWithoutAParentTransformLeavesThePositionAlone()
    {
        PlayerEntity entity = new(new NitroxTransform(new NitroxVector3(1, 2, 3), NitroxQuaternion.Identity, NitroxVector3.One), 0, null, false, new NitroxId(), NitroxTechType.None, null, null, []);
        Player player = CreatePlayer(new NitroxVector3(7, 7, 7));

        JoiningManager.AlignWithCyclops(player, entity, parentIsCyclops: true);

        player.Position.Should().Be(new NitroxVector3(7, 7, 7));
    }
}
