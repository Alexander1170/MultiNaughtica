using System.Text.RegularExpressions;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>The few texts of the navigator console that need a rule of their own (pure, so that they are tested).</summary>
public static class NavigatorConsoleText
{
    private static readonly Regex SPRITE_NAME = new("name=\"(?:Keyboard)?([A-Za-z0-9]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The name of a key from the game's text for a button, which is TextMesh Pro markup that a plain UI text cannot draw (<c>&lt;sprite name="KeyboardE" color=#ADF8FFFF&gt;</c> is "E"). Text without markup is kept
    /// as it is (the markup tags removed); nothing at all gives <paramref name="fallback" />.
    /// </summary>
    public static string PlainKeyName(string formatted, string fallback)
    {
        if (string.IsNullOrWhiteSpace(formatted))
        {
            return fallback;
        }
        Match sprite = SPRITE_NAME.Match(formatted);
        if (formatted.Contains("<sprite") && sprite.Success)
        {
            string name = sprite.Groups[1].Value;
            return name.Length > 0 ? name : fallback;
        }
        string plain = Regex.Replace(formatted, "<[^>]*>", string.Empty).Trim();
        return plain.Length > 0 ? plain : fallback;
    }
}
