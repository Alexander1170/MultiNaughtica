using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.GameLogic;

/// <summary>The helm's copy of the map and the buttons of the helm HUD: where the map hangs, how big it is, which rows of the button column are free, and when the solar "+" shows.</summary>
[TestClass]
public class HelmLayoutTest
{
    [TestMethod]
    public void TheMapHangsWhereTheGamesOwnHelmMapHungToThePilotsRightAndIsFifteenPercentSmallerThanTheOldOne()
    {
        (float x, float y, float z) = HelmLayout.MapCenter;

        x.Should().BeApproximately(-1.347f, 1e-4f, "the place of SonarMap_Small in the Cyclops scene");
        y.Should().BeApproximately(0.525f, 1e-4f);
        z.Should().BeApproximately(-24.365f, 1e-4f);
        x.Should().BeLessThan(HelmLayout.SeatEye.X - 1f, "to the pilot's right, which is -x, more than a metre from the eyes");
        y.Should().BeLessThan(HelmLayout.SeatEye.Y, "a little below the eyes, so that the tilted disc is looked down on");
        HelmLayout.RADIUS.Should().BeApproximately(1.25f * 0.85f, 1e-4f, "the old helm hologram's 130 m at 0.00962 units per metre was a radius of 1.25 m");
        z.Should().BeGreaterThan(-26.5f, "the nose of the hull is at about -27");
        HelmLayout.SeatEye.Z.Should().BeGreaterThan(z, "the seat is behind the map");
    }

    [TestMethod]
    public void TheRangeIsThatOfTheNavigatorButNeverMoreThanTheOldDisplayShowed()
    {
        HelmLayout.RangeFor(300f).Should().Be(130f);
        HelmLayout.RangeFor(130f).Should().Be(130f);
        HelmLayout.RangeFor(90f).Should().Be(90f);
        HelmLayout.RangeFor(2f).Should().Be(10f, "at least a few metres");
        HelmLayout.RangeFor(float.NaN).Should().Be(130f);
    }

    [TestMethod]
    public void TheMapIsTurnedTowardsThePilotsSeat()
    {
        // Unity turns +x to (cos, 0, -sin): the frame's +x, which it is tilted towards, must point from the middle of the map at the seat
        double radians = HelmLayout.FRAME_YAW * Math.PI / 180.0;
        double dx = HelmLayout.SeatEye.X - HelmLayout.MapCenter.X;
        double dz = HelmLayout.SeatEye.Z - HelmLayout.MapCenter.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);
        Math.Cos(radians).Should().BeApproximately(dx / length, 1e-5, "towards the pilot, who is to the left of the map");
        (-Math.Sin(radians)).Should().BeApproximately(dz / length, 1e-5, "and a little back");
        NavigatorConsoleLayout.FrameYawToward(HelmLayout.SeatEye, HelmLayout.MapCenter, 0f).Should().BeApproximately(HelmLayout.FRAME_YAW, 1e-3f, "the same answer as for any viewer of the navigator's hologram");
        HelmLayout.TILT.Should().BeInRange(0f, 90f, "the helm's map leans towards the pilot, from flat to upright");
        HelmLayout.HIDE_DISTANCE.Should().BeGreaterThan(HelmLayout.SHOW_DISTANCE);
    }

    [TestMethod]
    public void TheButtonsTakeTheSonarsAndTheShieldsRowsWhileTheirModulesAreOutAndTheRowsBelowThemOtherwise()
    {
        HelmLayout.FreeRows(sonarButtonThere: false, shieldButtonThere: false, 2).Should().Equal(3, 4);
        HelmLayout.FreeRows(sonarButtonThere: false, shieldButtonThere: true, 2).Should().Equal(3, 5);
        HelmLayout.FreeRows(sonarButtonThere: true, shieldButtonThere: false, 2).Should().Equal(4, 5);
        HelmLayout.FreeRows(sonarButtonThere: true, shieldButtonThere: true, 2).Should().Equal(5, 6);
        HelmLayout.FreeRows(sonarButtonThere: false, shieldButtonThere: false, 1).Should().Equal(3);
        HelmLayout.FreeRows(sonarButtonThere: true, shieldButtonThere: true, 0).Should().BeEmpty();
    }

    [TestMethod]
    public void TheRowsOfTheColumnFollowTheStepFromTheSonarsRowToTheShieldsAndTheMapButtonIsAboveTheShareButton()
    {
        (float X, float Y) sonar = (-80f, -102f);
        (float X, float Y) shield = (-100f, -275f);

        HelmLayout.RowPosition(sonar, shield, 3).Should().Be(sonar);
        HelmLayout.RowPosition(sonar, shield, 4).Should().Be(shield);
        HelmLayout.RowPosition(sonar, shield, 5).Should().Be((-120f, -448f));
        HelmLayout.RowPosition(sonar, shield, 6).Y.Should().BeLessThan(HelmLayout.RowPosition(sonar, shield, 5).Y, "each row is lower than the last");
        IReadOnlyList<int> rows = HelmLayout.FreeRows(false, false, 2);
        HelmLayout.RowPosition(sonar, shield, rows[0]).Y.Should().BeGreaterThan(HelmLayout.RowPosition(sonar, shield, rows[1]).Y, "the map button is first, the power share under it");
    }

    [TestMethod]
    public void TheSolarPlusShowsOnlyWhileTheChargerReallyAddsPower()
    {
        SolarIndicatorRules.IsCharging(true, 1, true, 0.4f, 50f, 100f).Should().BeTrue("module in, daylight, not too deep, room in the cells");
        SolarIndicatorRules.IsCharging(false, 1, true, 0.4f, 50f, 100f).Should().BeFalse("the upgrades are off");
        SolarIndicatorRules.IsCharging(true, 0, true, 0.4f, 50f, 100f).Should().BeFalse("no module");
        SolarIndicatorRules.IsCharging(true, 1, false, 0.4f, 50f, 100f).Should().BeFalse("a game mode that does not use power");
        SolarIndicatorRules.IsCharging(true, 1, true, 0f, 50f, 100f).Should().BeFalse("night, or too deep");
        SolarIndicatorRules.IsCharging(true, 1, true, float.NaN, 50f, 100f).Should().BeFalse();
        SolarIndicatorRules.IsCharging(true, 1, true, 0.4f, 100f, 100f).Should().BeFalse("the cells are full: nothing to add");
        SolarIndicatorRules.IsCharging(true, 2, true, 0.4f, 99f, 100f).Should().BeTrue();
    }
}
