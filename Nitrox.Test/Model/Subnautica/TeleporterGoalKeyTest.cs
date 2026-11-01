using Nitrox.Model.Subnautica.Helper;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class TeleporterGoalKeyTest
{
    [TestMethod]
    public void AFormattedKeyParsesBackToItsIdentifier()
    {
        TeleporterGoalKey.TryParse(TeleporterGoalKey.Format("cragfieldtp"), out string identifier).Should().BeTrue();
        identifier.Should().Be("cragfieldtp");
    }

    [TestMethod]
    public void AnIdentifierWithColonsSurvives()
    {
        TeleporterGoalKey.TryParse(TeleporterGoalKey.Format("a:b:c"), out string identifier).Should().BeTrue();
        identifier.Should().Be("a:b:c");
    }

    [TestMethod]
    public void TheCaseOfThePrefixDoesNotMatter()
    {
        TeleporterGoalKey.TryParse("nitrox_teleporteractive:guntp", out string identifier).Should().BeTrue();
        identifier.Should().Be("guntp");
    }

    [TestMethod]
    [DataRow("Goal_Disable_Gun")]
    [DataRow("PrecursorGunDoor")]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("Nitrox_TeleporterActive:")]
    public void OtherGoalsAreNotTeleporters(string key)
    {
        TeleporterGoalKey.TryParse(key, out string identifier).Should().BeFalse();
        identifier.Should().BeNull();
    }
}
