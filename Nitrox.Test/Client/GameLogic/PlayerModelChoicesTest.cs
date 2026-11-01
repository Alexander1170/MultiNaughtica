using Nitrox.Model.Core;
using NitroxClient.GameLogic.PlayerModels;

namespace NitroxClient.GameLogic;

/// <summary>
///     What a client remembers of the other players' custom player models (docs/coop/PLAYER_MODELS.md) belongs to the session that is running: a session id comes up again in the next session, on this server
///     or on another, and must not show a stranger as somebody who is gone.
/// </summary>
[TestClass]
public class PlayerModelChoicesTest
{
    private static readonly SessionId Bob = (ushort)2;
    private static readonly SessionId Carol = (ushort)3;

    [TestInitialize]
    public void Init() => PlayerModelChoices.Reset();

    [TestCleanup]
    public void Clean() => PlayerModelChoices.Reset();

    [TestMethod]
    public void TheLatestChoiceOfEveryPlayerIsRemembered()
    {
        PlayerModelChoices.Choose(Bob, "player-example", 21741924, null);
        PlayerModelChoices.Choose(Carol, "other-model", 1000, null);
        PlayerModelChoices.Choose(Bob, "third-model", 2000, null);

        PlayerModelChoices.Count.Should().Be(2);
        PlayerModelChoices.Describe(null).Should().Contain("session2:third-model/2000").And.Contain("session3:other-model/1000").And.NotContain("player-example");
    }

    [TestMethod]
    public void ChoosingTheVanillaBodyForgetsTheChoice()
    {
        PlayerModelChoices.Choose(Bob, "player-example", 21741924, null);

        PlayerModelChoices.Choose(Bob, "", 0, null);

        PlayerModelChoices.Count.Should().Be(0);
        PlayerModelChoices.Describe(null).Should().Be("none");
    }

    [TestMethod]
    public void AnIdThatCannotBeAFileNameIsTakenAsTheVanillaBody()
    {
        PlayerModelChoices.Choose(Bob, "..\\..\\evil", 100, null);

        PlayerModelChoices.Count.Should().Be(0);
    }

    [TestMethod]
    public void WhenTheSessionEndsNothingOfItIsLeftForTheNextOne()
    {
        PlayerModelChoices.Choose(Bob, "player-example", 21741924, null);
        PlayerModelChoices.Choose(Carol, "other-model", 1000, null);

        PlayerModelChoices.Reset();

        PlayerModelChoices.Count.Should().Be(0, "a stranger who gets Bob's session id in the next session must not wear Bob's model");
        PlayerModelChoices.Describe(null).Should().Be("none");
    }

    [TestMethod]
    public void AChoiceOfTheNextSessionIsRememberedAsUsualAfterAReset()
    {
        PlayerModelChoices.Choose(Bob, "player-example", 21741924, null);
        PlayerModelChoices.Reset();

        PlayerModelChoices.Choose(Bob, "other-model", 1000, null);

        PlayerModelChoices.Describe(null).Should().Be("session2:other-model/1000");
    }

    [TestMethod]
    public void ForgettingAPlayerWhoLeftKeepsTheOthers()
    {
        PlayerModelChoices.Choose(Bob, "player-example", 21741924, null);
        PlayerModelChoices.Choose(Carol, "other-model", 1000, null);

        PlayerModelChoices.Forget(Bob);

        PlayerModelChoices.Describe(null).Should().Be("session3:other-model/1000");
    }

    [TestMethod]
    public void TheAnnouncerResetsWithoutAGame()
    {
        // (a wait that was started in the session that ended finds the counter changed and sends nothing; that needs the game's clock and is shown by the live check)
        PlayerModelAnnouncer.Reset();

        PlayerModelAnnouncer.LastSent.Should().BeEmpty();
    }
}
