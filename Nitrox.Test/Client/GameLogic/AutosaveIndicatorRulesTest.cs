namespace NitroxClient.GameLogic;

/// <summary>
///     How the "Saving..." indicator behaves over time (<see cref="AutosaveIndicatorRules" />, item a): shown 2.5 s from the notice, a short fade in, a fade out at the end, and a
///     second notice while it is showing starts the show over without a flicker.
/// </summary>
[TestClass]
public class AutosaveIndicatorRulesTest
{
    [TestMethod]
    public void NothingShowsBeforeTheNoticeAndAfterTheShow()
    {
        AutosaveIndicatorRules.Alpha(-0.1f).Should().Be(0f);
        AutosaveIndicatorRules.Alpha(AutosaveIndicatorRules.VISIBLE_SECONDS).Should().Be(0f);
        AutosaveIndicatorRules.Alpha(10f).Should().Be(0f);
        AutosaveIndicatorRules.Alpha(AutosaveIndicatorRules.IDLE).Should().Be(0f);
        AutosaveIndicatorRules.IsDone(AutosaveIndicatorRules.IDLE).Should().BeTrue();
    }

    [TestMethod]
    public void TheShowLastsTwoAndAHalfSecondsAsAskedFor()
    {
        // "2 to 3 seconds"
        AutosaveIndicatorRules.VISIBLE_SECONDS.Should().BeInRange(2f, 3f);
        AutosaveIndicatorRules.IsDone(AutosaveIndicatorRules.VISIBLE_SECONDS - 0.01f).Should().BeFalse();
        AutosaveIndicatorRules.IsDone(AutosaveIndicatorRules.VISIBLE_SECONDS).Should().BeTrue();
    }

    [TestMethod]
    public void TheIndicatorFadesInQuicklyAndIsFullInTheMiddle()
    {
        AutosaveIndicatorRules.Alpha(0f).Should().Be(0f);
        AutosaveIndicatorRules.Alpha(AutosaveIndicatorRules.FADE_IN_SECONDS / 2f).Should().BeApproximately(0.5f, 1e-5f);
        AutosaveIndicatorRules.Alpha(AutosaveIndicatorRules.FADE_IN_SECONDS).Should().Be(1f);
        AutosaveIndicatorRules.Alpha(1f).Should().Be(1f);
        AutosaveIndicatorRules.Alpha(AutosaveIndicatorRules.VISIBLE_SECONDS - AutosaveIndicatorRules.FADE_OUT_SECONDS - 0.01f).Should().Be(1f);
    }

    [TestMethod]
    public void TheIndicatorFadesOutOverTheLastSecondsAndNeverGrowsAgain()
    {
        float previous = 1f;
        for (float t = AutosaveIndicatorRules.VISIBLE_SECONDS - AutosaveIndicatorRules.FADE_OUT_SECONDS; t < AutosaveIndicatorRules.VISIBLE_SECONDS; t += 0.05f)
        {
            float alpha = AutosaveIndicatorRules.Alpha(t);
            alpha.Should().BeLessThanOrEqualTo(previous);
            alpha.Should().BeInRange(0f, 1f);
            previous = alpha;
        }

        AutosaveIndicatorRules.Alpha(AutosaveIndicatorRules.VISIBLE_SECONDS - AutosaveIndicatorRules.FADE_OUT_SECONDS / 2f).Should().BeApproximately(0.5f, 1e-5f);
    }

    [TestMethod]
    public void TheAlphaStaysBetweenZeroAndOneAtEveryMoment()
    {
        for (float t = -1f; t < 4f; t += 0.01f)
        {
            AutosaveIndicatorRules.Alpha(t).Should().BeInRange(0f, 1f);
        }
    }

    [TestMethod]
    public void ANoticeForAnIdleIndicatorStartsAtZero()
    {
        AutosaveIndicatorRules.ElapsedForNewNotice(AutosaveIndicatorRules.IDLE).Should().Be(0f);
        AutosaveIndicatorRules.ElapsedForNewNotice(AutosaveIndicatorRules.VISIBLE_SECONDS).Should().Be(0f);
        AutosaveIndicatorRules.ElapsedForNewNotice(7f).Should().Be(0f);
    }

    [TestMethod]
    public void ASecondNoticeDuringTheFadeInGoesOnFadingIn()
    {
        AutosaveIndicatorRules.ElapsedForNewNotice(0.05f).Should().Be(0.05f);
    }

    [TestMethod]
    public void ASecondNoticeLaterOnStartsOverAtFullAlphaWithoutADip()
    {
        foreach (float current in new[] { 0.3f, 1.0f, 1.8f, 2.2f, 2.49f })
        {
            float start = AutosaveIndicatorRules.ElapsedForNewNotice(current);

            // the indicator never gets dimmer than it was when the second notice came, and is at full alpha at once
            AutosaveIndicatorRules.Alpha(start).Should().BeGreaterThanOrEqualTo(AutosaveIndicatorRules.Alpha(current));
            AutosaveIndicatorRules.Alpha(start).Should().Be(1f);
            AutosaveIndicatorRules.IsDone(start).Should().BeFalse();
        }
    }

    [TestMethod]
    public void TheShowRunsForItsWholeLengthAfterASecondNotice()
    {
        float start = AutosaveIndicatorRules.ElapsedForNewNotice(2.0f);

        // after the restart the remaining show is longer than what was left of the first one
        (AutosaveIndicatorRules.VISIBLE_SECONDS - start).Should().BeGreaterThan(AutosaveIndicatorRules.VISIBLE_SECONDS - 2.0f);
    }
}
