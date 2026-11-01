namespace NitroxClient.GameLogic;

[TestClass]
public class SealedDoorRulesTest
{
    private const float MAX = 450f;

    [TestMethod]
    public void ARestoredDoorThatWasCutOpenIsOpenAtMaximumAndOpensAtOnce()
    {
        // what the server holds for the two doors of the Test world that the players cut: Sealed false, OpenedAmount 450
        SealedDoorRules.Result result = SealedDoorRules.Apply(currentSealed: true, currentOpened: 0f, MAX, incomingSealed: false, incomingOpened: 450f, freshlySpawned: true);

        result.Sealed.Should().BeFalse();
        result.OpenedAmount.Should().Be(MAX);
        result.OpenAtOnce.Should().BeTrue();
        result.ShowCutFx.Should().BeFalse();
    }

    [TestMethod]
    public void ARestoredDoorThatWasCutPartlyStaysSealedWithItsProgressAndShowsNoBeam()
    {
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 0f, MAX, incomingSealed: true, incomingOpened: 200f, freshlySpawned: true);

        result.Sealed.Should().BeTrue();
        result.OpenedAmount.Should().Be(200f);
        result.OpenAtOnce.Should().BeFalse();
        result.ShowCutFx.Should().BeFalse("a joiner must not see the cutting beam of a cut that ended long ago");
    }

    [TestMethod]
    public void ACutByAnotherPlayerRaisesTheProgressAndShowsTheBeam()
    {
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 100f, MAX, true, 125f, freshlySpawned: false);

        result.Sealed.Should().BeTrue();
        result.OpenedAmount.Should().Be(125f);
        result.ShowCutFx.Should().BeTrue();
        result.OpenAtOnce.Should().BeFalse();
    }

    [TestMethod]
    public void TheCutThatEndsTheDoorOpensItForTheOtherPlayerWithoutHidingTheAnimation()
    {
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 425f, MAX, incomingSealed: false, incomingOpened: 450f, freshlySpawned: false);

        result.Sealed.Should().BeFalse();
        result.OpenedAmount.Should().Be(MAX);
        result.OpenAtOnce.Should().BeFalse("LaserCutObject.Update opens a door that is in the world, with its animation");
        result.ShowCutFx.Should().BeFalse();
    }

    [TestMethod]
    public void AnOpenDoorIsNeverSealedAgainByALateOrRepeatedReport()
    {
        // two players cutting the same door, or the entity sent again when a cell woke up, deliver an older state after the newer one
        SealedDoorRules.Result result = SealedDoorRules.Apply(currentSealed: false, currentOpened: 450f, MAX, incomingSealed: true, incomingOpened: 200f, freshlySpawned: false);

        result.Sealed.Should().BeFalse();
        result.OpenedAmount.Should().Be(MAX);
        result.ShowCutFx.Should().BeFalse();
    }

    [TestMethod]
    public void TheProgressNeverGoesBack()
    {
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 300f, MAX, true, 200f, freshlySpawned: false);

        result.Sealed.Should().BeTrue();
        result.OpenedAmount.Should().Be(300f);
        result.ShowCutFx.Should().BeFalse("nothing was cut");
    }

    [TestMethod]
    public void AnOpenReportWithTooLittleProgressStillMakesTheDoorFullyOpen()
    {
        // the vanilla invariant: Sealed.Weld clears _sealed in the call that brings openedAmount to maxOpenedAmount
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 0f, MAX, incomingSealed: false, incomingOpened: 30f, freshlySpawned: true);

        result.Sealed.Should().BeFalse();
        result.OpenedAmount.Should().Be(MAX);
    }

    [TestMethod]
    public void TheProgressIsCappedAtTheMaximumOfTheDoor()
    {
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 0f, MAX, true, 9999f, freshlySpawned: false);

        result.OpenedAmount.Should().Be(MAX);
    }

    [TestMethod]
    public void AnUnknownMaximumDoesNotCapOrFillTheProgress()
    {
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 0f, maxOpened: 0f, incomingSealed: false, incomingOpened: 30f, freshlySpawned: true);

        result.Sealed.Should().BeFalse();
        result.OpenedAmount.Should().Be(30f);
    }

    [TestMethod]
    public void GarbageProgressCountsAsNoProgress()
    {
        SealedDoorRules.Apply(true, float.NaN, MAX, true, float.NaN, freshlySpawned: true).OpenedAmount.Should().Be(0f);
        SealedDoorRules.Apply(true, 0f, MAX, true, -5f, freshlySpawned: true).OpenedAmount.Should().Be(0f);
    }

    [TestMethod]
    public void ASealedReportForAFreshDoorChangesNothingExceptTheProgress()
    {
        // the case the first report of bug 10 feared: "unsealed or open" applied to a closed door. A door that is still sealed on the server stays sealed on every client.
        SealedDoorRules.Result result = SealedDoorRules.Apply(true, 0f, MAX, incomingSealed: true, incomingOpened: 0f, freshlySpawned: true);

        result.Sealed.Should().BeTrue();
        result.OpenedAmount.Should().Be(0f);
        result.OpenAtOnce.Should().BeFalse();
        result.ShowCutFx.Should().BeFalse();
    }
}
