namespace NitroxClient.GameLogic;

[TestClass]
public class PassengerHeightRulesTest
{
    // The values of the vanilla constructor of PlayerController (IL of the game): standheight 1.5, swimheight 0.5
    private const float STAND = PassengerHeightRules.STAND_HEIGHT_DEFAULT;
    private const float SWIM = PassengerHeightRules.SWIM_HEIGHT_DEFAULT;

    [TestMethod]
    public void StanceStep_WithTheVanillaHeights_IsOneMeter()
    {
        // The amount GroundMotor.SetControllerHeight lifts the root of a swimmer who gets up, and the size of the error of bug 6
        PassengerHeightRules.StanceStep(STAND, SWIM).Should().BeApproximately(1f, 0.0001f);
    }

    [TestMethod]
    public void StandingRootAboveFeet_IsTheStandHeightAndTheCameraOffset()
    {
        PassengerHeightRules.STANDING_ROOT_ABOVE_FEET_DEFAULT.Should().BeApproximately(1.55f, 0.0001f);
    }

    [TestMethod]
    public void FloorGap_AFloorExactlyAtTheFeet_IsZero()
    {
        // The ray starts 0.4 m above the expected feet, the floor is hit 0.4 m along it
        PassengerHeightRules.FloorGap(0.4f, 0.4f).Should().BeApproximately(0f, 0.0001f);
    }

    [TestMethod]
    public void FloorGap_AFloorAMeterBelowTheFeet_IsAMeter()
    {
        PassengerHeightRules.FloorGap(0.4f, 1.4f).Should().BeApproximately(1f, 0.0001f);
    }

    [TestMethod]
    public void FloorGap_FeetInTheFloor_IsNegative()
    {
        PassengerHeightRules.FloorGap(0.4f, 0.3f).Should().BeNegative();
    }

    [TestMethod]
    public void RootAboveFloor_OfAStandingBody_IsTheStandingHeight()
    {
        // A root at 0.97 above the hull's pivot on a floor at -0.58 (the pawn of the harness runs)
        PassengerHeightRules.RootAboveFloor(0.97f, -0.58f).Should().BeApproximately(PassengerHeightRules.STANDING_ROOT_ABOVE_FEET_DEFAULT, 0.0001f);
    }

    [TestMethod]
    public void IsFloating_AStepOfTheStairs_IsNot()
    {
        PassengerHeightRules.IsFloating(0.2f).Should().BeFalse();
    }

    [TestMethod]
    public void IsFloating_AMeterAboveTheFloor_Is()
    {
        PassengerHeightRules.IsFloating(1f).Should().BeTrue();
    }

    [TestMethod]
    public void IsFloating_WithoutAFloor_IsNotJudged()
    {
        PassengerHeightRules.IsFloating(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void IsFloating_FeetInTheFloor_IsNot()
    {
        PassengerHeightRules.IsFloating(-0.3f).Should().BeFalse();
    }

    [TestMethod]
    public void Classify_TwoReadingsWithinTenCentimeters_IsNone()
    {
        PassengerHeightRules.Classify(0.08f, STAND, SWIM).Should().Be(PassengerHeightRules.OffsetKind.NONE);
        PassengerHeightRules.Classify(-0.08f, STAND, SWIM).Should().Be(PassengerHeightRules.OffsetKind.NONE);
    }

    [TestMethod]
    public void Classify_AMeterToo_High_IsTheStanceStep()
    {
        // 1.02 m: the legs of a swimming pose around the root of a walker, or a walker's root on the floor of a swimmer
        PassengerHeightRules.Classify(1.02f, STAND, SWIM).Should().Be(PassengerHeightRules.OffsetKind.STANCE_STEP);
        PassengerHeightRules.Classify(-0.9f, STAND, SWIM).Should().Be(PassengerHeightRules.OffsetKind.STANCE_STEP);
    }

    [TestMethod]
    public void Classify_HalfAMeter_IsSomethingElse()
    {
        PassengerHeightRules.Classify(0.5f, STAND, SWIM).Should().Be(PassengerHeightRules.OffsetKind.OTHER);
    }

    [TestMethod]
    public void Classify_UsesTheHeightsOfThePrefab_NotTheDefaults()
    {
        // A prefab with a stand height of 1.8 and a swim height of 0.6 has a step of 1.2 m
        PassengerHeightRules.Classify(1.2f, 1.8f, 0.6f).Should().Be(PassengerHeightRules.OffsetKind.STANCE_STEP);
        PassengerHeightRules.Classify(1.0f, 1.8f, 0.6f).Should().Be(PassengerHeightRules.OffsetKind.OTHER);
    }

    [TestMethod]
    public void JudgePoseTheory_TheSwimmingPoseAboutAMeterShorterBelowTheRoot_SupportsIt()
    {
        // The swimming pose has its lowest foot 0.5 m below the root, the walking pose 1.5 m: a copy in the swimming pose around a walker's root has its legs a meter too high
        PassengerHeightRules.JudgePoseTheory(1.02f, STAND, SWIM).Should().Be(PassengerHeightRules.PoseTheory.SUPPORTED);
    }

    [TestMethod]
    public void JudgePoseTheory_TwoPosesOfTheSameLength_RefutesIt()
    {
        // Both poses hang the feet the same distance below the root: the swimming pose cannot make a meter of difference, the report has another cause
        PassengerHeightRules.JudgePoseTheory(0.05f, STAND, SWIM).Should().Be(PassengerHeightRules.PoseTheory.REFUTED);
    }

    [TestMethod]
    public void JudgePoseTheory_ADifferenceOfHalfAMeter_RefutesIt()
    {
        PassengerHeightRules.JudgePoseTheory(0.5f, STAND, SWIM).Should().Be(PassengerHeightRules.PoseTheory.REFUTED);
    }

    [TestMethod]
    public void JudgePoseTheory_AMeterTheOtherWay_RefutesIt()
    {
        // The swimming pose reaching a meter LOWER than the walking pose would put the legs of the copy into the floor, not above it
        PassengerHeightRules.JudgePoseTheory(-1.0f, STAND, SWIM).Should().Be(PassengerHeightRules.PoseTheory.REFUTED);
    }

    [TestMethod]
    public void JudgePoseTheory_NothingMeasured_IsUnknown()
    {
        PassengerHeightRules.JudgePoseTheory(float.NaN, STAND, SWIM).Should().Be(PassengerHeightRules.PoseTheory.UNKNOWN);
    }

    [TestMethod]
    public void JudgePoseTheory_UsesTheHeightsOfThePrefab()
    {
        // A prefab with a step of 1.2 m: a measured 1.0 m is no longer the stance step
        PassengerHeightRules.JudgePoseTheory(1.2f, 1.8f, 0.6f).Should().Be(PassengerHeightRules.PoseTheory.SUPPORTED);
        PassengerHeightRules.JudgePoseTheory(1.0f, 1.8f, 0.6f).Should().Be(PassengerHeightRules.PoseTheory.REFUTED);
    }

    [TestMethod]
    public void Agree_TwoReadingsOfTheSameRoot_WithinTenCentimeters()
    {
        // The live check: the passenger's height on her own client against the copy on the pilot's client
        PassengerHeightRules.Agree(0.97f, 1.03f).Should().BeTrue();
        PassengerHeightRules.Agree(0.97f, 1.97f).Should().BeFalse();
    }

    [TestMethod]
    public void IsBoardingSettled_APawnJustMade_IsNot()
    {
        PassengerHeightRules.IsBoardingSettled(0f).Should().BeFalse();
        PassengerHeightRules.IsBoardingSettled(PassengerHeightRules.BOARDING_SETTLE_SECONDS - 0.1f).Should().BeFalse();
    }

    [TestMethod]
    public void IsBoardingSettled_AfterTheSettleTime_Is()
    {
        PassengerHeightRules.IsBoardingSettled(PassengerHeightRules.BOARDING_SETTLE_SECONDS).Should().BeTrue();
        PassengerHeightRules.IsBoardingSettled(30f).Should().BeTrue();
    }

    [TestMethod]
    public void IsBoardingSettled_ForAPawnTheWatchDidNotSeeBeingMade_IsNot()
    {
        // The start time of such a pawn is NaN: no line about where it "came to rest", the watch has no start to compare it with
        PassengerHeightRules.IsBoardingSettled(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void IsPawnSilent_ACopyThatWalksOrIsYoung_IsNot()
    {
        PassengerHeightRules.IsPawnSilent(30f, walking: true).Should().BeFalse("the first pawn packet makes the copy a walker");
        PassengerHeightRules.IsPawnSilent(0.5f, walking: false).Should().BeFalse("the first pawn packet is still on its way");
        PassengerHeightRules.IsPawnSilent(PassengerHeightRules.PAWN_SILENT_AFTER_SECONDS - 0.1f, walking: false).Should().BeFalse();
    }

    [TestMethod]
    public void IsPawnSilent_APawnWithoutAnyPawnPacketForSeconds_Is()
    {
        // The sender announces a standing player twice a second: a copy with a pawn that got nothing for 3 s is fed another kind of movement
        PassengerHeightRules.IsPawnSilent(PassengerHeightRules.PAWN_SILENT_AFTER_SECONDS, walking: false).Should().BeTrue();
        PassengerHeightRules.IsPawnSilent(60f, walking: false).Should().BeTrue();
    }

    [TestMethod]
    public void IsPawnSilent_APawnTheWatchDidNotSeeBeingMade_IsNotJudged()
    {
        PassengerHeightRules.IsPawnSilent(float.NaN, walking: false).Should().BeFalse();
    }

    [TestMethod]
    public void JudgedGap_ARightRootInAWrongPose_IsTheDrawnGap()
    {
        // The expected feet are on the floor (the root is right), the lowest foot bone a meter above it (the swimming pose around a walker's root)
        PassengerHeightRules.JudgedGap(0.02f, 1.0f).Should().BeApproximately(1.0f, 0.0001f);
        PassengerHeightRules.IsFloating(PassengerHeightRules.JudgedGap(0.02f, 1.0f)).Should().BeTrue();
    }

    [TestMethod]
    public void JudgedGap_AWrongRootInARightPose_IsTheExpectedGap()
    {
        PassengerHeightRules.JudgedGap(1.0f, 1.02f).Should().BeApproximately(1.02f, 0.0001f);
        PassengerHeightRules.JudgedGap(1.0f, 0.1f).Should().BeApproximately(1.0f, 0.0001f);
    }

    [TestMethod]
    public void JudgedGap_WithoutFootBones_IsTheExpectedGap()
    {
        PassengerHeightRules.JudgedGap(0.3f, float.NaN).Should().BeApproximately(0.3f, 0.0001f);
    }

    [TestMethod]
    public void JudgedGap_WithoutAFloor_IsNotANumber()
    {
        float.IsNaN(PassengerHeightRules.JudgedGap(float.NaN, float.NaN)).Should().BeTrue();
        PassengerHeightRules.IsFloating(PassengerHeightRules.JudgedGap(float.NaN, float.NaN)).Should().BeFalse();
    }

    [TestMethod]
    public void JudgedGap_AStandingBody_IsNotFloating()
    {
        // A walker on the floor: the soles of the model and the skin of the collider are a few centimeters
        PassengerHeightRules.IsFloating(PassengerHeightRules.JudgedGap(0.02f, 0.08f)).Should().BeFalse();
    }

    [TestMethod]
    public void FloatTracker_ABodyOnTheFloor_NeverReports()
    {
        PassengerHeightRules.FloatTracker tracker = new();

        for (int i = 0; i < 100; i++)
        {
            tracker.Step(0.02f, 1f).Should().BeFalse();
        }

        tracker.FloatingFor.Should().Be(0f);
    }

    [TestMethod]
    public void FloatTracker_AJump_DoesNotReport()
    {
        // A jump reaches a meter and lasts 0.7 s: below the 2 s a body has to stand above the floor
        PassengerHeightRules.FloatTracker tracker = new();

        tracker.Step(1.0f, 0.5f).Should().BeFalse();
        tracker.Step(1.0f, 0.5f).Should().BeFalse();
        tracker.Step(0.0f, 0.5f).Should().BeFalse();

        tracker.FloatingFor.Should().Be(0f);
    }

    [TestMethod]
    public void FloatTracker_AMeterAboveTheFloorForTwoSeconds_ReportsOnce()
    {
        PassengerHeightRules.FloatTracker tracker = new();

        tracker.Step(1.0f, 1f).Should().BeFalse();
        tracker.Step(1.0f, 1f).Should().BeTrue("2 s of floating");
        tracker.Step(1.0f, 1f).Should().BeFalse("not again within the interval");
    }

    [TestMethod]
    public void FloatTracker_StillFloatingAfterTheInterval_ReportsAgain()
    {
        PassengerHeightRules.FloatTracker tracker = new();
        int reports = 0;

        for (int i = 0; i < 70; i++)
        {
            if (tracker.Step(1.0f, 1f))
            {
                reports++;
            }
        }

        // at 2 s, 32 s and 62 s
        reports.Should().Be(3);
    }

    [TestMethod]
    public void FloatTracker_APauseOnTheFloor_StartsTheCountAgain()
    {
        PassengerHeightRules.FloatTracker tracker = new();

        tracker.Step(1.0f, 1.5f).Should().BeFalse();
        tracker.Step(0.0f, 0.5f).Should().BeFalse();
        tracker.Step(1.0f, 1.5f).Should().BeFalse("the first 1.5 s do not count any more");
        tracker.FloatingFor.Should().BeApproximately(1.5f, 0.0001f);
    }

    [TestMethod]
    public void FloatTracker_WithoutAFloor_IsNotJudgedAndStartsTheCountAgain()
    {
        PassengerHeightRules.FloatTracker tracker = new();
        tracker.Step(1.0f, 1.5f);

        tracker.Step(float.NaN, 1f).Should().BeFalse();

        tracker.FloatingFor.Should().Be(0f);
    }
}
