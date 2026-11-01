using System;
using NitroxClient.GameLogic.Gunner;

namespace NitroxClient.GameLogic;

/// <summary>The pure parts of the gunner station (docs/coop/VEHICLE_UPGRADES.md, station 2): where the turret may point, how a shot is charged and paid for, the flight of the sphere and where the console is in the hull.</summary>
[TestClass]
public class GunnerRulesTest
{
    // ---- the turret

    [TestMethod]
    public void TheYawWrapsIntoHalfATurnEachWayAndNotANumberIsTheBow()
    {
        GunnerRules.WrapYaw(190f).Should().BeApproximately(-170f, 0.001f);
        GunnerRules.WrapYaw(-181f).Should().BeApproximately(179f, 0.001f);
        GunnerRules.WrapYaw(180f).Should().Be(180f);
        GunnerRules.WrapYaw(-180f).Should().Be(180f);
        GunnerRules.WrapYaw(720f).Should().Be(0f);
        GunnerRules.WrapYaw(float.NaN).Should().Be(0f);
        GunnerRules.WrapYaw(float.PositiveInfinity).Should().Be(0f);
    }

    [TestMethod]
    public void TheHullAllowsLessDownwardAimAheadAndAsternThanAbeam()
    {
        GunnerRules.MinPitch(0f).Should().Be(-14f);
        GunnerRules.MinPitch(90f).Should().Be(-58f);
        GunnerRules.MinPitch(180f).Should().Be(-13f);
        GunnerRules.MinPitch(-30f).Should().Be(GunnerRules.MinPitch(30f), "the table is the same on both sides");
        GunnerRules.MinPitch(190f).Should().BeApproximately(GunnerRules.MinPitch(-170f), 0.001f);
        GunnerRules.MinPitch(7.5f).Should().BeApproximately(-20f, 0.001f, "half way between -14 at the bow and -26 at 15 degrees");
        for (float yaw = -180f; yaw <= 180f; yaw += 1f)
        {
            GunnerRules.MinPitch(yaw).Should().BeInRange(-65f, -10f, $"at yaw {yaw}");
        }
    }

    [TestMethod]
    public void ThePitchStaysBetweenWhatTheHullAllowsAndStraightUpAndNotANumberIsLevel()
    {
        GunnerRules.ClampPitch(0f, -90f).Should().Be(-14f);
        GunnerRules.ClampPitch(90f, -90f).Should().Be(-58f);
        GunnerRules.ClampPitch(0f, 90f).Should().Be(GunnerRules.MAX_PITCH);
        GunnerRules.ClampPitch(0f, 10f).Should().Be(10f);
        GunnerRules.ClampPitch(0f, float.NaN).Should().Be(0f);
        GunnerRules.ClampPitch(float.NaN, -20f).Should().Be(-14f, "a yaw that is no number is the bow");
    }

    [TestMethod]
    public void TheMouseTurnsTheTurretRightToStarboardAndUpUpAndTheHullPushesTheAimUp()
    {
        (float yaw, float pitch) = GunnerRules.Step(0f, 0f, 10f, 10f);

        yaw.Should().BeApproximately(10f * GunnerRules.DEGREES_PER_UNIT, 0.001f);
        pitch.Should().BeApproximately(10f * GunnerRules.DEGREES_PER_UNIT, 0.001f);

        // aimed steeply down abeam, then swung to the bow where the hull allows far less: the pitch is pushed up to the new limit
        (float swungYaw, float swungPitch) = GunnerRules.Step(60f, -50f, -60f / GunnerRules.DEGREES_PER_UNIT, 0f);
        swungYaw.Should().BeApproximately(0f, 0.01f);
        swungPitch.Should().BeApproximately(-14f, 0.01f);
    }

    [TestMethod]
    public void OneFramesMovementIsCappedAndNothingThatIsNotANumberMovesTheTurret()
    {
        (float yaw, _) = GunnerRules.Step(0f, 0f, 100000f, 0f);
        yaw.Should().BeApproximately(GunnerRules.MAX_FRAME_DELTA * GunnerRules.DEGREES_PER_UNIT, 0.001f);

        GunnerRules.Step(30f, 20f, float.NaN, float.PositiveInfinity).Should().Be((30f, 20f));
        GunnerRules.Step(float.NaN, float.NaN, 0f, 0f).Should().Be((0f, 0f));
        GunnerRules.Step(30f, 20f, 0f, 0f).Should().Be((30f, 20f));
    }

    [TestMethod]
    public void TheDirectionIsTheBowAtZeroStarboardAtNinetyAndUpAtPitchNinety()
    {
        AssertDirection(GunnerRules.Direction(0f, 0f), 0f, 0f, -1f);
        AssertDirection(GunnerRules.Direction(90f, 0f), -1f, 0f, 0f);
        AssertDirection(GunnerRules.Direction(-90f, 0f), 1f, 0f, 0f);
        AssertDirection(GunnerRules.Direction(180f, 0f), 0f, 0f, 1f);
        AssertDirection(GunnerRules.Direction(0f, 90f), 0f, 1f, 0f);
        AssertDirection(GunnerRules.Direction(0f, -45f), 0f, -0.7071f, -0.7071f);
        AssertDirection(GunnerRules.Direction(float.NaN, float.NaN), 0f, 0f, -1f);
    }

    [TestMethod]
    public void TheDirectionIsAlwaysOfLengthOneAndTheMuzzleIsInFrontOfThePivot()
    {
        foreach ((float yaw, float pitch) in new[] { (0f, 0f), (33f, -20f), (-120f, 40f), (179f, 75f) })
        {
            (float x, float y, float z) = GunnerRules.Direction(yaw, pitch);
            (x * x + y * y + z * z).Should().BeApproximately(1f, 0.0001f);
        }

        (float X, float Y, float Z) muzzle = GunnerRules.Muzzle((-0.04f, 9.93f, -3.15f), (0f, 0f, -1f));

        muzzle.Z.Should().BeApproximately(-3.15f - GunnerRules.MUZZLE_OFFSET, 0.0001f);
        muzzle.Y.Should().Be(9.93f);
    }

    [TestMethod]
    public void AScannedHorizonOnlyEverRaisesTheLimitAndNotScannedSectorsFallBackOnTheTable()
    {
        float[] scan = new float[GunnerRules.SCAN_SECTORS];
        for (int i = 0; i < scan.Length; i++)
        {
            scan[i] = float.NaN;
        }
        GunnerRules.MinPitch(90f, scan).Should().Be(GunnerRules.MinPitch(90f), "nothing scanned: the table");
        GunnerRules.MinPitch(90f, null).Should().Be(GunnerRules.MinPitch(90f));
        GunnerRules.MinPitch(90f, new float[3]).Should().Be(GunnerRules.MinPitch(90f), "a scan of the wrong size is not used");

        // the sector centred on 90 degrees (index (90 + 180) / 5 = 54) is scanned at -49: higher than the table's -58
        scan[54] = -49f;
        GunnerRules.MinPitch(90f, scan).Should().Be(-49f);
        // a scan that is lower than the table cannot let the turret go below it
        scan[0] = -75f;
        GunnerRules.MinPitch(-180f, scan).Should().Be(GunnerRules.MinPitch(180f), "the table's limit holds when the scan finds more room");
        // between a scanned sector and one that is not, the scanned one counts
        GunnerRules.MinPitch(92.5f, scan).Should().Be(-49f);
        GunnerRules.ClampPitch(90f, -70f, scan).Should().Be(-49f);
        GunnerRules.Step(90f, -48f, 0f, -100f, scan).Pitch.Should().Be(-49f);
    }

    [TestMethod]
    public void TheScannedHorizonWrapsRoundFromTheLastSectorToTheFirst()
    {
        float[] scan = new float[GunnerRules.SCAN_SECTORS];
        for (int i = 0; i < scan.Length; i++)
        {
            scan[i] = -20f;
        }
        scan[GunnerRules.SCAN_SECTORS - 1] = -30f; // centred on 175
        scan[0] = -40f; // centred on -180 (= 180)

        float between = GunnerRules.MinPitch(177.5f, scan);

        between.Should().BeApproximately(Math.Max(GunnerRules.MinPitch(177.5f), -35f), 0.01f, "half way between -30 and -40, or the table if that is higher");
    }

    // ---- the shot

    [TestMethod]
    public void TheChargeGrowsToOneInThreeSecondsAndCannotGoBackwards()
    {
        GunnerRules.ChargeAfter(0f, GunnerRules.CHARGE_SECONDS / 2f).Should().BeApproximately(0.5f, 0.0001f);
        GunnerRules.ChargeAfter(0.5f, 1.5f).Should().Be(1f);
        GunnerRules.ChargeAfter(0.9f, 100f).Should().Be(1f);
        GunnerRules.ChargeAfter(0.4f, -3f).Should().Be(0.4f);
        GunnerRules.ChargeAfter(float.NaN, 1f).Should().BeApproximately(1f / GunnerRules.CHARGE_SECONDS, 0.0001f);
        GunnerRules.ChargeAfter(2f, 0f).Should().Be(1f);
    }

    [TestMethod]
    public void ATapIsNotAShotAndTheEnergyIsTheFullChargesTimesTheCharge()
    {
        GunnerRules.IsShot(0.05f).Should().BeFalse();
        GunnerRules.IsShot(GunnerRules.MIN_CHARGE).Should().BeTrue();
        GunnerRules.IsShot(1f).Should().BeTrue();
        GunnerRules.IsShot(float.NaN).Should().BeFalse();

        GunnerRules.ShotEnergy(1f, 10f).Should().Be(10f);
        GunnerRules.ShotEnergy(0.5f, 10f).Should().Be(5f);
        GunnerRules.ShotEnergy(3f, 10f).Should().Be(10f, "a charge past full is full");
        GunnerRules.ShotEnergy(0.05f, 10f).Should().Be(0f, "a click costs nothing");
        GunnerRules.ShotEnergy(1f, 0f).Should().Be(0f, "a host can make the shots free");
        GunnerRules.ShotEnergy(1f, -4f).Should().Be(0f);
        GunnerRules.ShotEnergy(1f, float.NaN).Should().Be(0f);
    }

    [TestMethod]
    public void TheCooldownCountsDownFromTheLastShotAndNeverBlocksWithoutOne()
    {
        GunnerRules.CooldownRemaining(10f, 11f, 3f).Should().BeApproximately(2f, 0.0001f);
        GunnerRules.CooldownRemaining(10f, 13f, 3f).Should().Be(0f);
        GunnerRules.CooldownRemaining(10f, 99f, 3f).Should().Be(0f);
        GunnerRules.CooldownRemaining(10f, 10.5f, 0f).Should().Be(0f, "a cooldown of 0 is no wait");
        GunnerRules.CooldownRemaining(float.NegativeInfinity, 0f, 3f).Should().Be(0f, "no shot yet");
        GunnerRules.CooldownRemaining(float.NaN, 0f, 3f).Should().Be(0f);
    }

    [TestMethod]
    public void TheSphereFliesTheRangeItWasAskedFor()
    {
        foreach (float range in new[] { 20f, 37.5f, 60f, 90f, 120f })
        {
            (float speed, float lifeTime) = GunnerRules.SphereFlight(range);

            GunnerRules.Reach(speed, lifeTime).Should().BeApproximately(range, 0.01f, $"range {range}");
            speed.Should().BeInRange(GunnerRules.MIN_SPHERE_SPEED, GunnerRules.MAX_SPHERE_SPEED);
        }

        GunnerRules.SphereFlight(37.5f).Should().Be((25f, 3f), "the rifle's own flight");
        GunnerRules.SphereFlight(60f).Should().Be((40f, 3f));
        GunnerRules.SphereFlight(120f).Speed.Should().Be(GunnerRules.MAX_SPHERE_SPEED);
        GunnerRules.SphereFlight(120f).LifeTime.Should().Be(4f);
    }

    [TestMethod]
    public void ARangeThatIsNoNumberStillGivesAFlight()
    {
        (float speed, float lifeTime) = GunnerRules.SphereFlight(float.NaN);

        speed.Should().BeGreaterThan(0f);
        lifeTime.Should().BeGreaterThan(0f);
        GunnerRules.Reach(float.NaN, 3f).Should().Be(0f);
        GunnerRules.Reach(25f, -1f).Should().Be(0f);
    }

    [TestMethod]
    public void TheGunnerWaitsLongerForTheServerThanTheServerWaitsForThePayingPlayer()
    {
        GunnerRules.REQUEST_TIMEOUT_SECONDS.Should().BeGreaterThan(Nitrox.Model.Subnautica.DataStructures.GameLogic.CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS / 1000f);
    }

    // ---- the console in the hull

    [TestMethod]
    public void TheHandTargetStartsInFrontOfTheDeskAndStaysBetweenTheTwoScreens()
    {
        float front = GunnerStationLayout.HandTargetCenter.X + GunnerStationLayout.HandTargetSize.X / 2f;
        float minZ = GunnerStationLayout.HandTargetCenter.Z - GunnerStationLayout.HandTargetSize.Z / 2f;
        float maxZ = GunnerStationLayout.HandTargetCenter.Z + GunnerStationLayout.HandTargetSize.Z / 2f;

        front.Should().BeGreaterThan(GunnerStationLayout.DESK_AISLE_X, "its front face is before the desk's, so a ray from the eyes enters it first");
        minZ.Should().BeGreaterThan(GunnerStationLayout.SUB_NAME_MAX_Z, "clear of the sub-name screen");
        maxZ.Should().BeLessThan(GunnerStationLayout.LIGHTING_ROW_MIN_Z, "clear of the buttons of the lighting panel, which have hand targets of their own");
    }

    [TestMethod]
    public void TheHandTargetIsWhereTheGunnerLooksAndNotWhereTheyStand()
    {
        GunnerStationLayout.InsideHandTarget((-2.0f, 0.5f, -20.6f)).Should().BeTrue("in front of the desk");
        GunnerStationLayout.InsideHandTarget((-1.70f, 0.8f, -20.6f)).Should().BeTrue("the front part, where the eye ray enters");
        GunnerStationLayout.InsideHandTarget((GunnerStationLayout.StandSpot.X, 0.74f, GunnerStationLayout.StandSpot.Z)).Should().BeFalse("the eyes of the gunner must not be inside the trigger");
        GunnerStationLayout.InsideHandTarget((-2.0f, 0.5f, -19.2f)).Should().BeFalse("that is the lighting panel");
        GunnerStationLayout.InsideHandTarget((-2.0f, 0.5f, -21.6f)).Should().BeFalse("that is the sub-name screen");
    }

    [TestMethod]
    public void TheHandTargetIsNotWhereTheNavigatorsIsAndTheGunnerLooksAtTheRightDesk()
    {
        GunnerStationLayout.HandTargetCenter.X.Should().BeLessThan(0f, "the gunner's desk is the pilot's right: the hull's -x");
        GunnerStationLayout.LookAt.X.Should().BeLessThan(GunnerStationLayout.StandSpot.X, "the gunner stands in the aisle and looks to -x");
        GunnerStationLayout.LookAt.Y.Should().BeApproximately(GunnerStationLayout.DESK_TOP_Y, 0.01f);
        GunnerStationLayout.MountPosition.Y.Should().BeGreaterThan(9f, "the conning tower, above everything");
    }

    private static void AssertDirection((float X, float Y, float Z) direction, float x, float y, float z)
    {
        direction.X.Should().BeApproximately(x, 0.001f);
        direction.Y.Should().BeApproximately(y, 0.001f);
        direction.Z.Should().BeApproximately(z, 0.001f);
    }
}
