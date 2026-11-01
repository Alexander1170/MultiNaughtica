using Nitrox.Model.Configuration;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace Nitrox.Model.Subnautica;

/// <summary>
/// The rules of the buddy oxygen (docs/coop/BUDDY_OXYGEN.md): what a giver takes in a step, what a receiver may answer, what goes back, who counts as looked at, the server's limit on how fast air is taken, and
/// the three server options with their limits.
/// </summary>
[TestClass]
public class BuddyOxygenRulesTest
{
    [TestMethod]
    public void TheDefaultsAreTheOnesOfTheBrief()
    {
        BuddyOxygenRules.DEFAULT_RATE.Should().Be(5f);
        BuddyOxygenRules.DEFAULT_RANGE.Should().Be(2.5f);
        BuddyOxygenRules.DEFAULT_FLOOR.Should().Be(15f);
        new SubnauticaServerOptions().BuddyOxygenRate.Should().Be(BuddyOxygenRules.DEFAULT_RATE);
        new SubnauticaServerOptions().BuddyOxygenRange.Should().Be(BuddyOxygenRules.DEFAULT_RANGE);
        new SubnauticaServerOptions().BuddyOxygenFloor.Should().Be(BuddyOxygenRules.DEFAULT_FLOOR);
        new SessionSettings().BuddyOxygenRate.Should().Be(BuddyOxygenRules.DEFAULT_RATE);
        new SessionSettings().BuddyOxygenRange.Should().Be(BuddyOxygenRules.DEFAULT_RANGE);
        new SessionSettings().BuddyOxygenFloor.Should().Be(BuddyOxygenRules.DEFAULT_FLOOR);
    }

    [TestMethod]
    [DataRow(5f, 5f)]
    [DataRow(0f, 0f)]
    [DataRow(-3f, 0f)]
    [DataRow(99f, 20f)]
    [DataRow(float.NaN, 5f)]
    [DataRow(float.NegativeInfinity, 5f)]
    public void TheRateStaysInsideItsLimitsAndNoNumberIsTheDefault(float option, float expected)
    {
        BuddyOxygenRules.ClampRate(option).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(2.5f, 2.5f)]
    [DataRow(0.2f, 1f)]
    [DataRow(80f, 6f)]
    [DataRow(float.NaN, 2.5f)]
    public void TheRangeStaysInsideItsLimitsAndNoNumberIsTheDefault(float option, float expected)
    {
        BuddyOxygenRules.ClampRange(option).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(15f, 15f)]
    [DataRow(0f, 0f)]
    [DataRow(-1f, 0f)]
    [DataRow(500f, 60f)]
    [DataRow(float.NaN, 15f)]
    public void TheFloorStaysInsideItsLimitsAndNoNumberIsTheDefault(float option, float expected)
    {
        BuddyOxygenRules.ClampFloor(option).Should().Be(expected);
    }

    [TestMethod]
    public void ARateOfZeroSwitchesTheSharingOff()
    {
        BuddyOxygenRules.IsEnabled(0f).Should().BeFalse();
        BuddyOxygenRules.IsEnabled(0.1f).Should().BeTrue();
        BuddyOxygenRules.IsEnabled(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void AStepIsTheRateForTheTimeSinceTheLastStep()
    {
        BuddyOxygenRules.StepAmount(5f, 0.25f, 100f, 15f).Should().BeApproximately(1.25f, 1e-5f);
        BuddyOxygenRules.StepAmount(10f, 0.5f, 100f, 15f).Should().BeApproximately(5f, 1e-5f);
    }

    [TestMethod]
    public void AFrameHitchDoesNotBecomeOneBigStep()
    {
        BuddyOxygenRules.StepAmount(5f, 30f, 100f, 15f).Should().BeApproximately(5f * BuddyOxygenRules.MAX_STEP_SECONDS, 1e-5f);
        BuddyOxygenRules.StepAmount(BuddyOxygenRules.MAX_RATE, 999f, 1000f, 0f).Should().BeLessThanOrEqualTo(BuddyOxygenRules.MAX_AMOUNT);
    }

    [TestMethod]
    public void TheGiverNeverGoesBelowTheirFloor()
    {
        // 16 s of air, floor 15: one second is spare, a step of 1.25 is cut to it
        BuddyOxygenRules.StepAmount(5f, 0.25f, 16f, 15f).Should().BeApproximately(1f, 1e-5f);
        BuddyOxygenRules.StepAmount(5f, 0.25f, 15f, 15f).Should().Be(0f);
        BuddyOxygenRules.StepAmount(5f, 0.25f, 10f, 15f).Should().Be(0f);
        BuddyOxygenRules.StepAmount(5f, 0.25f, 15.01f, 15f).Should().Be(0f, "less than the smallest step is not worth a packet");
    }

    [TestMethod]
    public void AFloorOfZeroLetsTheGiverGiveEverything()
    {
        BuddyOxygenRules.StepAmount(5f, 0.25f, 1f, 0f).Should().BeApproximately(1f, 1e-5f);
        BuddyOxygenRules.StepAmount(5f, 0.25f, 0f, 0f).Should().Be(0f);
    }

    [TestMethod]
    public void NoStepComesOfNothingOrOfGarbage()
    {
        BuddyOxygenRules.StepAmount(0f, 0.25f, 100f, 15f).Should().Be(0f, "the sharing is off");
        BuddyOxygenRules.StepAmount(5f, 0f, 100f, 15f).Should().Be(0f);
        BuddyOxygenRules.StepAmount(5f, -1f, 100f, 15f).Should().Be(0f);
        BuddyOxygenRules.StepAmount(float.NaN, 0.25f, 100f, 15f).Should().Be(0f);
        BuddyOxygenRules.StepAmount(5f, float.NaN, 100f, 15f).Should().Be(0f);
        BuddyOxygenRules.StepAmount(5f, 0.25f, float.NaN, 15f).Should().Be(0f);
    }

    [TestMethod]
    public void TheServerTakesOnlyASensibleStep()
    {
        BuddyOxygenRules.IsValidAmount(1.25f).Should().BeTrue();
        BuddyOxygenRules.IsValidAmount(BuddyOxygenRules.MAX_AMOUNT).Should().BeTrue();
        BuddyOxygenRules.IsValidAmount(BuddyOxygenRules.MAX_AMOUNT + 0.01f).Should().BeFalse();
        BuddyOxygenRules.IsValidAmount(0f).Should().BeFalse();
        BuddyOxygenRules.IsValidAmount(-1f).Should().BeFalse();
        BuddyOxygenRules.IsValidAmount(float.NaN).Should().BeFalse();
        BuddyOxygenRules.IsValidAmount(float.PositiveInfinity).Should().BeFalse();
    }

    [TestMethod]
    public void AReceiverCannotMakeAirByAcceptingMoreThanItWasGiven()
    {
        BuddyOxygenRules.SanitizeAccepted(3f, 1.25f).Should().Be(1.25f);
        BuddyOxygenRules.SanitizeAccepted(1f, 1.25f).Should().Be(1f);
        BuddyOxygenRules.SanitizeAccepted(-4f, 1.25f).Should().Be(0f);
        BuddyOxygenRules.SanitizeAccepted(float.NaN, 1.25f).Should().Be(0f);
        BuddyOxygenRules.SanitizeAccepted(float.PositiveInfinity, 1.25f).Should().Be(1.25f);
        BuddyOxygenRules.SanitizeAccepted(1f, float.NaN).Should().Be(0f);
    }

    [TestMethod]
    public void WhatTheGiverLostIsWhatTheReceiverGot()
    {
        foreach (float given in new[] { 0.05f, 0.7f, 1.25f, 3f, 12f })
        {
            foreach (float accepted in new[] { -1f, 0f, 0.01f, given / 3f, given / 2f, given, given * 2f, float.NaN })
            {
                float sanitised = BuddyOxygenRules.SanitizeAccepted(accepted, given);
                float refund = BuddyOxygenRules.Refund(given, accepted);

                (given - refund).Should().BeApproximately(sanitised, 1e-5f, $"given {given}, accepted {accepted}");
                refund.Should().BeInRange(0f, given);
            }
        }
    }

    [TestMethod]
    public void AFullTankGivesEverythingBackAndANoNumberAnswerToo()
    {
        BuddyOxygenRules.Refund(1.25f, 0f).Should().Be(1.25f);
        BuddyOxygenRules.Refund(1.25f, float.NaN).Should().Be(1.25f);
        BuddyOxygenRules.Refund(1.25f, 1.25f).Should().Be(0f);
        BuddyOxygenRules.Refund(1.25f, 9f).Should().Be(0f);
    }

    [TestMethod]
    public void TheRangeIsInclusiveAndTheServerAddsSlack()
    {
        BuddyOxygenRules.InRange(2.5f, 2.5f, 0f).Should().BeTrue();
        BuddyOxygenRules.InRange(2.51f, 2.5f, 0f).Should().BeFalse();
        BuddyOxygenRules.InRange(4.4f, 2.5f, BuddyOxygenRules.RANGE_SLACK).Should().BeTrue();
        BuddyOxygenRules.InRange(4.6f, 2.5f, BuddyOxygenRules.RANGE_SLACK).Should().BeFalse();
        BuddyOxygenRules.InRange(float.NaN, 2.5f, BuddyOxygenRules.RANGE_SLACK).Should().BeFalse();
        BuddyOxygenRules.InRange(-1f, 2.5f, BuddyOxygenRules.RANGE_SLACK).Should().BeFalse();
    }

    [TestMethod]
    public void ATeammateIsLookedAtWithinTheRangeAndTheCone()
    {
        BuddyOxygenRules.IsLookedAt(2f, 10f, 2.5f).Should().BeTrue();
        BuddyOxygenRules.IsLookedAt(3f, 10f, 2.5f).Should().BeFalse("too far");
        BuddyOxygenRules.IsLookedAt(2f, BuddyOxygenRules.CONE_DEGREES + 1f, 2.5f).Should().BeFalse("behind the shoulder");
        BuddyOxygenRules.IsLookedAt(2f, 180f, 2.5f).Should().BeFalse("behind the diver");
        BuddyOxygenRules.IsLookedAt(2f, float.NaN, 2.5f).Should().BeFalse();
    }

    [TestMethod]
    public void WhenSeveralAreInViewTheMostCentredOneIsMeant()
    {
        (float Distance, float AngleDegrees)[] candidates = [(1f, 30f), (2f, 5f), (1.5f, 20f), (0.5f, 80f)];

        BuddyOxygenRules.PickBuddy(candidates, 2.5f).Should().Be(1);
        BuddyOxygenRules.PickBuddy([], 2.5f).Should().Be(-1);
        BuddyOxygenRules.PickBuddy([(5f, 1f)], 2.5f).Should().Be(-1, "out of range");
        BuddyOxygenRules.PickBuddy([(2f, 10f), (1f, 10f)], 2.5f).Should().Be(1, "the nearer of two that are equally centred");
    }

    [TestMethod]
    public void AGiverAtThePaceOfTheRateIsNeverTurnedDown()
    {
        GiveBudget<int> budget = new();
        double now = 100.0;
        float step = BuddyOxygenRules.StepAmount(5f, BuddyOxygenRules.TICK_SECONDS, 100f, 15f);

        for (int i = 0; i < 240; i++)
        {
            // a quarter of a second between the steps, with the jitter of a network
            now += BuddyOxygenRules.TICK_SECONDS + (i % 3 == 0 ? 0.05 : -0.04);
            budget.TryTake(1, step, now, 5f).Should().BeTrue($"step {i}");
        }
    }

    [TestMethod]
    public void AGiverWhoTakesFasterThanTheRateIsTurnedDownAndGetsAirAgainWhenHeSlowsDown()
    {
        GiveBudget<int> budget = new();
        double now = 100.0;
        int accepted = 0;
        for (int i = 0; i < 100; i++)
        {
            // a modified client: 5 per second of steps of 1.25, but sent every 10 ms
            now += 0.01;
            if (budget.TryTake(1, 1.25f, now, 5f))
            {
                accepted++;
            }
        }

        // the bucket (7.5) and the refill of one second (7.5): about 15 units, 12 steps, not 100
        accepted.Should().BeInRange(5, 14);
        budget.TryTake(1, 1.25f, now, 5f).Should().BeFalse();
        budget.TryTake(1, 1.25f, now + 2.0, 5f).Should().BeTrue("a bucket that had time to fill");
    }

    [TestMethod]
    public void TheBudgetOfOneGiverIsNotTheBudgetOfAnotherAndAForgottenGiverStartsFull()
    {
        GiveBudget<int> budget = new();
        budget.TryTake(1, 7.5f, 10.0, 5f).Should().BeTrue();
        budget.TryTake(1, 1f, 10.0, 5f).Should().BeFalse("the bucket of the first giver is empty");
        budget.TryTake(2, 7.5f, 10.0, 5f).Should().BeTrue("another giver has a bucket of their own");

        budget.Forget(1);
        budget.TryTake(1, 7.5f, 10.0, 5f).Should().BeTrue();
        budget.Count.Should().Be(2);
    }

    [TestMethod]
    public void AClockThatWentBackwardsGivesNoFreeAir()
    {
        GiveBudget<int> budget = new();
        budget.TryTake(1, 7.5f, 1000.0, 5f).Should().BeTrue();

        budget.TryTake(1, 7.5f, 10.0, 5f).Should().BeFalse("no time passed, whatever the clock says");
    }

    [TestMethod]
    public void WithARateOfZeroNothingGetsThrough()
    {
        new GiveBudget<int>().TryTake(1, 0.1f, 10.0, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void TheOptionsAreClampedWhenAPlayerJoinsAndSaySo()
    {
        SessionSettings told = new();
        List<string> messages = [];

        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { BuddyOxygenRate = 99f, BuddyOxygenRange = 0.1f, BuddyOxygenFloor = float.NaN }, messages);

        told.BuddyOxygenRate.Should().Be(BuddyOxygenRules.MAX_RATE);
        told.BuddyOxygenRange.Should().Be(BuddyOxygenRules.MIN_RANGE);
        told.BuddyOxygenFloor.Should().Be(BuddyOxygenRules.DEFAULT_FLOOR);
        messages.Should().HaveCount(3);
        messages.Should().Contain(message => message.Contains("BuddyOxygenRate") && message.Contains("above 20"));
        messages.Should().Contain(message => message.Contains("BuddyOxygenRange") && message.Contains("below 1"));
        messages.Should().Contain(message => message.Contains("BuddyOxygenFloor") && message.Contains("not a number"));

        messages.Clear();
        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { BuddyOxygenRate = 0f, BuddyOxygenRange = 3f, BuddyOxygenFloor = 30f }, messages);
        told.BuddyOxygenRate.Should().Be(0f);
        told.BuddyOxygenRange.Should().Be(3f);
        told.BuddyOxygenFloor.Should().Be(30f);
        messages.Should().BeEmpty();
    }

    [TestMethod]
    public void TheClientTakesTheOptionsAndClampsThemAgain()
    {
        try
        {
            VehicleUpgradeSettings.Apply(new SessionSettings { BuddyOxygenRate = 8f, BuddyOxygenRange = 4f, BuddyOxygenFloor = 20f });
            VehicleUpgradeSettings.BuddyOxygenRate.Should().Be(8f);
            VehicleUpgradeSettings.BuddyOxygenRange.Should().Be(4f);
            VehicleUpgradeSettings.BuddyOxygenFloor.Should().Be(20f);

            VehicleUpgradeSettings.Apply(new SessionSettings { BuddyOxygenRate = 500f, BuddyOxygenRange = -2f, BuddyOxygenFloor = float.PositiveInfinity });
            VehicleUpgradeSettings.BuddyOxygenRate.Should().Be(BuddyOxygenRules.MAX_RATE);
            VehicleUpgradeSettings.BuddyOxygenRange.Should().Be(BuddyOxygenRules.MIN_RANGE);
            VehicleUpgradeSettings.BuddyOxygenFloor.Should().Be(BuddyOxygenRules.DEFAULT_FLOOR);
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
        VehicleUpgradeSettings.BuddyOxygenRate.Should().Be(BuddyOxygenRules.DEFAULT_RATE);
        VehicleUpgradeSettings.BuddyOxygenRange.Should().Be(BuddyOxygenRules.DEFAULT_RANGE);
        VehicleUpgradeSettings.BuddyOxygenFloor.Should().Be(BuddyOxygenRules.DEFAULT_FLOOR);
    }

    [TestMethod]
    public void TheBuddyOxygenDoesNotDependOnTheVehicleUpgradeSwitch()
    {
        SessionSettings told = new();

        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { VehicleUpgrades = false, BuddyOxygenRate = 7f });

        told.VehicleUpgrades.Should().BeFalse();
        told.BuddyOxygenRate.Should().Be(7f);
    }
}
