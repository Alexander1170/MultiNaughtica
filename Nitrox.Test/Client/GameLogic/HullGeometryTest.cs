using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.HullGeometry;

namespace NitroxClient.GameLogic;

/// <summary>
///     The anchors of the tow cable on the hulls: the point at the nose or the rear of the box around a hull, along its forward direction, a little outside it.
/// </summary>
[TestClass]
public class HullGeometryTest
{
    [TestMethod]
    public void ABoxReachesItsHalfSizeAlongAnAxisAndMoreAlongADiagonal()
    {
        SupportExtent(0f, 0f, 1f, 1f, 2f, 3f).Should().Be(3f);
        SupportExtent(1f, 0f, 0f, 1f, 2f, 3f).Should().Be(1f);
        SupportExtent(0f, 0f, -1f, 1f, 2f, 3f).Should().Be(3f, "the same on the other side");
        SupportExtent(0.6f, 0f, 0.8f, 1f, 2f, 3f).Should().BeApproximately(0.6f + 2.4f, 0.0001f);
    }

    [TestMethod]
    public void TheNoseIsOneMarginBeyondTheFrontOfTheBoxAndTheRearOneMarginBehindIt()
    {
        // A Seamoth-like box: 1.2 m wide, 1.5 m high, 4 m long (half sizes), forward is +z
        (float x, float y, float z) nose = EndAnchor(0f, 0f, 0f, 0.6f, 0.75f, 2f, 0f, 0f, 1f, forward: true);
        (float x, float y, float z) rear = EndAnchor(0f, 0f, 0f, 0.6f, 0.75f, 2f, 0f, 0f, 1f, forward: false);

        nose.z.Should().BeApproximately(2f + ANCHOR_MARGIN, 0.0001f);
        rear.z.Should().BeApproximately(-2f - ANCHOR_MARGIN, 0.0001f);
        nose.x.Should().Be(0f);
        nose.y.Should().Be(0f);
    }

    [TestMethod]
    public void TheBoxMayBeOffCenter()
    {
        (float x, float y, float z) rear = EndAnchor(1f, 0.5f, 3f, 0.6f, 0.75f, 2f, 0f, 0f, 1f, forward: false);

        rear.x.Should().Be(1f);
        rear.y.Should().Be(0.5f);
        rear.z.Should().BeApproximately(3f - 2f - ANCHOR_MARGIN, 0.0001f);
    }

    [TestMethod]
    public void TheDirectionDoesNotHaveToBeAUnitVector()
    {
        EndAnchor(0f, 0f, 0f, 1f, 1f, 5f, 0f, 0f, 10f, true).Z.Should().BeApproximately(5f + ANCHOR_MARGIN, 0.0001f);
    }

    [TestMethod]
    public void ATiltedDirectionReachesFurtherInTheBox()
    {
        (float x, float y, float z) anchor = EndAnchor(0f, 0f, 0f, 1f, 1f, 5f, 0f, 0.6f, 0.8f, true);

        anchor.y.Should().BeApproximately(0.6f * (0.6f * 1f + 0.8f * 5f + ANCHOR_MARGIN), 0.0001f);
        anchor.z.Should().BeApproximately(0.8f * (0.6f * 1f + 0.8f * 5f + ANCHOR_MARGIN), 0.0001f);
    }

    [TestMethod]
    public void AZeroDirectionGivesTheCenter()
    {
        EndAnchor(1f, 2f, 3f, 1f, 1f, 5f, 0f, 0f, 0f, true).Should().Be((1f, 2f, 3f));
    }

    [TestMethod]
    public void TheCableLengthIsTheDistanceBetweenTheAnchors()
    {
        Distance(0f, 0f, 0f, 3f, 4f, 0f).Should().Be(5f);
        Distance(1f, 1f, 1f, 1f, 1f, 1f).Should().Be(0f);
    }

    [TestMethod]
    public void ASlackCableHangsBelowTheLineAndATautOneDoesNot()
    {
        float middleSlack = Sag(4f, 8.75f, 0.5f, 1.5f);

        middleSlack.Should().BeGreaterThan(0f).And.BeLessThanOrEqualTo(1.5f);
        Sag(4f, 8.75f, 0f, 1.5f).Should().Be(0f, "the ends are on the vehicles");
        Sag(4f, 8.75f, 1f, 1.5f).Should().Be(0f);
        Sag(9f, 8.75f, 0.5f, 1.5f).Should().Be(0f, "a taut cable is straight");
        Sag(20f, 8.75f, 0.5f, 1.5f).Should().Be(0f);
    }

    [TestMethod]
    public void TheSagIsDeepestInTheMiddleAndNeverMoreThanTheLimit()
    {
        float quarter = Sag(0f, 8.75f, 0.25f, 1.5f);
        float middle = Sag(0f, 8.75f, 0.5f, 1.5f);

        middle.Should().BeGreaterThan(quarter);
        middle.Should().BeLessThanOrEqualTo(1.5f);
        Sag(0f, 8.75f, 0.5f, 0f).Should().Be(0f, "a limit of 0 draws a straight cable");
    }
    // ---- which boxes can be the hull of a vehicle

    [TestMethod]
    [DataRow(0f, 0f, 0f, 1.1f, 0.8f, 2.2f)]
    [DataRow(0.1f, 0.2f, 0.3f, 1f, 1.2f, 1.1f)]
    [DataRow(0f, 0f, 0f, 4.5f, 5f, 32f)]
    public void TheBoxOfARealVehicleIsPlausible(float cx, float cy, float cz, float ex, float ey, float ez)
    {
        HullGeometry.IsPlausibleBox(cx, cy, cz, ex, ey, ez).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(0f, 0f, 0f, 0.1f, 0.1f, 0.2f, "what is left of a docked vehicle's colliders")]
    [DataRow(0f, 0f, 0f, 1.1f, 0.8f, 0.5f, "too short for a vehicle")]
    [DataRow(0f, 0f, 0f, 90f, 1f, 3f, "a huge trigger")]
    [DataRow(500f, 0f, 0f, 1.1f, 0.8f, 2.2f, "stale bounds of a collider that has not moved with its object")]
    [DataRow(float.NaN, 0f, 0f, 1.1f, 0.8f, 2.2f, "garbage")]
    [DataRow(0f, 0f, 0f, float.PositiveInfinity, 1f, 2.2f, "garbage")]
    public void ABoxThatCannotBeTheHullOfAVehicleIsNotUsed(float cx, float cy, float cz, float ex, float ey, float ez, string why)
    {
        HullGeometry.IsPlausibleBox(cx, cy, cz, ex, ey, ez).Should().BeFalse(why);
    }

    [TestMethod]
    public void TheCenterMayBeOffTheOriginByAboutTheSizeOfTheBox()
    {
        HullGeometry.IsPlausibleBox(30f, 0f, 0f, 1.1f, 1f, 33f).Should().BeTrue("the origin of a long Cyclops need not be in its middle");
    }
}
