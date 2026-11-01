using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica;

/// <summary>The limits and the checks of the marks of a Cyclops' navigator (feature group 3, station 1), shared by the server and the client.</summary>
[TestClass]
public class CyclopsNavRulesTest
{
    [TestMethod]
    public void OnlyTheThreeKindsOfMarkAreKnown()
    {
        CyclopsNavRules.IsKnownKind(CyclopsNavRules.MARK_POINT).Should().BeTrue();
        CyclopsNavRules.IsKnownKind(CyclopsNavRules.MARK_HAZARD).Should().BeTrue();
        CyclopsNavRules.IsKnownKind(CyclopsNavRules.MARK_TARGET).Should().BeTrue();
        CyclopsNavRules.IsKnownKind(CyclopsNavRules.MARK_RESOURCE).Should().BeTrue();
        CyclopsNavRules.IsKnownKind(CyclopsNavRules.MARK_THREAT).Should().BeTrue();
        CyclopsNavRules.IsKnownKind(5).Should().BeFalse();
        CyclopsNavRules.IsKnownKind(255).Should().BeFalse();
    }

    [TestMethod]
    public void APositionIsAPlaceInTheWorld()
    {
        CyclopsNavRules.IsValidPosition(-78f, -12f, 395f).Should().BeTrue();
        CyclopsNavRules.IsValidPosition(0f, 0f, 0f).Should().BeTrue();
        CyclopsNavRules.IsValidPosition(1800f, -1500f, -1900f).Should().BeTrue("the deepest places are well inside");
        CyclopsNavRules.IsValidPosition(float.NaN, 0f, 0f).Should().BeFalse();
        CyclopsNavRules.IsValidPosition(0f, float.NegativeInfinity, 0f).Should().BeFalse();
        CyclopsNavRules.IsValidPosition(6001f, 0f, 0f).Should().BeFalse();
        CyclopsNavRules.IsValidPosition(0f, 0f, -6001f).Should().BeFalse();
        CyclopsNavRules.IsValidPosition(0f, -3501f, 0f).Should().BeFalse();
        CyclopsNavRules.IsValidPosition(0f, 601f, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void ALabelIsCleanedAndCut()
    {
        CyclopsNavRules.SanitizeLabel(null).Should().BeEmpty();
        CyclopsNavRules.SanitizeLabel("").Should().BeEmpty();
        CyclopsNavRules.SanitizeLabel("  Reef \t wall \n").Should().Be("Reef wall", "blanks are folded and the ends trimmed");
        CyclopsNavRules.SanitizeLabel("a\u0000b\u0007c").Should().Be("abc", "control characters are dropped");
        CyclopsNavRules.SanitizeLabel(new string('x', 100)).Should().HaveLength(CyclopsNavRules.MAX_LABEL_LENGTH);
        CyclopsNavRules.SanitizeLabel(new string('x', 23) + " yyyy").Should().Be(new string('x', 23), "a cut that ends in a blank is trimmed");
    }

    [TestMethod]
    public void ALabelLosesTheCharactersThatChangeHowTextIsDrawnOrThatAreNotWholeCharacters()
    {
        CyclopsNavRules.SanitizeLabel("\u202Eevil").Should().Be("evil", "the right-to-left override would show the label reversed on every screen");
        CyclopsNavRules.SanitizeLabel("a\u200Bb\u200Dc\uFEFFd").Should().Be("abcd", "zero width characters are dropped");
        CyclopsNavRules.SanitizeLabel(new string('\u200B', 24)).Should().BeEmpty("a label of nothing visible is no label, so the default takes its place");
        CyclopsNavRules.SanitizeLabel(new string('x', 23) + "\uD83D\uDE00").Should().Be(new string('x', 23), "a surrogate pair is dropped whole, never cut in half");
        CyclopsNavRules.SanitizeLabel("a\uE000b").Should().Be("ab", "private use characters are dropped");
        CyclopsNavRules.SanitizeLabel(new string('x', 1_000_000)).Should().HaveLength(CyclopsNavRules.MAX_LABEL_LENGTH);
    }

    [TestMethod]
    public void TheDefaultLabelIsNumbered()
    {
        CyclopsNavRules.DefaultLabel(3).Should().Be("Mark 3");
        CyclopsNavRules.DefaultLabel(0).Should().Be("Mark 1");
        CyclopsNavRules.DefaultLabel(-4).Should().Be("Mark 1");
    }

    [TestMethod]
    public void TheHudPingOfAMarkUsesTheGamesOwnColours()
    {
        // PingManager.colorOptions: 0 cyan, 1 orange, 2 red, 3 teal, 4 yellow
        CyclopsNavRules.HudColorIndex(CyclopsNavRules.MARK_POINT).Should().Be(0);
        CyclopsNavRules.HudColorIndex(CyclopsNavRules.MARK_HAZARD).Should().Be(1);
        CyclopsNavRules.HudColorIndex(CyclopsNavRules.MARK_TARGET).Should().Be(4);
        CyclopsNavRules.HudColorIndex(CyclopsNavRules.MARK_THREAT).Should().Be(2, "red");
        CyclopsNavRules.HudColorIndex(CyclopsNavRules.MARK_RESOURCE).Should().Be(3, "teal");
        CyclopsNavRules.HudColorIndex(200).Should().Be(0, "an unknown kind gets the plain colour, never an index outside the five");
    }

    [TestMethod]
    public void TheHudTextNamesThePlayerAndTheLabel()
    {
        CyclopsNavRules.PingText("Alice", "Reef wall").Should().Be("Alice: Reef wall");
        CyclopsNavRules.PingText("", "Reef wall").Should().Be("Reef wall");
        CyclopsNavRules.PingText(null, null).Should().Be("Mark");
        CyclopsNavRules.PingText("Alice", "").Should().Be("Alice: Mark");
    }
}
