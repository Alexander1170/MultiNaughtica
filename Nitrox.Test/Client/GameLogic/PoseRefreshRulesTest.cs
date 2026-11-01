namespace NitroxClient.GameLogic;

[TestClass]
public class PoseRefreshRulesTest
{
    [TestMethod]
    public void IsDue_AChangedState_IsSentAtOnce()
    {
        PoseRefreshRules.IsDue(changed: true, secondsSinceLastSend: 0f).Should().BeTrue();
    }

    [TestMethod]
    public void IsDue_AnUnchangedStateJustSent_IsNot()
    {
        PoseRefreshRules.IsDue(changed: false, secondsSinceLastSend: 0.02f).Should().BeFalse();
        PoseRefreshRules.IsDue(changed: false, secondsSinceLastSend: PoseRefreshRules.REFRESH_INTERVAL - 0.1f).Should().BeFalse();
    }

    [TestMethod]
    public void IsDue_AnUnchangedStateAfterTheInterval_IsSentAgain()
    {
        // The state that went missing once (an event before the body existed) is healed by the next announcement, without a change
        PoseRefreshRules.IsDue(changed: false, secondsSinceLastSend: PoseRefreshRules.REFRESH_INTERVAL).Should().BeTrue();
        PoseRefreshRules.IsDue(changed: false, secondsSinceLastSend: 60f).Should().BeTrue();
    }

    [TestMethod]
    public void IsDue_TheFirstUpdateAfterTheLoading_IsSent()
    {
        // The tracker starts with "never sent" (negative infinity as the time of the last send): now minus that is infinite
        PoseRefreshRules.IsDue(changed: false, secondsSinceLastSend: float.PositiveInfinity).Should().BeTrue();
    }

    [TestMethod]
    public void RefreshInterval_IsAFewSeconds()
    {
        // A packet of a few bytes per player and interval: not a stream
        PoseRefreshRules.REFRESH_INTERVAL.Should().BeInRange(2f, 30f);
    }

    [TestMethod]
    public void SettledAfter_IsLongerThanTheRefreshInterval()
    {
        // The line "walks and still reports the swimming state" is evidence only when the sender has announced its state again in the meantime
        PoseRefreshRules.SETTLED_AFTER.Should().BeGreaterThan(PoseRefreshRules.REFRESH_INTERVAL);
    }

    [TestMethod]
    public void IsSettled_WithinTheRefreshInterval_IsNot()
    {
        // The review of bug 6: the first pawn packet of the boarding comes with the swimming state still in force; that is no evidence
        PoseRefreshRules.IsSettled(0f).Should().BeFalse();
        PoseRefreshRules.IsSettled(0.5f).Should().BeFalse();
        PoseRefreshRules.IsSettled(PoseRefreshRules.REFRESH_INTERVAL).Should().BeFalse("the announcement may still be on its way");
    }

    [TestMethod]
    public void IsSettled_AfterTheIntervalAndTheMargin_Is()
    {
        PoseRefreshRules.IsSettled(PoseRefreshRules.SETTLED_AFTER).Should().BeTrue();
        PoseRefreshRules.IsSettled(60f).Should().BeTrue();
    }

    [TestMethod]
    public void IsSettled_ForAPlayerWhoDoesNotWalk_IsNot()
    {
        PoseRefreshRules.IsSettled(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void IsLateCorrection_TheTwoMessagesOfTheBoarding_AreNot()
    {
        // The pawn packet and the state change come from the same client within a frame or two, in either order
        PoseRefreshRules.IsLateCorrection(0f).Should().BeFalse();
        PoseRefreshRules.IsLateCorrection(0.2f).Should().BeFalse();
        PoseRefreshRules.IsLateCorrection(PoseRefreshRules.LATE_AFTER - 0.01f).Should().BeFalse();
    }

    [TestMethod]
    public void IsLateCorrection_ACorrectionBySeconds_Is()
    {
        PoseRefreshRules.IsLateCorrection(PoseRefreshRules.LATE_AFTER).Should().BeTrue();
        PoseRefreshRules.IsLateCorrection(PoseRefreshRules.REFRESH_INTERVAL).Should().BeTrue();
    }

    [TestMethod]
    public void IsLateCorrection_WithoutAWalker_IsNot()
    {
        PoseRefreshRules.IsLateCorrection(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void LateAfter_IsBelowTheRefreshInterval()
    {
        // A state healed by the next announcement arrives up to REFRESH_INTERVAL after the walk began; the limit must leave most of that window above it
        PoseRefreshRules.LATE_AFTER.Should().BeLessThan(PoseRefreshRules.REFRESH_INTERVAL / 2f);
    }
}
