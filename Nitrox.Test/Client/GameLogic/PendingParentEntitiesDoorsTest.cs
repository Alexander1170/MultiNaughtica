using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace NitroxClient.GameLogic;

[TestClass]
public class PendingParentEntitiesDoorsTest
{
    private static WorldEntity Placeholder(NitroxId parentId, string classId)
    {
        // a door, a panel or a prop of a wreck: TechType None, a child of the wreck's group
        return new WorldEntity(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("None"), 1, classId, true, new NitroxId(), null) { ParentId = parentId };
    }

    [TestMethod]
    public void TheLogLineCountsTheLaserCutDoorsThatWaitedForAGroupThatNeverCame()
    {
        PendingParentEntities pending = new();
        NitroxId wreck = new();
        pending.Add(Placeholder(wreck, StarshipDoorRules.SEALED_DOOR_CLASS_ID), 0f);
        pending.Add(Placeholder(wreck, StarshipDoorRules.SEALED_DOOR_CLASS_ID), 0f);
        pending.Add(Placeholder(wreck, "some-prop"), 0f);

        string description = pending.DescribeOlderThan(100f);

        description.Should().StartWith("1 parents;").And.Contain("WorldEntity/None x3").And.Contain("laser-cut doors x2").And.Contain($"parents {wreck.ToString().Substring(0, 8)} x3");
    }

    [TestMethod]
    public void TheLogLineSaysNothingAboutDoorsWhenNoDoorWaited()
    {
        PendingParentEntities pending = new();
        pending.Add(Placeholder(new NitroxId(), "some-prop"), 0f);

        pending.DescribeOlderThan(100f).Should().NotContain("laser-cut");
    }

    [TestMethod]
    public void AnEntryThatWaitedLessThanTheLimitIsNotDescribed()
    {
        PendingParentEntities pending = new();
        pending.Add(Placeholder(new NitroxId(), StarshipDoorRules.SEALED_DOOR_CLASS_ID), 500f);

        pending.DescribeOlderThan(100f).Should().StartWith("0 parents;").And.NotContain("laser-cut");
    }
}
