using System;
using System.Collections.Generic;
using System.Linq;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The numbers and the decisions of the quick ping (docs/coop/QUICK_PING.md), free of game types so that the client, the server and the tests use the same ones. The quick ping is stock Nitrox's player ping
/// (the middle mouse button) with the checks that a shared server needs: the server stamps the sender and keeps a rate limit, a player has one ping at a time, a ping lives for the server's
/// <c>QuickPingSeconds</c>, and its text names what the player looked at.
/// </summary>
public static class QuickPingRules
{
    /// <summary>Seconds a ping stays on the HUD of everybody when the server says nothing else (the server option <c>QuickPingSeconds</c>).</summary>
    public const float DEFAULT_SECONDS = 20f;

    public const float MIN_SECONDS = 5f;
    public const float MAX_SECONDS = 120f;

    /// <summary>Metres that the ray of the key reaches from the camera.</summary>
    public const float AIM_RANGE = 300f;

    /// <summary>A hit closer than this is the player's own gear (a helmet, a tool in the hand), not a target.</summary>
    public const float MIN_AIM_DISTANCE = 0.5f;

    /// <summary>Seconds a client waits after one ping before it sends the next ("about one per 2 s per player").</summary>
    public const float CLIENT_INTERVAL_SECONDS = 2f;

    /// <summary>
    /// Seconds the server wants between two pings of one player: a little under what the client keeps, so that a ping that the client let through because of its clock and its frame time is never turned down
    /// (the pinger would then see a marker that nobody else sees).
    /// </summary>
    public const float SERVER_INTERVAL_SECONDS = 1.8f;

    /// <summary>What a ping says when the player looked at nothing in particular: a rock, the sea floor, a wall of a base.</summary>
    public const string LOOK_HERE = "Look here";

    /// <summary>The lifetime of a ping: the server's number, but never outside the limits (a value that is no number is the default).</summary>
    public static float Lifetime(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
        {
            return DEFAULT_SECONDS;
        }
        return Math.Min(MAX_SECONDS, Math.Max(MIN_SECONDS, seconds));
    }

    /// <summary>Whether a position can be a place in the world (a number, not far outside it).</summary>
    public static bool IsValidPosition(float x, float y, float z) => CyclopsNavRules.IsValidPosition(x, y, z);

    /// <summary>
    /// What a ping says it is about: the name of the creature, else the name of the resource or item, else <see cref="LOOK_HERE" />. A name is cleaned (no control characters, no markup tricks, cut to
    /// a length the HUD can draw); a name that is empty after that counts as none.
    /// </summary>
    public static string TargetLabel(string? creatureName, string? thingName)
    {
        string creature = Clean(creatureName);
        if (creature.Length > 0)
        {
            return creature;
        }
        string thing = Clean(thingName);
        return thing.Length > 0 ? thing : LOOK_HERE;
    }

    /// <summary>A text for the HUD: the label rules of the navigator marks, and no angle brackets (the HUD text reads rich text tags: a name must not be able to resize or recolour the line).</summary>
    private static string Clean(string? text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : CyclopsNavRules.SanitizeLabel(text!.Replace("<", string.Empty).Replace(">", string.Empty));
    }

    /// <summary>The text of the ping on the HUD: the player and what they looked at ("Bob: Reaper Leviathan"). The game adds the distance itself.</summary>
    public static string MarkerText(string? senderName, string? target)
    {
        string owner = Clean(senderName);
        string text = TargetLabel(target, null);
        return owner.Length == 0 ? text : $"{owner}: {text}";
    }

    /// <summary>
    /// Whether the nearest hit of the ray counts as something that was looked at: further than <see cref="MIN_AIM_DISTANCE" /> and within <see cref="AIM_RANGE" />. A distance that is no number does not.
    /// </summary>
    public static bool IsAimDistance(float distance) => distance >= MIN_AIM_DISTANCE && distance <= AIM_RANGE;

    /// <summary>Whether a client may send its next ping now (<paramref name="sinceLast" /> seconds after its last one). The first ping of a session has no last one.</summary>
    public static bool ClientMayPing(float sinceLast) => float.IsNaN(sinceLast) || sinceLast < 0f || sinceLast >= CLIENT_INTERVAL_SECONDS;
}

/// <summary>
/// "At most one in <c>interval</c> seconds per sender": the server's rate limit of the quick ping. Holds the time of each sender's last accepted ping; the clock is given by the caller (seconds from any
/// steady source), so a test can drive it. A clock that went backwards accepts and starts again, so that a jump of the clock can never shut a sender out.
/// </summary>
public sealed class PerSenderRateLimiter<TKey> where TKey : notnull
{
    private const int PRUNE_ABOVE = 64;

    private readonly double interval;
    private readonly object gate = new();
    private readonly Dictionary<TKey, double> lastAccepted = [];

    public PerSenderRateLimiter(double intervalSeconds)
    {
        interval = intervalSeconds;
    }

    /// <summary>True (and the sender's time is now) when the sender is allowed to send; false when the last accepted one was less than the interval ago.</summary>
    public bool TryAccept(TKey sender, double nowSeconds)
    {
        lock (gate)
        {
            if (lastAccepted.TryGetValue(sender, out double previous) && nowSeconds >= previous && nowSeconds - previous < interval)
            {
                return false;
            }
            lastAccepted[sender] = nowSeconds;
            if (lastAccepted.Count > PRUNE_ABOVE)
            {
                // Players come and go: forget those whose last ping is old enough not to matter
                foreach (TKey old in lastAccepted.Where(pair => nowSeconds - pair.Value >= interval).Select(pair => pair.Key).ToList())
                {
                    lastAccepted.Remove(old);
                }
            }
            return true;
        }
    }

    /// <summary>The sender left: nothing is kept of them.</summary>
    public void Forget(TKey sender)
    {
        lock (gate)
        {
            lastAccepted.Remove(sender);
        }
    }

    /// <summary>The number of senders that are kept (for the tests).</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return lastAccepted.Count;
            }
        }
    }
}
