namespace NitroxClient.GameLogic;

/// <summary>
/// When the copy of a vehicle that another player simulates replays a hit (sound, sparks, smoke) for a health drop it is told about, plus the gates which vanilla
/// <c>LiveMixin.TakeDamage</c> puts on those effects (V0, VD2). Plain data, no game types, so that it can be tested; <see cref="LiveMixinManager"/> makes the calls.
/// </summary>
public static class RemoteHitRules
{
    /// <summary>
    /// A smaller drop is noise (a float rounding of the shared value, a repair that was undone).
    /// </summary>
    public const float MIN_HIT_DAMAGE = 0.5f;

    /// <summary>
    /// Vanilla spawns the hit effect of a <c>LiveMixin</c> at most once per second (<c>Time.time &gt; timeLastDamageEffect + 1</c>).
    /// </summary>
    public const float DAMAGE_EFFECT_INTERVAL = 1f;

    /// <summary>
    /// Whether a health value which we were told about is a hit the other player took, for which our copy plays the hit effects.
    /// </summary>
    /// <param name="localHealth">The health of our copy before the update.</param>
    /// <param name="remoteHealth">The health we were told about.</param>
    /// <param name="isFirstSync">The first health this object gets: its spawn state (initial sync, a cell streaming in), not a hit.</param>
    /// <param name="initialSyncCompleted">Nothing is played while the world is still being built.</param>
    /// <param name="holdsLock">We simulate the object (or cannot tell): its hits happen here with the real <c>TakeDamage</c>, and a lock can move while a packet is in flight.</param>
    public static bool ShouldReplayHit(float localHealth, float remoteHealth, bool isFirstSync, bool initialSyncCompleted, bool holdsLock)
    {
        if (isFirstSync || !initialSyncCompleted || holdsLock)
        {
            return false;
        }

        // Negated so that NaN fails too. A death (0 HP) is replicated by the destruction itself, never as a last hit: nothing on the copy may reach Kill.
        if (!(remoteHealth > 0f))
        {
            return false;
        }

        return localHealth - remoteHealth >= MIN_HIT_DAMAGE;
    }

    /// <summary>
    /// Vanilla: <c>damageClip &amp;&amp; damage &gt; 0 &amp;&amp; damage &gt;= minDamageForSound</c> (the damage type is Normal here, so the radiation exception never applies).
    /// </summary>
    /// <param name="soundIsSynced">The event is in the sound whitelist: the player who simulates the object shares it, a second one would double it.</param>
    public static bool ShouldPlayDamageSound(float damage, float minDamageForSound, bool soundIsSynced)
    {
        return !soundIsSynced && damage > 0f && damage >= minDamageForSound;
    }

    /// <summary>
    /// Vanilla: <c>damage &gt; 0 &amp;&amp; Time.time &gt; timeLastDamageEffect + 1</c> (and a <c>damageEffect</c> prefab, which the caller checks).
    /// </summary>
    public static bool ShouldSpawnDamageEffect(float damage, float now, float lastEffectTime)
    {
        return damage > 0f && now > lastEffectTime + DAMAGE_EFFECT_INTERVAL;
    }

    /// <summary>
    /// Vanilla: <c>loopingDamageEffect &amp;&amp; !loopingDamageEffectObj &amp;&amp; GetHealthFraction() &lt; loopEffectBelowPercent</c>.
    /// </summary>
    public static bool ShouldSpawnLoopingEffect(float healthFraction, float loopEffectBelowPercent, bool hasEffectPrefab, bool hasEffectObject)
    {
        return hasEffectPrefab && !hasEffectObject && healthFraction < loopEffectBelowPercent;
    }
}
