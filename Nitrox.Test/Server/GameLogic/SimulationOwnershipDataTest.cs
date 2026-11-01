using System.Collections.Generic;
using System.Threading;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

[TestClass]
public class SimulationOwnershipDataTest
{
    [TestMethod]
    public void WasOwnerWithin_AfterRevokeIfOwner_ReturnsTrueForThePreviousOwnerOnly()
    {
        SimulationOwnershipData data = new();
        NitroxId id = new();
        Player previousOwner = CreatePlayer(1);
        Player other = CreatePlayer(2);
        data.TryToAcquire(id, previousOwner, SimulationLockType.TRANSIENT);

        data.RevokeIfOwner(id, previousOwner).Should().BeTrue();

        data.WasOwnerWithin(id, previousOwner.SessionId, 1500).Should().BeTrue();
        data.WasOwnerWithin(id, other.SessionId, 1500).Should().BeFalse();
    }

    [TestMethod]
    public void WasOwnerWithin_AfterRevokeOwnerOfId_ReturnsTrue()
    {
        SimulationOwnershipData data = new();
        NitroxId id = new();
        Player previousOwner = CreatePlayer(1);
        data.TryToAcquire(id, previousOwner, SimulationLockType.TRANSIENT);

        data.RevokeOwnerOfId(id).Should().BeTrue();

        data.WasOwnerWithin(id, previousOwner.SessionId, 1500).Should().BeTrue();
    }

    [TestMethod]
    public void WasOwnerWithin_WhenNeverRevoked_ReturnsFalse()
    {
        SimulationOwnershipData data = new();
        NitroxId id = new();
        Player owner = CreatePlayer(1);
        data.TryToAcquire(id, owner, SimulationLockType.TRANSIENT);

        data.WasOwnerWithin(id, owner.SessionId, 1500).Should().BeFalse();
    }

    [TestMethod]
    public void WasOwnerWithin_AfterTheWindow_ReturnsFalse()
    {
        SimulationOwnershipData data = new();
        NitroxId id = new();
        Player previousOwner = CreatePlayer(1);
        data.TryToAcquire(id, previousOwner, SimulationLockType.TRANSIENT);
        data.RevokeIfOwner(id, previousOwner);

        Thread.Sleep(60);

        data.WasOwnerWithin(id, previousOwner.SessionId, 20).Should().BeFalse();
    }

    [TestMethod]
    public void ForgetPreviousOwner_DropsTheRecord()
    {
        SimulationOwnershipData data = new();
        NitroxId id = new();
        Player previousOwner = CreatePlayer(1);
        data.TryToAcquire(id, previousOwner, SimulationLockType.TRANSIENT);
        data.RevokeIfOwner(id, previousOwner);

        data.ForgetPreviousOwner(id);

        data.WasOwnerWithin(id, previousOwner.SessionId, 1500).Should().BeFalse();
    }

    [TestMethod]
    public void RevokeIfOwner_ByAnotherPlayer_RecordsNothing()
    {
        SimulationOwnershipData data = new();
        NitroxId id = new();
        Player owner = CreatePlayer(1);
        Player other = CreatePlayer(2);
        data.TryToAcquire(id, owner, SimulationLockType.TRANSIENT);

        data.RevokeIfOwner(id, other).Should().BeFalse();

        data.WasOwnerWithin(id, other.SessionId, 1500).Should().BeFalse();
        data.WasOwnerWithin(id, owner.SessionId, 1500).Should().BeFalse();
    }

    private static Player CreatePlayer(ushort id)
    {
        return new Player(id, id, $"Player{id}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }
}
