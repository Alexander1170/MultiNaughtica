using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// An emote of a player who wears a custom player model (docs/coop/PLAYER_MODELS.md): <see cref="Playing" /> says that the player started the clip <see cref="EmoteId" /> of his model (the name of the clip, which
/// is a valid model id), or that he stopped (the emote was cancelled: he moved, was hurt, went into a vehicle or pressed the key again; the id is empty then). A clip that ends by itself needs no packet: every
/// client knows how long it is. The server does not believe the session in it: it puts the real one of the connection in, cleans the id, takes one start per
/// <c>PlayerEmoteRules.SERVER_START_INTERVAL_SECONDS</c> of a player, and passes it to everybody else; it keeps nothing, so a player who joins later does not see an emote that is already running.
/// A client plays it only when the player has a custom model that has a clip of that name; for a vanilla body it does nothing.
/// </summary>
[Serializable]
public class PlayerEmote(SessionId sessionId, string emoteId, bool playing) : Packet
{
    /// <summary>The player who emotes. A client sends its own; the server replaces it with the one of the connection.</summary>
    public SessionId SessionId { get; } = sessionId;

    /// <summary>The name of the clip, empty when the emote stopped.</summary>
    public string EmoteId { get; } = emoteId;

    /// <summary>True when the emote starts, false when it stopped.</summary>
    public bool Playing { get; } = playing;
}
