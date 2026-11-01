using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.AfterburnerState;
using AfterburnerPhase = NitroxClient.GameLogic.VehicleUpgrades.AfterburnerState.Phase;

namespace NitroxClient.GameLogic;

/// <summary>
///     The Seamoth afterburner: a press burns for a while at a higher speed, costs energy, and is followed by a cooldown. Time is passed in, so the tests run without a game.
/// </summary>
[TestClass]
public class AfterburnerStateTest
{
    // double speed, 5 s burn, 15 s cooldown, 10 energy (5 percent of a power cell of 200)
    private static readonly AfterburnerSettings Defaults = new(2f, 5f, 15f, 10f);

    private const float FULL_CELL = 200f;

    [TestMethod]
    public void ANewAfterburnerIsReadyAndDoesNotChangeTheSpeed()
    {
        AfterburnerState state = new();

        state.PhaseAt(0f).Should().Be(AfterburnerPhase.Ready);
        state.SpeedFactor(0f).Should().Be(1f);
        state.IsBurning.Should().BeFalse();
        state.Uses.Should().Be(0);
    }

    [TestMethod]
    public void APressBurnsAtTheConfiguredSpeedForTheConfiguredTime()
    {
        AfterburnerState state = new();

        state.Press(100f, true, FULL_CELL, Defaults).Should().Be(PressResult.Started);

        state.PhaseAt(100f).Should().Be(AfterburnerPhase.Burning);
        state.SpeedFactor(100f).Should().Be(2f);
        state.SpeedFactor(104.9f).Should().Be(2f);
        state.PhaseAt(105f).Should().Be(AfterburnerPhase.Cooldown, "the burn is over after exactly 5 seconds");
        state.SpeedFactor(105f).Should().Be(1f);
        state.Uses.Should().Be(1);
    }

    [TestMethod]
    public void TheCooldownStartsWhenTheBurnEndsAndLastsTheConfiguredTime()
    {
        AfterburnerState state = new();
        state.Press(100f, true, FULL_CELL, Defaults);

        state.PhaseAt(119.9f).Should().Be(AfterburnerPhase.Cooldown);
        state.PhaseAt(120f).Should().Be(AfterburnerPhase.Ready, "5 s burn + 15 s cooldown after the press");
        state.Press(120f, true, FULL_CELL, Defaults).Should().Be(PressResult.Started);
        state.Uses.Should().Be(2);
    }

    [TestMethod]
    public void APressDuringTheBurnDoesNothing()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        state.Press(2f, true, FULL_CELL, Defaults).Should().Be(PressResult.AlreadyBurning);

        state.Uses.Should().Be(1);
        state.PhaseAt(4.9f).Should().Be(AfterburnerPhase.Burning, "the second press must not extend the burn");
        state.PhaseAt(5f).Should().Be(AfterburnerPhase.Cooldown);
    }

    [TestMethod]
    public void APressDuringTheCooldownDoesNothing()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        state.Press(10f, true, FULL_CELL, Defaults).Should().Be(PressResult.CoolingDown);
        state.Press(19.9f, true, FULL_CELL, Defaults).Should().Be(PressResult.CoolingDown);

        state.Uses.Should().Be(1);
        state.PhaseAt(10f).Should().Be(AfterburnerPhase.Cooldown);
    }

    [TestMethod]
    public void WithTooLittleEnergyAPressDoesNothing()
    {
        AfterburnerState state = new();

        state.Press(0f, true, 9.99f, Defaults).Should().Be(PressResult.NotEnoughEnergy);

        state.Uses.Should().Be(0);
        state.PhaseAt(0f).Should().Be(AfterburnerPhase.Ready);
        state.Press(1f, true, 10f, Defaults).Should().Be(PressResult.Started, "exactly the cost is enough");
    }

    [TestMethod]
    public void AFreeAfterburnerStillNeedsSomePower()
    {
        AfterburnerState state = new();
        AfterburnerSettings free = new(2f, 5f, 15f, 0f);

        state.Press(0f, true, 0f, free).Should().Be(PressResult.NotEnoughEnergy);
        state.Press(0f, true, 0.5f, free).Should().Be(PressResult.Started);
    }

    [TestMethod]
    public void ANotAllowedPressDoesNothing()
    {
        AfterburnerState state = new();

        state.Press(0f, false, FULL_CELL, Defaults).Should().Be(PressResult.NotAllowed);

        state.Uses.Should().Be(0);
        state.PhaseAt(0f).Should().Be(AfterburnerPhase.Ready);
    }

    [TestMethod]
    [DataRow(EndReason.Exited)]
    [DataRow(EndReason.Docked)]
    [DataRow(EndReason.Disabled)]
    public void TheBurnEndsWhenThePilotLeavesTheSeamothDocksOrTheModuleIsGone(EndReason reason)
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        state.End(2f, reason);

        state.IsBurning.Should().BeFalse();
        state.SpeedFactor(2f).Should().Be(1f);
        state.LastEndReason.Should().Be(reason);
        state.PhaseAt(2f).Should().Be(AfterburnerPhase.Cooldown, "leaving early does not give the burn back for free");
    }

    [TestMethod]
    public void AnEarlyEndStartsTheCooldownAtThatMoment()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);
        state.End(2f, EndReason.Exited);

        state.PhaseAt(16.9f).Should().Be(AfterburnerPhase.Cooldown);
        state.PhaseAt(17f).Should().Be(AfterburnerPhase.Ready, "2 s into the burn + 15 s cooldown");
    }

    [TestMethod]
    public void EndingWhenNothingBurnsDoesNothing()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);
        state.Tick(5f, FULL_CELL).Should().BeTrue();

        state.End(8f, EndReason.Exited);

        state.LastEndReason.Should().Be(EndReason.Elapsed);
        state.PhaseAt(19.9f).Should().Be(AfterburnerPhase.Cooldown);
        state.PhaseAt(20f).Should().Be(AfterburnerPhase.Ready, "the late End must not move the cooldown");
    }

    [TestMethod]
    public void TheBurnEndsWhenThePowerRunsOut()
    {
        AfterburnerState state = new();
        state.Press(0f, true, 12f, Defaults);

        state.Tick(1f, 2f).Should().BeFalse("the Seamoth still has power");
        state.Tick(2f, 0f).Should().BeTrue();

        state.LastEndReason.Should().Be(EndReason.PowerLost);
        state.SpeedFactor(2f).Should().Be(1f);
        state.PhaseAt(2f).Should().Be(AfterburnerPhase.Cooldown);
    }

    [TestMethod]
    public void ALateTickEndsTheBurnAtItsPlannedEndSoTheCooldownIsNotExtended()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        state.Tick(9f, FULL_CELL).Should().BeTrue("the frame came 4 seconds late");

        state.LastEndReason.Should().Be(EndReason.Elapsed);
        state.PhaseAt(19.9f).Should().Be(AfterburnerPhase.Cooldown);
        state.PhaseAt(20f).Should().Be(AfterburnerPhase.Ready);
    }

    [TestMethod]
    public void NoTickIsNeededForTheTimeToRunOut()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        state.SpeedFactor(7f).Should().Be(1f);
        state.PhaseAt(7f).Should().Be(AfterburnerPhase.Cooldown);
        state.Press(7f, true, FULL_CELL, Defaults).Should().Be(PressResult.CoolingDown);
        state.Press(20f, true, FULL_CELL, Defaults).Should().Be(PressResult.Started);
    }

    [TestMethod]
    public void TheQuickSlotShowsTheBurnAndTheCooldownTogether()
    {
        AfterburnerState state = new();
        state.Press(50f, true, FULL_CELL, Defaults);

        state.SlotTimeUsed.Should().Be(50f);
        state.SlotCooldown.Should().Be(20f, "5 s burn + 15 s cooldown");

        state.End(52f, EndReason.Docked);

        state.SlotTimeUsed.Should().Be(50f);
        state.SlotCooldown.Should().Be(17f, "the cooldown now ends 15 s after the early end");
    }

    [TestMethod]
    public void ARunningBurnKeepsItsOwnNumbersWhenTheSettingsChange()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        // The settings are not passed again after the press: a later press with other numbers must not change the running burn
        state.Press(1f, true, FULL_CELL, new AfterburnerSettings(5f, 60f, 300f, 0f)).Should().Be(PressResult.AlreadyBurning);

        state.SpeedFactor(1f).Should().Be(2f);
        state.PhaseAt(5f).Should().Be(AfterburnerPhase.Cooldown);
        state.PhaseAt(20f).Should().Be(AfterburnerPhase.Ready);
    }

    [TestMethod]
    public void AZeroCooldownIsReadyAsSoonAsTheBurnIsOver()
    {
        AfterburnerState state = new();
        AfterburnerSettings noCooldown = new(2f, 5f, 0f, 10f);
        state.Press(0f, true, FULL_CELL, noCooldown);

        state.PhaseAt(4.9f).Should().Be(AfterburnerPhase.Burning);
        state.PhaseAt(5f).Should().Be(AfterburnerPhase.Ready);
        state.Press(5f, true, FULL_CELL, noCooldown).Should().Be(PressResult.Started);
    }

    [TestMethod]
    public void ResetForgetsEverything()
    {
        AfterburnerState state = new();
        state.Press(0f, true, FULL_CELL, Defaults);

        state.Reset();

        state.IsBurning.Should().BeFalse();
        state.Uses.Should().Be(0);
        state.PhaseAt(1f).Should().Be(AfterburnerPhase.Ready);
        state.Press(1f, true, FULL_CELL, Defaults).Should().Be(PressResult.Started, "a new session starts with a ready afterburner even when the clock starts again at 0");
    }

    [TestMethod]
    public void TheEnergyOfTheDefaultsIsFivePercentOfAPowerCell()
    {
        AfterburnerSettings current = AfterburnerSettings.Current;

        (current.Energy / FULL_CELL).Should().BeApproximately(0.05f, 0.0001f);
        current.Seconds.Should().Be(5f);
        current.CooldownSeconds.Should().Be(15f);
        current.Speed.Should().Be(2f);
    }
}
