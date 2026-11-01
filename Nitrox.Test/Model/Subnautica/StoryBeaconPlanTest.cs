using Nitrox.Model.Subnautica.Helper;

namespace Nitrox.Model.Subnautica;

/// <summary>
/// The beacons of the story (bug 4 of the first real play session, 2026-10-23/24: the beacon of the first lifepod message appeared, no later one did, although the played-goals of all
/// the messages had completed on both clients). The table below is the part of the game's unlock data (<c>OnGoalUnlockData</c>, read from the main scene bundle of the game) that
/// the radio messages of the session use: goal, description key of the language file, position.
/// </summary>
[TestClass]
public class StoryBeaconPlanTest
{
    private static readonly BeaconSpec lifepod3 = new("OnPlayRadioGrassy25", "Signal_Lifepod19a", -35f, -20f, 409f);
    private static readonly BeaconSpec lifepod13 = new("OnPlayRadioMushroom24", "Signal_Lifepod13", -925f, -182f, 507f);
    private static readonly BeaconSpec lifepod17 = new("OnPlayRadioGrassy21", "Signal_Lifepod17a", -516f, -93f, -56f);
    private static readonly BeaconSpec lifepod19 = new("OnPlayRadioSecondOfficer", "Signal_Lifepod19", -808f, -297f, -874f);
    private static readonly BeaconSpec lifepod12 = new("OnPlayRadioKoosh26", "Signal_Lifepod12", 1117f, -269f, 568f);
    private static readonly BeaconSpec lifepod2 = new("OnPlayRadioBloodKelp29", "Signal_Lifepod2", -480f, -501f, 1325f);

    private static HashSet<string> Completed(params string[] goals) => new(goals, StringComparer.OrdinalIgnoreCase);

    private static List<BeaconSpec> Missing(BeaconSpec[] table, HashSet<string> completed, params BeaconSpec[] standing)
    {
        return StoryBeaconPlan.Missing(table, completed.Contains, spec => standing.Any(ping => spec.IsSameSpot(ping.DescriptionKey, ping.X, ping.Y, ping.Z)));
    }

    [TestMethod]
    public void ACompleteGoalWithoutItsBeaconIsMissing()
    {
        Missing([lifepod3], Completed("OnPlayRadioGrassy25")).Should().ContainSingle().Which.Should().BeSameAs(lifepod3);
    }

    [TestMethod]
    public void ABeaconThatStandsIsNotCreatedAgain()
    {
        Missing([lifepod3], Completed("OnPlayRadioGrassy25"), lifepod3).Should().BeEmpty();
    }

    [TestMethod]
    public void AGoalThatIsNotCompleteGivesNoBeacon()
    {
        Missing([lifepod3, lifepod13], Completed("OnPlayRadioGrassy25"), lifepod3).Should().BeEmpty("the Mushroom24 message was not played yet");
    }

    [TestMethod]
    public void TheFirstBeaconStandingDoesNotCoverTheLaterOnes()
    {
        // the real play session: the beacon of the first message was there, the beacons of the three messages after it were not (the played-goals were complete on both clients)
        BeaconSpec[] table = [lifepod3, lifepod13, lifepod17, lifepod19];
        List<BeaconSpec> missing = Missing(table, Completed("OnPlayRadioGrassy25", "OnPlayRadioMushroom24", "OnPlayRadioGrassy21", "OnPlayRadioSecondOfficer"), lifepod3);

        missing.Should().Equal(lifepod13, lifepod17, lifepod19);
    }

    [TestMethod]
    public void TheMessagesOfLifepod4And6And7HaveNoBeaconAndGetNone()
    {
        // The game's unlock data has six lifepod signals (Lifepod 3, 13, 17, 19, 12, 2). Lifepod 4 (RadiationSuit), 6 (Shallows22) and 7 (Kelp28) have none: the game answers with the
        // "Signal co-ordinates corrupted" PDA line and a databank photo (the design notes (not part of this repository)). The plan restores entries of the table only, so a complete goal without an entry
        // never produces a marker, in the real session (Shallows22 was played between Grassy25 and Mushroom24) and in the worst case (all three played, nothing standing).
        BeaconSpec[] table = [lifepod3, lifepod13, lifepod17, lifepod19, lifepod12, lifepod2];
        HashSet<string> firstEvening = Completed("OnPlayRadioBounceBack", "OnPlayRadioGrassy25", "OnPlayRadioShallows22", "OnPlayRadioWarper1", "OnPlayRadioSunbeam1", "OnPlayRadioMushroom24",
                                                 "OnPlayRadioSunbeam2", "OnPlayRadioCaptainsQuartersCode", "OnPlayRadioGrassy21", "OnPlayRadioSecondOfficer");

        Missing(table, firstEvening, lifepod3).Should().Equal(lifepod13, lifepod17, lifepod19);
        Missing(table, Completed("OnPlayRadioRadiationSuit", "OnPlayRadioShallows22", "OnPlayRadioKelp28")).Should().BeEmpty();
    }

    [TestMethod]
    public void ALateJoinerOrARestartedWorldGetsEveryBeaconOfTheCompleteGoals()
    {
        // nothing stands on a client that has just joined; the completed set is the server's
        BeaconSpec[] table = [lifepod3, lifepod13, lifepod17, lifepod19];
        List<BeaconSpec> missing = Missing(table, Completed("OnPlayRadioGrassy25", "OnPlayRadioGrassy21"));

        missing.Should().Equal(lifepod3, lifepod17);
    }

    [TestMethod]
    public void TheKeysOfTheCompletedGoalsAreComparedWithoutRegardToCase()
    {
        // the game's completed set and its unlock table both ignore case, and so does the harness console (it types the key of a goal in lower case)
        Missing([lifepod13], Completed("onplayradiomushroom24")).Should().ContainSingle();
    }

    [TestMethod]
    public void TwoEntriesThatHandOutTheSameBeaconCreateItOnce()
    {
        BeaconSpec twin = new("RadioMushroom24", lifepod13.DescriptionKey, lifepod13.X + 0.5f, lifepod13.Y, lifepod13.Z);

        Missing([lifepod13, twin], Completed("OnPlayRadioMushroom24", "RadioMushroom24")).Should().ContainSingle();
    }

    [TestMethod]
    public void TheSameDescriptionAtAnotherPlaceIsAnotherBeacon()
    {
        BeaconSpec elsewhere = new("OnPlayRadioKelp28", lifepod13.DescriptionKey, lifepod13.X + 500f, lifepod13.Y, lifepod13.Z);

        Missing([lifepod13, elsewhere], Completed("OnPlayRadioMushroom24", "OnPlayRadioKelp28"), lifepod13).Should().ContainSingle().Which.Should().BeSameAs(elsewhere);
    }

    [TestMethod]
    public void EntriesWithoutAGoalOrADescriptionAreIgnored()
    {
        BeaconSpec noGoal = new(null, "Signal_Lifepod2", 0f, 0f, 0f);
        BeaconSpec noKey = new("OnPlayRadioKoosh26", string.Empty, 0f, 0f, 0f);

        Missing([noGoal, noKey], Completed("OnPlayRadioKoosh26", string.Empty)).Should().BeEmpty();
    }

    [TestMethod]
    public void APingNextToTheTargetIsTheBeaconAPingFarAwayIsNot()
    {
        lifepod3.IsSameSpot("Signal_Lifepod19a", -35f, -20f, 409f).Should().BeTrue();
        lifepod3.IsSameSpot("Signal_Lifepod19a", -34f, -20f, 409f).Should().BeTrue();
        lifepod3.IsSameSpot("Signal_Lifepod19a", -25f, -20f, 409f).Should().BeFalse();
        lifepod3.IsSameSpot("Signal_Lifepod19b", -35f, -20f, 409f).Should().BeFalse("another description is another beacon");
        lifepod3.IsSameSpot(null, -35f, -20f, 409f).Should().BeFalse();
    }

    [TestMethod]
    public void OnlyAnInitializedPingThatTheManagerDoesNotKnowNeedsToRegister()
    {
        StoryBeaconPlan.NeedsRegistering(initialized: true, registeredInManager: false).Should().BeTrue();
        StoryBeaconPlan.NeedsRegistering(initialized: true, registeredInManager: true).Should().BeFalse();
        StoryBeaconPlan.NeedsRegistering(initialized: false, registeredInManager: false).Should().BeFalse("its Start has not run yet; it registers itself");
        StoryBeaconPlan.NeedsRegistering(initialized: false, registeredInManager: true).Should().BeFalse();
    }

    [TestMethod]
    public void APlayedMessageWhoseGoalTheGameLeftOpenGetsItCompleted()
    {
        // PDALog.Add returned nothing because the other player's entry was there first: ExecutePendingRadioMessage skipped OnGoalComplete
        StoryBeaconPlan.ShouldCompletePlayedGoal("RadioMushroom24", playedGoalComplete: false).Should().BeTrue();
    }

    [TestMethod]
    public void APlayedMessageWhoseGoalTheGameCompletedIsLeftAlone()
    {
        StoryBeaconPlan.ShouldCompletePlayedGoal("RadioMushroom24", playedGoalComplete: true).Should().BeFalse();
    }

    [TestMethod]
    public void NothingIsCompletedWhenNoMessageWasPlayed()
    {
        // the queue was empty: the game returned at once
        StoryBeaconPlan.ShouldCompletePlayedGoal(null, playedGoalComplete: false).Should().BeFalse();
        StoryBeaconPlan.ShouldCompletePlayedGoal(string.Empty, playedGoalComplete: false).Should().BeFalse();
    }
}
