using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// What the game is told about one model besides its file (docs/coop/PLAYER_MODELS.md, "Adding a new model"): a small JSON file beside the GLB, <c>&lt;id&gt;.profile.json</c>. Every field is optional: the game works
/// out what it can from the skeleton of the file (sizes, the palm from the finger bones, the arm lengths) and a model with no profile works; the profile only <b>overrides</b> what the measurement gets wrong or
/// adds what no measurement can tell (the name in the Options list, which bones are hidden in first person). <see cref="Parse" /> never throws: what cannot be read is named in the problems and the field keeps its default.
/// </summary>
public sealed class PlayerModelProfile
{
    public const int SCHEMA = 1;

    public const string FILE_SUFFIX = ".profile.json";

    /// <summary>The model whose emote clips another model with the same skeleton plays, retargeted onto its own bones. None by default: the game ships no model, so a model with no emotes of its own has none unless its profile names a source (<c>emotes.source</c>) that is installed.</summary>
    public const string DEFAULT_EMOTE_SOURCE = "";

    public const int MAX_DISPLAY_NAME_LENGTH = 40;

    private const int MAX_NAME_LENGTH = 100;

    private const int MAX_LIST = 24;

    /// <summary>A profile with nothing in it: everything is measured.</summary>
    public static PlayerModelProfile Default { get; } = new();

    /// <summary>The name that the Options list shows; null when the profile has none (the file name, made presentable, is shown then).</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Free text for the next person who works on the model: where it came from, what was fixed by hand.</summary>
    public string? Notes { get; private set; }

    /// <summary>How the model is sized against the diver (the hips, so that the feet stand on the floor, or the head); null: the choice of the player or the hips.</summary>
    public PlayerModelScaleSource? ScaleSource { get; private set; }

    /// <summary>The factor that sizes the model, when it is not to be worked out from the hips or the head; null for none.</summary>
    public float? ScaleFactor { get; private set; }

    /// <summary>The height of the model's eyes above its feet in the model's own units: the sizing by the head then takes the eye height of the diver (the camera bone) against it; null: the head bone is used.</summary>
    public float? EyeHeight { get; private set; }

    /// <summary>How the rig names its bones (the Mixamo names with the exporter's prefix unless the profile says otherwise).</summary>
    public PlayerModelBoneNames BoneNames { get; private set; } = PlayerModelBoneNames.Mixamo;

    /// <summary>Whether the emote clips of <see cref="EmoteSource" /> are played on this model too, retargeted onto its bones (the model's own clips win). Always false while no source is named.</summary>
    public bool SharedEmotes
    {
        get => sharedEmotes && EmoteSource.Length > 0;
        private set => sharedEmotes = value;
    }

    private bool sharedEmotes = true;

    public string EmoteSource { get; private set; } = DEFAULT_EMOTE_SOURCE;

    /// <summary>The bones that are not drawn while the player looks through the model's eyes (their size is turned to nothing, so what hangs on them goes too): the head, and what else gets in front of the camera.</summary>
    public IReadOnlyList<string> HiddenBones { get; private set; } = ["Head"];

    /// <summary>Bones that are made smaller in first person (a shoulder pad that comes within the near clip of the camera): the bone, and the factor of its size.</summary>
    public IReadOnlyDictionary<string, float> Trims { get; private set; } = new Dictionary<string, float>();

    /// <summary>Where the whole model stands against the vanilla body in first person (metres in the frame of the body: right, up, forward).</summary>
    public Vector3 FirstPersonOffset { get; private set; }

    /// <summary>What the hands do for this model (the starting values: the Debug commands change them while a model is tuned).</summary>
    public PlayerModelHandSettings Hands { get; private set; } = new();

    /// <summary>What the Debug command <c>vufp calibrate</c> measured, as it wrote it (the game does not read it: it is there for the next person, and a test keeps it up to date); null when there is none.</summary>
    public JObject? Measured { get; private set; }

    /// <summary>The name for a list: the profile's, else the one that the file name gives.</summary>
    public string DisplayNameOr(string id) => !string.IsNullOrEmpty(DisplayName) ? DisplayName! : PlayerModelRules.DisplayName(id);

    /// <summary>Reads a profile. Anything that is wrong with it is a line in <paramref name="problems" /> and the field is left at its default; a text that is not JSON at all gives the default profile.</summary>
    public static PlayerModelProfile Parse(string? json, out IReadOnlyList<string> problems)
    {
        List<string> found = [];
        problems = found;
        PlayerModelProfile profile = new();
        if (string.IsNullOrWhiteSpace(json))
        {
            return profile;
        }
        JObject root;
        try
        {
            root = JObject.Parse(json!);
        }
        catch (Exception exception)
        {
            found.Add($"the profile is not JSON: {exception.Message}");
            return profile;
        }

        int? schema = ReadInt(root, "schema", "", found);
        if (schema > SCHEMA)
        {
            found.Add($"the profile has schema {schema}, this game reads {SCHEMA}: the fields that it knows are used");
        }
        string? displayName = ReadString(root, "displayName", "", found, MAX_DISPLAY_NAME_LENGTH);
        if (displayName != null)
        {
            if (IsPrintableAscii(displayName))
            {
                profile.DisplayName = displayName.Trim();
            }
            else
            {
                found.Add($"displayName '{displayName}' has to be plain text (letters, digits, blanks and punctuation of the keyboard): the file name is shown");
            }
        }
        profile.Notes = ReadString(root, "notes", "", found, 2000);

        if (Object(root, "scale", found) is { } scale)
        {
            string? source = ReadString(scale, "source", "scale.", found, 16);
            if (source != null)
            {
                if (source.Equals("hips", StringComparison.OrdinalIgnoreCase))
                {
                    profile.ScaleSource = PlayerModelScaleSource.Hips;
                }
                else if (source.Equals("head", StringComparison.OrdinalIgnoreCase))
                {
                    profile.ScaleSource = PlayerModelScaleSource.Head;
                }
                else
                {
                    found.Add($"scale.source '{source}' is neither hips nor head: ignored");
                }
            }
            profile.ScaleFactor = InRange(ReadFloat(scale, "factor", "scale.", found), "scale.factor", 0.2f, 5f, found);
        }
        profile.EyeHeight = InRange(ReadFloat(root, "eyeHeight", "", found), "eyeHeight", 0.3f, 3f, found);

        if (Object(root, "bones", found) is { } bones)
        {
            string? prefix = ReadString(bones, "prefix", "bones.", found, MAX_NAME_LENGTH, allowEmpty: true);
            Dictionary<string, string>? map = null;
            if (Object(bones, "map", found, "bones.") is { } mapObject)
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JToken?> entry in mapObject)
                {
                    if (map.Count >= 128 || entry.Key.Length == 0 || entry.Key.Length > MAX_NAME_LENGTH || entry.Value is not { Type: JTokenType.String } name || string.IsNullOrEmpty((string?)name) || ((string?)name)!.Length > MAX_NAME_LENGTH)
                    {
                        found.Add($"bones.map '{entry.Key}' is not a bone name that is mapped to a node name: ignored");
                        continue;
                    }
                    map[entry.Key] = (string)name!;
                }
            }
            profile.BoneNames = new PlayerModelBoneNames(prefix ?? PlayerModelRig.MIXAMO_PREFIX, map);
        }

        if (Object(root, "emotes", found) is { } emotes)
        {
            profile.SharedEmotes = ReadBool(emotes, "shared", "emotes.", found) ?? true;
            string? emoteSource = ReadString(emotes, "source", "emotes.", found, PlayerModelRules.MAX_ID_LENGTH);
            if (emoteSource != null)
            {
                if (PlayerModelRules.IsValidId(emoteSource))
                {
                    profile.EmoteSource = emoteSource;
                }
                else
                {
                    found.Add($"emotes.source '{emoteSource}' is not a model id: ignored");
                }
            }
        }

        if (Object(root, "firstPerson", found) is { } firstPerson)
        {
            List<string>? hidden = ReadNames(firstPerson, "hiddenBones", "firstPerson.", found);
            if (hidden != null)
            {
                profile.HiddenBones = hidden;
            }
            if (Object(firstPerson, "trims", found, "firstPerson.") is { } trimObject)
            {
                Dictionary<string, float> trims = new(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JToken?> entry in trimObject)
                {
                    float? factor = entry.Value is { Type: JTokenType.Float or JTokenType.Integer } ? (float)entry.Value : null;
                    if (trims.Count >= MAX_LIST || entry.Key.Length == 0 || entry.Key.Length > MAX_NAME_LENGTH || factor is null || factor < 0.05f || factor > 1.5f)
                    {
                        found.Add($"firstPerson.trims '{entry.Key}' is not a bone with a factor of 0.05 to 1.5: ignored");
                        continue;
                    }
                    trims[entry.Key] = factor.Value;
                }
                profile.Trims = trims;
            }
            profile.FirstPersonOffset = ReadVector(firstPerson, "offset", "firstPerson.", found, 0.5f) ?? Vector3.Zero;
        }

        if (Object(root, "hands", found) is { } hands)
        {
            PlayerModelHandSettings settings = profile.Hands;
            settings.Enabled = ReadBool(hands, "enabled", "hands.", found) ?? settings.Enabled;
            settings.Fingers = ReadBool(hands, "fingers", "hands.", found) ?? settings.Fingers;
            settings.Thumbs = ReadBool(hands, "thumbs", "hands.", found) ?? settings.Thumbs;
            settings.ToolTargets = ReadBool(hands, "toolTargets", "hands.", found) ?? settings.ToolTargets;
            settings.MaxStretch = InRange(ReadFloat(hands, "maxStretch", "hands.", found), "hands.maxStretch", 1f, 1.5f, found) ?? settings.MaxStretch;
            settings.PdaPullBack = InRange(ReadFloat(hands, "pdaPullBack", "hands.", found), "hands.pdaPullBack", 0f, 0.1f, found) ?? settings.PdaPullBack;
            settings.RollTrimDegrees = InRange(ReadFloat(hands, "rollTrim", "hands.", found), "hands.rollTrim", -90f, 90f, found) ?? settings.RollTrimDegrees;
            settings.TwistShare = InRange(ReadFloat(hands, "twistShare", "hands.", found), "hands.twistShare", 0f, 1f, found) ?? settings.TwistShare;
            Vector3? anchor = ReadVector(hands, "anchor", "hands.", found, 0.1f);
            if (anchor.HasValue)
            {
                settings.AnchorForward = anchor.Value.X;
                settings.AnchorPalm = anchor.Value.Y;
                settings.AnchorThumb = anchor.Value.Z;
            }
        }

        if (root["measured"] is JObject measured)
        {
            profile.Measured = (JObject)measured.DeepClone();
        }
        return profile;
    }

    private static bool IsPrintableAscii(string text)
    {
        foreach (char c in text)
        {
            if (c < ' ' || c > '~')
            {
                return false;
            }
        }
        return text.Trim().Length > 0;
    }

    private static JObject? Object(JObject parent, string name, List<string> problems, string where = "")
    {
        JToken? token = parent[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }
        if (token is JObject obj)
        {
            return obj;
        }
        problems.Add($"{where}{name} has to be an object: ignored");
        return null;
    }

    private static string? ReadString(JObject parent, string name, string where, List<string> problems, int maxLength, bool allowEmpty = false)
    {
        JToken? token = parent[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }
        if (token.Type != JTokenType.String)
        {
            problems.Add($"{where}{name} has to be text: ignored");
            return null;
        }
        string text = (string)token!;
        if ((text.Length == 0 && !allowEmpty) || text.Length > maxLength)
        {
            problems.Add($"{where}{name} has to be {(allowEmpty ? "" : "1 to ")}{maxLength} characters: ignored");
            return null;
        }
        return text;
    }

    private static float? ReadFloat(JObject parent, string name, string where, List<string> problems)
    {
        JToken? token = parent[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }
        if (token.Type is not (JTokenType.Float or JTokenType.Integer))
        {
            problems.Add($"{where}{name} has to be a number: ignored");
            return null;
        }
        float value = (float)token;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            problems.Add($"{where}{name} is not a number: ignored");
            return null;
        }
        return value;
    }

    private static int? ReadInt(JObject parent, string name, string where, List<string> problems)
    {
        float? value = ReadFloat(parent, name, where, problems);
        return value.HasValue ? (int)value.Value : null;
    }

    private static bool? ReadBool(JObject parent, string name, string where, List<string> problems)
    {
        JToken? token = parent[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }
        if (token.Type != JTokenType.Boolean)
        {
            problems.Add($"{where}{name} has to be true or false: ignored");
            return null;
        }
        return (bool)token;
    }

    private static float? InRange(float? value, string name, float low, float high, List<string> problems)
    {
        if (!value.HasValue)
        {
            return null;
        }
        if (value < low || value > high)
        {
            problems.Add($"{name} {value.Value.ToString(CultureInfo.InvariantCulture)} is out of range {low.ToString(CultureInfo.InvariantCulture)} to {high.ToString(CultureInfo.InvariantCulture)}: ignored");
            return null;
        }
        return value;
    }

    private static Vector3? ReadVector(JObject parent, string name, string where, List<string> problems, float limit)
    {
        JToken? token = parent[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }
        if (token is not JArray { Count: 3 } array || array[0].Type is not (JTokenType.Float or JTokenType.Integer) || array[1].Type is not (JTokenType.Float or JTokenType.Integer) || array[2].Type is not (JTokenType.Float or JTokenType.Integer))
        {
            problems.Add($"{where}{name} has to be three numbers: ignored");
            return null;
        }
        Vector3 vector = new((float)array[0], (float)array[1], (float)array[2]);
        if (Math.Abs(vector.X) > limit || Math.Abs(vector.Y) > limit || Math.Abs(vector.Z) > limit)
        {
            problems.Add($"{where}{name} is more than {limit.ToString(CultureInfo.InvariantCulture)} m away from nothing: ignored");
            return null;
        }
        return vector;
    }

    private static List<string>? ReadNames(JObject parent, string name, string where, List<string> problems)
    {
        JToken? token = parent[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }
        if (token is not JArray array)
        {
            problems.Add($"{where}{name} has to be a list of bone names: ignored");
            return null;
        }
        List<string> names = [];
        foreach (JToken item in array)
        {
            if (item.Type != JTokenType.String || string.IsNullOrEmpty((string?)item) || ((string)item!).Length > MAX_NAME_LENGTH || names.Count >= MAX_LIST)
            {
                problems.Add($"{where}{name} has an entry that is not a bone name: ignored");
                continue;
            }
            names.Add((string)item!);
        }
        return names;
    }
}
