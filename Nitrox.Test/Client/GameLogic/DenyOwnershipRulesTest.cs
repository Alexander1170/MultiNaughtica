namespace NitroxClient.GameLogic;

/// <summary>
///     When the refusal "Another player is interacting with that object" shows on the hand (coop.9 polish): wherever the player looks for a tool that grabs, but for a vehicle or a chair only while the hand points at that
///     very object, so that a hand target below it (the Rider Bar on a Seamoth) keeps its own text.
/// </summary>
[TestClass]
public class DenyOwnershipRulesTest
{
    [TestMethod]
    public void AnUnscopedRefusalShowsWhereverThePlayerLooks()
    {
        object refused = new();

        DenyOwnershipRules.ShowsText(false, new object(), refused).Should().BeTrue();
        DenyOwnershipRules.ShowsText(false, null, refused).Should().BeTrue();
    }

    [TestMethod]
    public void AScopedRefusalShowsWhileTheHandPointsAtTheRefusedObject()
    {
        object refused = new();

        DenyOwnershipRules.ShowsText(true, refused, refused).Should().BeTrue();
    }

    [TestMethod]
    public void AScopedRefusalStepsAsideForAHandTargetBelowTheRefusedObject()
    {
        object seamoth = new();
        object riderBar = new();

        DenyOwnershipRules.ShowsText(true, riderBar, seamoth).Should().BeFalse();
    }

    [TestMethod]
    public void AScopedRefusalDoesNotShowWhenTheHandPointsAtNothingOrAtAnotherObject()
    {
        object refused = new();

        DenyOwnershipRules.ShowsText(true, null, refused).Should().BeFalse();
        DenyOwnershipRules.ShowsText(true, new object(), refused).Should().BeFalse();
    }

    [TestMethod]
    public void TheRefusalLastsTwoSeconds()
    {
        float until = 10f + DenyOwnershipRules.SHOW_SECONDS;

        DenyOwnershipRules.SHOW_SECONDS.Should().Be(2f);
        DenyOwnershipRules.IsOver(11.99f, until).Should().BeFalse();
        DenyOwnershipRules.IsOver(12f, until).Should().BeTrue();
    }
}
