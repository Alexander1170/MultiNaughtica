using System;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class PDAEncyclopediaEntryAdd : Packet
{
    public string Key { get; }
    /// <summary>
    ///     If true, shows a notification to the player.
    /// </summary>
    public bool Verbose { get; }

    /// <summary>
    ///     The text and picture of a time capsule entry (null for every other entry): the other players do not have the capsule, and the content is per player.
    /// </summary>
    public TimeCapsuleContentData Content { get; }

    public PDAEncyclopediaEntryAdd(string key, bool verbose, TimeCapsuleContentData content = null)
    {
        Key = key;
        Verbose = verbose;
        Content = content;
    }
}
