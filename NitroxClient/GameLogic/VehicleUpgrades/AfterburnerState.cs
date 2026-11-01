namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The numbers of one Seamoth afterburner, taken from <see cref="VehicleUpgradeSettings" /> when a burn starts (the settings only change when a player joins, but a running burn must not
/// change its length or its cooldown halfway).
/// </summary>
public readonly struct AfterburnerSettings
{
    public AfterburnerSettings(float speed, float seconds, float cooldownSeconds, float energy)
    {
        Speed = speed;
        Seconds = seconds;
        CooldownSeconds = cooldownSeconds;
        Energy = energy;
    }

    /// <summary>How many times faster the Seamoth is while the afterburner burns.</summary>
    public float Speed { get; }

    public float Seconds { get; }
    public float CooldownSeconds { get; }

    /// <summary>Energy units one use takes from the Seamoth's power.</summary>
    public float Energy { get; }

    public static AfterburnerSettings Current => new(VehicleUpgradeSettings.AfterburnerSpeed, VehicleUpgradeSettings.AfterburnerSeconds, VehicleUpgradeSettings.AfterburnerCooldownSeconds, VehicleUpgradeSettings.AfterburnerEnergy);
}

/// <summary>
/// The Seamoth afterburner of one vehicle (docs/coop/VEHICLE_UPGRADES.md): an instant-use quick slot. A press burns for <see cref="AfterburnerSettings.Seconds" /> seconds at
/// <see cref="AfterburnerSettings.Speed" /> times the speed and costs <see cref="AfterburnerSettings.Energy" />; afterwards the module cannot be used for
/// <see cref="AfterburnerSettings.CooldownSeconds" /> seconds, which the quick slot shows as its cooldown. A press does nothing while it burns, during the cooldown or without enough energy.
/// The burn ends early when the pilot leaves, when the Seamoth docks or when its power runs out; the cooldown then starts at that moment.
/// <para>
/// Free of Unity types so that it can be tested: the caller passes the time (the game's <c>Time.time</c>), the energy of the Seamoth and the settings, and spends the energy itself when
/// <see cref="Press" /> returns <see cref="PressResult.Started" />. It runs on the pilot's client only; the other players see the Seamoth move faster because the movement is what the game
/// already syncs.
/// </para>
/// </summary>
public sealed class AfterburnerState
{
    public enum Phase
    {
        Ready,
        Burning,
        Cooldown
    }

    public enum PressResult
    {
        /// <summary>The burn started; the caller takes <see cref="AfterburnerSettings.Energy" /> from the Seamoth.</summary>
        Started,

        /// <summary>The module is not installed, the vehicle upgrades are off, or nobody pilots the Seamoth.</summary>
        NotAllowed,

        AlreadyBurning,
        CoolingDown,
        NotEnoughEnergy
    }

    public enum EndReason
    {
        /// <summary>The burn time was over.</summary>
        Elapsed,

        /// <summary>The pilot left the Seamoth.</summary>
        Exited,

        /// <summary>The Seamoth docked (in a moon pool or a Cyclops).</summary>
        Docked,

        /// <summary>The Seamoth's power ran out.</summary>
        PowerLost,

        /// <summary>The module was removed or the vehicle upgrades were switched off.</summary>
        Disabled
    }

    private bool burning;
    private float burnStart;
    private float burnEnd;
    private float cooldownSeconds;
    private float cooldownEnd = float.NegativeInfinity;
    private float speed = 1f;
    private bool everUsed;

    /// <summary>The reason the last burn ended; only meaningful after <see cref="Phase.Burning" /> was left.</summary>
    public EndReason LastEndReason { get; private set; }

    /// <summary>Times a burn was started, for the harness.</summary>
    public int Uses { get; private set; }

    public bool IsBurning => burning;

    public Phase PhaseAt(float now)
    {
        if (burning && now < burnEnd)
        {
            return Phase.Burning;
        }
        // A burn whose time is over counts as ended even when nobody has ticked yet; its cooldown started when it ended, not when someone noticed
        float end = burning ? burnEnd + cooldownSeconds : cooldownEnd;
        return everUsed && now < end ? Phase.Cooldown : Phase.Ready;
    }

    /// <summary>The factor the Seamoth's speed is multiplied by right now: the afterburner's while it burns, otherwise 1.</summary>
    public float SpeedFactor(float now)
    {
        return PhaseAt(now) == Phase.Burning ? speed : 1f;
    }

    /// <summary>When the quick slot's cooldown started to count (the press), for the game's <c>quickSlotTimeUsed</c>.</summary>
    public float SlotTimeUsed => burnStart;

    /// <summary>How long the quick slot's cooldown lasts counted from <see cref="SlotTimeUsed" /> (the burn and the cooldown together), for the game's <c>quickSlotCooldown</c>.</summary>
    public float SlotCooldown => (burning ? burnEnd + cooldownSeconds : cooldownEnd) - burnStart;

    /// <summary>
    /// A press of the quick slot. <paramref name="allowed" /> is false when the module is not installed, the vehicle upgrades are off or the player does not pilot the Seamoth.
    /// <paramref name="energyAvailable" /> is the Seamoth's current power.
    /// </summary>
    public PressResult Press(float now, bool allowed, float energyAvailable, AfterburnerSettings settings)
    {
        Settle(now);
        if (!allowed)
        {
            return PressResult.NotAllowed;
        }
        if (burning)
        {
            return PressResult.AlreadyBurning;
        }
        if (everUsed && now < cooldownEnd)
        {
            return PressResult.CoolingDown;
        }
        // A Seamoth without any power cannot move at all, so a free afterburner (cost 0) still needs some power to burn
        if (energyAvailable <= 0f || energyAvailable < settings.Energy)
        {
            return PressResult.NotEnoughEnergy;
        }

        burning = true;
        everUsed = true;
        burnStart = now;
        burnEnd = now + settings.Seconds;
        cooldownSeconds = settings.CooldownSeconds;
        cooldownEnd = burnEnd + cooldownSeconds;
        speed = settings.Speed;
        Uses++;
        return PressResult.Started;
    }

    /// <summary>
    /// Called every frame (or fixed update) on the pilot's client. Ends the burn when its time is over or the power ran out. Returns true when the burn is over after this call, so that the
    /// caller can undo its speed change.
    /// </summary>
    public bool Tick(float now, float energyAvailable)
    {
        Settle(now);
        if (burning && energyAvailable <= 0f)
        {
            End(now, EndReason.PowerLost);
        }
        return !burning;
    }

    /// <summary>Ends a running burn for another reason than its time. Does nothing when nothing burns. The cooldown starts now.</summary>
    public void End(float now, EndReason reason)
    {
        Settle(now);
        if (!burning)
        {
            return;
        }
        burning = false;
        LastEndReason = reason;
        cooldownEnd = now + cooldownSeconds;
    }

    /// <summary>Forgets everything (a new session, a new Seamoth).</summary>
    public void Reset()
    {
        burning = false;
        everUsed = false;
        burnStart = 0f;
        burnEnd = 0f;
        cooldownEnd = float.NegativeInfinity;
        cooldownSeconds = 0f;
        speed = 1f;
        Uses = 0;
        LastEndReason = EndReason.Elapsed;
    }

    /// <summary>A burn whose time is over ends at its planned end: the cooldown runs from then on whenever somebody looks.</summary>
    private void Settle(float now)
    {
        if (burning && now >= burnEnd)
        {
            burning = false;
            LastEndReason = EndReason.Elapsed;
            cooldownEnd = burnEnd + cooldownSeconds;
        }
    }
}
