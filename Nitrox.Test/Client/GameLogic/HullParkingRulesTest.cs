namespace NitroxClient.GameLogic;

[TestClass]
public class HullParkingRulesTest
{
    private const float STEP_10_HZ = 0.1f;
    private const float STEP_50_HZ = 0.02f;

    [TestMethod]
    public void ShouldBrake_AParkedHullWithNobodyAtTheHelm_IsTrue()
    {
        HullParkingRules.ShouldBrake(steered: false, isReplica: false, kinematic: false, destroyed: false, floodFraction: 0f).Should().BeTrue();
    }

    [TestMethod]
    public void ShouldBrake_ASteeredHull_IsFalse()
    {
        HullParkingRules.ShouldBrake(steered: true, isReplica: false, kinematic: false, destroyed: false, floodFraction: 0f).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldBrake_ACopyThatAnotherClientSimulates_IsFalse()
    {
        // Its transform is the replicator's, braking its rigidbody would only fight it
        HullParkingRules.ShouldBrake(steered: false, isReplica: true, kinematic: false, destroyed: false, floodFraction: 0f).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldBrake_AKinematicOrDestroyedHull_IsFalse()
    {
        HullParkingRules.ShouldBrake(steered: false, isReplica: false, kinematic: true, destroyed: false, floodFraction: 0f).Should().BeFalse();
        HullParkingRules.ShouldBrake(steered: false, isReplica: false, kinematic: false, destroyed: true, floodFraction: 0f).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0f, true)]
    [DataRow(0.1f, true)]
    [DataRow(HullParkingRules.FLOOD_LIMIT, true)]
    [DataRow(0.3f, false)]
    [DataRow(1f, false)]
    [DataRow(float.NaN, true)]
    public void ShouldBrake_ByHowFloodedTheHullIs(float floodFraction, bool expected)
    {
        // A hull flooded beyond the limit sinks (SubRoot.FixedUpdate) and the brake must not work against that
        HullParkingRules.ShouldBrake(steered: false, isReplica: false, kinematic: false, destroyed: false, floodFraction: floodFraction).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(HullParkingRules.RELEASE_SPEED + 0.01f)]
    [DataRow(1f)]
    [DataRow(3f)]
    [DataRow(6f)]
    public void Step_AFastHull_IsLeftAlone(float speed)
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();

        brake.Step(speed, STEP_10_HZ).Should().Be(1f);
        brake.Braking.Should().BeFalse();
    }

    [TestMethod]
    public void Step_ASlowHull_IsBrakedByTheDecayOfTheStep()
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();

        brake.Step(0.3f, STEP_10_HZ).Should().BeApproximately((float)Math.Exp(-HullParkingRules.BRAKE_RATE * STEP_10_HZ), 1e-6f);
        brake.Braking.Should().BeTrue();
    }

    [TestMethod]
    public void Step_TheDecayPerSecondDoesNotDependOnThePhysicsStep()
    {
        // 10 Hz outside, 50 Hz while the local player walks in the Cyclops: the same hull must be braked at the same pace
        HullParkingRules.Brake slow = HullParkingRules.CreateLinearBrake();
        HullParkingRules.Brake fast = HullParkingRules.CreateYawBrake();

        float perSecondAt10Hz = (float)Math.Pow(slow.Step(0.3f, STEP_10_HZ), 1f / STEP_10_HZ);
        float perSecondAt50Hz = (float)Math.Pow(fast.Step(0.03f, STEP_50_HZ), 1f / STEP_50_HZ);

        perSecondAt10Hz.Should().BeApproximately(perSecondAt50Hz, 1e-3f);
        perSecondAt10Hz.Should().BeApproximately((float)Math.Exp(-HullParkingRules.BRAKE_RATE), 1e-3f);
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(0.005f)]
    [DataRow(0.0199f)]
    public void Step_AHullBelowTheStopSpeed_IsStopped(float speed)
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();

        brake.Step(speed, STEP_10_HZ).Should().Be(0f);
    }

    [TestMethod]
    public void Step_BetweenTheBrakeAndTheReleaseSpeed_KeepsTheStateItHad()
    {
        const float between = (HullParkingRules.BRAKE_SPEED + HullParkingRules.RELEASE_SPEED) / 2f;

        HullParkingRules.Brake free = HullParkingRules.CreateLinearBrake();
        free.Step(between, STEP_10_HZ).Should().Be(1f, "a hull that was moving at that speed has not been slow yet");

        HullParkingRules.Brake braked = HullParkingRules.CreateLinearBrake();
        braked.Step(0.1f, STEP_10_HZ).Should().BeLessThan(1f);
        braked.Step(between, STEP_10_HZ).Should().BeLessThan(1f, "a braked hull stays braked until it was clearly pushed");
    }

    [TestMethod]
    public void Step_AHullPushedAboveTheReleaseSpeed_ReleasesTheBrakeUntilItIsSlowAgain()
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        brake.Step(0.1f, STEP_10_HZ).Should().BeLessThan(1f);

        brake.Step(HullParkingRules.RELEASE_SPEED + 0.1f, STEP_10_HZ).Should().Be(1f);
        brake.Braking.Should().BeFalse();
        brake.Step(HullParkingRules.BRAKE_SPEED + 0.05f, STEP_10_HZ).Should().Be(1f, "not slow enough yet");
        brake.Step(0.3f, STEP_10_HZ).Should().BeLessThan(1f);
        brake.Braking.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(float.NaN, 0.1f)]
    [DataRow(-1f, 0.1f)]
    [DataRow(0.1f, 0f)]
    [DataRow(0.1f, -0.1f)]
    [DataRow(0.1f, float.NaN)]
    public void Step_NonsenseInput_LeavesTheBodyAlone(float speed, float deltaTime)
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        brake.Step(0.1f, STEP_10_HZ);
        brake.Braking.Should().BeTrue();

        brake.Step(speed, deltaTime).Should().Be(1f);
        brake.Braking.Should().BeFalse();
    }

    [TestMethod]
    public void Reset_StartsFromAReleasedBrake()
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        brake.Step(0.1f, STEP_10_HZ);
        brake.Braking.Should().BeTrue();

        brake.Reset();

        brake.Braking.Should().BeFalse();
        // Between the limits and released: not braked (a hull that was just steered coasts down to the brake speed first)
        brake.Step((HullParkingRules.BRAKE_SPEED + HullParkingRules.RELEASE_SPEED) / 2f, STEP_10_HZ).Should().Be(1f);
    }

    [TestMethod]
    public void YawBrake_UsesItsOwnLimitsInRadiansPerSecond()
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateYawBrake();

        brake.Step(0.2f, STEP_10_HZ).Should().Be(1f, "0.2 rad/s is 11 degrees per second, the Cyclops turns on purpose");
        brake.Step(0.03f, STEP_10_HZ).Should().BeLessThan(1f);
        brake.Step(0.001f, STEP_10_HZ).Should().Be(0f);
    }

    [TestMethod]
    [DataRow(STEP_10_HZ)]
    [DataRow(STEP_50_HZ)]
    public void Step_AWeakForceWhichNeverStops_CannotMakeTheHullDriftFasterThanForceTimesStep(float step)
    {
        // The drifting of bug 11 as a model: every physics step the unknown force adds a * step to the speed, the brake runs before the step (FixedUpdate), the step then moves the hull.
        // Only for a force whose gain a * step per step is below the stop speed (0.05 m/s^2 is): for stronger ones see Step_AForceAboveTheStopBand_...
        const float acceleration = 0.05f;
        const float seconds = 60f;
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        float speed = 0f;
        float travelled = 0f;
        int steps = (int)(seconds / step);
        for (int i = 0; i < steps; i++)
        {
            speed *= brake.Step(speed, step);
            speed += acceleration * step;
            travelled += speed * step;
        }

        (travelled / seconds).Should().BeLessThanOrEqualTo(acceleration * step * 1.01f);
        // The same hull without a brake (a drag of 0.5 per second against the force) gets an order of magnitude farther in the same minute
        float freeSpeed = 0f;
        float freeTravelled = 0f;
        for (int i = 0; i < steps; i++)
        {
            freeSpeed += acceleration * step;
            freeSpeed *= 1f - 0.5f * step;
            freeTravelled += freeSpeed * step;
        }

        freeTravelled.Should().BeGreaterThan(travelled * 10f);
    }

    /// <summary>
    /// The model of the drifting of bug 11 as a physics loop: every step the unknown force adds a * step to the speed, the brake runs before the step (FixedUpdate), the step then
    /// moves the hull. Returns the average speed over the time.
    /// </summary>
    private static float DriftSpeed(float acceleration, float step, float seconds = 60f)
    {
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        float speed = 0f;
        float travelled = 0f;
        int steps = (int)(seconds / step);
        for (int i = 0; i < steps; i++)
        {
            speed *= brake.Step(speed, step);
            speed += acceleration * step;
            travelled += speed * step;
        }

        return travelled / (steps * step);
    }

    /// <summary>
    /// The closed form of the same model, as VEHICLES.md section 7 and the remarks of <see cref="HullParkingRules" /> state it.
    /// </summary>
    private static float SteadyDriftSpeed(float acceleration, float step)
    {
        float gained = acceleration * step;
        // While one step's gain is below the stop speed the next step stops it again, above it only the decay holds the speed
        return gained < HullParkingRules.STOP_SPEED ? gained : gained / (1f - (float)Math.Exp(-HullParkingRules.BRAKE_RATE * step));
    }

    [TestMethod]
    [DataRow(0.05f, STEP_10_HZ)]
    [DataRow(0.15f, STEP_10_HZ)]
    [DataRow(0.9f, STEP_50_HZ)]
    public void Step_AForceBelowTheStopBand_DriftsAtForceTimesStep(float acceleration, float step)
    {
        // a * step below the stop speed (0.2 m/s^2 at 10 Hz, 1 m/s^2 at 50 Hz): the guarantee of the brake holds
        (acceleration * step).Should().BeLessThan(HullParkingRules.STOP_SPEED);

        DriftSpeed(acceleration, step).Should().BeApproximately(acceleration * step, acceleration * step * 0.02f);
    }

    [TestMethod]
    [DataRow(0.3f, STEP_10_HZ)]
    [DataRow(0.5f, STEP_10_HZ)]
    [DataRow(1.5f, STEP_50_HZ)]
    public void Step_AForceAboveTheStopBand_DriftsAtAboutAThirdOfTheForce_NotAtForceTimesStep(float acceleration, float step)
    {
        // The limit of the brake: a * step above the stop speed, the stop never takes hold and the hull drifts at a * step / (1 - e^(-3 * step)), about 0.39 * a at 10 Hz and 0.34 * a at 50 Hz
        (acceleration * step).Should().BeGreaterThan(HullParkingRules.STOP_SPEED);

        float drift = DriftSpeed(acceleration, step);

        drift.Should().BeApproximately(SteadyDriftSpeed(acceleration, step), SteadyDriftSpeed(acceleration, step) * 0.05f);
        drift.Should().BeGreaterThan(acceleration * step * 2f, "the guarantee 'no faster than force times step' does not hold here");
        drift.Should().BeApproximately(acceleration * 0.35f, acceleration * 0.06f, "about a third of the force in m/s");
    }

    [TestMethod]
    public void Step_TheDriftJumpsWhereOneStepOfForceReachesTheStopSpeed()
    {
        // 0.19 m/s^2 and 0.21 m/s^2 at 10 Hz differ by 10 %, the drift by a factor of four: the cliff at a * step = 0.02 m/s that the doc must not hide
        float justBelow = DriftSpeed(0.19f, STEP_10_HZ);
        float justAbove = DriftSpeed(0.21f, STEP_10_HZ);

        justBelow.Should().BeApproximately(0.019f, 0.001f);
        justAbove.Should().BeGreaterThan(justBelow * 3.5f);
        justAbove.Should().BeLessThan(0.1f, "still braked: a hull without the brake drifts at about five times that");
    }

    [TestMethod]
    public void Step_AForceAboveTheStopBand_IsStillBrakedAgainstAHullWithoutTheBrake()
    {
        // A drag of 0.5 per second against the force of 0.3 m/s^2, 10 Hz, 60 s, no brake: the same model as the test above
        const float acceleration = 0.3f;
        float freeSpeed = 0f;
        float freeTravelled = 0f;
        int steps = (int)(60f / STEP_10_HZ);
        for (int i = 0; i < steps; i++)
        {
            freeSpeed += acceleration * STEP_10_HZ;
            freeSpeed *= 1f - 0.5f * STEP_10_HZ;
            freeTravelled += freeSpeed * STEP_10_HZ;
        }

        (freeTravelled / 60f).Should().BeGreaterThan(DriftSpeed(acceleration, STEP_10_HZ) * 3f);
    }

    [TestMethod]
    [DataRow(0.6f, 1f, true)]
    [DataRow(HullParkingRules.BRAKE_SPEED, 1f, true)]
    [DataRow(3f, 1f, true)]
    [DataRow(0.6f, 0.74f, false)]
    [DataRow(0.6f, 0f, false)]
    [DataRow(0.3f, 1f, false)]
    [DataRow(0f, 1f, false)]
    [DataRow(float.NaN, 1f, false)]
    [DataRow(1f, float.NaN, false)]
    public void OutrunsBrake_OnlyWhenFastAndTheBrakeDidNotAct(float horizontalSpeed, float factor, bool expected)
    {
        HullParkingRules.OutrunsBrake(horizontalSpeed, factor).Should().Be(expected);
    }

    [TestMethod]
    public void OutrunsBrake_AgreesWithTheBrakeItself()
    {
        // A hull at the brake speed or faster that was not slow before is left alone by the brake (factor 1): that is what the log line counts as "faster than the brake takes"
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        float fast = HullParkingRules.RELEASE_SPEED + 0.5f;
        HullParkingRules.OutrunsBrake(fast, brake.Step(fast, STEP_10_HZ)).Should().BeTrue();

        // and one the brake holds (slow, then between the limits) is not
        float slow = 0.2f;
        HullParkingRules.OutrunsBrake(slow, brake.Step(slow, STEP_10_HZ)).Should().BeFalse();
        float between = (HullParkingRules.BRAKE_SPEED + HullParkingRules.RELEASE_SPEED) / 2f;
        HullParkingRules.OutrunsBrake(between, brake.Step(between, STEP_10_HZ)).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0f, 0f)]
    [DataRow(0.0005f, 0f)]
    [DataRow(0.0019f, 0f)]
    [DataRow(HullParkingRules.UNEXPLAINED_STEP_MIN, HullParkingRules.UNEXPLAINED_STEP_MIN)]
    [DataRow(0.05f, 0.05f)]
    [DataRow(3f, 3f)]
    [DataRow(float.NaN, 0f)]
    [DataRow(float.PositiveInfinity, 0f)]
    [DataRow(-0.5f, 0f)]
    public void UnexplainedStep_IgnoresNoiseAndBrokenMeasurements(float difference, float expected)
    {
        HullParkingRules.UnexplainedStep(difference).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(STEP_10_HZ, STEP_10_HZ, true)]
    [DataRow(STEP_50_HZ, STEP_50_HZ, true)]
    [DataRow(STEP_10_HZ, STEP_50_HZ, false)]
    [DataRow(STEP_50_HZ, STEP_10_HZ, false)]
    [DataRow(STEP_10_HZ, 0f, false)]
    [DataRow(STEP_10_HZ, -STEP_10_HZ, false)]
    [DataRow(STEP_10_HZ, float.NaN, false)]
    [DataRow(float.NaN, STEP_10_HZ, false)]
    public void AreSameStep_OnlyForTheSamePhysicsStep(float previousStep, float step, bool expected)
    {
        // The step goes from 0.1 s to 0.02 s when the local player walks in the hull: the one comparison across that change would be a false jump
        HullParkingRules.AreSameStep(previousStep, step).Should().Be(expected);
    }

    [TestMethod]
    public void UnexplainedStep_ACleanPhysicsStep_IsExplainedByTheSpeed()
    {
        // The comparison NitroxCyclops makes: position now - position at the last step - speed now * step. A body moves by its new speed times the step, so the difference is 0
        const float step = STEP_10_HZ;
        float position = 330.123f;
        float speed = 0.04f;
        float next = position + speed * step;

        HullParkingRules.UnexplainedStep(Math.Abs(next - position - speed * step)).Should().Be(0f);
        // a write to the pose of 3 cm in between is not explained by the speed
        HullParkingRules.UnexplainedStep(Math.Abs(next + 0.03f - position - speed * step)).Should().BeApproximately(0.03f, 1e-3f);
    }

    [TestMethod]
    [DataRow(10f, 5f, 5f, 99, false)]
    [DataRow(29.9f, 5f, 5f, 99, false)]
    [DataRow(30f, 0.049f, 0.049f, 0, false)]
    [DataRow(30f, 0.05f, 0f, 0, true)]
    [DataRow(30f, 0f, 0.05f, 0, true)]
    [DataRow(30f, 0f, 0f, 1, true)]
    [DataRow(float.PositiveInfinity, 0.1f, 0f, 0, true)]
    [DataRow(float.NaN, 5f, 5f, 99, false)]
    [DataRow(float.NegativeInfinity, 5f, 5f, 99, false)]
    public void IsCreepNews_NotBeforeTheInterval_AndNotForLessThanTheDistance(float secondsSinceLastReport, float removed, float unexplained, int fastSteps, bool expected)
    {
        // The interval is 30 s and the distance 5 cm (the rows are the limits in float)
        HullParkingRules.IsCreepNews(secondsSinceLastReport, removed, unexplained, fastSteps).Should().Be(expected);
    }

    [TestMethod]
    public void IsCreepNews_ByWhatTheHullDid()
    {
        HullParkingRules.IsCreepNews(float.PositiveInfinity, 0f, 0f, 0).Should().BeFalse("a quiet hull");
        HullParkingRules.IsCreepNews(float.PositiveInfinity, HullParkingRules.REPORT_DISTANCE, 0f, 0).Should().BeTrue("the brake took 5 cm out of the speed");
        HullParkingRules.IsCreepNews(float.PositiveInfinity, 0f, HullParkingRules.REPORT_DISTANCE, 0).Should().BeTrue("something moved the hull by 5 cm without its speed: a pose write or a depenetration");
        HullParkingRules.IsCreepNews(float.PositiveInfinity, 0f, 0f, 1).Should().BeTrue("the hull outran the brake once");
        HullParkingRules.IsCreepNews(HullParkingRules.REPORT_INTERVAL, 0f, 0f, 1).Should().BeTrue("exactly the interval");
        HullParkingRules.IsCreepNews(HullParkingRules.REPORT_INTERVAL - 0.1f, 5f, 5f, 5).Should().BeFalse("at most one line per interval");
    }

    [TestMethod]
    public void Step_AHullThatCoastsDown_ArrivesAtStandstillWithoutAJump()
    {
        // A hull released by its pilot at 0.35 m/s: the factor of the first braked step is that of the decay, not 0
        HullParkingRules.Brake brake = HullParkingRules.CreateLinearBrake();
        float speed = 0.35f;
        float previous = speed;
        int steps = 0;
        while (speed > 0f && steps < 200)
        {
            speed *= brake.Step(speed, STEP_10_HZ);
            speed.Should().BeLessThan(previous);
            previous = speed;
            steps++;
        }

        speed.Should().Be(0f);
        steps.Should().BeLessThan(40, "about a second at 10 Hz");
    }
}
