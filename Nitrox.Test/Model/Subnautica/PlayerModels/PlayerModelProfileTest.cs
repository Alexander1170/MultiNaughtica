using System.Numerics;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The profile beside a model (docs/coop/PLAYER_MODELS.md, "Adding a new model"): optional, it only overrides what the game measures; what cannot be read is a line in the problems and the field keeps its default.
/// </summary>
[TestClass]
public class PlayerModelProfileTest
{
    private const string FULL = """
        {
          "schema": 1,
          "displayName": "Example Model",
          "notes": "Made by hand, 2026-10-30.",
          "scale": { "source": "head", "factor": 1.05 },
          "eyeHeight": 1.52,
          "bones": { "prefix": "rig_", "map": { "RightHandIndex1": "R_Pointer_01", "Hips": "pelvis" } },
          "emotes": { "shared": false, "source": "player-other" },
          "firstPerson": { "hiddenBones": ["Head", "Neck"], "trims": { "LeftShoulder": 0.8 }, "offset": [0, -0.02, 0.03] },
          "hands": { "enabled": true, "fingers": false, "thumbs": false, "toolTargets": false, "maxStretch": 1.1, "pdaPullBack": 0.02, "anchor": [0.01, -0.005, 0.0], "rollTrim": 5, "twistShare": 0.25 },
          "measured": { "hipsHeight": 0.969, "upperArm": 0.2415 },
          "somethingNew": { "a": 1 }
        }
        """;

    [TestMethod]
    public void AProfileThatSaysNothingChangesNothing()
    {
        foreach (string text in new[] { "", "  ", "{}", "{ \"schema\": 1 }" })
        {
            PlayerModelProfile profile = PlayerModelProfile.Parse(text, out IReadOnlyList<string> problems);

            problems.Should().BeEmpty(text);
            profile.DisplayName.Should().BeNull();
            profile.ScaleSource.Should().BeNull();
            profile.ScaleFactor.Should().BeNull();
            profile.EyeHeight.Should().BeNull();
            profile.SharedEmotes.Should().BeFalse("no model ships, so nothing is shared unless a profile names a source");
            profile.EmoteSource.Should().Be(PlayerModelProfile.DEFAULT_EMOTE_SOURCE);
            profile.HiddenBones.Should().Equal("Head");
            profile.Trims.Should().BeEmpty();
            profile.FirstPersonOffset.Should().Be(Vector3.Zero);
            profile.BoneNames.Full("Hips").Should().Be("mixamorig:Hips");
            profile.Measured.Should().BeNull();
            profile.Hands.Enabled.Should().BeTrue();
            profile.Hands.ToolTargets.Should().BeTrue();
            profile.Hands.MaxStretch.Should().Be(PlayerModelHandSettings.DEFAULT_MAX_STRETCH);
        }
        PlayerModelProfile.Default.DisplayName.Should().BeNull();
        PlayerModelProfile.Parse(null, out _).DisplayName.Should().BeNull();
    }

    [TestMethod]
    public void EveryFieldOfAFullProfileIsRead()
    {
        PlayerModelProfile profile = PlayerModelProfile.Parse(FULL, out IReadOnlyList<string> problems);

        problems.Should().BeEmpty();
        profile.DisplayName.Should().Be("Example Model");
        profile.Notes.Should().Contain("by hand");
        profile.ScaleSource.Should().Be(PlayerModelScaleSource.Head);
        profile.ScaleFactor.Should().Be(1.05f);
        profile.EyeHeight.Should().Be(1.52f);
        profile.BoneNames.Prefix.Should().Be("rig_");
        profile.BoneNames.Full("RightHandIndex1").Should().Be("R_Pointer_01");
        profile.BoneNames.Full("Hips").Should().Be("pelvis");
        profile.BoneNames.Full("Head").Should().Be("rig_Head");
        profile.SharedEmotes.Should().BeFalse();
        profile.EmoteSource.Should().Be("player-other");
        profile.HiddenBones.Should().Equal("Head", "Neck");
        profile.Trims.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, float>("LeftShoulder", 0.8f));
        profile.FirstPersonOffset.Should().Be(new Vector3(0f, -0.02f, 0.03f));
        PlayerModelHandSettings hands = profile.Hands;
        hands.Enabled.Should().BeTrue();
        hands.Fingers.Should().BeFalse();
        hands.Thumbs.Should().BeFalse();
        hands.ToolTargets.Should().BeFalse();
        hands.MaxStretch.Should().Be(1.1f);
        hands.PdaPullBack.Should().Be(0.02f);
        hands.AnchorForward.Should().Be(0.01f);
        hands.AnchorPalm.Should().Be(-0.005f);
        hands.AnchorThumb.Should().Be(0f);
        hands.RollTrimDegrees.Should().Be(5f);
        hands.TwistShare.Should().Be(0.25f);
        profile.Measured.Should().NotBeNull();
        ((double)profile.Measured!["hipsHeight"]!).Should().BeApproximately(0.969, 1e-9);
    }

    [TestMethod]
    public void AProfileThatIsNotJsonGivesTheDefaultAndOneProblem()
    {
        PlayerModelProfile profile = PlayerModelProfile.Parse("{ \"displayName\": ", out IReadOnlyList<string> problems);

        problems.Should().ContainSingle().Which.Should().Contain("not JSON");
        profile.DisplayName.Should().BeNull();
        profile.SharedEmotes.Should().BeFalse("no source is named");
    }

    [TestMethod]
    public void WhatCannotBeReadIsNamedAndLeftAtItsDefaultWhileTheRestIsKept()
    {
        const string bad = """
            {
              "displayName": "Mónica",
              "scale": { "source": "feet", "factor": 99 },
              "eyeHeight": "high",
              "bones": { "map": { "Hips": 3 } },
              "emotes": { "shared": "yes", "source": "../../evil" },
              "firstPerson": { "hiddenBones": "Head", "trims": { "LeftShoulder": 9 }, "offset": [5, 0, 0] },
              "hands": { "thumbs": 1, "maxStretch": 4, "pdaPullBack": 3, "anchor": [1, 2], "rollTrim": 200, "twistShare": -1, "fingers": false }
            }
            """;

        PlayerModelProfile profile = PlayerModelProfile.Parse(bad, out IReadOnlyList<string> problems);

        string all = string.Join("\n", problems);
        foreach (string expected in new[] { "displayName", "scale.source", "scale.factor", "eyeHeight", "bones.map", "emotes.shared", "emotes.source", "firstPerson.hiddenBones", "firstPerson.trims", "firstPerson.offset", "hands.thumbs", "hands.maxStretch", "hands.pdaPullBack", "hands.anchor", "hands.rollTrim", "hands.twistShare" })
        {
            all.Should().Contain(expected);
        }
        profile.DisplayName.Should().BeNull("a name with a letter that the game's font may lack is not shown");
        profile.ScaleSource.Should().BeNull();
        profile.ScaleFactor.Should().BeNull();
        profile.EyeHeight.Should().BeNull();
        profile.SharedEmotes.Should().BeFalse("no source is named");
        profile.EmoteSource.Should().Be(PlayerModelProfile.DEFAULT_EMOTE_SOURCE);
        profile.HiddenBones.Should().Equal("Head");
        profile.FirstPersonOffset.Should().Be(Vector3.Zero);
        profile.Hands.Thumbs.Should().BeTrue();
        profile.Hands.MaxStretch.Should().Be(PlayerModelHandSettings.DEFAULT_MAX_STRETCH);
        profile.Hands.PdaPullBack.Should().Be(0f);
        profile.Hands.RollTrimDegrees.Should().Be(0f);
        profile.Hands.TwistShare.Should().Be(PlayerModelArmIk.DEFAULT_TWIST_SHARE);
        profile.Hands.Fingers.Should().BeFalse("the field that was fine is kept");
    }

    [TestMethod]
    public void TheNameOfAModelIsTheProfilesOrTheFileNameMadePresentable()
    {
        PlayerModelProfile.Parse("{ \"displayName\": \"Example Person\" }", out _).DisplayNameOr("player-example").Should().Be("Example Person");
        PlayerModelProfile.Default.DisplayNameOr("player-example").Should().Be("Example");
        PlayerModelProfile.Default.DisplayNameOr("player-sample_two").Should().Be("Sample Two");
        PlayerModelProfile.Default.DisplayNameOr("my_cool-model").Should().Be("My Cool Model");
        PlayerModelProfile.Parse($"{{ \"displayName\": \"{new string('x', PlayerModelProfile.MAX_DISPLAY_NAME_LENGTH + 1)}\" }}", out IReadOnlyList<string> problems).DisplayName.Should().BeNull();
        problems.Should().ContainSingle();
    }

    [TestMethod]
    public void ThePrettyNameOfAFileNameDropsPlayerAndCapitalisesTheWords()
    {
        PlayerModelRules.PrettyName("player-example").Should().Be("Example");
        PlayerModelRules.PrettyName("Player_Ann").Should().Be("Ann");
        PlayerModelRules.PrettyName("player-").Should().Be("Player", "nothing is left of the name without the prefix");
        PlayerModelRules.PrettyName("alice").Should().Be("Alice");
        PlayerModelRules.PrettyName("a--b__c").Should().Be("A B C");
        PlayerModelRules.PrettyName("clockwork.farmhand-v2").Should().Be("Clockwork.farmhand V2");
        PlayerModelRules.DisplayName(null).Should().Be("Default");
        PlayerModelRules.DisplayName("").Should().Be("Default");
        PlayerModelRules.DisplayName("player-ann").Should().Be("Ann");
    }
}
