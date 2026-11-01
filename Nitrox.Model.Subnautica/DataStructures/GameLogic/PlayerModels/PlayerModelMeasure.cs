using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// The measurements of a model's skeleton and of the vanilla body (docs/coop/PLAYER_MODELS.md, "Adding a new model"): heights, widths and lengths in metres, from the rest pose of the file and the bind pose of the
/// diver. The game works the sizes out from the skeleton by itself (the auto-calibration); this is what a person reads to see how a new model differs from the diver, what the Debug command <c>vufp calibrate</c>
/// writes into the <c>measured</c> block of the profile, and what a test checks the profile against so that it does not go out of date when the file changes. A measure that the skeleton lacks (the rig has no toes) is not
/// in the list. Plain numbers, no game.
/// </summary>
public static class PlayerModelMeasure
{
    /// <summary>The names of the measures, in the order that they are listed in.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "hipsHeight", "shoulderHeight", "headHeight", "shoulderWidth", "hipWidth", "upperArm", "foreArm", "hand", "fingers", "thigh", "shin", "foot", "armRestDegrees"
    ];

    /// <summary>
    /// What the rest pose of a model says, in the model's own units (not yet sized): the height of the hips, of the joints of the arms (the shoulders), of the head bone; the distance between the joints of the
    /// two arms and of the two legs; the length of the upper arm, the forearm, the hand (from the wrist to the knuckle of the middle finger) and the fingers (from there to the tip of the middle finger), the thigh, the shin
    /// and the foot (from the ankle to the ball of the foot), each the mean of the two sides; and how many degrees below horizontal the upper arm rests (0 in a T, about 40 in an A).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, float>> OfModel(PlayerModelRest rest, PlayerModelBoneNames names)
    {
        List<KeyValuePair<string, float>> values = [];

        bool Find(string canonical, out Vector3 position)
        {
            int node = rest.Find(names, canonical);
            position = node >= 0 ? rest.Position(node) : Vector3.Zero;
            return node >= 0;
        }

        void Add(string name, float value)
        {
            values.Add(new KeyValuePair<string, float>(name, value));
        }

        float? Mean(Func<string, float?> measure)
        {
            float? left = measure("Left");
            float? right = measure("Right");
            if (left.HasValue && right.HasValue)
            {
                return (left.Value + right.Value) / 2f;
            }
            return left ?? right;
        }

        float? Between(string start, string end)
        {
            return Find(start, out Vector3 a) && Find(end, out Vector3 b) ? Vector3.Distance(a, b) : null;
        }

        if (Find(PlayerModelRig.HIPS, out Vector3 hips))
        {
            Add("hipsHeight", hips.Y);
        }
        float? shoulderHeight = Mean(side => Find($"{side}Arm", out Vector3 arm) ? arm.Y : null);
        if (shoulderHeight.HasValue)
        {
            Add("shoulderHeight", shoulderHeight.Value);
        }
        if (Find(PlayerModelRig.HEAD, out Vector3 head))
        {
            Add("headHeight", head.Y);
        }
        if (Find("LeftArm", out Vector3 leftArm) && Find("RightArm", out Vector3 rightArm))
        {
            Add("shoulderWidth", Math.Abs(leftArm.X - rightArm.X));
        }
        if (Find("LeftUpLeg", out Vector3 leftLeg) && Find("RightUpLeg", out Vector3 rightLeg))
        {
            Add("hipWidth", Math.Abs(leftLeg.X - rightLeg.X));
        }
        AddIfThere(values, "upperArm", Mean(side => Between($"{side}Arm", $"{side}ForeArm")));
        AddIfThere(values, "foreArm", Mean(side => Between($"{side}ForeArm", $"{side}Hand")));
        AddIfThere(values, "hand", Mean(side => Between($"{side}Hand", $"{side}HandMiddle1")));
        AddIfThere(values, "fingers", Mean(side => Between($"{side}HandMiddle1", $"{side}HandMiddle4")));
        AddIfThere(values, "thigh", Mean(side => Between($"{side}UpLeg", $"{side}Leg")));
        AddIfThere(values, "shin", Mean(side => Between($"{side}Leg", $"{side}Foot")));
        AddIfThere(values, "foot", Mean(side => Between($"{side}Foot", $"{side}ToeBase")));
        AddIfThere(values, "armRestDegrees", Mean(side => Find($"{side}Arm", out Vector3 shoulderJoint) && Find($"{side}ForeArm", out Vector3 elbowJoint) ? DegreesBelowHorizontal(elbowJoint - shoulderJoint) : null));
        return values;
    }

    /// <summary>The same measures of the vanilla body, from its bind pose (in metres, the diver as the game has him).</summary>
    public static IReadOnlyList<KeyValuePair<string, float>> OfVanilla(IReadOnlyDictionary<string, PlayerModelBind> bind)
    {
        List<KeyValuePair<string, float>> values = [];

        bool Find(string name, out Vector3 position)
        {
            bool found = bind.TryGetValue(name, out PlayerModelBind pose);
            position = found ? pose.Position : Vector3.Zero;
            return found;
        }

        float? Mean(Func<string, float?> measure)
        {
            float? left = measure("L");
            float? right = measure("R");
            if (left.HasValue && right.HasValue)
            {
                return (left.Value + right.Value) / 2f;
            }
            return left ?? right;
        }

        float? Between(string start, string end)
        {
            return Find(start, out Vector3 a) && Find(end, out Vector3 b) ? Vector3.Distance(a, b) : null;
        }

        string Tip(string side) => Find($"hand_{side}_midl_tip_rig", out _) ? $"hand_{side}_midl_tip_rig" : $"hand_{side}_midl_tip";

        if (Find(PlayerModelRig.VANILLA_HIPS, out Vector3 hips))
        {
            values.Add(new KeyValuePair<string, float>("hipsHeight", hips.Y));
        }
        AddIfThere(values, "shoulderHeight", Mean(side => Find($"shoulder_{side}", out Vector3 shoulder) ? shoulder.Y : null));
        if (Find(PlayerModelRig.VANILLA_HEAD, out Vector3 head))
        {
            values.Add(new KeyValuePair<string, float>("headHeight", head.Y));
        }
        if (Find("shoulder_L", out Vector3 leftShoulder) && Find("shoulder_R", out Vector3 rightShoulder))
        {
            values.Add(new KeyValuePair<string, float>("shoulderWidth", Math.Abs(leftShoulder.X - rightShoulder.X)));
        }
        if (Find("thigh_L", out Vector3 leftThigh) && Find("thigh_R", out Vector3 rightThigh))
        {
            values.Add(new KeyValuePair<string, float>("hipWidth", Math.Abs(leftThigh.X - rightThigh.X)));
        }
        AddIfThere(values, "upperArm", Mean(side => Between($"shoulder_{side}", $"elbow_{side}")));
        AddIfThere(values, "foreArm", Mean(side => Between($"elbow_{side}", $"hand_{side}")));
        AddIfThere(values, "hand", Mean(side => Between($"hand_{side}", $"hand_{side}_midl_base")));
        AddIfThere(values, "fingers", Mean(side => Between($"hand_{side}_midl_base", Tip(side))));
        AddIfThere(values, "thigh", Mean(side => Between($"thigh_{side}", $"calf_{side}")));
        AddIfThere(values, "shin", Mean(side => Between($"calf_{side}", $"ankle_{side}")));
        AddIfThere(values, "foot", Mean(side => Between($"ankle_{side}", side == "L" ? "toe_L_rig" : "toe_R")));
        AddIfThere(values, "armRestDegrees", Mean(side => Find($"shoulder_{side}", out Vector3 shoulderJoint) && Find($"elbow_{side}", out Vector3 elbowJoint) ? DegreesBelowHorizontal(elbowJoint - shoulderJoint) : null));
        return values;
    }

    private static void AddIfThere(List<KeyValuePair<string, float>> values, string name, float? value)
    {
        if (value.HasValue)
        {
            values.Add(new KeyValuePair<string, float>(name, value.Value));
        }
    }

    /// <summary>How many degrees below horizontal a direction points (90 straight down, 0 sideways, negative above the horizon).</summary>
    public static float DegreesBelowHorizontal(Vector3 direction)
    {
        float length = direction.Length();
        if (length < 1e-6f)
        {
            return 0f;
        }
        return (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, -direction.Y / length))) * 180.0 / Math.PI);
    }

    /// <summary>The measures as the <c>measured</c> object of a profile: every number rounded to a tenth of a millimetre.</summary>
    public static JObject ToJson(IReadOnlyList<KeyValuePair<string, float>> values)
    {
        JObject json = new();
        foreach (KeyValuePair<string, float> value in values)
        {
            json[value.Key] = Math.Round(value.Value, 4);
        }
        return json;
    }

    /// <summary>
    /// The measures of a model next to the diver's, one line each: the model's own value, the same sized by <paramref name="scale" /> (what the game shows), the diver's, and the model's over the diver's. A difference
    /// of more than 10% in a length is what makes a hand fall short of a grip or a thigh poke through a seat: it is worth a look at the model, or a profile field.
    /// </summary>
    public static string Compare(IReadOnlyList<KeyValuePair<string, float>> model, IReadOnlyList<KeyValuePair<string, float>> vanilla, float scale)
    {
        StringBuilder text = new();
        foreach (KeyValuePair<string, float> measure in model)
        {
            bool isAngle = measure.Key == "armRestDegrees";
            float sized = isAngle ? measure.Value : measure.Value * scale;
            float? diver = null;
            foreach (KeyValuePair<string, float> candidate in vanilla)
            {
                if (candidate.Key == measure.Key)
                {
                    diver = candidate.Value;
                }
            }
            string unit = isAngle ? "deg" : "m";
            string against = diver.HasValue ? string.Format(CultureInfo.InvariantCulture, "diver {0:F3} {1}{2}", diver.Value, unit, isAngle ? "" : string.Format(CultureInfo.InvariantCulture, ", model/diver {0:F2}", sized / diver.Value)) : "diver -";
            text.Append(string.Format(CultureInfo.InvariantCulture, "{0}: model {1:F3} {2}, sized {3:F3} {2}, {4}", measure.Key, measure.Value, unit, sized, against));
            text.Append('\n');
        }
        return text.ToString();
    }
}

/// <summary>
/// The profile file as a person and the Debug command <c>vufp calibrate</c> keep it (docs/coop/PLAYER_MODELS.md): writing the measured block into a profile changes nothing else in it, so that what a person put in
/// by hand (a name, the trims, the notes) survives every measurement.
/// </summary>
public static class PlayerModelProfileFile
{
    /// <summary>
    /// The text of the profile with its <c>measured</c> block replaced by <paramref name="measured" /> (and <c>schema</c> set): everything else in <paramref name="existingText" /> is kept as it is, fields that this game
    /// does not know included. A text that is empty or not JSON starts a new profile. Indented, one field to a line.
    /// </summary>
    public static string WithMeasured(string? existingText, IReadOnlyList<KeyValuePair<string, float>> measured)
    {
        return WithMeasured(existingText, PlayerModelMeasure.ToJson(measured));
    }

    /// <summary>The same with the measured block as a JSON object.</summary>
    public static string WithMeasured(string? existingText, JObject measured)
    {
        JObject root = new();
        if (!string.IsNullOrWhiteSpace(existingText))
        {
            try
            {
                root = JObject.Parse(existingText!);
            }
            catch (JsonException)
            {
                root = new JObject();
            }
        }
        root["schema"] = PlayerModelProfile.SCHEMA;
        root["measured"] = measured;
        return root.ToString(Formatting.Indented);
    }
}
