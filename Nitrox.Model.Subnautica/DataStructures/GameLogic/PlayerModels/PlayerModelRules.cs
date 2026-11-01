using System;
using System.IO;
using System.Text;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// The game-free rules of the custom player models (docs/coop/PLAYER_MODELS.md): what a model id may look like, how the id comes from a file name, how it is shown, and which of the clips of a model are emotes.
/// The id of a model is the name of its file without the extension: it travels between the players, so it must be a name and never a path.
/// </summary>
public static class PlayerModelRules
{
    public const string FILE_EXTENSION = ".glb";

    public const int MAX_ID_LENGTH = 40;

    /// <summary>The folder (inside the launcher's Resources) from which the zip ships its models.</summary>
    public const string SHIPPED_FOLDER = "PlayerModels";

    /// <summary>The folder (inside the Nitrox folder of the AppData of the current Windows user) of the models that a player adds.</summary>
    public const string USER_FOLDER = "models";

    /// <summary>The id of "no custom model": the vanilla body.</summary>
    public const string VANILLA = "";

    /// <summary>The server takes one choice of a player per this many seconds; a client sends one at most every <see cref="CLIENT_INTERVAL_SECONDS" />, so an honest client is never turned down.</summary>
    public const double SERVER_INTERVAL_SECONDS = 2.0;

    public const double CLIENT_INTERVAL_SECONDS = 2.3;

    /// <summary>
    /// The id that a server or a client takes from a packet: a valid one is kept, anything else (a path, markup, something too long) is the vanilla body. The id is only ever looked up among the files that were
    /// scanned, so it can never name a path, but it is also written to logs and shown to players.
    /// </summary>
    public static string SanitizeId(string? id)
    {
        return IsValidId(id) ? id! : VANILLA;
    }

    /// <summary>The length that goes with a choice: none for the vanilla body, never negative.</summary>
    public static long SanitizeBytes(string id, long bytes)
    {
        return id.Length == 0 || bytes < 0 ? 0 : bytes;
    }

    /// <summary>
    /// Whether the file that a player has is the one that another player chose: the lengths are the same (and not nothing). Two files of the same name and another length are not the same model, and a file that
    /// is not there has no length: both show the vanilla body, with one warning.
    /// </summary>
    public static bool SameFile(long ownBytes, long chosenBytes)
    {
        return ownBytes > 0 && ownBytes == chosenBytes;
    }

    /// <summary>An id is 1 to <see cref="MAX_ID_LENGTH" /> letters, digits, dots, dashes and underscores, and not made of dots only.</summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id!.Length > MAX_ID_LENGTH)
        {
            return false;
        }
        bool onlyDots = true;
        foreach (char c in id)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_';
            if (!ok)
            {
                return false;
            }
            onlyDots &= c == '.';
        }
        return !onlyDots;
    }

    /// <summary>
    /// The id that a file name gives: the name without ".glb" (any case), when that is a valid id; null for a file that is not a model or whose name cannot be an id (the player has to rename it, and the log says so).
    /// </summary>
    public static string? IdFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }
        string name = Path.GetFileName(fileName);
        if (!name.EndsWith(FILE_EXTENSION, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string id = name.Substring(0, name.Length - FILE_EXTENSION.Length);
        return IsValidId(id) ? id : null;
    }

    /// <summary>What a list shows for an id when the model's profile gives no name: the file name made presentable (<see cref="PrettyName" />); an empty id is the vanilla body.</summary>
    public static string DisplayName(string? id)
    {
        return string.IsNullOrEmpty(id) ? "Default" : PrettyName(id!);
    }

    /// <summary>
    /// A file name as a name: the "player-" or "player_" in front of it goes, dashes and underscores become blanks, every word starts with a capital: "player-sample" is "Sample", "my_cool-model" is "My Cool Model".
    /// The name that the model's profile gives (<see cref="PlayerModelProfile.DisplayName" />) is shown instead when there is one.
    /// </summary>
    public static string PrettyName(string id)
    {
        string name = id;
        foreach (string prefix in new[] { "player-", "player_" })
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(prefix.Length);
                break;
            }
        }
        StringBuilder builder = new(name.Length);
        bool newWord = true;
        foreach (char c in name)
        {
            if (c == '-' || c == '_' || c == ' ')
            {
                if (!newWord)
                {
                    builder.Append(' ');
                }
                newWord = true;
                continue;
            }
            builder.Append(newWord ? char.ToUpperInvariant(c) : c);
            newWord = false;
        }
        string pretty = builder.ToString().Trim();
        return pretty.Length > 0 ? pretty : id;
    }

    /// <summary>
    /// Whether a clip of a model is an emote. The clips that a model brings for walking and running are not: the game drives the movement. Anything else is one (the first model has ten emotes and four
    /// movement clips).
    /// </summary>
    public static bool IsEmoteClip(string? clipName)
    {
        if (string.IsNullOrWhiteSpace(clipName))
        {
            return false;
        }
        foreach (string movement in new[] { "walk", "run", "crawl", "idle" })
        {
            if (clipName!.IndexOf(movement, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>What the wheel shows for a clip: "Gangnam_Groove" is "Gangnam Groove".</summary>
    public static string EmoteLabel(string? clipName)
    {
        return string.IsNullOrWhiteSpace(clipName) ? string.Empty : clipName!.Replace('_', ' ').Trim();
    }
}
