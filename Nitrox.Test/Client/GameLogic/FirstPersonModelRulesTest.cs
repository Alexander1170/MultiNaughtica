using NitroxClient.GameLogic.PlayerModels;

namespace NitroxClient.GameLogic;

/// <summary>The rules of the model that a player wears in his own first-person view (docs/coop/PLAYER_MODELS.md, "Your own model").</summary>
[TestClass]
public class FirstPersonModelRulesTest
{
    [TestMethod]
    public void TheCopyIsWornWhenTheSettingIsOnAModelIsChosenAndThePlayerIsInASession()
    {
        FirstPersonModelRules.Wanted(true, "player-example", true).Should().BeTrue();
        FirstPersonModelRules.Wanted(false, "player-example", true).Should().BeFalse("the player switched it off in the Options menu");
        FirstPersonModelRules.Wanted(true, "", true).Should().BeFalse("the vanilla body is chosen: nothing to show");
        FirstPersonModelRules.Wanted(true, null, true).Should().BeFalse();
        FirstPersonModelRules.Wanted(true, "player-example", false).Should().BeFalse("outside a session there is no body");
    }

    [TestMethod]
    public void TheCopyIsDrawnWhereTheVanillaBodyIsAndHiddenWhereItIsNot()
    {
        // the vanilla body is drawn (its object is active, at its size, with a mesh switched on)
        FirstPersonModelRules.Shown(false, true, 1f, true).Should().BeTrue();
        // the game hides it: nothing switched on (a seat, a cinematic), an inactive object, a body that is shrunk
        FirstPersonModelRules.Shown(false, true, 1f, false).Should().BeFalse();
        FirstPersonModelRules.Shown(false, false, 1f, true).Should().BeFalse();
        FirstPersonModelRules.Shown(false, true, 0f, true).Should().BeFalse();
        FirstPersonModelRules.Shown(false, true, FirstPersonModelRules.MIN_BODY_SCALE * 0.5f, true).Should().BeFalse();
        FirstPersonModelRules.Shown(false, true, FirstPersonModelRules.MIN_BODY_SCALE, true).Should().BeTrue();
        // the emote camera looks at it: drawn whatever the vanilla body does
        FirstPersonModelRules.Shown(true, false, 0f, false).Should().BeTrue();
        FirstPersonModelRules.Shown(true, true, 1f, true).Should().BeTrue();
    }

    [TestMethod]
    public void TheHeadIsHiddenInFirstPersonAndTheModelsOwnInTheEmoteCamera()
    {
        FirstPersonModelRules.HeadScale(true).Should().Be(FirstPersonModelRules.HIDDEN_HEAD_SCALE);
        FirstPersonModelRules.HeadScale(false).Should().Be(1f);
        FirstPersonModelRules.HIDDEN_HEAD_SCALE.Should().BeGreaterThan(0f, "a matrix with no volume is a warning in the game's log");
        FirstPersonModelRules.HIDDEN_HEAD_SCALE.Should().BeLessThan(0.01f);
        FirstPersonModelRules.SCAN_INTERVAL_SECONDS.Should().BeInRange(0.1f, 2f);
    }
}
