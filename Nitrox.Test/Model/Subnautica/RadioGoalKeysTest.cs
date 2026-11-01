using Nitrox.Model.Subnautica.Helper;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class RadioGoalKeysTest
{
    [TestMethod]
    public void ThePlayedGoalOfAMessageIsOnPlayAndItsKey()
    {
        // the goals of the first real play session (server log 2026-10-23): OnPlayRadioBounceBack, OnPlayRadioGrassy25, OnPlayRadioShallows22
        RadioGoalKeys.PlayedGoalOf("RadioBounceBack").Should().Be("OnPlayRadioBounceBack");
        RadioGoalKeys.PlayedGoalOf("RadioGrassy25").Should().Be("OnPlayRadioGrassy25");
        RadioGoalKeys.PlayedGoalOf("RadioSunbeam4").Should().Be("OnPlayRadioSunbeam4");
    }

    [TestMethod]
    public void APlayedGoalIsRecognizedByItsPrefix()
    {
        RadioGoalKeys.IsPlayedGoal("OnPlayRadioSecondOfficer").Should().BeTrue();
        RadioGoalKeys.IsPlayedGoal("OnPlayX").Should().BeTrue();
    }

    [TestMethod]
    public void OtherGoalsAreNotPlayedGoals()
    {
        RadioGoalKeys.IsPlayedGoal("RadioGrassy25").Should().BeFalse("the radio goal itself is completed when the message arrives, not when it is played");
        RadioGoalKeys.IsPlayedGoal("OnCraftBattery").Should().BeFalse();
        RadioGoalKeys.IsPlayedGoal("OnPlay").Should().BeFalse("a prefix alone names no message");
        RadioGoalKeys.IsPlayedGoal("onplayRadioX").Should().BeFalse("the keys the game builds are exact; only the lookups ignore case");
        RadioGoalKeys.IsPlayedGoal(string.Empty).Should().BeFalse();
        RadioGoalKeys.IsPlayedGoal(null).Should().BeFalse();
    }

    [TestMethod]
    public void TheRadioKeyIsWhatFollowsThePrefix()
    {
        RadioGoalKeys.RadioKeyOf("OnPlayRadioMushroom24").Should().Be("RadioMushroom24");
        RadioGoalKeys.RadioKeyOf(RadioGoalKeys.PlayedGoalOf("RadioKoosh26")).Should().Be("RadioKoosh26");
    }

    [TestMethod]
    public void NoRadioKeyComesOutOfAnOrdinaryGoal()
    {
        RadioGoalKeys.RadioKeyOf("Goal_Disable_Gun").Should().BeNull();
        RadioGoalKeys.RadioKeyOf("OnPlay").Should().BeNull();
        RadioGoalKeys.RadioKeyOf(null).Should().BeNull();
    }
}
