namespace NitroxClient.GameLogic;

[TestClass]
public class MapRoomScanTargetRulesTest
{
    [TestCleanup]
    public void CloseEveryScope()
    {
        // the rules keep their scope in a static: a test must never leave it open for the next one
        while (MapRoomScanTargetRules.ScreenDisableRunning)
        {
            MapRoomScanTargetRules.ExitScreenDisable();
        }
    }

    [TestMethod]
    public void AChoiceOfAPlayerIsBroadcast()
    {
        // a click on a resource or on the cancel button: the target changed and the screen is not going away
        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: true).Should().BeTrue();
    }

    [TestMethod]
    public void ACallThatChangedNothingIsNotBroadcast()
    {
        // MapRoomFunctionality.Start calls StartScanning with the room's own value
        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: false).Should().BeFalse();
    }

    [TestMethod]
    public void TheCancelOfAScreenThatIsGoingAwayIsNotBroadcast()
    {
        // the room object is destroyed (a base this client lost, as the dropbase command simulates it) or its power goes down: uGUI_MapRoomScanner.OnDisable cancels its scan,
        // which must not reach the server and the other players
        MapRoomScanTargetRules.EnterScreenDisable();

        MapRoomScanTargetRules.ScreenDisableRunning.Should().BeTrue();
        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: true).Should().BeFalse();
    }

    [TestMethod]
    public void AChoiceIsBroadcastAgainOnceTheScreenIsGone()
    {
        MapRoomScanTargetRules.EnterScreenDisable();
        MapRoomScanTargetRules.ExitScreenDisable();

        MapRoomScanTargetRules.ScreenDisableRunning.Should().BeFalse();
        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: true).Should().BeTrue();
    }

    [TestMethod]
    public void TheScopeStaysOpenUntilEveryDisableHasEnded()
    {
        // two rooms losing their screen in one frame, one after the other, or one inside the other: the cancel of the second must not be sent either
        MapRoomScanTargetRules.EnterScreenDisable();
        MapRoomScanTargetRules.EnterScreenDisable();
        MapRoomScanTargetRules.ExitScreenDisable();

        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: true).Should().BeFalse();

        MapRoomScanTargetRules.ExitScreenDisable();

        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: true).Should().BeTrue();
    }

    [TestMethod]
    public void ACloseWithoutAnOpenIsHarmless()
    {
        // the finalizer of the patch runs even when its prefix did not (a patch applied while the game was inside OnDisable): the count must not go below zero
        MapRoomScanTargetRules.ExitScreenDisable();
        MapRoomScanTargetRules.ExitScreenDisable();
        MapRoomScanTargetRules.EnterScreenDisable();

        MapRoomScanTargetRules.ScreenDisableRunning.Should().BeTrue();

        MapRoomScanTargetRules.ExitScreenDisable();

        MapRoomScanTargetRules.ScreenDisableRunning.Should().BeFalse();
    }

    [TestMethod]
    public void TheCancelOfADisabledScreenIsNotBroadcastWhateverTheChangeWas()
    {
        // not only None: whatever StartScanning ended on while the screen goes away stays on this client
        MapRoomScanTargetRules.EnterScreenDisable();

        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: true).Should().BeFalse();
        MapRoomScanTargetRules.ShouldBroadcast(targetChanged: false).Should().BeFalse();
    }
}
