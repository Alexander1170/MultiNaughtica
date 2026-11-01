using System;
using System.Collections.Generic;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>One entry of the emote wheel: the clip (its name is the id that travels between the players), what the wheel shows for it, how long it is and whether it repeats until it is stopped.</summary>
public sealed class EmoteEntry
{
    public EmoteEntry(string id, string label, float duration, bool loop)
    {
        Id = id;
        Label = label;
        Duration = duration;
        Loop = loop;
    }

    public string Id { get; }

    public string Label { get; }

    public float Duration { get; }

    public bool Loop { get; }
}

/// <summary>
/// The game-free rules of the emotes of the custom player models (docs/coop/PLAYER_MODELS.md): which clips are on the wheel, which sector of the wheel a pointer is in, how long a clip is played, when the
/// player is not allowed to start one and what stops one. The numbers are the ones the code uses; the page of the docs quotes them and a test keeps the two together.
/// </summary>
public static class PlayerEmoteRules
{
    /// <summary>The server takes one start of an emote per this many seconds of a player; a client sends one at most every <see cref="CLIENT_START_INTERVAL_SECONDS" />, so an honest client is never turned down.</summary>
    public const double SERVER_START_INTERVAL_SECONDS = 1.0;

    public const double CLIENT_START_INTERVAL_SECONDS = 1.2;

    /// <summary>A stop is cheap but is passed on to everybody, so it has a (short) limit too.</summary>
    public const double SERVER_STOP_INTERVAL_SECONDS = 0.2;

    /// <summary>The pose of the model changes from the one that follows the body to the first one of the clip over this many seconds.</summary>
    public const float BLEND_IN_SECONDS = 0.15f;

    /// <summary>A clip shorter than this is a loop (the breakdance step is half a second long) and is repeated for <see cref="LOOP_PLAY_SECONDS" />.</summary>
    public const float LOOPING_BELOW_SECONDS = 1.5f;

    public const float LOOP_PLAY_SECONDS = 6f;

    /// <summary>No emote is played longer than this, whatever the clip says.</summary>
    public const float MAX_PLAY_SECONDS = 15f;

    /// <summary>The wheel has room for this many clips (a model with more has the first ones).</summary>
    public const int MAX_WHEEL_ENTRIES = 12;

    /// <summary>The middle of the wheel, as a part of its radius, where the pointer chooses nothing: letting go of the key there cancels.</summary>
    public const float DEAD_ZONE = 0.3f;

    /// <summary>The length of the move direction (0 to 1) above which the player is walking or swimming and the emote stops.</summary>
    public const float MOVE_CANCEL_THRESHOLD = 0.2f;

    /// <summary>The health that a player has to lose in one frame or more for the emote to stop (he was hurt).</summary>
    public const float DAMAGE_CANCEL_THRESHOLD = 0.01f;

    /// <summary>The id that a server or a client takes from a packet: a clip name that is a valid id, anything else is nothing (an empty id).</summary>
    public static string SanitizeId(string? id)
    {
        return PlayerModelRules.SanitizeId(id);
    }

    /// <summary>The entries of the wheel for the clips of a model: the emotes (not the clips for walking and running), in the order of the file, as many as the wheel has room for.</summary>
    public static IReadOnlyList<EmoteEntry> EmotesOf(IEnumerable<GlbClip> clips)
    {
        List<EmoteEntry> entries = new();
        foreach (GlbClip clip in clips)
        {
            if (!PlayerModelRules.IsEmoteClip(clip.Name) || !PlayerModelRules.IsValidId(clip.Name))
            {
                continue;
            }
            entries.Add(new EmoteEntry(clip.Name, PlayerModelRules.EmoteLabel(clip.Name), clip.Duration, IsLooping(clip.Duration)));
            if (entries.Count >= MAX_WHEEL_ENTRIES)
            {
                break;
            }
        }
        return entries;
    }

    public static bool IsLooping(float duration)
    {
        return duration > 0f && duration < LOOPING_BELOW_SECONDS;
    }

    /// <summary>How long an emote of a clip of this length is played: the clip once, a loop for <see cref="LOOP_PLAY_SECONDS" />, never more than <see cref="MAX_PLAY_SECONDS" />.</summary>
    public static float PlaySeconds(float duration)
    {
        return IsLooping(duration) ? LOOP_PLAY_SECONDS : Math.Min(Math.Max(duration, 0f), MAX_PLAY_SECONDS);
    }

    /// <summary>Whether an emote that started <paramref name="elapsed" /> seconds ago is over.</summary>
    public static bool IsOver(float elapsed, float duration)
    {
        return elapsed >= PlaySeconds(duration);
    }

    /// <summary>The time inside the clip for the seconds since the start: a loop starts again at its end, anything else stays on its last frame.</summary>
    public static float ClipTime(float elapsed, float duration)
    {
        if (duration <= 0f || elapsed <= 0f)
        {
            return 0f;
        }
        return IsLooping(duration) ? elapsed % duration : Math.Min(elapsed, duration);
    }

    /// <summary>How far the pose has changed over from the one that follows the body to the one of the clip: 0 at the start, 1 from <see cref="BLEND_IN_SECONDS" /> on.</summary>
    public static float BlendFactor(float elapsed)
    {
        if (elapsed <= 0f)
        {
            return 0f;
        }
        return elapsed >= BLEND_IN_SECONDS ? 1f : elapsed / BLEND_IN_SECONDS;
    }

    /// <summary>
    /// The sector of the wheel that the pointer at (<paramref name="x" />, <paramref name="y" />) is in, with y up and the length in parts of the wheel's radius: 0 is at the top and they go round clockwise. -1 when
    /// the pointer is in the <see cref="DEAD_ZONE" /> in the middle or the wheel has no sectors.
    /// </summary>
    public static int SectorAt(float x, float y, int count)
    {
        if (count <= 0)
        {
            return -1;
        }
        double length = Math.Sqrt((double)x * x + (double)y * y);
        if (length < DEAD_ZONE)
        {
            return -1;
        }
        double degrees = Math.Atan2(x, y) * 180.0 / Math.PI;
        if (degrees < 0.0)
        {
            degrees += 360.0;
        }
        double width = 360.0 / count;
        return (int)Math.Floor((degrees + width / 2.0) / width) % count;
    }

    /// <summary>The middle of the sector <paramref name="index" /> at <paramref name="radius" /> (x to the right, y up): where its label goes, and where a test puts the pointer to choose it.</summary>
    public static void SectorCenter(int index, int count, float radius, out float x, out float y)
    {
        double radians = index * 360.0 / Math.Max(count, 1) * Math.PI / 180.0;
        x = (float)(Math.Sin(radians) * radius);
        y = (float)(Math.Cos(radians) * radius);
    }

    /// <summary>
    /// Why the player may not start an emote now: null when he may. A player who has no custom model with emotes has nothing to start; a vehicle, a seat (a bench, a chair at the helm), being dead and a menu or the
    /// PDA that is open refuse it.
    /// </summary>
    public static string? Refusal(bool hasEmotes, bool inVehicle, bool seated, bool dead, bool menuOpen)
    {
        if (!hasEmotes)
        {
            return "pick a player model with emotes in Options, Nitrox, first";
        }
        if (inVehicle)
        {
            return "no emotes in a vehicle";
        }
        if (seated)
        {
            return "no emotes while seated";
        }
        if (dead)
        {
            return "no emotes when dead";
        }
        if (menuOpen)
        {
            return "close the menu first";
        }
        return null;
    }

    /// <summary>What stops an emote that is playing: the player moves (the length of his move direction) or loses health. Null when nothing does.</summary>
    public static string? CancelReason(float moveLength, float healthLost)
    {
        if (moveLength > MOVE_CANCEL_THRESHOLD)
        {
            return "he moved";
        }
        if (healthLost > DAMAGE_CANCEL_THRESHOLD)
        {
            return "he was hurt";
        }
        return null;
    }
}
