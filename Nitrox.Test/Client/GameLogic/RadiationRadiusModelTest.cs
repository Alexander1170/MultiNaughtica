using System.Linq;

namespace NitroxClient.GameLogic;

[TestClass]
public class RadiationRadiusModelTest
{
    private const int LEAKS = 11;
    private const float START = 10f;
    private const float GROW = 0.5f;
    private const float DISSIPATION = -0.25f;
    private const float MAX = 300f;

    private static RadiationRadiusModel.Result Compute(float elapsed, float explosion, params float[] fixTimes)
    {
        return RadiationRadiusModel.Compute(elapsed, explosion, fixTimes, LEAKS, START, GROW, DISSIPATION, MAX, currentRadius: 42f);
    }

    private static float[] Leaks(int fixedCount, float fixTime)
    {
        return Enumerable.Range(0, LEAKS).Select(i => i < fixedCount ? fixTime : -1f).ToArray();
    }

    [TestMethod]
    public void NoLeakFixedBeforeTheExplosionLeavesTheRadiusAlone()
    {
        RadiationRadiusModel.Result result = Compute(100f, 500f, Leaks(0, 0f));

        result.ApplyRadius.Should().BeFalse();
        result.AllFixed.Should().BeFalse();
    }

    [TestMethod]
    public void NoLeakFixedAfterTheExplosionGrowsTheRadiusFromTheExplosion()
    {
        RadiationRadiusModel.Result result = Compute(700f, 500f, Leaks(0, 0f));

        result.ApplyRadius.Should().BeTrue();
        result.Radius.Should().Be(START + 200f * GROW);
        result.AllFixed.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(6)]
    [DataRow(10)]
    public void AFieldWithSomeLeaksFixedIsTreatedLikeOneWithNoneFixed(int fixedCount)
    {
        // Every leak that is not fixed still makes it grow, and the field is not repaired until the last one is
        RadiationRadiusModel.Result partly = Compute(700f, 500f, Leaks(fixedCount, 600f));
        RadiationRadiusModel.Result none = Compute(700f, 500f, Leaks(0, 0f));

        partly.Should().Be(none);
        partly.AllFixed.Should().BeFalse();
    }

    [TestMethod]
    public void AllLeaksFixedAfterTheExplosionGrowsUntilTheLastFixAndShrinksSince()
    {
        // exploded at 500, the last leak was fixed at 600, now 640: grew for 100 s, shrank for 40 s
        RadiationRadiusModel.Result result = Compute(640f, 500f, Leaks(LEAKS, 600f));

        result.AllFixed.Should().BeTrue();
        result.Radius.Should().Be(START + 100f * GROW + 40f * DISSIPATION);
    }

    [TestMethod]
    public void TheLastFixIsTheLatestOfTheLeaks()
    {
        float[] times = Enumerable.Range(0, LEAKS).Select(i => 510f + i * 10f).ToArray(); // the last at 610

        RadiationRadiusModel.Result result = Compute(650f, 500f, times);

        result.Radius.Should().Be(START + 110f * GROW + 40f * DISSIPATION);
    }

    [TestMethod]
    public void AllLeaksFixedBeforeTheExplosionShrinkFromTheExplosion()
    {
        RadiationRadiusModel.Result result = Compute(540f, 500f, Leaks(LEAKS, 100f));

        result.AllFixed.Should().BeTrue();
        // it did not grow (the fix was before the explosion): it keeps the radius it has and shrinks from the explosion
        result.Radius.Should().Be(42f + 40f * DISSIPATION);
    }

    [TestMethod]
    public void TheRadiusFloorsAtZeroLongAfterTheRepair()
    {
        RadiationRadiusModel.Result result = Compute(100000f, 500f, Leaks(LEAKS, 600f));

        result.Radius.Should().Be(0f);
    }

    [TestMethod]
    public void TheRadiusIsLimitedToTheMaximum()
    {
        RadiationRadiusModel.Result result = Compute(100000f, 500f, Leaks(0, 0f));

        result.Radius.Should().Be(MAX);
    }

    [TestMethod]
    public void TheRadiusIsComputedAgainWhenTheClockOfAFirstPlayerRunsOnlyAfterTheExplosionWithAllLeaksBound()
    {
        RadiationRadiusModel.ShouldRecalculateWhenClockRuns(exploded: true, leaksBound: LEAKS, totalLeaks: LEAKS).Should().BeTrue();
        RadiationRadiusModel.ShouldRecalculateWhenClockRuns(exploded: false, leaksBound: LEAKS, totalLeaks: LEAKS).Should().BeFalse();
        RadiationRadiusModel.ShouldRecalculateWhenClockRuns(exploded: true, leaksBound: LEAKS - 1, totalLeaks: LEAKS).Should().BeFalse();
        RadiationRadiusModel.ShouldRecalculateWhenClockRuns(exploded: true, leaksBound: 0, totalLeaks: LEAKS).Should().BeFalse();
    }

    [TestMethod]
    public void ADriftedRadiusIsPutRightByTheSecondComputation()
    {
        // The first player: applied from the frozen clock (elapsed 700, explosion 500: radius 110), then the vanilla update grew it by 6.6 s of frame time while the clock stood still: 113.3.
        RadiationRadiusModel.Result first = Compute(700f, 500f, Leaks(3, 600f));
        float drifted = first.Radius + 6.6f * GROW;
        drifted.Should().BeGreaterThan(first.Radius);

        // The clock runs again and says that the server has advanced by 2 s only: the second computation replaces the drifted value by the value of the formula, whatever the radius was
        RadiationRadiusModel.Result second = RadiationRadiusModel.Compute(702f, 500f, Leaks(3, 600f), LEAKS, START, GROW, DISSIPATION, MAX, drifted);

        second.ApplyRadius.Should().BeTrue();
        second.Radius.Should().Be(START + 202f * GROW);
        second.Radius.Should().BeLessThan(drifted);
    }

    [TestMethod]
    public void TheSecondComputationDoesNotChangeTheAnswerOfAClockThatWasRightAlready()
    {
        // The second player's clock runs from the start: the same elapsed time gives the same radius, a second time or a third
        RadiationRadiusModel.Result once = Compute(700f, 500f, Leaks(3, 600f));
        RadiationRadiusModel.Result again = RadiationRadiusModel.Compute(700f, 500f, Leaks(3, 600f), LEAKS, START, GROW, DISSIPATION, MAX, once.Radius);

        again.Radius.Should().Be(once.Radius);
    }

    [TestMethod]
    public void LeaksTheServerDidNotTellAboutCountAsLeaking()
    {
        // 10 entries only: not "all fixed" even if they all are
        RadiationRadiusModel.Result result = Compute(700f, 500f, Enumerable.Repeat(600f, 10).ToArray());

        result.AllFixed.Should().BeFalse();
    }
}
