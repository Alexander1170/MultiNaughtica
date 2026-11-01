using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
/// 2026-10-24: both players of the world "Test" had left inside their base and every join after a server restart threw a NullReferenceException, because the Cyclops alignment of
/// <see cref="JoiningManager" /> read <c>parent.TechType.Name</c> of the player's saved parent, and a base (<see cref="BuildEntity" />) has no tech type.
/// </summary>
[TestClass]
public class PlayerParentRulesIsCyclopsTest
{
    private static WorldEntity CreateWorldEntity(string techType)
    {
        return new WorldEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, "classId", false, new NitroxId(), new NitroxTechType(techType), null, null, []);
    }

    [TestMethod]
    public void IsCyclops_ABase_IsFalseAndDoesNotThrow()
    {
        // The saved parent of a player who logged off inside a base: a BuildEntity, TechType null
        BuildEntity baseEntity = BuildEntity.MakeEmpty();
        baseEntity.TechType.Should().BeNull("this is the premise of the 2026-10-24 crash");

        PlayerParentRules.IsCyclops(baseEntity).Should().BeFalse();
    }

    [TestMethod]
    public void IsCyclops_ACyclops_IsTrue()
    {
        PlayerParentRules.IsCyclops(CreateWorldEntity("Cyclops")).Should().BeTrue();
    }

    [TestMethod]
    public void IsCyclops_OtherVehiclesAndAnEmptyTechType_AreFalse()
    {
        PlayerParentRules.IsCyclops(CreateWorldEntity("Seamoth")).Should().BeFalse();
        PlayerParentRules.IsCyclops(CreateWorldEntity("Exosuit")).Should().BeFalse();
        PlayerParentRules.IsCyclops(CreateWorldEntity("None")).Should().BeFalse();
    }

    [TestMethod]
    public void IsCyclops_NoEntity_IsFalse()
    {
        PlayerParentRules.IsCyclops(null).Should().BeFalse();
    }

    [TestMethod]
    public void IsCyclops_APlayerEntity_IsFalse()
    {
        // A player entity is no Cyclops either (a saved parent chain can pass through one)
        PlayerParentRules.IsCyclops(new PlayerEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, null, false, new NitroxId(), NitroxTechType.None, null, null, [])).Should().BeFalse();
    }

    [TestMethod]
    public void ContainerCapacityArbiter_AnItemWithoutATechType_DoesNotThrow()
    {
        // The other .TechType.Name read of the audit: guarded by a null check, pinned here (a base as the "item" has no tech type)
        ContainerCapacityArbiter arbiter = new();
        InventoryItemEntity container = new(new NitroxId(), "classId", new NitroxTechType("Locker"), null, null, []);

        Action fits = () => arbiter.Fits(container, BuildEntity.MakeEmpty(), 1, 1, 6, 8);

        fits.Should().NotThrow();
    }
}
