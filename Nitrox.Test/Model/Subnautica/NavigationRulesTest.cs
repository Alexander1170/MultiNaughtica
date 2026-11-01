using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica;

/// <summary>The arithmetic of the route that a navigator sets for a Cyclops (feature group 3, station 1): waypoints, reaching them, the line between them, the turn to the next one.</summary>
[TestClass]
public class NavigationRulesTest
{
    // ---- waypoints

    [TestMethod]
    public void AWaypointNeedsRoomAPlaceInTheWorldAndSpaceFromTheLastOne()
    {
        NavigationRules.CanAppend(0, false, 0f, 0f, 0f, -78f, -10f, 330f).Should().BeTrue("the first waypoint");
        NavigationRules.CanAppend(3, true, 0f, 0f, 0f, 10f, 0f, 0f).Should().BeTrue();
        NavigationRules.CanAppend(3, true, 0f, 0f, 0f, 3f, 0f, 0f).Should().BeFalse("on top of the last one: a double click adds nothing");
        NavigationRules.CanAppend(3, true, 0f, 0f, 0f, 5f, 0f, 0f).Should().BeTrue("exactly the spacing");
        NavigationRules.CanAppend(NavigationRules.MAX_WAYPOINTS, true, 0f, 0f, 0f, 100f, 0f, 0f).Should().BeFalse("the route is full");
        NavigationRules.CanAppend(0, false, 0f, 0f, 0f, float.NaN, 0f, 0f).Should().BeFalse();
        NavigationRules.CanAppend(0, false, 0f, 0f, 0f, 9000f, 0f, 0f).Should().BeFalse("outside the world");
        NavigationRules.CanAppend(-1, false, 0f, 0f, 0f, 0f, 0f, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void AnAdvanceGoesForwardByOneAndNeverPastTheEnd()
    {
        NavigationRules.IsValidAdvance(4, 0, 1).Should().BeTrue();
        NavigationRules.IsValidAdvance(4, 3, 4).Should().BeTrue("the number of waypoints means finished");
        NavigationRules.IsValidAdvance(4, 4, 5).Should().BeFalse("a finished route does not advance");
        NavigationRules.IsValidAdvance(4, 1, 3).Should().BeFalse("one at a time");
        NavigationRules.IsValidAdvance(4, 2, 2).Should().BeFalse("not the same index again");
        NavigationRules.IsValidAdvance(4, 2, 1).Should().BeFalse("never backwards");
        NavigationRules.IsValidAdvance(0, 0, 1).Should().BeFalse("no route");
    }

    [TestMethod]
    public void RemovingAWaypointKeepsTheNextOneWhereItWas()
    {
        NavigationRules.NextAfterRemoval(2, 0, 3).Should().Be(1, "a waypoint before the next one was taken out: the next one moved up by one");
        NavigationRules.NextAfterRemoval(2, 2, 3).Should().Be(2, "the next one itself was taken out: the one after it is next");
        NavigationRules.NextAfterRemoval(2, 3, 3).Should().Be(2, "a later one does not matter");
        NavigationRules.NextAfterRemoval(3, 2, 2).Should().Be(2, "a finished route stays finished at its new end");
        NavigationRules.NextAfterRemoval(0, 0, 0).Should().Be(0);
        NavigationRules.IsFinished(3, 3).Should().BeTrue();
        NavigationRules.IsFinished(3, 2).Should().BeFalse();
        NavigationRules.IsFinished(0, 0).Should().BeFalse("no route is not a finished one");
    }

    // ---- reaching a waypoint

    [TestMethod]
    public void AWaypointIsReachedWithinTwelveMetresOfTheHullAndNotBefore()
    {
        NavigationRules.HasReached(0f).Should().BeTrue("the point is inside the hull's box");
        NavigationRules.HasReached(12f).Should().BeTrue("exactly at the limit");
        NavigationRules.HasReached(12.1f).Should().BeFalse();
        NavigationRules.HasReached(float.NaN).Should().BeFalse();
        NavigationRules.HasReached(-1f).Should().BeFalse();
    }

    [TestMethod]
    public void AWaypointThatTheCyclopsSlidPastStillAdvancesTheRoute()
    {
        NavigationRules.HasPassed(105f, 100f, 20f).Should().BeTrue("past the point along the leg and not far from it");
        NavigationRules.HasPassed(95f, 100f, 20f).Should().BeFalse("not along the leg yet");
        NavigationRules.HasPassed(150f, 100f, 80f).Should().BeFalse("far from the point: a different place altogether");
        NavigationRules.HasPassed(10f, 0f, 5f).Should().BeFalse("a leg of no length");
    }

    // ---- the line between two waypoints

    [TestMethod]
    public void TheNearestPointOnALegIsHeldBetweenItsEnds()
    {
        (float t, float x, float y, float z, float distance) = NavigationRules.NearestOnSegment(50f, 30f, 0f, 0f, 0f, 0f, 100f, 0f, 0f);
        (t, x, y, z, distance).Should().Be((0.5f, 50f, 0f, 0f, 30f));

        NavigationRules.NearestOnSegment(-40f, 0f, 30f, 0f, 0f, 0f, 100f, 0f, 0f).Should().Be((0f, 0f, 0f, 0f, 50f), "before A it is A");
        NavigationRules.NearestOnSegment(250f, 0f, 0f, 0f, 0f, 0f, 100f, 0f, 0f).Should().Be((1f, 100f, 0f, 0f, 150f), "past B it is B");
        NavigationRules.NearestOnSegment(3f, 4f, 0f, 0f, 0f, 0f, 0f, 0f, 0f).Distance.Should().Be(5f, "a leg of no length is its point");
    }

    [TestMethod]
    public void CrossTrackIsTheDistanceFromTheLine()
    {
        NavigationRules.CrossTrack(50f, 0f, 20f, 0f, 0f, 0f, 100f, 0f, 0f).Should().Be(20f);
        NavigationRules.CrossTrack(50f, 0f, 0f, 0f, 0f, 0f, 100f, 0f, 0f).Should().Be(0f);
        NavigationRules.CrossTrack(0f, 10f, 0f, 0f, 0f, 0f, 0f, 100f, 0f).Should().Be(0f, "a point on a line that goes up is on it");
    }

    // ---- the turn to the next waypoint

    [TestMethod]
    public void ABearingIsPositiveToStarboardAndNegativeToPortAndWrapsAtHalfATurn()
    {
        NavigationRules.RelativeBearing(0f, 0f, 100f).Should().BeApproximately(0f, 0.001f, "dead ahead");
        NavigationRules.RelativeBearing(0f, 100f, 0f).Should().BeApproximately(90f, 0.001f, "starboard");
        NavigationRules.RelativeBearing(0f, -100f, 0f).Should().BeApproximately(-90f, 0.001f, "port");
        NavigationRules.RelativeBearing(0f, 0f, -100f).Should().BeApproximately(180f, 0.001f, "astern");
        NavigationRules.RelativeBearing(170f, -100f, -10f).Should().BeApproximately(94.3f, 0.5f, "from heading 170 the target at about 264 degrees is 94 degrees round to starboard, across the wrap");
        NavigationRules.RelativeBearing(90f, 0f, 0f).Should().Be(0f, "a point on top of us has no direction");
        NavigationRules.WrapDegrees(540f).Should().BeApproximately(180f, 0.001f);
        NavigationRules.WrapDegrees(-181f).Should().BeApproximately(179f, 0.001f);
        NavigationRules.WrapDegrees(float.NaN).Should().Be(0f);
    }

    [TestMethod]
    public void TheArrowPointsAtTheTargetInTheCyclopsFrame()
    {
        NavigationRules.ArrowAngles(0f, 0f, 50f).Should().Be((0f, 0f));
        (float yaw, float pitch) = NavigationRules.ArrowAngles(50f, 0f, 50f);
        yaw.Should().BeApproximately(45f, 0.001f);
        pitch.Should().BeApproximately(0f, 0.001f);
        NavigationRules.ArrowAngles(0f, 50f, 50f).Pitch.Should().BeApproximately(45f, 0.001f, "a target above is a pitch up");
        NavigationRules.ArrowAngles(-30f, -30f, 0f).Yaw.Should().BeApproximately(-90f, 0.001f, "to port");
        NavigationRules.ArrowAngles(0f, 0f, 0f).Should().Be((0f, 0f), "no direction is no angle, not a NaN");
    }

    // ---- off course

    [TestMethod]
    public void TheCyclopsIsOffCourseWhenFarFromTheLineOrHeadingTheWrongWayAndNotBeforeTheLimits()
    {
        NavigationRules.IsOffCourse(false, 10f, 20f, 5f).Should().BeFalse();
        NavigationRules.IsOffCourse(false, 36f, 20f, 5f).Should().BeTrue("too far from the line");
        NavigationRules.IsOffCourse(false, 10f, 71f, 5f).Should().BeTrue("heading the wrong way while moving");
        NavigationRules.IsOffCourse(false, 10f, 170f, 0.2f).Should().BeFalse("a Cyclops that hardly moves has no heading worth comparing");
        NavigationRules.IsOffCourse(false, float.NaN, float.NaN, 5f).Should().BeFalse("nothing known is not off course");
        NavigationRules.IsOffCourse(false, float.NaN, 100f, 5f).Should().BeTrue("the first leg has no line, only the heading");
    }

    [TestMethod]
    public void OnceOffCourseTheNumbersHaveToFallWellBelowTheLimitsToBeBackOnIt()
    {
        NavigationRules.IsOffCourse(true, 30f, 20f, 5f).Should().BeTrue("30 m is under 35 but not under 70 percent of it: still off");
        NavigationRules.IsOffCourse(true, 20f, 20f, 5f).Should().BeFalse("back on course");
        NavigationRules.IsOffCourse(true, 10f, 60f, 5f).Should().BeTrue("60 degrees is under 70 but not under 70 percent of it");
        NavigationRules.IsOffCourse(true, 10f, 45f, 5f).Should().BeFalse();
    }

    [TestMethod]
    public void AHeadingThatCannotBeJudgedBecauseTheCyclopsStoppedKeepsTheVerdictThatWasGiven()
    {
        OffCourseReason wrongWay = NavigationRules.OffCourse(OffCourseReason.None, 10f, 100f, 5f);
        wrongWay.Should().Be(OffCourseReason.WrongWay);

        NavigationRules.OffCourse(wrongWay, 10f, 100f, 0.2f).Should().Be(OffCourseReason.WrongWay, "stopping the throttle does not make the Cyclops point the right way");
        NavigationRules.OffCourse(wrongWay, 10f, float.NaN, 0f).Should().Be(OffCourseReason.WrongWay);
        NavigationRules.OffCourse(wrongWay, 10f, 20f, 5f).Should().Be(OffCourseReason.None, "moving again and pointing the right way clears it");
        NavigationRules.OffCourse(OffCourseReason.None, 10f, 170f, 0.2f).Should().Be(OffCourseReason.None, "a Cyclops that was fine and hardly moves is still fine");
        NavigationRules.IsOffCourse(true, 10f, 100f, 0.2f).Should().BeTrue("the old form of the verdict keeps it too");
    }

    [TestMethod]
    public void EachLimitHasItsOwnHysteresisAndTheReasonsAddUp()
    {
        NavigationRules.OffCourse(OffCourseReason.Far, 30f, 60f, 5f).Should().Be(OffCourseReason.Far, "30 m is not under 70 percent of 35, and 60 degrees is under 70 and was never wrong");
        NavigationRules.OffCourse(OffCourseReason.None, 40f, 100f, 5f).Should().Be(OffCourseReason.Far | OffCourseReason.WrongWay);
        NavigationRules.OffCourse(OffCourseReason.Far | OffCourseReason.WrongWay, 20f, 45f, 5f).Should().Be(OffCourseReason.None);
    }

    // ---- the numbers on the screen

    [TestMethod]
    public void TheEtaNeedsASpeedAndTheLineWidthGrowsWithTheDistanceWithinLimits()
    {
        NavigationRules.EtaSeconds(300f, 6f).Should().BeApproximately(50f, 0.001f);
        float.IsNaN(NavigationRules.EtaSeconds(300f, 0.5f)).Should().BeTrue("too slow for the number to mean anything");
        float.IsNaN(NavigationRules.EtaSeconds(-1f, 6f)).Should().BeTrue();

        NavigationRules.LineWidth(10f).Should().Be(0.12f, "close up the width of a cable");
        NavigationRules.LineWidth(100f).Should().BeApproximately(0.4f, 0.0001f);
        NavigationRules.LineWidth(5000f).Should().Be(2f, "never wider than two metres");
        NavigationRules.LineWidth(float.NaN).Should().Be(0.12f);
    }
}
