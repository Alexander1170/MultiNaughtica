using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.GameLogic;

/// <summary>Which creatures the navigator's map draws in red and in orange (docs/coop/VEHICLE_UPGRADES.md, the navigator console).</summary>
[TestClass]
public class CreatureDangerRulesTest
{
    [TestMethod]
    [DataRow(TechType.ReaperLeviathan)]
    [DataRow(TechType.GhostLeviathan)]
    [DataRow(TechType.GhostLeviathanJuvenile)]
    [DataRow(TechType.SeaDragon)]
    [DataRow(TechType.SeaEmperor)]
    [DataRow(TechType.SeaEmperorLeviathan)]
    [DataRow(TechType.SeaEmperorJuvenile)]
    public void TheLeviathansAreRed(TechType tech)
    {
        CreatureDangerRules.Classify(tech).Should().Be(CreatureDanger.Leviathan);
    }

    [TestMethod]
    [DataRow(TechType.Warper)]
    [DataRow(TechType.CrabSquid)]
    [DataRow(TechType.Shocker)]
    [DataRow(TechType.Crabsnake)]
    [DataRow(TechType.LavaLizard)]
    [DataRow(TechType.SpineEel)]
    [DataRow(TechType.BoneShark)]
    public void TheOtherDangersAreOrange(TechType tech)
    {
        CreatureDangerRules.Classify(tech).Should().Be(CreatureDanger.Dangerous);
        CreatureDangerRules.IsShown(CreatureDanger.Dangerous, false).Should().BeTrue("the T key does not hide an orange danger");
    }

    [TestMethod]
    [DataRow(TechType.CaveCrawler)]
    [DataRow(TechType.RockPuncher)]
    [DataRow(TechType.Biter)]
    [DataRow(TechType.Gasopod)]
    [DataRow(TechType.Stalker)]
    [DataRow(TechType.Sandshark)]
    [DataRow(TechType.Mesmer)]
    [DataRow(TechType.Bleeder)]
    [DataRow(TechType.Crash)]
    public void TheSmallBitersAreMinorAndOnlyShownWhenAsked(TechType tech)
    {
        CreatureDangerRules.Classify(tech).Should().Be(CreatureDanger.Minor);
        CreatureDangerRules.IsShown(CreatureDanger.Minor, false).Should().BeFalse();
        CreatureDangerRules.IsShown(CreatureDanger.Minor, true).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(TechType.Seamoth)]
    [DataRow(TechType.Exosuit)]
    [DataRow(TechType.Cyclops)]
    [DataRow(TechType.Reefback)]
    [DataRow(TechType.SeaTreader)]
    [DataRow(TechType.SeaEmperorBaby)]
    [DataRow(TechType.None)]
    public void VehiclesHarmlessGiantsAndUnknownThingsAreNotDangers(TechType tech)
    {
        CreatureDangerRules.Classify(tech).Should().Be(CreatureDanger.None, "the game's own behaviour type counts vehicles as sharks, which a map of dangers must not");
        CreatureDangerRules.IsShown(CreatureDanger.None, true).Should().BeFalse();
    }

    [TestMethod]
    public void DangersAreAlwaysShown()
    {
        CreatureDangerRules.IsShown(CreatureDanger.Leviathan, false).Should().BeTrue();
        CreatureDangerRules.IsShown(CreatureDanger.Dangerous, false).Should().BeTrue();
    }

    [TestMethod]
    public void ARedOutranksAnOrangeWhichOutranksAMinorOneWhenTheMapIsFull()
    {
        ContactRules.Priority(ContactKind.Creature, CreatureDanger.Leviathan).Should().BeGreaterThan(ContactRules.Priority(ContactKind.Creature, CreatureDanger.Dangerous));
        ContactRules.Priority(ContactKind.Creature, CreatureDanger.Dangerous).Should().BeGreaterThan(ContactRules.Priority(ContactKind.Teammate, CreatureDanger.None), "a danger is never dropped for a friend");
        ContactRules.Priority(ContactKind.Creature, CreatureDanger.Minor).Should().BeLessThan(ContactRules.Priority(ContactKind.Lifepod, CreatureDanger.None));
        ContactRules.Priority(ContactKind.Cyclops, CreatureDanger.None).Should().BeGreaterThan(ContactRules.Priority(ContactKind.Creature, CreatureDanger.Leviathan), "the own hull is always on the map");
    }
}
