namespace NitroxClient.GameLogic;

[TestClass]
public class MapRoomCameraBatteryRulesTest
{
    [TestMethod]
    public void TheCameraThatTheDockMadeItselfGetsALocalDefaultBattery()
    {
        MapRoomCameraBatteryRules.ShouldInstallLocalDefault(dockWasDeserialized: false, cameraHasNitroxId: false, cameraHasBattery: false, cameraHasDefaultBattery: true).Should().BeTrue();
    }

    [TestMethod]
    public void ADeserializedDockLeavesTheCameraAlone()
    {
        // the dock did not make the camera: it is a camera of its own with whatever battery it was saved with
        MapRoomCameraBatteryRules.ShouldInstallLocalDefault(dockWasDeserialized: true, cameraHasNitroxId: false, cameraHasBattery: false, cameraHasDefaultBattery: true).Should().BeFalse();
    }

    [TestMethod]
    public void ACameraWithAnIdGetsItsBatteryFromTheEntitySystem()
    {
        // a second, local battery would race the InstalledBatteryEntity ("Failed to add battery to EnergyMixin batterySlot")
        MapRoomCameraBatteryRules.ShouldInstallLocalDefault(dockWasDeserialized: false, cameraHasNitroxId: true, cameraHasBattery: false, cameraHasDefaultBattery: true).Should().BeFalse();
    }

    [TestMethod]
    public void ACameraThatAlreadyHasABatteryKeepsIt()
    {
        MapRoomCameraBatteryRules.ShouldInstallLocalDefault(dockWasDeserialized: false, cameraHasNitroxId: false, cameraHasBattery: true, cameraHasDefaultBattery: true).Should().BeFalse();
    }

    [TestMethod]
    public void ACameraWhosePrefabHasNoDefaultBatteryGetsNone()
    {
        MapRoomCameraBatteryRules.ShouldInstallLocalDefault(dockWasDeserialized: false, cameraHasNitroxId: false, cameraHasBattery: false, cameraHasDefaultBattery: false).Should().BeFalse();
    }

    [TestMethod]
    public void ASpawnedBatteryGoesIntoTheEmptySlotOfALiveCameraWithoutAnId()
    {
        MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive: true, batterySlotExists: true, slotHasBattery: false, cameraHasNitroxId: false).Should().BeTrue();
    }

    [TestMethod]
    public void ASpawnedBatteryIsNotAddedWhenTheDockWasDestroyedWhileThePrefabLoaded()
    {
        // a battery added to the slot of a destroyed camera is never parented or deactivated: it would stay active at the world origin, where any player could pick it up
        MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive: false, batterySlotExists: false, slotHasBattery: false, cameraHasNitroxId: false).Should().BeFalse();
        MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive: false, batterySlotExists: true, slotHasBattery: false, cameraHasNitroxId: false).Should().BeFalse();
    }

    [TestMethod]
    public void ASpawnedBatteryIsNotAddedWithoutASlot()
    {
        MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive: true, batterySlotExists: false, slotHasBattery: false, cameraHasNitroxId: false).Should().BeFalse();
    }

    [TestMethod]
    public void ASpawnedBatteryIsNotAddedWhenSomebodyFilledTheSlotMeanwhile()
    {
        // the slot refuses a second item ("Failed to add battery to EnergyMixin batterySlot")
        MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive: true, batterySlotExists: true, slotHasBattery: true, cameraHasNitroxId: false).Should().BeFalse();
    }

    [TestMethod]
    public void ASpawnedBatteryIsNotAddedWhenTheCameraBecameAnEntityMeanwhile()
    {
        // from then on the entity system gives the camera its battery
        MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive: true, batterySlotExists: true, slotHasBattery: false, cameraHasNitroxId: true).Should().BeFalse();
    }

    [TestMethod]
    public void TheBatteryOfACameraWithoutAnIdIsLocal()
    {
        MapRoomCameraBatteryRules.IsLocalOnlyBattery(ownerIsMapRoomCamera: true, ownerHasNitroxId: false).Should().BeTrue();
    }

    [TestMethod]
    public void TheBatteryOfACameraWithAnIdIsAnEntityAndIsBroadcast()
    {
        MapRoomCameraBatteryRules.IsLocalOnlyBattery(ownerIsMapRoomCamera: true, ownerHasNitroxId: true).Should().BeFalse();
    }

    [TestMethod]
    public void TheBatteryOfAnythingButACameraIsNeverLocal()
    {
        MapRoomCameraBatteryRules.IsLocalOnlyBattery(ownerIsMapRoomCamera: false, ownerHasNitroxId: false).Should().BeFalse();
        MapRoomCameraBatteryRules.IsLocalOnlyBattery(ownerIsMapRoomCamera: false, ownerHasNitroxId: true).Should().BeFalse();
    }
}
