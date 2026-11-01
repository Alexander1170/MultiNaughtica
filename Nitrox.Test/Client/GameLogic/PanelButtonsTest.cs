using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The two buttons the Cyclops lighting panel gets (the power share switch and the tow hook): the state of the switch, the layout of the row, the words of a failed hooking. The Unity side (copying the
///     panel's own button) is checked in the live check.
/// </summary>
[TestClass]
public class PanelButtonsTest
{
    private readonly NitroxId firstCyclops = new();
    private readonly NitroxId secondCyclops = new();

    [TestCleanup]
    public void Cleanup() => PowerShareSwitch.Clear();

    // ---- the switch

    [TestMethod]
    public void ACyclopsIsSwitchedOffUntilSomebodyPressesTheButton()
    {
        PowerShareSwitch.Clear();

        PowerShareSwitch.IsOn(firstCyclops).Should().BeFalse();
        PowerShareSwitch.IsOn((NitroxId)null).Should().BeFalse();
    }

    [TestMethod]
    public void TheSwitchIsPerCyclops()
    {
        PowerShareSwitch.Clear();

        PowerShareSwitch.Set(firstCyclops, true).Should().BeTrue("the state changed");

        PowerShareSwitch.IsOn(firstCyclops).Should().BeTrue();
        PowerShareSwitch.IsOn(secondCyclops).Should().BeFalse();
        PowerShareSwitch.Count.Should().Be(1);
    }

    [TestMethod]
    public void SettingTheStateItAlreadyHasChangesNothing()
    {
        PowerShareSwitch.Clear();
        PowerShareSwitch.Set(firstCyclops, true);

        PowerShareSwitch.Set(firstCyclops, true).Should().BeFalse();
        PowerShareSwitch.Set(secondCyclops, false).Should().BeFalse();
    }

    [TestMethod]
    public void SwitchingOffForgetsTheCyclops()
    {
        PowerShareSwitch.Clear();
        PowerShareSwitch.Set(firstCyclops, true);

        PowerShareSwitch.Set(firstCyclops, false).Should().BeTrue();

        PowerShareSwitch.IsOn(firstCyclops).Should().BeFalse();
        PowerShareSwitch.Count.Should().Be(0);
    }

    [TestMethod]
    public void ACyclopsWithoutAnIdCannotBeSwitched()
    {
        PowerShareSwitch.Set(null, true).Should().BeFalse();
        PowerShareSwitch.Count.Should().Be(0);
    }

    [TestMethod]
    public void TheEndOfTheSessionSwitchesEveryCyclopsOff()
    {
        PowerShareSwitch.Set(firstCyclops, true);
        PowerShareSwitch.Set(secondCyclops, true);

        PowerShareSwitch.Clear();

        PowerShareSwitch.Count.Should().Be(0);
        PowerShareSwitch.IsOn(firstCyclops).Should().BeFalse();
    }

    // ---- the row of buttons

    [TestMethod]
    public void TheNextButtonsContinueTheStepFromTheFirstLightButtonToTheSecond()
    {
        (float x, float y) step = PanelButtonRules.Step(-40f, 10f, 40f, 10f, 60f);

        step.Should().Be((80f, 0f));
        PanelButtonRules.Slot(40f, 10f, step, 1).Should().Be((120f, 10f));
        PanelButtonRules.Slot(40f, 10f, step, 2).Should().Be((200f, 10f));
    }

    [TestMethod]
    public void AColumnOfLightButtonsGetsMoreButtonsInTheColumn()
    {
        (float x, float y) step = PanelButtonRules.Step(0f, 50f, 0f, 0f, 60f);

        step.Should().Be((0f, -50f));
        PanelButtonRules.Slot(0f, 0f, step, 1).Should().Be((0f, -50f));
    }

    [TestMethod]
    [DataRow(0f, 0f, 0f, 0f)]
    [DataRow(5f, 5f, 5f, 5f)]
    [DataRow(float.NaN, 0f, 1f, 1f)]
    public void WhenTheTwoLightButtonsDoNotShowTheStepTheNextButtonStandsASmallGapToTheRight(float firstX, float firstY, float secondX, float secondY)
    {
        (float x, float y) step = PanelButtonRules.Step(firstX, firstY, secondX, secondY, 50f);

        step.Should().Be((50f * PanelButtonRules.FALLBACK_STEP_IN_WIDTHS, 0f));
    }

    [TestMethod]
    public void AButtonThatIsNotWantedLeavesNoGap()
    {
        PanelButtonRules.AssignSlots(true, true).Should().Equal(1, 2);
        // the tow hook takes the first place when there is no power share
        PanelButtonRules.AssignSlots(false, true).Should().Equal(0, 1);
        PanelButtonRules.AssignSlots(true, false).Should().Equal(1, 0);
        PanelButtonRules.AssignSlots(false, false).Should().Equal(0, 0);
    }

    [TestMethod]
    public void ALitButtonIsBrighterAndMoreOpaqueThanAnUnlitOne()
    {
        PanelButtonRules.IconBrightness(true).Should().BeGreaterThan(PanelButtonRules.IconBrightness(false));
        PanelButtonRules.IconOpacity(true).Should().BeGreaterThan(PanelButtonRules.IconOpacity(false));
        PanelButtonRules.IconBrightness(false).Should().BeGreaterThan(0f, "an unlit button is dim, not gone");
    }

    // ---- the words of a failed hooking

    [TestMethod]
    public void EveryReasonHasWordsAndTheyNameTheVehicleWhenTheClientKnowsIt()
    {
        foreach (CyclopsTowFailure reason in Enum.GetValues<CyclopsTowFailure>())
        {
            TowRules.FailureText(reason).Should().StartWith("Tow hook:");
        }

        TowRules.FailureText(CyclopsTowFailure.InUse, "Seamoth").Should().Contain("Seamoth");
        TowRules.FailureText(CyclopsTowFailure.InUse).Should().NotContain("  ");
        TowRules.FailureText(CyclopsTowFailure.NothingInReach).Should().Contain("reach");
    }
}
