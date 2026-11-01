using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class RocketMetadataTest
{
    private static RocketMetadata Rocket(int stage, float transition, int elevatorState, float elevatorPosition, params int[] checks)
    {
        return new RocketMetadata(stage, transition, elevatorState, elevatorPosition, new List<int>(checks));
    }

    [TestMethod]
    public void TheFirstReportIsStoredAsItIs()
    {
        RocketMetadata incoming = Rocket(1, 10f, 3, 0f);

        RocketMetadata.Merge(null, incoming).Should().BeSameAs(incoming);
    }

    [TestMethod]
    public void TwoSwitchesThrownInOneRoundTripAreBothKept()
    {
        RocketMetadata storedFromAlice = Rocket(5, 100f, 3, 0f, 1, 2, 3, 4); // Alice threw Hydraulics (4); her view lacks Bob's switch
        RocketMetadata fromBob = Rocket(5, 150f, 3, 0f, 1, 2, 3, 5);         // Bob's view has his own switch (5) but not hers

        RocketMetadata merged = RocketMetadata.Merge(storedFromAlice, fromBob);

        merged.PreflightChecks.Should().Equal(1, 2, 3, 4, 5);
    }

    [TestMethod]
    public void TheStageNeverGoesDown()
    {
        RocketMetadata merged = RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(3, 300f, 3, 0f));

        merged.CurrentStage.Should().Be(4);
    }

    [TestMethod]
    public void TheTransitionTimeOnlyMovesWithTheStage()
    {
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(4, 900f, 3, 0f)).LastStageTransitionTime.Should().Be(200f);
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(5, 900f, 3, 0f)).LastStageTransitionTime.Should().Be(900f);
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(3, 900f, 3, 0f)).LastStageTransitionTime.Should().Be(200f);
    }

    [TestMethod]
    public void TheElevatorIsWhatTheReporterSees()
    {
        RocketMetadata merged = RocketMetadata.Merge(Rocket(5, 100f, 3, 0f), Rocket(5, 100f, 0, 0.4f));

        merged.ElevatorState.Should().Be(0);
        merged.ElevatorPosition.Should().Be(0.4f);
    }

    [TestMethod]
    public void MergingTheSameReportAgainChangesNothing()
    {
        RocketMetadata report = Rocket(5, 100f, 2, 1f, 3, 1, 2);

        RocketMetadata merged = RocketMetadata.Merge(RocketMetadata.Merge(null, report), report);

        merged.HasSameStateAs(RocketMetadata.Merge(null, report)).Should().BeTrue();
    }

    [TestMethod]
    public void AReportThatAddsNothingMatchesItsMergeSoItNeedsNoCorrection()
    {
        RocketMetadata report = Rocket(5, 100f, 3, 0f, 1, 2);

        RocketMetadata.Merge(Rocket(5, 100f, 3, 0f, 1), report).HasSameStateAs(report).Should().BeTrue();
    }

    [TestMethod]
    public void AReportOfTheSameStageWithALaterStampNeedsNoCorrection()
    {
        // every report is stamped with the time it was made; the stored transition time stays the older one
        RocketMetadata stored = Rocket(5, 100f, 3, 0f, 1, 2);
        RocketMetadata elevatorCall = Rocket(5, 160f, 0, 0f, 1, 2);

        RocketMetadata.Merge(stored, elevatorCall).HasSameStateAs(elevatorCall).Should().BeTrue();
    }

    [TestMethod]
    public void AReportThatLacksWhatTheServerHasDiffersFromItsMerge()
    {
        RocketMetadata report = Rocket(5, 100f, 3, 0f, 1);

        RocketMetadata.Merge(Rocket(5, 100f, 3, 0f, 1, 2), report).HasSameStateAs(report).Should().BeFalse();
    }
}
