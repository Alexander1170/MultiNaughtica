using Microsoft.Extensions.Logging;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Unlockables;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

[TestClass]
public class PdaManagerTimeCapsuleTest
{
    private static PdaManager NewManager() => new(Substitute.For<ILogger<PdaManager>>());

    private static TimeCapsuleContentData Content(string key, string title = "Hello", string text = "Greetings from the other side") => new(key, title, text, "images/capsule.jpg");

    [TestMethod]
    public void ANewTimeCapsuleEntryKeepsItsContent()
    {
        PdaManager manager = NewManager();

        manager.AddEncyclopediaEntry("capsule-1", Content("capsule-1")).Should().BeTrue();

        InitialPDAData data = manager.GetInitialPDAData();
        data.EncyclopediaEntries.Should().Contain("capsule-1");
        data.TimeCapsuleContents.Should().ContainSingle().Which.Title.Should().Be("Hello");
    }

    [TestMethod]
    public void AnEntryThatWasKnownKeepsTheContentItHad()
    {
        PdaManager manager = NewManager();
        manager.AddEncyclopediaEntry("capsule-1", Content("capsule-1", title: "First"));

        manager.AddEncyclopediaEntry("capsule-1", Content("capsule-1", title: "Second")).Should().BeFalse();

        manager.GetInitialPDAData().TimeCapsuleContents.Should().ContainSingle().Which.Title.Should().Be("First");
    }

    [TestMethod]
    public void AnEntryWithoutContentStoresNone()
    {
        PdaManager manager = NewManager();

        manager.AddEncyclopediaEntry("Lifepod");

        manager.GetInitialPDAData().TimeCapsuleContents.Should().BeEmpty();
    }

    [TestMethod]
    public void ContentForAnotherKeyIsNotStored()
    {
        PdaManager manager = NewManager();

        manager.AddEncyclopediaEntry("capsule-1", Content("capsule-2"));

        manager.GetInitialPDAData().TimeCapsuleContents.Should().BeEmpty();
    }

    [TestMethod]
    public void TheStateCopyHasTheContentToo()
    {
        PdaManager manager = NewManager();
        manager.AddEncyclopediaEntry("capsule-1", Content("capsule-1"));

        PdaStateData copy = manager.GetPdaStateCopy();

        copy.TimeCapsuleContents.Should().ContainSingle();
    }

    [TestMethod]
    public void TooLongTextIsCutBeforeItIsStored()
    {
        TimeCapsuleContentData content = new("capsule-1", new string('t', 1000), new string('x', 10000), new string('u', 5000));

        content.Title.Length.Should().Be(TimeCapsuleContentData.MAX_TITLE_LENGTH);
        content.Text.Length.Should().Be(TimeCapsuleContentData.MAX_TEXT_LENGTH);
        content.ImageUrl.Length.Should().Be(TimeCapsuleContentData.MAX_IMAGE_URL_LENGTH);
    }

    [TestMethod]
    public void MissingTextBecomesEmptyText()
    {
        new TimeCapsuleContentData("capsule-1", null, null, null).Title.Should().BeEmpty();
    }
}
