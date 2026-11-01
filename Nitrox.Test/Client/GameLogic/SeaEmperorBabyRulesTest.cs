namespace NitroxClient.GameLogic;

/// <summary>
///     What a client does with a Sea Emperor baby after its <c>Start</c> (<see cref="SeaEmperorBabyRules" />). The first live run of <c>story_emperor</c> showed that a baby
///     of the server never swam to the mother on the second player's client: the test "the baby has no parent" does not hold for a baby that the entity spawner parented to the cell
///     root, so the baby was never registered with the Emperor and her mother interaction never ran there. And 2 of 5 animation babies were still there after 6 minutes
///     because the end of the egg animation is an animator event that does not come while nobody looks at the egg.
/// </summary>
[TestClass]
public class SeaEmperorBabyRulesTest
{
    [TestMethod]
    public void ABabyOfTheServerHasNoMarkerAndNoEgg()
    {
        // the cell root is its parent, not an egg: the old test (parent != null) took it for a baby of the local hatch
        SeaEmperorBabyRules.Classify(isTemporary: false, markedAsHatchedHere: false, underIncubatorEgg: false).Should().Be(SeaEmperorBabyRules.BabyOrigin.SpawnedByServer);
    }

    [TestMethod]
    public void TheBabyOfTheLocalHatchIsKnownByItsMarkerOrByItsEgg()
    {
        SeaEmperorBabyRules.Classify(isTemporary: false, markedAsHatchedHere: true, underIncubatorEgg: false).Should().Be(SeaEmperorBabyRules.BabyOrigin.HatchedHere);
        SeaEmperorBabyRules.Classify(isTemporary: false, markedAsHatchedHere: false, underIncubatorEgg: true).Should().Be(SeaEmperorBabyRules.BabyOrigin.HatchedHere);
        SeaEmperorBabyRules.Classify(isTemporary: false, markedAsHatchedHere: true, underIncubatorEgg: true).Should().Be(SeaEmperorBabyRules.BabyOrigin.HatchedHere);
    }

    [TestMethod]
    public void TheAnimationBabyIsTemporaryWhateverElseIsTrue()
    {
        // the animation baby sits under the egg too
        SeaEmperorBabyRules.Classify(isTemporary: true, markedAsHatchedHere: false, underIncubatorEgg: true).Should().Be(SeaEmperorBabyRules.BabyOrigin.Temporary);
        SeaEmperorBabyRules.Classify(isTemporary: true, markedAsHatchedHere: true, underIncubatorEgg: false).Should().Be(SeaEmperorBabyRules.BabyOrigin.Temporary);
        SeaEmperorBabyRules.Classify(isTemporary: true, markedAsHatchedHere: false, underIncubatorEgg: false).Should().Be(SeaEmperorBabyRules.BabyOrigin.Temporary);
    }

    [TestMethod]
    public void ABabyOfTheServerSwimsToTheMotherAtOnce()
    {
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SpawnedByServer, emperorExists: true, hasSwimTarget: false).Should().Be(SeaEmperorBabyRules.StartAction.SwimToMother);
    }

    [TestMethod]
    public void ABabyOfTheServerWaitsForTheEmperorWhenHerCellIsNotLoadedYet()
    {
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SpawnedByServer, emperorExists: false, hasSwimTarget: false).Should().Be(SeaEmperorBabyRules.StartAction.WaitForEmperor);
    }

    [TestMethod]
    public void ABabyThatIsOnItsWayIsNeverRegisteredTwice()
    {
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SpawnedByServer, emperorExists: true, hasSwimTarget: true).Should().Be(SeaEmperorBabyRules.StartAction.LeaveAlone);
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SpawnedByServer, emperorExists: false, hasSwimTarget: true).Should().Be(SeaEmperorBabyRules.StartAction.LeaveAlone);
    }

    [TestMethod]
    public void TheHatchingBabyAndTheAnimationBabyAreNotSentByStart()
    {
        // the egg animation sends the real baby of the hatch to the mother at its end; the animation baby never goes
        foreach (bool emperorExists in new[] { true, false })
        {
            foreach (bool hasSwimTarget in new[] { true, false })
            {
                SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.HatchedHere, emperorExists, hasSwimTarget).Should().Be(SeaEmperorBabyRules.StartAction.LeaveAlone);
                SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.Temporary, emperorExists, hasSwimTarget).Should().Be(SeaEmperorBabyRules.StartAction.LeaveAlone);
            }
        }
    }

    [TestMethod]
    public void TheFiveBabiesOfTheServerTakeTheFivePlacesAtTheMotherOneAfterTheOther()
    {
        // each registered baby counts: the places 0 to 4 in the order the babies start
        for (int registered = 0; registered < 5; registered++)
        {
            SeaEmperorBabyRules.NextBabyId(registered, attachPoints: 5).Should().Be(registered);
        }
    }

    [TestMethod]
    public void ABabyAfterAllPlacesAreTakenStartsAgainAtTheFirstOne()
    {
        // the Emperor clears her set when the interaction starts, so this is a baby of a later spawn; an id past the last place would be an error of the game and a baby at the Emperor's root
        SeaEmperorBabyRules.NextBabyId(5, attachPoints: 5).Should().Be(0);
        SeaEmperorBabyRules.NextBabyId(7, attachPoints: 5).Should().Be(2);
    }

    [TestMethod]
    public void NoPlaceIsNeverNegativeOrPastTheEnd()
    {
        SeaEmperorBabyRules.NextBabyId(3, attachPoints: 0).Should().Be(0);
        SeaEmperorBabyRules.NextBabyId(-1, attachPoints: 5).Should().Be(0);
        SeaEmperorBabyRules.NextBabyId(0, attachPoints: 5).Should().Be(0);
        for (int registered = 0; registered < 40; registered++)
        {
            SeaEmperorBabyRules.NextBabyId(registered, attachPoints: 5).Should().BeInRange(0, 4);
        }
    }

    [TestMethod]
    public void TheAnimationBabyLivesLongerThanTheAnimationAndIsGoneAfterThirtySeconds()
    {
        // the animation took up to 8 s when the egg was seen
        SeaEmperorBabyRules.TemporaryBabyExpired(0f).Should().BeFalse();
        SeaEmperorBabyRules.TemporaryBabyExpired(8f).Should().BeFalse();
        SeaEmperorBabyRules.TemporaryBabyExpired(SeaEmperorBabyRules.TEMPORARY_BABY_LIFETIME_SECONDS - 0.1f).Should().BeFalse();
        SeaEmperorBabyRules.TemporaryBabyExpired(SeaEmperorBabyRules.TEMPORARY_BABY_LIFETIME_SECONDS).Should().BeTrue();
        SeaEmperorBabyRules.TemporaryBabyExpired(360f).Should().BeTrue();
        SeaEmperorBabyRules.TEMPORARY_BABY_LIFETIME_SECONDS.Should().Be(30f);
    }

    [TestMethod]
    public void ABabyWaitsOneMinuteForTheEmperorAtMost()
    {
        SeaEmperorBabyRules.EmperorWaitOver(0f).Should().BeFalse();
        SeaEmperorBabyRules.EmperorWaitOver(SeaEmperorBabyRules.EMPEROR_WAIT_SECONDS - 1f).Should().BeFalse();
        SeaEmperorBabyRules.EmperorWaitOver(SeaEmperorBabyRules.EMPEROR_WAIT_SECONDS).Should().BeTrue();
    }
}
