using NitroxClient.GameLogic.Drone;
using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.GameLogic;

/// <summary>
///     Where the drone console is in the engine room (feature group 3, station 4): clear of the game's Upgrade Console and of the engineer's hand target, which it must not cover, and on the same face of the engine.
/// </summary>
[TestClass]
public class DroneLayoutTest
{
    [TestMethod]
    public void TheHandTargetIsClearOfTheUpgradeConsoleAndOfTheEngineersTarget()
    {
        (float min, float max) = DroneLayout.HandTargetZ;

        max.Should().BeLessThan(DroneLayout.UPGRADE_CONSOLE_Z - DroneLayout.UPGRADE_CONSOLE_HALF_Z, "the game's Upgrade Console keeps its own hand target");
        max.Should().BeLessThan(EngineerConsoleLayout.HandTargetCenter.Z - EngineerConsoleLayout.HandTargetSize.Z / 2f, "and so does the engineer's panel");
        DroneLayout.ENGINEER_TARGET_MIN_Z.Should().BeApproximately(EngineerConsoleLayout.HandTargetCenter.Z - EngineerConsoleLayout.HandTargetSize.Z / 2f, 0.001f, "the constant is the engineer's target, as it was written");
        min.Should().BeGreaterThan(1f, "inside the engine room, aft of the helm");
    }

    [TestMethod]
    public void TheConsoleIsOnTheSameFaceOfTheEngineAsTheEngineersAndInFrontOfThePanel()
    {
        DroneLayout.PanelCenter.X.Should().Be(EngineerConsoleLayout.PanelCenter.X);
        DroneLayout.PanelCenter.Y.Should().Be(EngineerConsoleLayout.PanelCenter.Y);
        DroneLayout.HandTargetCenter.X.Should().BeGreaterThan(DroneLayout.PanelCenter.X, "the hand's ray from the walkway enters the trigger before it reaches the panel");
        DroneLayout.InsideHandTarget(DroneLayout.HandTargetCenter).Should().BeTrue();
        DroneLayout.InsideHandTarget(DroneLayout.LookAt).Should().BeTrue("the middle of the panel is inside the trigger, which reaches 0.3 m beyond it into the walkway, as the engineer's does");
        DroneLayout.PanelSize.Should().Be(EngineerConsoleLayout.PanelSize);
    }

    [TestMethod]
    public void TheDockIsOutsideTheHullOnThePortSideAtTheHeightOfTheConsole()
    {
        DroneLayout.DockCenter.X.Should().BeGreaterThan(EngineerConsoleLayout.PORT_WALL_X, "beyond the engine room's wall on the port side");
        DroneLayout.DockCenter.Z.Should().Be(DroneLayout.PanelCenter.Z, "level with the console");
        DroneLayout.UNDOCK_DISTANCE.Should().BeGreaterThan(DroneLayout.DockTriggerSize.Z / 2f, "a drone that leaves is put outside the trigger, or it would dock again at once");
    }
}
