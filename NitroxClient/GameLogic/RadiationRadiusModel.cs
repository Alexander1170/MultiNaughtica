using System;
using System.Collections.Generic;
using System.Linq;

namespace NitroxClient.GameLogic;

/// <summary>
/// The radius of the Aurora's radiation field for a player who joins after the explosion (the radius itself is never sent: every client runs the vanilla
/// <c>LeakingRadiation.Update</c>, which grows it while any leak leaks and shrinks it once all are fixed). Plain data, no game types.
/// </summary>
public static class RadiationRadiusModel
{
    // Math.Clamp does not exist on net472 (the game's runtime)
    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

    public readonly record struct Result(bool ApplyRadius, float Radius, bool AllFixed);

    /// <summary>
    /// Whether the radius is computed AGAIN when the clock of a first player is released. The first player joins with a frozen clock (<c>TimeManager.freezeTime</c>): the radius is applied once, when the
    /// 11th leak is bound, from the frozen reading, and from then on the vanilla update grows it with the frame time while the clock that the server will confirm stands still. Until the first time
    /// packet after the join the server's own clock has not necessarily run for the whole of that stretch, so the first player's radius could end up ahead of the truth for good (0.66 m in the first live
    /// run, 6.6 s of growth at kGrow 0.1; the second player's clock runs from the start and was exact). Nothing to put right before the explosion, and nothing until all 11 leaks are bound.
    /// </summary>
    public static bool ShouldRecalculateWhenClockRuns(bool exploded, int leaksBound, int totalLeaks)
    {
        return exploded && leaksBound == totalLeaks;
    }

    /// <param name="realElapsed">The real play time of the world now (server active time).</param>
    /// <param name="realExplosion">The real time at which the Aurora exploded; before it the radius does not grow.</param>
    /// <param name="fixTimes">One entry per leak that the server told about: the real time at which it was repaired to full, -1 for a leak that still leaks.</param>
    /// <param name="totalLeaks">The leaks of the Aurora (11).</param>
    /// <param name="currentRadius">The radius that the field has before this is applied (used when nothing grew it).</param>
    public static Result Compute(float realElapsed, float realExplosion, IReadOnlyList<float> fixTimes, int totalLeaks, float kStartRadius, float kGrowRate, float kNaturalDissipation,
                                 float kMaxRadius, float currentRadius)
    {
        // The field only counts as repaired when every leak is: vanilla grows the radius while ANY leak leaks (LeakingRadiation.Update) and sets radiationFixed only when none does
        // (NotifyLeaksFixed). The first version took the latest fix time of ANY leak for "all fixed": a joiner who came when 6 of the 11 leaks were welded got a field that was
        // too small and a radiationFixed that skipped the leaksFixedGoal when he welded the last one.
        bool allFixed = fixTimes.Count == totalLeaks && fixTimes.All(time => time >= 0f);

        if (!allFixed)
        {
            float elapsedAfterExplosion = realElapsed - realExplosion;
            if (elapsedAfterExplosion <= 0f)
            {
                return new Result(false, currentRadius, false);
            }

            return new Result(true, Clamp(kStartRadius + elapsedAfterExplosion * kGrowRate, 0f, kMaxRadius), false);
        }

        float lastFix = fixTimes.Max();
        float radius = currentRadius;

        // It grew from the explosion until the last leak was fixed (only if that was after the explosion: before it the radius does not grow)
        float grownFor = lastFix - realExplosion;
        if (grownFor > 0f)
        {
            radius = Clamp(kStartRadius + grownFor * kGrowRate, 0f, kMaxRadius);
        }

        // and it has shrunk since (kNaturalDissipation is negative); a repair before the explosion counts from the explosion
        float shrunkFor = realElapsed - Math.Max(lastFix, realExplosion);
        if (shrunkFor > 0f)
        {
            radius = Clamp(radius + shrunkFor * kNaturalDissipation, 0f, kMaxRadius);
        }

        return new Result(true, radius, true);
    }
}
