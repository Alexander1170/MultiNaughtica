using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The path that a carried vehicle follows (docs/coop/VEHICLE_UPGRADES.md, tow hooks): the breadcrumbs of the tower's rear anchor, and the point on them a distance behind it. A vehicle that nothing collides
///     with goes there and not straight for the tower, so that it does not cut the corner of a cave through its wall.
/// </summary>
[TestClass]
public class TowTrailTest
{
    private static void Drive(TowTrail trail, params (float X, float Y, float Z)[] points)
    {
        foreach ((float x, float y, float z) in points)
        {
            trail.Add(x, y, z);
        }
    }

    [TestMethod]
    public void NothingIsBehindATowerThatHasNotBeenSeen()
    {
        TowTrail trail = new(0.5f, 40f);

        trail.PointBehind(8f).Should().BeNull();
        trail.HasHead.Should().BeFalse();
        trail.Length.Should().Be(0f);
    }

    [TestMethod]
    public void OnAStraightPathThePointBehindIsThatManyMetresBackAlongTheLine()
    {
        TowTrail trail = new(0.5f, 40f);
        for (int step = 0; step <= 40; step++)
        {
            trail.Add(0f, 0f, step * 0.5f); // 20 m along +z
        }

        (float X, float Y, float Z)? behind = trail.PointBehind(8.75f);

        behind.Should().NotBeNull();
        behind!.Value.X.Should().BeApproximately(0f, 0.001f);
        behind.Value.Y.Should().BeApproximately(0f, 0.001f);
        behind.Value.Z.Should().BeApproximately(20f - 8.75f, 0.001f);
    }

    [TestMethod]
    public void AroundACornerThePointIsOnTheWayThatTheTowerWentAndNotOnTheStraightLineToIt()
    {
        // 10 m north (+z), then 10 m east (+x): the tower is at (10, 0, 10)
        TowTrail trail = new(0.5f, 60f);
        for (int step = 0; step <= 20; step++)
        {
            trail.Add(0f, 0f, step * 0.5f);
        }
        for (int step = 1; step <= 20; step++)
        {
            trail.Add(step * 0.5f, 0f, 10f);
        }

        (float X, float Y, float Z)? behind = trail.PointBehind(14f);

        // 10 m back along the east leg to the corner (0, 0, 10), then 4 m more back down the north leg: (0, 0, 6). The straight line from the tower, 14 m back, would be at (0.2, 0, 5.1) off the path: that is the diagonal
        behind!.Value.X.Should().BeApproximately(0f, 0.001f);
        behind.Value.Z.Should().BeApproximately(6f, 0.001f);
        Distance(behind.Value, (10f, 0f, 10f)).Should().BeLessThan(14f, "the straight distance to the tower is shorter than the way it went");
    }

    [TestMethod]
    public void AShortPathGivesItsOldestPoint()
    {
        TowTrail trail = new(0.5f, 40f);
        Drive(trail, (3f, 0f, 0f), (3f, 0f, 1f), (3f, 0f, 2f));

        (float X, float Y, float Z)? behind = trail.PointBehind(8.75f);

        behind!.Value.Should().Be((3f, 0f, 0f), "the vehicle stays where it was hooked until the tower has gone far enough");
    }

    [TestMethod]
    public void ADistanceOfZeroIsTheTowerItselfAndNegativeIsTreatedAsZero()
    {
        TowTrail trail = new(0.5f, 40f);
        Drive(trail, (0f, 0f, 0f), (0f, 0f, 2f), (0f, 0f, 4f));

        trail.PointBehind(0f)!.Value.Should().Be((0f, 0f, 4f));
        trail.PointBehind(-3f)!.Value.Should().Be((0f, 0f, 4f));
    }

    [TestMethod]
    public void AMoveSmallerThanTheSpacingLeavesNoBreadcrumbButMovesTheHead()
    {
        TowTrail trail = new(0.5f, 40f);
        trail.Add(0f, 0f, 0f);
        trail.Add(0f, 0f, 0.2f);
        trail.Add(0f, 0f, 0.4f);

        trail.Count.Should().Be(1);
        trail.PointBehind(0.1f)!.Value.Z.Should().BeApproximately(0.3f, 0.001f, "measured from the head, not from the last breadcrumb");
    }

    [TestMethod]
    public void OldBreadcrumbsAreDroppedBeyondTheKeptLengthAndTheDistanceBehindStillWorks()
    {
        TowTrail trail = new(0.5f, 20f);
        for (int step = 0; step <= 400; step++)
        {
            trail.Add(0f, 0f, step * 0.5f); // 200 m
        }

        trail.Count.Should().BeLessThan(60, "20 m of path at 0.5 m is 40 breadcrumbs and a few more");
        trail.Length.Should().BeGreaterThanOrEqualTo(19f).And.BeLessThanOrEqualTo(22f);
        trail.PointBehind(8.75f)!.Value.Z.Should().BeApproximately(200f - 8.75f, 0.001f);
        trail.PointBehind(500f)!.Value.Z.Should().BeGreaterThan(170f, "a path that was cut off gives its oldest kept point");
    }

    [TestMethod]
    public void ClearingStartsOver()
    {
        TowTrail trail = new(0.5f, 40f);
        Drive(trail, (0f, 0f, 0f), (0f, 0f, 3f));

        trail.Clear();

        trail.HasHead.Should().BeFalse();
        trail.Count.Should().Be(0);
        trail.PointBehind(1f).Should().BeNull();
    }

    [TestMethod]
    public void TheKeptLengthIsTwiceTheLongestCablePlusAMargin()
    {
        TowRules.TrailKeepLength(25f).Should().Be(60f);
        TowRules.TrailKeepLength(-5f).Should().Be(10f);
        TowRules.TRAIL_SPACING.Should().Be(0.5f);
    }

    private static float Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}
