using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// A player's quick ping (docs/coop/QUICK_PING.md): the player looked at something and pressed the ping key. A client sends it with its own session and name; the server does not believe either: it
/// puts the real <see cref="SessionId" /> and <see cref="SenderName" /> of the connection in, cleans <see cref="Text" />, drops a ping that comes too soon after the player's last one or that is not a place in
/// the world, and passes the result to everybody else (<c>PlayerPingCreatedProcessor</c>). The ping is not saved: a player who joins later sees none.
/// </summary>
/// <remarks>
/// Changed with the quick ping (the stock packet had the three first fields and the text was the whole HUD line, "Bob pinged Reaper Leviathan"): <see cref="Text" /> is now only what the player looked at, and
/// <see cref="SenderName" /> is new. Both sides of a game must run the same build, as they always have to.
/// </remarks>
[Serializable]
public class PlayerPingCreated(SessionId sessionId, string text, NitroxVector3 position, NitroxId pingId, string senderName = "")
    : Packet
{
    /// <summary>The player who pinged. A client sends its own; the server replaces it with the one of the connection.</summary>
    public SessionId SessionId { get; } = sessionId;

    /// <summary>What the player looked at ("Reaper Leviathan", "Look here"); the HUD line is made from it and the name of the sender (<c>QuickPingRules.MarkerText</c>).</summary>
    public string Text { get; } = text;

    public NitroxVector3 Position { get; } = position;

    public NitroxId PingId { get; } = pingId;

    /// <summary>The name of the player who pinged, as the server knows it. Empty in what a client sends.</summary>
    public string SenderName { get; } = senderName;
}
