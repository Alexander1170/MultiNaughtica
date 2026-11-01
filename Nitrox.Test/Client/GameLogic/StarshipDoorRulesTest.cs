namespace NitroxClient.GameLogic;

[TestClass]
public class StarshipDoorRulesTest
{
    [TestMethod]
    public void OnlyALaserCutDoorLeavesItsStateToTheSealedComponent()
    {
        StarshipDoorRules.ReportsOwnState(StarshipDoorRules.SEALED).Should().BeFalse();
        StarshipDoorRules.ReportsOwnState(StarshipDoorRules.MANUAL).Should().BeTrue();
        StarshipDoorRules.ReportsOwnState(StarshipDoorRules.KEYCARD).Should().BeTrue();
        StarshipDoorRules.ReportsOwnState(StarshipDoorRules.POWERCELL).Should().BeTrue();
    }

    [TestMethod]
    public void ASealedDoorSaysSealedDoorAndCutOpenWithAProgressRingWhateverThePlayerHolds()
    {
        // StarshipDoor.OnHandHover, case Sealed: no check of the door's lock flag or of the tool in the player's hand
        foreach (bool locked in new[] { true, false })
        {
            StarshipDoorRules.HandText text = StarshipDoorRules.Describe(StarshipDoorRules.SEALED, locked, open: false, "open", "close");

            text.Main.Should().Be("Sealed_Door");
            text.Sub.Should().Be("SealedInstructions");
            text.ShowsProgress.Should().BeTrue();
        }
    }

    [TestMethod]
    public void ALockedManualDoorWritesNoHandTextAtAll()
    {
        // The door that the damaged wall panel next to it unlocks: a plain closed door with no interaction, with a laser cutter or without
        StarshipDoorRules.HandText text = StarshipDoorRules.Describe(StarshipDoorRules.MANUAL, locked: true, open: false, "open", "close");

        text.IsEmpty.Should().BeTrue();
        text.ShowsProgress.Should().BeFalse();
    }

    [TestMethod]
    public void AnUnlockedManualDoorOffersToOpenOrCloseIt()
    {
        StarshipDoorRules.Describe(StarshipDoorRules.MANUAL, locked: false, open: false, "OpenDoor", "CloseDoor").Main.Should().Be("OpenDoor");
        StarshipDoorRules.Describe(StarshipDoorRules.MANUAL, locked: false, open: true, "OpenDoor", "CloseDoor").Main.Should().Be("CloseDoor");
    }

    [TestMethod]
    public void KeycardAndPowerCellDoorsSayLockedDoorWithTheirInstruction()
    {
        StarshipDoorRules.HandText keycard = StarshipDoorRules.Describe(StarshipDoorRules.KEYCARD, locked: true, open: false, "open", "close");
        StarshipDoorRules.HandText powercell = StarshipDoorRules.Describe(StarshipDoorRules.POWERCELL, locked: false, open: false, "open", "close");

        keycard.Main.Should().Be("Locked_Door");
        keycard.Sub.Should().Be("DoorInstructions_Keycard");
        powercell.Main.Should().Be("Locked_Door");
        powercell.Sub.Should().Be("DoorInstructions_Powercell");
    }

    [TestMethod]
    public void AnUnknownOpenMethodWritesNothing()
    {
        StarshipDoorRules.Describe(7, locked: false, open: false, "open", "close").IsEmpty.Should().BeTrue();
    }

    [TestMethod]
    public void ALaserCutDoorGetsASealedDoorLogLineWhetherOrNotItIsLocked()
    {
        StarshipDoorRules.StartLogTag(StarshipDoorRules.SEALED, locked: false).Should().Be("SealedDoor");
        StarshipDoorRules.StartLogTag(StarshipDoorRules.SEALED, locked: true).Should().Be("SealedDoor");
    }

    [TestMethod]
    public void ALockedManualDoorGetsALockedDoorLogLineAndTheDoorThatWritesNoHandTextIsExactlyThatDoor()
    {
        StarshipDoorRules.StartLogTag(StarshipDoorRules.MANUAL, locked: true).Should().Be("LockedDoor");
        StarshipDoorRules.Describe(StarshipDoorRules.MANUAL, locked: true, open: false, "open", "close").IsEmpty.Should().BeTrue();

        // the unlocked manual door offers to open it, so it needs no line: the line is for the doors that can be a "plain closed door"
        StarshipDoorRules.StartLogTag(StarshipDoorRules.MANUAL, locked: false).Should().BeNull();
        StarshipDoorRules.Describe(StarshipDoorRules.MANUAL, locked: false, open: false, "open", "close").IsEmpty.Should().BeFalse();
    }

    [TestMethod]
    public void KeycardAndPowerCellDoorsGetNoStartLogLineBecauseTheySayLockedDoor()
    {
        foreach (int method in new[] { StarshipDoorRules.KEYCARD, StarshipDoorRules.POWERCELL })
        {
            StarshipDoorRules.StartLogTag(method, locked: true).Should().BeNull();
            StarshipDoorRules.StartLogTag(method, locked: false).Should().BeNull();
        }
    }

    [TestMethod]
    public void TheLogTagsAreTheOnesTheLiveCheckGreps()
    {
        // a harness script that is not part of this repository and sealeddoors.ps1 grep these two
        StarshipDoorRules.LOG_TAG_SEALED.Should().Be("SealedDoor");
        StarshipDoorRules.LOG_TAG_LOCKED.Should().Be("LockedDoor");
    }

    [TestMethod]
    public void ADoorThatStandsOnceWithAnIdOfItsOwnIsOk()
    {
        // one object with the id (the door itself), nothing within half a metre
        StarshipDoorRules.DescribeWorldData(stackedCopies: 0, objectsWithSameId: 1).Should().Be(StarshipDoorRules.WORLD_DATA_OK);
        // a probe that could not find the id at all (no NitroxEntity: 0 users) is not a duplicate either
        StarshipDoorRules.DescribeWorldData(stackedCopies: 0, objectsWithSameId: 0).Should().Be("ok");
    }

    [TestMethod]
    public void ADoorWithACopyOnTopOfItIsStackedAndADoorWithASharedIdIsIdShared()
    {
        // the world data of the first play session after a restart: the same wreck generated twice with other ids, the second copy's door got the id of another entity
        StarshipDoorRules.DescribeWorldData(stackedCopies: 1, objectsWithSameId: 1).Should().Be("stacked");
        StarshipDoorRules.DescribeWorldData(stackedCopies: 0, objectsWithSameId: 2).Should().Be("idshared");
        StarshipDoorRules.DescribeWorldData(stackedCopies: 3, objectsWithSameId: 2).Should().Be("stacked+idshared");
    }
}
