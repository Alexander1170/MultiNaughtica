using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// What the encyclopedia entry of a time capsule shows (the content comes from the UWE web service to the client that spawned the capsule, per player): the other players
/// never got it, and after a restart of the game nobody had it, so the entry showed the raw id and an error text.
/// </summary>
[Serializable]
[DataContract]
public class TimeCapsuleContentData
{
    // The service limits what a player can write (title 60, text 1000): more than that is not an honest capsule
    public const int MAX_TITLE_LENGTH = 120;
    public const int MAX_TEXT_LENGTH = 2000;
    public const int MAX_IMAGE_URL_LENGTH = 500;

    [DataMember(Order = 1)]
    public string Key;

    [DataMember(Order = 2)]
    public string Title;

    [DataMember(Order = 3)]
    public string Text;

    /// <summary>
    /// Relative to the public image host (the game builds the full address)
    /// </summary>
    [DataMember(Order = 4)]
    public string ImageUrl;

    [IgnoreConstructor]
    protected TimeCapsuleContentData()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public TimeCapsuleContentData(string key, string title, string text, string imageUrl)
    {
        Key = key;
        Title = Truncate(title, MAX_TITLE_LENGTH);
        Text = Truncate(text, MAX_TEXT_LENGTH);
        ImageUrl = Truncate(imageUrl, MAX_IMAGE_URL_LENGTH);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public override string ToString()
    {
        return $"{nameof(Key)}: {Key}, {nameof(Title)}: {Title}";
    }
}
