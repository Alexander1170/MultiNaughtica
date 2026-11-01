using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.Serialization.World;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class RadiationLeakRulesTest
{
    [TestMethod]
    public void AWeldThatRaisesTheHealthIsProgress()
    {
        RadiationMetadata.IsProgress(40f, 55f).Should().BeTrue();
    }

    [TestMethod]
    public void AnOlderOrEqualUpdateIsNoProgress()
    {
        // Alice finished the leak (100), Bob's slower weld packet (95) arrives afterwards
        RadiationMetadata.IsProgress(100f, 95f).Should().BeFalse();
        RadiationMetadata.IsProgress(100f, 100f).Should().BeFalse();
    }

    [TestMethod]
    public void ALeakIsOwnedByTheServer()
    {
        // So that nobody simulates its movement (it used to be watched by the first player in and its transform broadcast four times a second)
        new RadiationLeakEntity(new NitroxId(), 3, new RadiationMetadata(0f)).SpawnedByServer.Should().BeTrue();
    }

    [TestMethod]
    public void AWorldWithNoLeaksMissesAllEleven()
    {
        WorldService.MissingLeakIndices([]).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
    }

    [TestMethod]
    public void OnlyTheLeaksThatDoNotExistAreMissing()
    {
        // A creation-time save that has been loaded again and an older save that had some
        WorldService.MissingLeakIndices([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]).Should().BeEmpty();
        WorldService.MissingLeakIndices([0, 2, 4, 10]).Should().Equal(1, 3, 5, 6, 7, 8, 9);
    }
}
