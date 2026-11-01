using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.GameLogic;

/// <summary>The placement of the engineer console in the engine room and the arithmetic of its damage-control cutaway (docs/coop/VEHICLE_UPGRADES.md, station 3).</summary>
[TestClass]
public class EngineerConsoleRulesTest
{
    // ---- the placement

    [TestMethod]
    public void TheHandTargetIsInFrontOfThePanelAndOfTheEngineAndInTheWalkway()
    {
        float front = EngineerConsoleLayout.HandTargetCenter.X + EngineerConsoleLayout.HandTargetSize.X / 2f;
        float back = EngineerConsoleLayout.HandTargetCenter.X - EngineerConsoleLayout.HandTargetSize.X / 2f;

        front.Should().BeGreaterThan(EngineerConsoleLayout.PanelCenter.X, "a ray from the eyes enters the trigger before it reaches the panel");
        back.Should().BeGreaterThanOrEqualTo(EngineerConsoleLayout.ENGINE_PORT_FACE_X - 0.1f, "the trigger hangs on the face of the engine, not inside it");
        front.Should().BeLessThan(EngineerConsoleLayout.PORT_WALL_X - 0.5f, "the walkway stays free to walk");
        EngineerConsoleLayout.StandSpot.X.Should().BeGreaterThan(front, "the engineer stands in the walkway, outside the trigger");
        EngineerConsoleLayout.StandSpot.X.Should().BeLessThan(EngineerConsoleLayout.PORT_WALL_X);
    }

    [TestMethod]
    public void TheHandTargetIsClearOfTheGamesUpgradeConsoleSoThatTheModulesCanStillBeInstalled()
    {
        float nearEdge = EngineerConsoleLayout.HandTargetCenter.Z - EngineerConsoleLayout.HandTargetSize.Z / 2f;
        float farEdge = EngineerConsoleLayout.UPGRADE_CONSOLE_Z + EngineerConsoleLayout.UPGRADE_CONSOLE_HALF_Z;

        nearEdge.Should().BeGreaterThan(farEdge + 0.5f, "half a metre of air between the two triggers");
    }

    [TestMethod]
    public void ThePanelIsAtAHeightThatAStandingPlayerLooksAtAndOnTheEnginesFace()
    {
        float bottom = EngineerConsoleLayout.PanelCenter.Y - EngineerConsoleLayout.PanelSize.Height / 2f;
        float top = EngineerConsoleLayout.PanelCenter.Y + EngineerConsoleLayout.PanelSize.Height / 2f;

        bottom.Should().BeGreaterThan(EngineerConsoleLayout.DECK_Y + 0.8f, "above the waist");
        top.Should().BeLessThan(EngineerConsoleLayout.DECK_Y + 2f, "below the top of the head");
        EngineerConsoleLayout.PanelCenter.X.Should().BeGreaterThan(EngineerConsoleLayout.ENGINE_PORT_FACE_X);
        EngineerConsoleLayout.LookAt.Should().Be(EngineerConsoleLayout.PanelCenter);
    }

    [TestMethod]
    public void APointInFrontOfThePanelIsInsideTheHandTargetAndAPointBehindTheWalkwayIsNot()
    {
        EngineerConsoleLayout.InsideHandTarget((1.3f, 0.5f, 9.3f)).Should().BeTrue();
        EngineerConsoleLayout.InsideHandTarget((1.55f, 0.9f, 9.9f)).Should().BeTrue("the front part, where the eye ray enters");
        EngineerConsoleLayout.InsideHandTarget((EngineerConsoleLayout.StandSpot.X, 0.74f, EngineerConsoleLayout.StandSpot.Z)).Should().BeFalse("the eyes of the engineer must not be inside it");
        EngineerConsoleLayout.InsideHandTarget((0.66f, 0.2f, 7.1f)).Should().BeFalse("that is the Upgrade Console");
    }

    // ---- the cutaway

    [TestMethod]
    public void TheHullEndsLandOnTheEdgesOfTheDrawingWithTheBowOnTheLeft()
    {
        CutawayRules.Project(CutawayRules.HULL_MIN_Z, 0f).X.Should().Be(0f);
        CutawayRules.Project(CutawayRules.HULL_MAX_Z, 0f).X.Should().Be(1f);
        CutawayRules.Project(0f, CutawayRules.HULL_MIN_Y).Y.Should().Be(0f);
        CutawayRules.Project(0f, CutawayRules.HULL_MAX_Y).Y.Should().Be(1f);
        CutawayRules.Project(0f, 0f).X.Should().BeApproximately(0.5f, 0.0001f);
    }

    [TestMethod]
    public void APointOutsideTheHullOrNoNumberStaysOnTheDrawing()
    {
        CutawayRules.Project(-500f, 500f).Should().Be((0f, 1f));
        CutawayRules.Project(500f, -500f).Should().Be((1f, 0f));
        CutawayRules.Project(float.NaN, float.NaN).Should().Be((0f, 0f));
    }

    [TestMethod]
    public void TheTenDamagePointsOfTheGamesCyclopsAllLandInsideTheDrawing()
    {
        // read from the game's scene file (hull-local x, y, z)
        (float X, float Y, float Z)[] points = [(1.06f, 6.19f, -1.99f), (-1.54f, -5.14f, -20.96f), (1.61f, -3.84f, -2.96f), (-1.70f, -2.65f, 5.90f), (1.73f, -3.56f, -20.06f),
                                                (-1.74f, -3.84f, -11.49f), (2.45f, 1.49f, 13.51f), (2.68f, 3.30f, -19.16f), (-2.84f, 3.36f, -4.71f), (3.20f, 2.59f, -8.21f)];

        foreach ((float x, float y, float z) in points)
        {
            z.Should().BeInRange(CutawayRules.HULL_MIN_Z, CutawayRules.HULL_MAX_Z);
            y.Should().BeInRange(CutawayRules.HULL_MIN_Y, CutawayRules.HULL_MAX_Y);
            x.Should().NotBe(0f);
        }
    }

    [TestMethod]
    public void TheRoomsOfTheGameHaveNames()
    {
        CutawayRules.RoomCount.Should().Be(7);
        CutawayRules.RoomName(0).Should().Be("Helm");
        CutawayRules.RoomName(3).Should().Be("Engine room");
        CutawayRules.RoomName(6).Should().Be("Lower storage");
        CutawayRules.RoomName(9).Should().Be("Hull");
        CutawayRules.RoomName(-1).Should().Be("Hull");
    }

    [TestMethod]
    public void ADirectionNamesAftForwardPortStarboardAboveAndBelowOnlyWhenItIsFarEnough()
    {
        CutawayRules.Describe(0f, 0f, 8f).Should().Be("8 m aft");
        CutawayRules.Describe(0f, 0f, -8f).Should().Be("8 m forward");
        CutawayRules.Describe(3f, 0f, 0f).Should().Be("3 m to port");
        CutawayRules.Describe(-3f, 0f, 0f).Should().Be("3 m to starboard");
        CutawayRules.Describe(0f, 4f, 0f).Should().Be("4 m above");
        CutawayRules.Describe(0f, -4f, 0f).Should().Be("4 m below");
        CutawayRules.Describe(2.4f, -3.6f, 11.4f).Should().Be("11 m aft, 2 m to port, 4 m below");
        CutawayRules.Describe(1.4f, 1.4f, 1.4f).Should().Be("right here");
    }

    [TestMethod]
    public void TheNearestBreachIsTheOneInTheShortestStraightLine()
    {
        (float X, float Y, float Z)[] breaches = [(2.45f, 1.49f, 13.51f), (-1.7f, -2.65f, 5.9f), (1.06f, 6.19f, -1.99f)];

        CutawayRules.Nearest(breaches, (0f, 0f, 7f)).Should().Be(1);
        CutawayRules.Nearest(breaches, (0f, 0f, 14f)).Should().Be(0);
        CutawayRules.Nearest(breaches, (0f, 0f, -3f)).Should().Be(2);
        CutawayRules.Nearest([], (0f, 0f, 0f)).Should().Be(-1);
    }

    [TestMethod]
    public void TheRepairHintNamesTheNearestBreachOrSaysTheHullIsTight()
    {
        (float X, float Y, float Z)[] breaches = [(-1.7f, -2.65f, 5.9f), (1.06f, 6.19f, -1.99f)];

        CutawayRules.RepairHint(breaches, (1.9f, -0.8f, 9.3f)).Should().Be("Nearest breach: 3 m forward, 4 m to starboard, 2 m below (2 in all)");
        CutawayRules.RepairHint([], (0f, 0f, 0f)).Should().Be("Hull is tight: no breaches");
    }
}
