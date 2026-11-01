using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.GameLogic;

/// <summary>The decisions of the clearance warnings ("Clearance 4 m, port") of a Cyclops with the Cyclops Terrain Sonar (feature group 3, station 1).</summary>
[TestClass]
public class ClearanceRulesTest
{
    [TestMethod]
    [DataRow(0f, 0f, 1f, ClearanceSide.Ahead)]
    [DataRow(0f, 0f, -1f, ClearanceSide.Astern)]
    [DataRow(1f, 0f, 0f, ClearanceSide.Starboard)]
    [DataRow(-1f, 0f, 0f, ClearanceSide.Port)]
    [DataRow(0f, 1f, 0f, ClearanceSide.Above)]
    [DataRow(0f, -1f, 0f, ClearanceSide.Below)]
    [DataRow(1f, 0.2f, 0.5f, ClearanceSide.Starboard)]
    [DataRow(0.3f, -0.9f, 0.3f, ClearanceSide.Below)]
    [DataRow(-0.6f, 0.1f, 0.7f, ClearanceSide.Ahead)]
    [DataRow(0f, 0f, 0f, ClearanceSide.None)]
    [DataRow(float.NaN, 0f, 1f, ClearanceSide.None)]
    public void ADirectionIsTheSideOfItsLongestAxisInTheCyclopsFrame(float x, float y, float z, ClearanceSide side)
    {
        ClearanceRules.SideOfDirection(x, y, z).Should().Be(side);
    }

    [TestMethod]
    public void TheGapIsWhereTheSphereHitPlusItsOwnRadiusAndNeverNegative()
    {
        ClearanceRules.GapFromSphereHit(3f, 1f, 0f).Should().Be(4f);
        ClearanceRules.GapFromSphereHit(0f, 1f, 0f).Should().Be(1f);
        ClearanceRules.GapFromSphereHit(2f, 1f, -5f).Should().Be(0f, "a cast that started well inside the hull");
        ClearanceRules.GapFromSphereHit(2f, 1f, 1f).Should().Be(4f);
    }

    [TestMethod]
    public void TheLookAheadFollowsTheSpeedWithinTheWarningDistanceAndTheLimit()
    {
        ClearanceRules.LookAheadDistance(0f, 20f).Should().Be(20f, "standing still: the warning distance");
        ClearanceRules.LookAheadDistance(8f, 20f).Should().Be(40f, "five seconds at 8 m/s");
        ClearanceRules.LookAheadDistance(50f, 20f).Should().Be(100f, "never further than 100 m");
        ClearanceRules.LookAheadDistance(float.NaN, 20f).Should().Be(20f);
        ClearanceRules.LookAheadDistance(-4f, 20f).Should().Be(20f, "reversing is not a speed ahead");
    }

    [TestMethod]
    public void TheLevelsFollowTheGapAndTheWarningDistance()
    {
        ClearanceRules.LevelFor(ClearanceLevel.Clear, 25f, 20f).Should().Be(ClearanceLevel.Clear);
        ClearanceRules.LevelFor(ClearanceLevel.Clear, 19f, 20f).Should().Be(ClearanceLevel.Caution);
        ClearanceRules.LevelFor(ClearanceLevel.Clear, 6.9f, 20f).Should().Be(ClearanceLevel.Danger, "below 35 percent of the distance");
        ClearanceRules.LevelFor(ClearanceLevel.Clear, 0f, 20f).Should().Be(ClearanceLevel.Danger);
        ClearanceRules.LevelFor(ClearanceLevel.Clear, float.NaN, 20f).Should().Be(ClearanceLevel.Clear, "nothing known is not tight");
        ClearanceRules.LevelFor(ClearanceLevel.Danger, 3f, 0f).Should().Be(ClearanceLevel.Clear, "a distance of 0 switches the warnings off");
    }

    [TestMethod]
    public void ALevelIsKeptUntilTheGapGrowsFifteenPercentPastTheLimit()
    {
        ClearanceRules.LevelFor(ClearanceLevel.Caution, 21f, 20f).Should().Be(ClearanceLevel.Caution, "21 m is past the limit of 20 but not 15 percent past it");
        ClearanceRules.LevelFor(ClearanceLevel.Caution, 23.5f, 20f).Should().Be(ClearanceLevel.Clear);
        ClearanceRules.LevelFor(ClearanceLevel.Danger, 7.5f, 20f).Should().Be(ClearanceLevel.Danger, "7.5 m is past 7 m but not 15 percent past it");
        ClearanceRules.LevelFor(ClearanceLevel.Danger, 8.5f, 20f).Should().Be(ClearanceLevel.Caution);
        ClearanceRules.LevelFor(ClearanceLevel.Clear, 21f, 20f).Should().Be(ClearanceLevel.Clear, "from clear the plain limit counts");
    }

    [TestMethod]
    public void TheWarningNamesTheGapAndTheSide()
    {
        ClearanceRules.WarningText(ClearanceSide.Port, 4.2f).Should().Be("Clearance 4 m, port");
        ClearanceRules.WarningText(ClearanceSide.Starboard, 12.6f).Should().Be("Clearance 13 m, starboard");
        ClearanceRules.WarningText(ClearanceSide.Below, 0.4f).Should().Be("Clearance under 1 m, below");
        ClearanceRules.WarningText(ClearanceSide.Ahead, 12f).Should().Be("Obstacle ahead, 12 m");
        ClearanceRules.WarningText(ClearanceSide.None, 3f).Should().BeEmpty();
        ClearanceRules.WarningText(ClearanceSide.Port, float.NaN).Should().BeEmpty();
    }

    [TestMethod]
    public void TheWarningIsSaidAloudOnlyWhenItGetsWorseAndNotMoreOftenThanEveryFourSeconds()
    {
        ClearanceRules.ShouldAnnounce(ClearanceLevel.Clear, ClearanceLevel.Caution, 10f).Should().BeTrue();
        ClearanceRules.ShouldAnnounce(ClearanceLevel.Caution, ClearanceLevel.Danger, 4f).Should().BeTrue("an escalation, exactly the pause");
        ClearanceRules.ShouldAnnounce(ClearanceLevel.Caution, ClearanceLevel.Danger, 3.9f).Should().BeFalse("too soon after the last");
        ClearanceRules.ShouldAnnounce(ClearanceLevel.Caution, ClearanceLevel.Caution, 60f).Should().BeFalse("the same level is shown, not repeated");
        ClearanceRules.ShouldAnnounce(ClearanceLevel.Danger, ClearanceLevel.Caution, 60f).Should().BeFalse("better");
        ClearanceRules.ShouldAnnounce(ClearanceLevel.Caution, ClearanceLevel.Clear, 60f).Should().BeFalse();
    }
}
