using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The game-free rules of the emotes (docs/coop/PLAYER_MODELS.md): which clips are on the wheel, which sector a pointer is in, how long a clip plays, how the pose changes over to the clip's, when an emote is
/// refused and what stops one.
/// </summary>
[TestClass]
public class PlayerEmoteRulesTest
{
    private static readonly GlbClip[] FirstModelClips =
    [
        new("Running", 0.63f, 134),
        new("Walking", 1.03f, 134),
        new("Backflip_Sweep_Kick", 4.83f, 134),
        new("Backflip_and_Rise", 2.67f, 134),
        new("Breakdance_1990", 0.5f, 134),
        new("Confident_Walk", 5.73f, 134),
        new("Crawl_Backward", 0.97f, 134),
        new("Gangnam_Groove", 10.13f, 134),
        new("Gentlemans_Bow", 7.27f, 134),
        new("Indoor_Swing", 8.0f, 134),
        new("Jazz_Hands_inplace", 2.73f, 134),
        new("Shot_and_Fall_Forward", 2.17f, 134),
        new("dying_backwards", 2.2f, 134),
        new("ymca_dance", 4.5f, 134)
    ];

    [TestMethod]
    public void TheWheelOfTheFirstModelHasItsTenEmotesInTheOrderOfTheFile()
    {
        IReadOnlyList<EmoteEntry> emotes = PlayerEmoteRules.EmotesOf(FirstModelClips);

        emotes.Select(entry => entry.Id).Should().Equal("Backflip_Sweep_Kick", "Backflip_and_Rise", "Breakdance_1990", "Gangnam_Groove", "Gentlemans_Bow", "Indoor_Swing", "Jazz_Hands_inplace",
                                                          "Shot_and_Fall_Forward", "dying_backwards", "ymca_dance");
        emotes.Select(entry => entry.Label).Should().Contain("Gangnam Groove").And.Contain("ymca dance");
    }

    [TestMethod]
    public void OnlyTheShortClipIsALoop()
    {
        IReadOnlyList<EmoteEntry> emotes = PlayerEmoteRules.EmotesOf(FirstModelClips);

        emotes.Where(entry => entry.Loop).Select(entry => entry.Id).Should().Equal("Breakdance_1990");
    }

    [TestMethod]
    public void AClipWhoseNameIsNotAnIdIsNotOnTheWheel()
    {
        GlbClip[] clips = [new("Armature|Dance one", 3f, 10), new("good_one", 3f, 10)];

        PlayerEmoteRules.EmotesOf(clips).Select(entry => entry.Id).Should().Equal("good_one");
    }

    [TestMethod]
    public void TheWheelHasRoomForTwelveEmotes()
    {
        GlbClip[] clips = Enumerable.Range(0, 20).Select(i => new GlbClip($"emote_{i}", 3f, 10)).ToArray();

        PlayerEmoteRules.EmotesOf(clips).Should().HaveCount(PlayerEmoteRules.MAX_WHEEL_ENTRIES);
    }

    [TestMethod]
    public void AClipIsPlayedOnceAndALoopForSixSecondsButNeverMoreThanFifteen()
    {
        PlayerEmoteRules.PlaySeconds(10.13f).Should().BeApproximately(10.13f, 1e-4f);
        PlayerEmoteRules.PlaySeconds(0.5f).Should().Be(PlayerEmoteRules.LOOP_PLAY_SECONDS);
        PlayerEmoteRules.PlaySeconds(60f).Should().Be(PlayerEmoteRules.MAX_PLAY_SECONDS);
        PlayerEmoteRules.PlaySeconds(0f).Should().Be(0f);
        PlayerEmoteRules.IsOver(5f, 10.13f).Should().BeFalse();
        PlayerEmoteRules.IsOver(10.2f, 10.13f).Should().BeTrue();
        PlayerEmoteRules.IsOver(5.9f, 0.5f).Should().BeFalse();
        PlayerEmoteRules.IsOver(6.1f, 0.5f).Should().BeTrue();
    }

    [TestMethod]
    public void ALoopStartsAgainAtItsEndAndAClipStaysOnItsLastFrame()
    {
        PlayerEmoteRules.ClipTime(0.7f, 0.5f).Should().BeApproximately(0.2f, 1e-4f);
        PlayerEmoteRules.ClipTime(3f, 2.67f).Should().Be(2.67f);
        PlayerEmoteRules.ClipTime(1f, 2.67f).Should().Be(1f);
        PlayerEmoteRules.ClipTime(-1f, 2.67f).Should().Be(0f);
    }

    [TestMethod]
    public void ThePoseChangesOverToTheClipInAFractionOfASecond()
    {
        PlayerEmoteRules.BlendFactor(0f).Should().Be(0f);
        PlayerEmoteRules.BlendFactor(PlayerEmoteRules.BLEND_IN_SECONDS / 2f).Should().BeApproximately(0.5f, 1e-4f);
        PlayerEmoteRules.BlendFactor(PlayerEmoteRules.BLEND_IN_SECONDS).Should().Be(1f);
        PlayerEmoteRules.BlendFactor(3f).Should().Be(1f);
    }

    [TestMethod]
    public void TheSectorsGoRoundClockwiseFromTheTop()
    {
        PlayerEmoteRules.SectorAt(0f, 0.9f, 4).Should().Be(0, "up");
        PlayerEmoteRules.SectorAt(0.9f, 0f, 4).Should().Be(1, "right");
        PlayerEmoteRules.SectorAt(0f, -0.9f, 4).Should().Be(2, "down");
        PlayerEmoteRules.SectorAt(-0.9f, 0f, 4).Should().Be(3, "left");
        PlayerEmoteRules.SectorAt(-0.3f, -0.6f, 4).Should().Be(2);
        PlayerEmoteRules.SectorAt(-0.05f, 0.9f, 10).Should().Be(0, "a little to the left of the top is still the first sector");
        PlayerEmoteRules.SectorAt(-0.5f, 0.5f, 10).Should().Be(9, "up and left is 315 degrees, the last of the ten sectors before the top");
    }

    [TestMethod]
    public void TheMiddleOfTheWheelChoosesNothing()
    {
        PlayerEmoteRules.SectorAt(0f, 0f, 10).Should().Be(-1);
        PlayerEmoteRules.SectorAt(0.29f, 0f, 10).Should().Be(-1);
        PlayerEmoteRules.SectorAt(0.31f, 0f, 4).Should().Be(1);
        PlayerEmoteRules.SectorAt(0f, 0.9f, 0).Should().Be(-1, "a wheel without sectors has none");
    }

    [TestMethod]
    public void ThePointerAtTheMiddleOfASectorIsInThatSector()
    {
        for (int count = 1; count <= PlayerEmoteRules.MAX_WHEEL_ENTRIES; count++)
        {
            for (int index = 0; index < count; index++)
            {
                PlayerEmoteRules.SectorCenter(index, count, 0.7f, out float x, out float y);

                PlayerEmoteRules.SectorAt(x, y, count).Should().Be(index, $"sector {index} of {count}");
            }
        }
    }

    [TestMethod]
    public void NoEmoteWithoutAModelWithEmotesAndNoEmoteInAVehicleASeatOrAMenu()
    {
        PlayerEmoteRules.Refusal(true, false, false, false, false).Should().BeNull();
        PlayerEmoteRules.Refusal(false, false, false, false, false).Should().Contain("player model");
        PlayerEmoteRules.Refusal(true, true, false, false, false).Should().Contain("vehicle");
        PlayerEmoteRules.Refusal(true, false, true, false, false).Should().Contain("seated");
        PlayerEmoteRules.Refusal(true, false, false, true, false).Should().Contain("dead");
        PlayerEmoteRules.Refusal(true, false, false, false, true).Should().Contain("menu");
    }

    [TestMethod]
    public void MovingOrLosingHealthStopsAnEmote()
    {
        PlayerEmoteRules.CancelReason(0f, 0f).Should().BeNull();
        PlayerEmoteRules.CancelReason(0.1f, 0f).Should().BeNull("a hair of stick drift does not stop it");
        PlayerEmoteRules.CancelReason(0.8f, 0f).Should().Contain("moved");
        PlayerEmoteRules.CancelReason(0f, 3f).Should().Contain("hurt");
    }

    [TestMethod]
    public void AnIdFromANetworkPacketIsAClipNameOrNothing()
    {
        PlayerEmoteRules.SanitizeId("Gangnam_Groove").Should().Be("Gangnam_Groove");
        PlayerEmoteRules.SanitizeId("..\\x").Should().BeEmpty();
        PlayerEmoteRules.SanitizeId(null).Should().BeEmpty();
    }
}
