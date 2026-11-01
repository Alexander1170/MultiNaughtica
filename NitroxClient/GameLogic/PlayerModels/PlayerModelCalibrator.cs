using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using UnityEngine;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// What the game makes of a model that nobody has set up (docs/coop/PLAYER_MODELS.md, "Adding a new model"): the auto-calibration run against the live diver, as a report, and the measured block of the model's
/// profile that goes with it. The Debug command <c>vufp calibrate</c> prints the report and writes the profile; it is how a person (or an agent) finds out in a minute whether a new model needs a profile field at all.
/// </summary>
internal static class PlayerModelCalibrator
{
    /// <summary>
    /// The report for <paramref name="model" />: how it is sized, whether the pose copy finds every bone, how each hand is turned and how many finger bones it has, every measure next to the diver's, whether the arms
    /// reach what the diver's reach (with the stretch), and what the emotes do. <paramref name="view" /> is the <c>player_view</c> of the local body, which gives the bind pose of the diver. The measures that go into
    /// the profile are in <paramref name="measured" />.
    /// </summary>
    public static IReadOnlyList<string> Report(LoadedPlayerModel model, Transform view, out IReadOnlyList<KeyValuePair<string, float>> measured)
    {
        List<string> lines = [];
        PlayerModelProfile profile = model.Profile;
        Dictionary<string, PlayerModelBind> bind = CustomPlayerModel.VanillaBindOf(view);
        Transform attach = view.Find(NitroxClient.GameLogic.PlayerLogic.PlayerModel.PlayerEquipmentConstants.ITEM_ATTACH_POINT_GAME_OBJECT_NAME);
        System.Numerics.Vector3 attachPosition = attach ? new System.Numerics.Vector3(attach.localPosition.x, attach.localPosition.y, attach.localPosition.z) : System.Numerics.Vector3.Zero;
        System.Numerics.Quaternion attachRotation = attach ? new System.Numerics.Quaternion(attach.localRotation.x, attach.localRotation.y, attach.localRotation.z, attach.localRotation.w) : System.Numerics.Quaternion.Identity;
        PlayerModelCalibration calibration = PlayerModelRig.Calibrate(model.Rest, bind, attachPosition, attachRotation, profile.ScaleSource ?? CustomPlayerModels.ScaleSource, profile.BoneNames, profile.ScaleFactor, profile.EyeHeight);

        lines.Add($"model '{model.Id}' ({profile.DisplayNameOr(model.Id)}): {model.Glb.SkinJoints.Length} bones, {model.Glb.VertexCount} vertices, {model.Height:F2} m tall, profile {(profile == PlayerModelProfile.Default ? "none" : "read")}");
        string sizedBy = profile.ScaleFactor.HasValue ? "the factor of its profile" : profile.ScaleSource == PlayerModelScaleSource.Head ? "the head (its profile says so)" : "the hips";
        lines.Add($"size: hips factor {calibration.HipsFactor:F4}, head factor {calibration.HeadFactor:F4}{(calibration.FactorsDiffer ? " (they differ by more than 8%: the head and the feet cannot both be right)" : "")}; sized by {sizedBy}: scale {calibration.Scale:F4}, so it stands {model.Height * calibration.Scale:F2} m tall");
        lines.Add($"pose copy: {calibration.Bones.Count} of {PlayerModelRig.Pairs.Count} bones follow the diver's{(calibration.Missing.Count > 0 ? $", missing: {string.Join("; ", calibration.Missing)}" : ", none missing")}");

        PlayerModelHandsCalibration hands = PlayerModelHands.Calibrate(model.Rest, bind, profile.BoneNames);
        foreach (PlayerModelHandCalibration hand in new[] { hands.Left, hands.Right })
        {
            if (hand == null)
            {
                continue;
            }
            float palm = System.Numerics.Vector3.Distance(model.Rest.Position(hand.HandNode), model.Rest.Position(hand.KnuckleNodes[1])) * calibration.Scale;
            lines.Add($"{hand.Side} hand: turned {hand.FixDegrees:F1} deg from the diver's at rest (palm roll {hand.RollDegrees:F1} deg), {hand.Fingers.Count} of {PlayerModelHands.FINGER_BONES_PER_HAND} finger bones, {palm:F3} m from the wrist to the middle knuckle (the diver's {Distance(bind, "hand_L", "hand_L_midl_base"):F3} m)");
        }
        foreach (string missing in hands.Missing)
        {
            lines.Add($"hands: {missing}");
        }

        IReadOnlyList<KeyValuePair<string, float>> diver = PlayerModelMeasure.OfVanilla(bind);
        measured = PlayerModelMeasure.OfModel(model.Rest, profile.BoneNames);
        foreach (string line in PlayerModelMeasure.Compare(measured, diver, calibration.Scale).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            lines.Add($"measure {line}");
        }

        float modelReach = (Value(measured, "upperArm") + Value(measured, "foreArm")) * calibration.Scale;
        float diverReach = Value(diver, "upperArm") + Value(diver, "foreArm");
        if (diverReach > 0f && modelReach > 0f)
        {
            float stretched = modelReach * profile.Hands.MaxStretch;
            lines.Add($"reach: its arm reaches {modelReach:F3} m against the diver's {diverReach:F3} m ({modelReach / diverReach:P0}); stretched up to {profile.Hands.MaxStretch:F2}x it reaches {stretched:F3} m, {(stretched >= diverReach ? "enough for every grip of the diver" : "NOT ENOUGH: far grips fall short, see hands.maxStretch in the profile")}");
        }

        int own = PlayerEmoteRules.EmotesOf(model.Glb.Clips).Count;
        SharedEmoteSet shared = profile.SharedEmotes ? SharedEmotes.TryGet(profile.EmoteSource) : null;
        StringBuilder emotes = new();
        emotes.Append($"emotes: {own} of its own");
        if (!profile.SharedEmotes)
        {
            emotes.Append(", the shared emotes are switched off in its profile");
        }
        else if (shared == null)
        {
            emotes.Append($", the shared emotes of '{profile.EmoteSource}' are being read: ask again in a moment");
        }
        else
        {
            int common = PlayerModelEmoteRetarget.SharedBoneCount(shared.Rest, shared.Names, model.Rest, profile.BoneNames);
            emotes.Append($", shares {common} of the bones of '{shared.SourceId}' ({(common >= PlayerModelEmoteRetarget.MIN_MAPPED_BONES ? $"plays its {shared.Entries.Count} emotes" : $"too few: it plays none of them, at least {PlayerModelEmoteRetarget.MIN_MAPPED_BONES} are needed")})");
        }
        lines.Add(emotes.ToString());
        return lines;
    }

    /// <summary>
    /// Writes the measured block into the profile beside the model's file (the file that was loaded: the launcher's folder for a shipped model, which the next build overwrites, so the person copies it into the repository
    /// from the path that is returned), keeping everything else in the profile as it is.
    /// </summary>
    public static string WriteProfile(LoadedPlayerModel model, IReadOnlyList<KeyValuePair<string, float>> measured)
    {
        string path = Path.Combine(Path.GetDirectoryName(model.Path) ?? "", $"{model.Id}{PlayerModelProfile.FILE_SUFFIX}");
        string existing = File.Exists(path) ? File.ReadAllText(path) : null;
        File.WriteAllText(path, PlayerModelProfileFile.WithMeasured(existing, measured), new UTF8Encoding(false));
        return path;
    }

    private static float Value(IReadOnlyList<KeyValuePair<string, float>> values, string name)
    {
        foreach (KeyValuePair<string, float> value in values)
        {
            if (value.Key == name)
            {
                return value.Value;
            }
        }
        return 0f;
    }

    private static float Distance(IReadOnlyDictionary<string, PlayerModelBind> bind, string a, string b)
    {
        return bind.TryGetValue(a, out PlayerModelBind first) && bind.TryGetValue(b, out PlayerModelBind second) ? System.Numerics.Vector3.Distance(first.Position, second.Position) : 0f;
    }
}
