using System;
using System.Collections.Generic;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The numbers and the arithmetic of the buddy oxygen (docs/coop/BUDDY_OXYGEN.md), free of game types so that the client, the server and the tests use the same ones. A diver with air gives some of it to a
/// teammate who is running out: the giver holds a key while looking at the teammate, and the air flows at <c>BuddyOxygenRate</c> per second for as long as the two stay within <c>BuddyOxygenRange</c> and the
/// giver keeps <c>BuddyOxygenFloor</c> seconds of air for themselves. Each player's oxygen belongs to their own game, so the air is taken out of the giver first and added to the receiver second, in small steps
/// (<see cref="TICK_SECONDS" />), through the server (<c>BuddyOxygenTransfer</c>); what the receiver cannot take goes back to the giver.
/// </summary>
public static class BuddyOxygenRules
{
    // The server options. Air is counted in seconds of breathing, as the game counts it (a diver without a tank holds 45).

    /// <summary>Seconds of air per second that flow from the giver to the receiver. 0 switches the sharing off.</summary>
    public const float DEFAULT_RATE = 5f;

    public const float MIN_RATE = 0f;
    public const float MAX_RATE = 20f;

    /// <summary>Metres between the two divers within which air can flow.</summary>
    public const float DEFAULT_RANGE = 2.5f;

    public const float MIN_RANGE = 1f;
    public const float MAX_RANGE = 6f;

    /// <summary>Seconds of air that the giver keeps for themselves: sharing stops when they are down to it.</summary>
    public const float DEFAULT_FLOOR = 15f;

    public const float MIN_FLOOR = 0f;
    public const float MAX_FLOOR = 60f;

    // The protocol

    /// <summary>Seconds between two steps of a giver. A step is about a quarter of a second of the rate.</summary>
    public const float TICK_SECONDS = 0.25f;

    /// <summary>
    /// Longest span that one step may cover: a frame hitch must not become one big step. A step carries the rate times at most this many seconds.
    /// </summary>
    public const float MAX_STEP_SECONDS = 0.6f;

    /// <summary>Smaller steps are not worth a packet.</summary>
    public const float MIN_AMOUNT = 0.05f;

    /// <summary>The most air that one packet may carry, whatever the option says (the highest rate for the longest step).</summary>
    public const float MAX_AMOUNT = MAX_RATE * MAX_STEP_SECONDS;

    /// <summary>The server lets a giver take this many times the rate per second, over a second: the clients keep the rate and the network jitters.</summary>
    public const float SERVER_RATE_FACTOR = 1.5f;

    /// <summary>Metres that the server adds to the range: the positions it knows are a moment old and a diver moves a few metres per second.</summary>
    public const float RANGE_SLACK = 2f;

    /// <summary>Steps of one giver that wait for their receiver's answer. A client that is alive answers within a fraction of a second; one that is not holds up this many steps and no more.</summary>
    public const int MAX_OPEN_PER_GIVER = 8;

    /// <summary>Seconds after which the server forgets a step that nobody answered and gives it back to the giver.</summary>
    public const float STALE_SECONDS = 30f;

    /// <summary>Degrees around the middle of the view within which a teammate counts as looked at.</summary>
    public const float CONE_DEGREES = 35f;

    /// <summary>The giver stops when this many steps in a row came back with nothing taken (the receiver is full, has surfaced, has died, sits in a vehicle).</summary>
    public const int GIVE_UP_AFTER_EMPTY_STEPS = 2;

    /// <summary>Seconds that the HUD text of the receiver ("Alice is giving you air") holds after the last delivery.</summary>
    public const float RECEIVING_DISPLAY_SECONDS = 2f;

    /// <summary>The value of a server option, kept inside its limits (a value that is no number is the default).</summary>
    public static float ClampRate(float rate) => ClampOption(rate, MIN_RATE, MAX_RATE, DEFAULT_RATE);

    public static float ClampRange(float range) => ClampOption(range, MIN_RANGE, MAX_RANGE, DEFAULT_RANGE);

    public static float ClampFloor(float floor) => ClampOption(floor, MIN_FLOOR, MAX_FLOOR, DEFAULT_FLOOR);

    private static float ClampOption(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }
        return Math.Min(max, Math.Max(min, value));
    }

    /// <summary>Whether the sharing is on at all (a rate of 0 switches it off).</summary>
    public static bool IsEnabled(float rate) => rate > 0f;

    /// <summary>
    /// What a giver takes out of their tank in one step: the rate for the time since their last step (at most <see cref="MAX_STEP_SECONDS" />), but never so much that they fall below their floor. 0 when that is
    /// less than <see cref="MIN_AMOUNT" /> (the giver is at their floor, or the rate is 0).
    /// </summary>
    public static float StepAmount(float ratePerSecond, float elapsedSeconds, float available, float floor)
    {
        if (!(ratePerSecond > 0f) || !(elapsedSeconds > 0f))
        {
            return 0f;
        }
        float spare = available - Math.Max(0f, floor);
        if (!(spare > 0f))
        {
            return 0f;
        }
        float amount = Math.Min(ratePerSecond * Math.Min(elapsedSeconds, MAX_STEP_SECONDS), spare);
        return amount >= MIN_AMOUNT ? amount : 0f;
    }

    /// <summary>A step the server may take seriously: a positive number up to <see cref="MAX_AMOUNT" />. NaN and infinity fail (every comparison with NaN is false).</summary>
    public static bool IsValidAmount(float amount) => amount > 0f && amount <= MAX_AMOUNT;

    /// <summary>What a receiver may say it accepted: never negative, never NaN, never more than was delivered (a receiver cannot make air by accepting more than it was given).</summary>
    public static float SanitizeAccepted(float accepted, float delivered)
    {
        if (!(accepted > 0f) || !(delivered > 0f))
        {
            return 0f;
        }
        return Math.Min(accepted, delivered);
    }

    /// <summary>What goes back to the giver of a step of <paramref name="given" /> of which the receiver accepted <paramref name="accepted" />: the rest. What the giver lost is what the receiver got.</summary>
    public static float Refund(float given, float accepted)
    {
        float rest = given - SanitizeAccepted(accepted, given);
        return rest > 0f ? rest : 0f;
    }

    /// <summary>Whether two divers are close enough: <paramref name="distance" /> within the range and the slack. A distance that is no number is not close.</summary>
    public static bool InRange(float distance, float range, float slack) => distance >= 0f && distance <= range + slack;

    /// <summary>Whether a teammate counts as looked at: within the range and within <see cref="CONE_DEGREES" /> of the middle of the view.</summary>
    public static bool IsLookedAt(float distance, float angleDegrees, float range) => InRange(distance, range, 0f) && angleDegrees >= 0f && angleDegrees <= CONE_DEGREES;

    /// <summary>
    /// The teammate the giver means when several are in view: the one closest to the middle of the view among those who are looked at, the nearer one when two are equally centred. -1 when nobody is.
    /// </summary>
    public static int PickBuddy(IReadOnlyList<(float Distance, float AngleDegrees)> candidates, float range)
    {
        int best = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            (float distance, float angle) = candidates[i];
            if (!IsLookedAt(distance, angle, range))
            {
                continue;
            }
            if (best < 0 || angle < candidates[best].AngleDegrees || (angle == candidates[best].AngleDegrees && distance < candidates[best].Distance))
            {
                best = i;
            }
        }
        return best;
    }
}

/// <summary>
/// The server's limit on how fast a giver may take air (a token bucket per giver): <see cref="BuddyOxygenRules.SERVER_RATE_FACTOR" /> times the rate per second, with a bucket of one second of that.
/// A client that keeps the rate never meets it; a modified one that asks for more is turned down (and gets its air back). The clock is given by the caller, in seconds from any steady source; a clock that went
/// backwards does not count as time passed.
/// </summary>
public sealed class GiveBudget<TKey> where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, (double Tokens, double At)> buckets = [];

    /// <summary>True (and the air is taken out of the giver's bucket) when the giver may take <paramref name="amount" /> now.</summary>
    public bool TryTake(TKey giver, float amount, double nowSeconds, float ratePerSecond)
    {
        double perSecond = ratePerSecond * BuddyOxygenRules.SERVER_RATE_FACTOR;
        double capacity = perSecond;
        lock (gate)
        {
            double tokens = capacity;
            if (buckets.TryGetValue(giver, out (double Tokens, double At) bucket))
            {
                double elapsed = nowSeconds >= bucket.At ? nowSeconds - bucket.At : 0.0;
                tokens = Math.Min(capacity, bucket.Tokens + elapsed * perSecond);
            }
            if (amount > tokens + 1e-4)
            {
                buckets[giver] = (tokens, nowSeconds);
                return false;
            }
            buckets[giver] = (tokens - amount, nowSeconds);
            return true;
        }
    }

    /// <summary>The giver left: nothing is kept of them.</summary>
    public void Forget(TKey giver)
    {
        lock (gate)
        {
            buckets.Remove(giver);
        }
    }

    /// <summary>The number of givers that are kept (for the tests).</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return buckets.Count;
            }
        }
    }
}
