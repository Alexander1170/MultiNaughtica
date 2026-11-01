using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using static Nitrox.Server.Subnautica.Models.GameLogic.Entities.RepairTestData;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     The numbers the live check reads: <c>registry entities: N, duplicate ids: D, doubled top-level: T</c> (the line is also logged by every world load).
/// </summary>
[TestClass]
public class WorldAuditTest
{
    [TestMethod]
    public void AHealthyRegistryShowsNoDuplicatesAndNoDoubles()
    {
        PlaceholderGroupWorldEntity group = Group(1);
        Kid(2, group);
        List<Entity> all = Flat(group);
        all.Add(Creature(3));
        all.Add(Creature(4, "Spadefish"));

        WorldAuditResult audit = WorldAudit.Run(all, IsCreature);

        audit.RegistryEntities.Should().Be(4);
        audit.DuplicateIds.Should().Be(0);
        audit.DoubledTopLevel.Should().Be(0);
        audit.ToString().Should().StartWith("registry entities: 4, duplicate ids: 0, doubled top-level: 0");
    }

    [TestMethod]
    public void AChildThatIsNotTheObjectRegisteredUnderItsIdIsADuplicateId()
    {
        // the signature of a replaced entity: the registry holds one object under the id, the child list of a parent holds another
        PlaceholderGroupWorldEntity group = Group(1);
        Kid(2, group);
        WorldEntity replacement = Creature(2);
        List<Entity> all = [group, replacement];

        WorldAudit.Run(all, IsCreature).DuplicateIds.Should().Be(1);
    }

    [TestMethod]
    public void TheSameIdTwiceInTheListIsADuplicateId()
    {
        List<Entity> all = [Creature(1), Creature(1, x: 50f)];

        WorldAudit.Run(all, IsCreature).DuplicateIds.Should().Be(1);
    }

    [TestMethod]
    public void TwinsAreCountedAsDoubledTopLevel()
    {
        List<Entity> all = [Creature(1), Creature(2), Creature(3, "Spadefish", x: 99f), Group(10), Group(11), Creature(20, "LimestoneChunk"), Creature(21, "LimestoneChunk"), Decor(30, 1, 2, 3), Decor(31, 1, 2, 3)];

        WorldAuditResult audit = WorldAudit.Run(all, IsCreature);

        audit.DoubledTopLevel.Should().Be(3, "one extra creature, one extra group and one extra resource");
        audit.TwinsLeftAlone.Should().Be(1, "the identical decor is the baked game data's");
        audit.ToString().Should().Contain("doubled top-level: 3");
    }

    [TestMethod]
    public void TheBatchesAreCountedByWhatTheEntitiesBelongTo()
    {
        List<Entity> all = [Decor(1, 3, 4, 5, x: 500f), Decor(2, 3, 4, 5), Creature(3, x: 10f, y: -5f, z: 20f), Creature(4, x: 11f, y: -5f, z: 20f)];
        PlaceholderGroupWorldEntity group = Group(5);
        Kid(6, group);
        all.AddRange(Flat(group));

        SortedDictionary<NitroxInt3, int> counts = WorldAudit.CountTopLevelByBatch(all);

        counts[new NitroxInt3(3, 4, 5)].Should().Be(2, "decor counts for the batch it was generated for, not for where it stands");
        counts[new NitroxInt3(12, 18, 12)].Should().Be(2);
        counts.Values.Sum().Should().Be(5, "the child of the group is not counted");
        counts.Keys.Should().BeInAscendingOrder(Comparer<NitroxInt3>.Create((left, right) => left.X != right.X ? left.X.CompareTo(right.X) : left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.Z.CompareTo(right.Z)));
    }
}
