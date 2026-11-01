namespace NitroxClient.Communication.Packets.Processors;

[TestClass]
public class BuildingResyncPairingTest
{
    private sealed record FakeEntity(string Name, int Position);

    private sealed record FakeComponent(string Name, int Position);

    private static bool SamePosition(FakeEntity entity, FakeComponent component) => entity.Position == component.Position;

    [TestMethod]
    public void EntityWithoutComponent_IsNotPaired()
    {
        // A client that lost the base has no component for it: pairing it with null made the resync throw and dropped the entity
        FakeEntity lostBase = new("base", 10);
        List<FakeComponent> unmarked = [];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { lostBase }, unmarked, SamePosition);

        pairs.Should().BeEmpty();
    }

    [TestMethod]
    public void EntityWithCorrespondingComponent_IsPairedAndTheComponentIsMarked()
    {
        FakeEntity entity = new("base", 10);
        FakeComponent component = new("component", 10);
        List<FakeComponent> unmarked = [new("other", 99), component];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { entity }, unmarked, SamePosition);

        pairs.Should().ContainSingle().Which.Should().Be((entity, component));
        unmarked.Should().NotContain(component);
        unmarked.Should().HaveCount(1);
    }

    [TestMethod]
    public void OneComponentTwoEntities_OnlyOneEntityIsPaired()
    {
        FakeEntity first = new("first", 10);
        FakeEntity second = new("second", 10);
        FakeComponent component = new("component", 10);
        List<FakeComponent> unmarked = [component];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { first, second }, unmarked, SamePosition);

        pairs.Should().ContainSingle();
        pairs.Should().OnlyContain(pair => pair.Component == component);
        unmarked.Should().BeEmpty();
    }

    [TestMethod]
    public void MixedList_PairsOnlyTheEntitiesThatHaveAComponent()
    {
        FakeEntity kept = new("kept", 1);
        FakeEntity lost = new("lost", 2);
        FakeEntity alsoKept = new("alsoKept", 3);
        FakeComponent keptComponent = new("keptComponent", 1);
        FakeComponent alsoKeptComponent = new("alsoKeptComponent", 3);
        List<FakeComponent> unmarked = [keptComponent, alsoKeptComponent];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { kept, lost, alsoKept }, unmarked, SamePosition);

        pairs.Select(pair => pair.Entity).Should().BeEquivalentTo([kept, alsoKept]);
        pairs.Select(pair => pair.Entity).Should().NotContain(lost);
        unmarked.Should().BeEmpty();
    }
}
