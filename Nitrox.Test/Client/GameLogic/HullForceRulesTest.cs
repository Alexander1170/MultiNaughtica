#if DEBUG
using System.Globalization;
using static NitroxClient.GameLogic.HullForceRules;

namespace NitroxClient.GameLogic;

/// <summary>
/// The bookkeeping of the Debug force probe (bug 11): the aggregation per caller, the impulse of a call, the rule for a physics step whose speed changed without a call that explains
/// it, the learning of the resting height. The numbers are those of the Cyclops of the live run of 2026-10-26: 12000 kg, drag 0.5, a physics step of 0.02 s while the local player
/// walks aboard, a creep of 0.0035 m/s.
/// </summary>
[TestClass]
public class HullForceRulesTest
{
    private const float MASS = 12000f;
    private const float STEP = 0.02f;

    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    // ---- the impulse of one call

    [TestMethod]
    public void LinearImpulse_AForceActsForTheStep()
    {
        // 1200 N for 0.02 s: 24 N s
        Vec3 impulse = LinearImpulse(MODE_FORCE, V(1200f, 0f, 0f), MASS, STEP);
        impulse.X.Should().BeApproximately(24f, 1e-3f);
        DeltaVelocity(impulse, MASS).X.Should().BeApproximately(0.002f, 1e-6f);
    }

    [TestMethod]
    public void LinearImpulse_AnAccelerationIsAForcePerKilogram()
    {
        // 0.2 m/s^2 for 0.02 s on 12000 kg: 48 N s, 0.004 m/s
        Vec3 impulse = LinearImpulse(MODE_ACCELERATION, V(0f, 0f, 0.2f), MASS, STEP);
        impulse.Z.Should().BeApproximately(48f, 1e-3f);
        DeltaVelocity(impulse, MASS).Z.Should().BeApproximately(0.004f, 1e-6f);
    }

    [TestMethod]
    public void LinearImpulse_AnImpulseIsTheImpulse()
    {
        Vec3 impulse = LinearImpulse(MODE_IMPULSE, V(120f, 0f, 0f), MASS, STEP);
        impulse.X.Should().Be(120f);
        DeltaVelocity(impulse, MASS).X.Should().BeApproximately(0.01f, 1e-6f);
    }

    [TestMethod]
    public void LinearImpulse_AVelocityChangeIsASpeed()
    {
        Vec3 impulse = LinearImpulse(MODE_VELOCITY_CHANGE, V(0.01f, 0f, 0f), MASS, STEP);
        impulse.X.Should().BeApproximately(120f, 1e-3f);
        DeltaVelocity(impulse, MASS).X.Should().BeApproximately(0.01f, 1e-6f);
    }

    [TestMethod]
    public void LinearImpulse_AnUnknownModeGivesNothing()
    {
        LinearImpulse(99, V(1f, 1f, 1f), MASS, STEP).Magnitude.Should().Be(0f);
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-5f)]
    [DataRow(float.NaN)]
    public void DeltaVelocity_ForAMassThatIsNotPositive_IsNothing(float mass)
    {
        DeltaVelocity(V(10f, 10f, 10f), mass).Magnitude.Should().Be(0f);
    }

    [TestMethod]
    [DataRow(MODE_FORCE, "Force")]
    [DataRow(MODE_IMPULSE, "Impulse")]
    [DataRow(MODE_VELOCITY_CHANGE, "VelocityChange")]
    [DataRow(MODE_ACCELERATION, "Acceleration")]
    [DataRow(3, "mode3")]
    public void ModeName_IsTheNameOfTheUnityForceMode(int mode, string expected)
    {
        ModeName(mode).Should().Be(expected);
    }

    [TestMethod]
    public void IsWrite_AForceIsNoWrite_AVelocityAndAPoseAre()
    {
        foreach (ForceKind kind in new[] { ForceKind.AddForce, ForceKind.AddRelativeForce, ForceKind.AddForceAtPosition, ForceKind.AddExplosionForce, ForceKind.AddTorque, ForceKind.AddRelativeTorque })
        {
            IsWrite(kind).Should().BeFalse(kind.ToString());
        }

        foreach (ForceKind kind in new[] { ForceKind.SetVelocity, ForceKind.SetAngularVelocity, ForceKind.SetPosition, ForceKind.SetRotation, ForceKind.MovePosition, ForceKind.MoveRotation, ForceKind.SetCenterOfMass, ForceKind.TransformWrite })
        {
            IsWrite(kind).Should().BeTrue(kind.ToString());
        }
    }

    [TestMethod]
    public void IsLinearForce_OnlyForcesThatMoveTheCenterOfMass()
    {
        IsLinearForce(ForceKind.AddForce).Should().BeTrue();
        IsLinearForce(ForceKind.AddRelativeForce).Should().BeTrue();
        IsLinearForce(ForceKind.AddForceAtPosition).Should().BeTrue();
        IsLinearForce(ForceKind.AddTorque).Should().BeFalse();
        IsLinearForce(ForceKind.SetVelocity).Should().BeFalse();
    }

    // ---- the aggregation per caller

    [TestMethod]
    public void Add_TwoCallers_AreCountedApart()
    {
        CallerTally tally = new() { Mass = MASS };
        tally.Add("Buoyancy.FixedUpdate", ForceKind.AddForceAtPosition, "Force", V(0f, 100f, 0f), V(0f, 2f, 0f));
        tally.Add("Buoyancy.FixedUpdate", ForceKind.AddForceAtPosition, "Force", V(0f, 300f, 0f), V(0f, 6f, 0f));
        tally.Add("WorldForces.DoFixedUpdate", ForceKind.AddForce, "Acceleration", V(0f, -9.81f, 0f), V(0f, -2354.4f, 0f));

        tally.Callers.Should().Be(2);
        tally.TotalCalls.Should().Be(3);
        Entry buoyancy = tally.Entries[0];
        buoyancy.Caller.Should().Be("Buoyancy.FixedUpdate");
        buoyancy.Calls.Should().Be(2);
        buoyancy.Sum.Y.Should().BeApproximately(400f, 1e-3f);
        buoyancy.Impulse.Y.Should().BeApproximately(8f, 1e-3f);
        buoyancy.MaxMagnitude.Should().BeApproximately(300f, 1e-3f);
        buoyancy.Mode.Should().Be("Force");
    }

    [TestMethod]
    public void Add_TheSameCallerWithTwoKinds_IsTwoEntries()
    {
        CallerTally tally = new();
        tally.Add("SubControl.FixedUpdate", ForceKind.AddForce, "Acceleration", V(0f, 0f, 1f), V(0f, 0f, 240f));
        tally.Add("SubControl.FixedUpdate", ForceKind.AddTorque, "Acceleration", V(0f, 1f, 0f), Vec3.Zero);

        tally.Callers.Should().Be(2);
        tally.Entries[0].Kind.Should().Be(ForceKind.AddForce);
        tally.Entries[1].Kind.Should().Be(ForceKind.AddTorque);
    }

    [TestMethod]
    public void Add_TwoModesOfTheSameCaller_AreMixed()
    {
        CallerTally tally = new();
        tally.Add("Mod.Push", ForceKind.AddForce, "Force", V(1f, 0f, 0f), Vec3.Zero);
        tally.Add("Mod.Push", ForceKind.AddForce, "Impulse", V(1f, 0f, 0f), Vec3.Zero);

        tally.Entries[0].Mode.Should().Be("mixed");
    }

    [TestMethod]
    public void Add_ABrokenValue_IsACallAndNothingElse()
    {
        CallerTally tally = new();
        tally.Add("Broken.Caller", ForceKind.AddForce, "Force", V(float.NaN, 0f, 0f), V(float.PositiveInfinity, 0f, 0f));

        Entry entry = tally.Entries[0];
        entry.Calls.Should().Be(1);
        entry.Sum.Magnitude.Should().Be(0f);
        entry.Impulse.Magnitude.Should().Be(0f);
        entry.MaxMagnitude.Should().Be(0f);
    }

    [TestMethod]
    public void Add_ANameThatIsEmpty_IsUnknown()
    {
        CallerTally tally = new();
        tally.Add(string.Empty, ForceKind.AddForce, "Force", V(1f, 0f, 0f), Vec3.Zero);

        tally.Entries[0].Caller.Should().Be("unknown");
    }

    [TestMethod]
    public void Add_AWrite_KeepsHowFarItMoved()
    {
        CallerTally tally = new();
        tally.Add("Mover.Push", ForceKind.SetPosition, "Move", V(0.01f, 0f, 0f), Vec3.Zero, 0.01f);
        tally.Add("Mover.Push", ForceKind.SetPosition, "Move", V(0f, 0f, -0.02f), Vec3.Zero, 0.02f);

        Entry entry = tally.Entries[0];
        entry.Moved.Should().BeApproximately(0.03f, 1e-6f);
        entry.Sum.Magnitude.Should().BeApproximately(0.0224f, 1e-4f);
    }

    // ---- the lines of a window

    [TestMethod]
    public void TakeWindow_WritesOneLineInTheFormatTheScriptsRead()
    {
        CallerTally tally = new() { Mass = MASS };
        tally.Add("Stabilizer.FixedUpdate", ForceKind.AddForceAtPosition, "Acceleration", V(0.001f, 0f, -0.003f), V(0.24f, 0f, -0.72f));
        tally.Add("Stabilizer.FixedUpdate", ForceKind.AddForceAtPosition, "Acceleration", V(0.001f, 0f, -0.003f), V(0.24f, 0f, -0.72f));

        List<string> lines = tally.TakeWindow(12.04f);

        lines.Should().ContainSingle();
        lines[0].Should().Be("t=12.0 caller=Stabilizer.FixedUpdate calls=2 sumForce=(0.00200,0.00000,-0.00600) maxMag=0.00316 mode=Acceleration kind=AddForceAtPosition impulse=(0.48000,0.00000,-1.44000) dvH=0.00013");
    }

    [TestMethod]
    public void TakeWindow_TheSecondWindowStartsEmpty()
    {
        CallerTally tally = new();
        tally.Add("A.B", ForceKind.AddForce, "Force", V(5f, 0f, 0f), V(0.1f, 0f, 0f));
        tally.TakeWindow(1f).Should().HaveCount(1);

        tally.TakeWindow(2f).Should().BeEmpty();
        // The totals are not reset by a window
        tally.Entries[0].Calls.Should().Be(1);
        tally.Entries[0].Sum.X.Should().Be(5f);
    }

    [TestMethod]
    public void TakeWindow_TheWindowPartIsNotTheTotal()
    {
        CallerTally tally = new();
        tally.Add("A.B", ForceKind.AddForce, "Force", V(5f, 0f, 0f), Vec3.Zero);
        tally.TakeWindow(1f);
        tally.Add("A.B", ForceKind.AddForce, "Force", V(7f, 0f, 0f), Vec3.Zero);

        List<string> lines = tally.TakeWindow(2f);

        lines.Should().ContainSingle();
        lines[0].Should().Contain("calls=1 sumForce=(7.00000,0.00000,0.00000)");
        tally.Entries[0].Sum.X.Should().Be(12f);
    }

    [TestMethod]
    public void TakeWindow_ATrivialForceIsNoNews()
    {
        CallerTally tally = new();
        tally.Add("Quiet.Caller", ForceKind.AddForce, "Force", V(1e-8f, 0f, 0f), Vec3.Zero);

        tally.TakeWindow(1f).Should().BeEmpty();
    }

    [TestMethod]
    public void TakeWindow_AWriteIsNewsWhateverItsSize()
    {
        CallerTally tally = new();
        tally.Add("NitroxCyclops.ApplyParkingBrake", ForceKind.SetVelocity, "Write", V(0f, 0f, 0f), Vec3.Zero);

        tally.TakeWindow(1f).Should().ContainSingle().Which.Should().Contain("kind=SetVelocity");
    }

    [TestMethod]
    public void TakeWindow_EveryCallerWithNewsGetsItsOwnLine()
    {
        CallerTally tally = new();
        tally.Add("A.One", ForceKind.AddForce, "Force", V(1f, 0f, 0f), Vec3.Zero);
        tally.Add("B.Two", ForceKind.AddTorque, "Force", V(0f, 1f, 0f), Vec3.Zero);
        tally.Add("C.Three", ForceKind.AddForce, "Force", V(0f, 0f, 1e-9f), Vec3.Zero);

        List<string> lines = tally.TakeWindow(3f);

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("caller=A.One");
        lines[1].Should().Contain("caller=B.Two");
    }

    [TestMethod]
    public void TakeWindow_TheHorizontalChangeOfSpeedIsTheImpulseOverTheMass()
    {
        CallerTally tally = new() { Mass = MASS };
        // 48 N s forward and 2000 N s up on 12000 kg: 0.004 m/s horizontal, the vertical part is not part of dvH
        tally.Add("A.B", ForceKind.AddForce, "Acceleration", V(0f, 0f, 0.2f), V(0f, 2000f, 48f));

        tally.TakeWindow(1f)[0].Should().EndWith("dvH=0.00400");
    }

    // ---- the top of the summary

    [TestMethod]
    public void Top_RanksByTheSizeOfTheVectorSum_SoThatOppositeForcesCancel()
    {
        CallerTally tally = new();
        // The two forces of the stabilizer: large, opposite, no push
        tally.Add("Stabilizer.FixedUpdate", ForceKind.AddForceAtPosition, "Acceleration", V(5f, 0f, 0f), V(100f, 0f, 0f));
        tally.Add("Stabilizer.FixedUpdate", ForceKind.AddForceAtPosition, "Acceleration", V(-5f, 0f, 0f), V(-100f, 0f, 0f));
        tally.Add("Mod.Push", ForceKind.AddForce, "Force", V(1f, 0f, 0f), V(2f, 0f, 0f));

        List<Entry> top = tally.Top(5);

        top[0].Caller.Should().Be("Mod.Push");
        top[1].Caller.Should().Be("Stabilizer.FixedUpdate");
        top[1].AbsImpulse.Should().BeApproximately(200f, 1e-3f);
    }

    [TestMethod]
    public void Top_IsCappedAtTheCount()
    {
        CallerTally tally = new();
        for (int i = 1; i <= 8; i++)
        {
            tally.Add($"Caller{i}.Do", ForceKind.AddForce, "Force", V(i, 0f, 0f), V(i, 0f, 0f));
        }

        List<Entry> top = tally.Top(TOP_COUNT);

        top.Should().HaveCount(5);
        top[0].Caller.Should().Be("Caller8.Do");
        top[4].Caller.Should().Be("Caller4.Do");
    }

    [TestMethod]
    public void Top_TiesGoToTheOneWithMoreCallsThenByName()
    {
        CallerTally tally = new();
        tally.Add("B.Same", ForceKind.AddForce, "Force", V(1f, 0f, 0f), V(1f, 0f, 0f));
        tally.Add("A.Same", ForceKind.AddForce, "Force", V(1f, 0f, 0f), V(1f, 0f, 0f));
        tally.Add("C.Many", ForceKind.AddForce, "Force", V(0.5f, 0f, 0f), V(0.5f, 0f, 0f));
        tally.Add("C.Many", ForceKind.AddForce, "Force", V(0.5f, 0f, 0f), V(0.5f, 0f, 0f));

        List<Entry> top = tally.Top(3);

        top[0].Caller.Should().Be("C.Many");
        top[1].Caller.Should().Be("A.Same");
        top[2].Caller.Should().Be("B.Same");
    }

    [TestMethod]
    public void Top_OfNothing_IsEmpty()
    {
        new CallerTally().Top(TOP_COUNT).Should().BeEmpty();
    }

    [TestMethod]
    public void FormatTopList_NamesTheCallersWithoutSpaces()
    {
        CallerTally tally = new();
        tally.Add("Mod.Push", ForceKind.AddForce, "Force", V(1f, 0f, 0f), V(2f, 0f, 0f));
        tally.Add("NitroxCyclops.ApplyParkingBrake", ForceKind.SetVelocity, "Write", V(-0.0036f, 0f, 0f), V(-43.2f, 0f, 0f));

        string list = tally.FormatTopList(TOP_COUNT);

        list.Should().Be("NitroxCyclops.ApplyParkingBrake:SetVelocity:43.2000;Mod.Push:AddForce:2.0000");
        list.Should().NotContain(" ");
        new CallerTally().FormatTopList(TOP_COUNT).Should().Be("none");
    }

    [TestMethod]
    public void FormatTop_IsOneLineOfKeyValuePairs()
    {
        CallerTally tally = new() { Mass = MASS };
        tally.Add("Mod.Push", ForceKind.AddForce, "Force", V(1f, 0f, 0f), V(24f, 0f, 0f));

        string line = tally.FormatTop(1, tally.Entries[0]);

        line.Should().Be("top rank=1 caller=Mod.Push kind=AddForce calls=1 impulse=(24.00000,0.00000,0.00000) mag=24.0000 absImpulse=24.0000 dvH=0.00200 moved=0.0000 sumForce=(1.00000,0.00000,0.00000) maxMag=1.00000 mode=Force");
    }

    // ---- a step whose speed changed without a call that explains it

    [TestMethod]
    public void UnexplainedVelocityStep_AForceThatExplainsTheChange_IsNothing()
    {
        // 0.0035 m/s gained from an AddForce of the step, nothing left over
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(0f, 0f, 0.0035f), V(0f, 0f, 0.0035f), 0f).Should().Be(0f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_AChangeWithNoCall_IsReported()
    {
        // A collision, a depenetration or a pose write: the speed jumped by 5 mm/s and no call did it
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(0.003f, 0f, 0.004f), Vec3.Zero, 0f).Should().BeApproximately(0.005f, 1e-6f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_TheWriteOfTheBrakeIsExplained()
    {
        // The brake took 0.02 m/s out of the hull: previous 0.02, the write explains -0.02, the step added nothing
        UnexplainedVelocityStep(V(0f, 0f, 0.02f), V(0f, 0f, 0f), V(0f, 0f, -0.02f), 0.01f).Should().Be(0f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_TheSpeedThatTheBrakeTookOutAndWhichCameBack_IsReported()
    {
        // The brake wrote -0.02, and after the step the hull has 0.0036 m/s again without a force that adds it: the creep of the live run
        float unexplained = UnexplainedVelocityStep(V(0f, 0f, 0.02f), V(0f, 0f, 0.0036f), V(0f, 0f, -0.02f), 0.01f);

        unexplained.Should().BeApproximately(0.0036f, 1e-5f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_TheDragOfTheStepIsExplained()
    {
        // 0.5 m/s with a drag of 0.5 and a step of 0.02 s: 1 % less after the step
        UnexplainedVelocityStep(V(0f, 0f, 0.5f), V(0f, 0f, 0.495f), Vec3.Zero, 0.5f * STEP).Should().Be(0f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_ADragThatDoesNotExplainIt_IsReported()
    {
        // The same step with a speed that did not fall at all: 5 mm/s too fast for the drag
        UnexplainedVelocityStep(V(0f, 0f, 0.5f), V(0f, 0f, 0.5f), Vec3.Zero, 0.5f * STEP).Should().BeApproximately(0.005f, 1e-5f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_OnlyTheHorizontalSpeedCounts()
    {
        // Buoyancy changes the vertical speed in every step, that is what the hull does at the surface
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(0f, 0.1f, 0f), Vec3.Zero, 0f).Should().Be(0f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_BelowTheLimit_IsNoise()
    {
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(0.0019f, 0f, 0f), Vec3.Zero, 0f).Should().Be(0f);
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(0.0021f, 0f, 0f), Vec3.Zero, 0f).Should().BeApproximately(0.0021f, 1e-6f);
    }

    [TestMethod]
    public void UnexplainedVelocityStep_ABrokenMeasurement_IsNothing()
    {
        UnexplainedVelocityStep(V(float.NaN, 0f, 0f), V(1f, 0f, 0f), Vec3.Zero, 0f).Should().Be(0f);
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(float.PositiveInfinity, 0f, 0f), Vec3.Zero, 0f).Should().Be(0f);
        UnexplainedVelocityStep(V(0f, 0f, 0f), V(1f, 0f, 0f), V(float.NaN, 0f, 0f), 0f).Should().Be(0f);
    }

    [TestMethod]
    [DataRow(float.NaN)]
    [DataRow(-1f)]
    [DataRow(2f)]
    public void UnexplainedVelocityStep_ADragThatIsNotAFraction_CountsAsNoDrag(float drag)
    {
        UnexplainedVelocityStep(V(0f, 0f, 0.5f), V(0f, 0f, 0.5f), Vec3.Zero, drag).Should().Be(0f);
    }

    // ---- the height at which a hull rests

    [TestMethod]
    public void RestDepthTracker_AHullThatNeverStandsStill_HasNoRest()
    {
        RestDepthTracker tracker = new();
        for (int i = 0; i < 100; i++)
        {
            tracker.Note(-0.62f, 0.0478f, 0.001f, i * 0.1f);
        }

        tracker.HasRest.Should().BeFalse();
        float.IsNaN(tracker.Offset(-0.62f)).Should().BeTrue();
    }

    [TestMethod]
    public void RestDepthTracker_TwoSecondsOfStandingStill_SetTheRestHeight()
    {
        RestDepthTracker tracker = new();
        for (int i = 0; i <= 25; i++)
        {
            tracker.Note(-0.923f, 0f, 0f, i * 0.1f);
        }

        tracker.HasRest.Should().BeTrue();
        tracker.RestY.Should().BeApproximately(-0.923f, 1e-6f);
        // The hull of the failing run starts 0.30 m above where it rests
        tracker.Offset(-0.625f).Should().BeApproximately(0.298f, 1e-4f);
    }

    [TestMethod]
    public void RestDepthTracker_LessThanTwoSeconds_IsNotRest()
    {
        RestDepthTracker tracker = new();
        for (int i = 0; i <= 15; i++)
        {
            tracker.Note(-0.9f, 0f, 0f, i * 0.1f);
        }

        tracker.HasRest.Should().BeFalse();
    }

    [TestMethod]
    public void RestDepthTracker_AMoveRestartsTheClock_AndKeepsTheRestHeightItLearned()
    {
        RestDepthTracker tracker = new();
        for (int i = 0; i <= 25; i++)
        {
            tracker.Note(-0.923f, 0f, 0f, i * 0.1f);
        }

        // The hull is driven and rises: the old rest height stays until the hull stands still again, at the new height
        tracker.Note(-0.6f, 0.05f, 0f, 3f);
        tracker.RestY.Should().BeApproximately(-0.923f, 1e-6f);
        for (int i = 0; i <= 25; i++)
        {
            tracker.Note(-0.6f, 0f, 0f, 4f + i * 0.1f);
        }

        tracker.RestY.Should().BeApproximately(-0.6f, 1e-6f);
    }

    [TestMethod]
    public void RestDepthTracker_ATurningHull_IsNotStill()
    {
        RestDepthTracker tracker = new();
        for (int i = 0; i <= 40; i++)
        {
            tracker.Note(-0.9f, 0f, 0.01f, i * 0.1f);
        }

        tracker.HasRest.Should().BeFalse();
    }

    [TestMethod]
    public void RestDepthTracker_BrokenNumbers_AreNeverStill()
    {
        RestDepthTracker tracker = new();
        for (int i = 0; i <= 40; i++)
        {
            tracker.Note(float.NaN, 0f, 0f, i * 0.1f);
            tracker.Note(-0.9f, float.NaN, 0f, i * 0.1f);
            tracker.Note(-0.9f, -1f, 0f, i * 0.1f);
        }

        tracker.HasRest.Should().BeFalse();
    }

    // ---- the numbers in the lines

    [TestMethod]
    public void Fixed_UsesTheDecimalPointWhateverTheCulture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Fixed(1.5f, 5).Should().Be("1.50000");
            V(0.25f, -1f, 0f).Format().Should().Be("(0.25000,-1.00000,0.00000)");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    public void FormatVector_HasNoSpaces()
    {
        FormatVector(1f, 2f, 3f, 3).Should().Be("(1.000,2.000,3.000)");
    }
}
#endif
