using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The mark key of the Scout Drone: a creature in the view is a threat and anything else a resource, the second key gives the other kind, and the name is what the drone saw, cleaned.
/// </summary>
[TestClass]
public class DroneMarkRulesTest
{
    [TestMethod]
    [DataRow(true, false, CyclopsNavRules.MARK_THREAT)]
    [DataRow(false, false, CyclopsNavRules.MARK_RESOURCE)]
    [DataRow(true, true, CyclopsNavRules.MARK_RESOURCE)]
    [DataRow(false, true, CyclopsNavRules.MARK_THREAT)]
    public void ACreatureIsAThreatAndTheSecondKeyGivesTheOtherKind(bool aimedAtCreature, bool otherKind, byte expected)
    {
        DroneMarkRules.Classify(aimedAtCreature, otherKind).Should().Be(expected);
    }

    [TestMethod]
    public void TheDronesKindsAreTheOnesThatNeedTheOperator()
    {
        CyclopsNavRules.IsDroneKind(DroneMarkRules.Classify(true, false)).Should().BeTrue();
        CyclopsNavRules.IsDroneKind(DroneMarkRules.Classify(false, false)).Should().BeTrue();
        CyclopsNavRules.IsDroneKind(CyclopsNavRules.MARK_POINT).Should().BeFalse();
        CyclopsNavRules.IsDroneKind(CyclopsNavRules.MARK_HAZARD).Should().BeFalse();
        CyclopsNavRules.IsDroneKind(CyclopsNavRules.MARK_TARGET).Should().BeFalse();
        CyclopsNavRules.CapOf(CyclopsNavRules.MARK_RESOURCE).Should().Be(CyclopsNavRules.MAX_DRONE_MARKS);
        CyclopsNavRules.CapOf(CyclopsNavRules.MARK_POINT).Should().Be(CyclopsNavRules.MAX_MARKS);
    }

    [TestMethod]
    [DataRow("Stalker(Clone)", CyclopsNavRules.MARK_THREAT, "Stalker")]
    [DataRow("  Titanium (Clone)(Clone) ", CyclopsNavRules.MARK_RESOURCE, "Titanium")]
    [DataRow("", CyclopsNavRules.MARK_THREAT, DroneMarkRules.THREAT_LABEL)]
    [DataRow(null, CyclopsNavRules.MARK_RESOURCE, DroneMarkRules.RESOURCE_LABEL)]
    [DataRow("(Clone)", CyclopsNavRules.MARK_RESOURCE, DroneMarkRules.RESOURCE_LABEL)]
    public void TheNameIsWhatTheDroneSawWithoutTheClone(string seen, byte kind, string expected)
    {
        DroneMarkRules.LabelFor(kind, seen).Should().Be(expected);
    }

    [TestMethod]
    public void AnUnsafeOrLongNameIsCleanedAndCut()
    {
        string label = DroneMarkRules.LabelFor(CyclopsNavRules.MARK_THREAT, "Sea‮Dragon\u0000 Leviathan of the deep trenches of the world");

        label.Length.Should().BeLessThanOrEqualTo(CyclopsNavRules.MAX_LABEL_LENGTH);
        label.Should().NotContain("‮").And.NotContain("\u0000");
        label.Should().StartWith("SeaDragon");
    }

    [TestMethod]
    public void TheAimReachesFarEnoughToSeeAThreatComingButNotAcrossTheWorld()
    {
        DroneMarkRules.AIM_RANGE.Should().BeInRange(100f, 1000f);
    }
}
