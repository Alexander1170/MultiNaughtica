using Nitrox.Model.DataStructures;
using static NitroxClient.GameLogic.CreatureVehicleHitMatcher;

namespace NitroxClient.GameLogic;

[TestClass]
public class CreatureVehicleHitMatcherTest
{
    private readonly NitroxId vehicle = new();
    private readonly NitroxId creature = new();
    private CreatureVehicleHitMatcher matcher;

    [TestInitialize]
    public void Init()
    {
        matcher = new CreatureVehicleHitMatcher();
    }

    [TestMethod]
    public void ARelayedHitWithoutALocalBiteIsApplied()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10f).Should().BeTrue();
    }

    [TestMethod]
    public void ALocalBiteWithoutARelayedHitIsApplied()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f).Should().BeTrue();
    }

    [TestMethod]
    public void TheRelayedReportOfALocalBiteIsDropped()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f).Should().BeTrue();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10.2f).Should().BeFalse();
    }

    [TestMethod]
    public void TheLocalBiteOfAHitThatWasRelayedFirstIsDropped()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10f).Should().BeTrue();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10.3f).Should().BeFalse();
    }

    [TestMethod]
    public void ACounterpartAfterTheWindowIsAnotherHit()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f).Should().BeTrue();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10f + MATCH_WINDOW + 0.1f).Should().BeTrue();
    }

    [TestMethod]
    public void AHitIsOnlyMatchedOnce()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f).Should().BeTrue();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10.1f).Should().BeFalse();
        // The second relayed hit has no bite of ours to be the report of
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10.2f).Should().BeTrue();
    }

    [TestMethod]
    public void AMissingLocalBiteDoesNotSwallowTheNextRelayedHit()
    {
        // The damage over time of a Reaper's grip ticks every second: our copy bites at 10 and 11 but not at 12
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f).Should().BeTrue();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10.1f).Should().BeFalse();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 11f).Should().BeTrue();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 11.1f).Should().BeFalse();
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 12.1f).Should().BeTrue();
    }

    [TestMethod]
    public void EveryRelayedHitIsAppliedWhenOurCopyNeverBites()
    {
        for (int i = 0; i < 20; i++)
        {
            matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10f + i).Should().BeTrue();
        }
    }

    [TestMethod]
    public void EveryHitIsAppliedOnceWhenBothSidesReportIt()
    {
        int applied = 0;
        for (int i = 0; i < 20; i++)
        {
            float time = 10f + i * 1.5f;
            applied += matcher.ShouldApply(vehicle, creature, HitOrigin.Local, time) ? 1 : 0;
            applied += matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, time + 0.15f) ? 1 : 0;
        }
        applied.Should().Be(20);
    }

    [TestMethod]
    public void TheSameHitAppliedOnceWhicheverSideReportsFirst()
    {
        int applied = 0;
        for (int i = 0; i < 20; i++)
        {
            float time = 10f + i * 1.5f;
            // The relayed hit overtakes our copy every second time
            HitOrigin first = i % 2 == 0 ? HitOrigin.Local : HitOrigin.Relayed;
            HitOrigin second = first == HitOrigin.Local ? HitOrigin.Relayed : HitOrigin.Local;
            applied += matcher.ShouldApply(vehicle, creature, first, time) ? 1 : 0;
            applied += matcher.ShouldApply(vehicle, creature, second, time + 0.2f) ? 1 : 0;
        }
        applied.Should().Be(20);
    }

    [TestMethod]
    public void OtherVehiclesAndCreaturesDoNotMatch()
    {
        matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f).Should().BeTrue();
        matcher.ShouldApply(new NitroxId(), creature, HitOrigin.Relayed, 10.1f).Should().BeTrue();
        matcher.ShouldApply(vehicle, new NitroxId(), HitOrigin.Relayed, 10.1f).Should().BeTrue();
        // The first pair still has its bite to match
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10.2f).Should().BeFalse();
    }

    [TestMethod]
    public void ManyUnansweredHitsStillMatchTheNextCounterpart()
    {
        // A bite of ours every 0.01 s for a long time with no relayed counterpart must not break the matching
        for (int i = 0; i < 1000; i++)
        {
            matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f + i * 0.01f).Should().BeTrue();
        }
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 20f).Should().BeFalse();
    }

    [TestMethod]
    public void OnlyTheNewestUnansweredHitsAreRemembered()
    {
        // Ten bites inside the window, then ten relayed hits: the queue of a pair holds eight, so two relayed hits find no bite to be the report of
        for (int i = 0; i < 10; i++)
        {
            matcher.ShouldApply(vehicle, creature, HitOrigin.Local, 10f + i * 0.01f).Should().BeTrue();
        }

        int applied = 0;
        for (int i = 0; i < 10; i++)
        {
            applied += matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 10.2f + i * 0.01f) ? 1 : 0;
        }
        applied.Should().Be(2);
    }

    [TestMethod]
    public void ThePairsOfExpiredHitsAreCleanedUp()
    {
        for (int i = 0; i < 200; i++)
        {
            matcher.ShouldApply(new NitroxId(), new NitroxId(), HitOrigin.Local, 10f + i).Should().BeTrue();
        }

        // Each pair expired a second after its hit; without the cleanup all 200 would be kept
        matcher.PendingPairCount.Should().BeLessThan(40);
        matcher.ShouldApply(vehicle, creature, HitOrigin.Relayed, 400f).Should().BeTrue();
    }
}
