using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>
/// What the engineer console has set on each Cyclops, as this client knows it from the server (<see cref="CyclopsEngineerState" />): the route of the power and the seconds that the overdrive and its cooldown and
/// the vent's cooldown still have to run. The server keeps the clock and sends the seconds that are left, so they are counted here on this client's own clock (<c>Time.time</c>), never compared with another
/// computer's. What the route does to the Cyclops (<see cref="SpeedFactor" /> and the others) is asked by the small patches of the game's engine, shield, sonar and noise code, on every client: the effect only matters
/// where the thing runs (the force of the engine on the client that simulates the Cyclops, the power of the shield on the client that pays it, the noise on the client whose creature listens). A Cyclops without the module,
/// or a server with the vehicle upgrades off, is a vanilla Cyclops whatever is set. Reset when the session ends.
/// </summary>
public static class EngineerState
{
    private const float MODULE_REFRESH_SECONDS = 1f;

    private sealed class Entry
    {
        public EngineerRoute Route;
        public float OverdriveUntil;
        public float OverdriveReadyAt;
        public float VentReadyAt;
    }

    private sealed class ModuleCache
    {
        public bool Has;
        public float NextCheck;
    }

    private static readonly Dictionary<NitroxId, Entry> byCyclops = [];
    private static readonly ConditionalWeakTable<SubRoot, ModuleCache> modules = new();
    private static bool subscribed;

    /// <summary>Something changed on that Cyclops (the console shows it).</summary>
    public static event Action<NitroxId> Changed;

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: what was set in it is not the next session's.</summary>
    public static void Reset()
    {
        byCyclops.Clear();
        EngineerActions.Reset();
        EngineerHeat.Reset();
    }

    // ---- what the server said

    public static void Apply(CyclopsEngineerState packet)
    {
        Subscribe();
        if (packet.CyclopsId == null)
        {
            return;
        }
        if (!byCyclops.TryGetValue(packet.CyclopsId, out Entry entry))
        {
            entry = new Entry();
            byCyclops[packet.CyclopsId] = entry;
        }
        float now = Time.time;
        entry.Route = CyclopsEngineerRules.RouteFrom(packet.Route);
        entry.OverdriveUntil = now + SafeSeconds(packet.OverdriveSeconds);
        entry.OverdriveReadyAt = now + SafeSeconds(packet.OverdriveReadySeconds);
        entry.VentReadyAt = now + SafeSeconds(packet.VentReadySeconds);
        Log.Info($"[VehicleUpgrades] engineer: Cyclops {packet.CyclopsId} route {entry.Route}, overdrive {packet.OverdriveSeconds:F1} s (ready in {packet.OverdriveReadySeconds:F1} s), vent ready in {packet.VentReadySeconds:F1} s");
        Changed?.Invoke(packet.CyclopsId);
    }

    /// <summary>A Cyclops is gone (destroyed): what was set on it goes too.</summary>
    public static void Forget(NitroxId cyclopsId)
    {
        if (cyclopsId != null)
        {
            byCyclops.Remove(cyclopsId);
        }
    }

    private static float SafeSeconds(float seconds)
    {
        return float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0f : Math.Max(0f, Math.Min(seconds, 600f));
    }

    // ---- what is set

    public static EngineerRoute RouteOf(NitroxId cyclopsId)
    {
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out Entry entry) ? entry.Route : EngineerRoute.Balanced;
    }

    public static bool IsOverdriveActive(NitroxId cyclopsId)
    {
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out Entry entry) && CyclopsEngineerRules.IsOverdriveActive(entry.Route, Time.time, entry.OverdriveUntil);
    }

    public static float OverdriveRemaining(NitroxId cyclopsId)
    {
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out Entry entry) ? CyclopsEngineerRules.Remaining(Time.time, entry.OverdriveUntil) : 0f;
    }

    public static float OverdriveReadyRemaining(NitroxId cyclopsId)
    {
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out Entry entry) ? CyclopsEngineerRules.Remaining(Time.time, entry.OverdriveReadyAt) : 0f;
    }

    public static float VentReadyRemaining(NitroxId cyclopsId)
    {
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out Entry entry) ? CyclopsEngineerRules.Remaining(Time.time, entry.VentReadyAt) : 0f;
    }

    // ---- what it does to a Cyclops

    /// <summary>
    /// The route that counts for this Cyclops: what the engineer set while the Cyclops has the module and the vehicle upgrades are on, else Balanced. Asked a lot (every physics step), so the module is looked for once
    /// a second.
    /// </summary>
    public static EngineerRoute EffectiveRoute(SubRoot cyclops, out NitroxId id)
    {
        id = null;
        if (!cyclops || !VehicleUpgradeSettings.Enabled || !VehicleUpgradeSettings.Received || byCyclops.Count == 0 || !cyclops.TryGetNitroxId(out id))
        {
            return EngineerRoute.Balanced;
        }
        if (!byCyclops.TryGetValue(id, out Entry entry) || entry.Route == EngineerRoute.Balanced)
        {
            return EngineerRoute.Balanced;
        }
        ModuleCache cache = modules.GetOrCreateValue(cyclops);
        if (Time.unscaledTime >= cache.NextCheck)
        {
            cache.NextCheck = Time.unscaledTime + MODULE_REFRESH_SECONDS;
            cache.Has = EngineerStation.HasModule(cyclops);
        }
        return cache.Has ? entry.Route : EngineerRoute.Balanced;
    }

#if DEBUG
    /// <summary>Debug (the live check): the acceleration scale that the SubControl patch put on the engine for its latest physics step (the engine's own scale times the tow and engineer factors).</summary>
    public static float DebugAppliedAcceleration { get; set; } = 1f;
#endif

    /// <summary>The factor on the acceleration of the Cyclops' engine (1: nothing).</summary>
    public static float SpeedFactor(SubRoot cyclops, bool flank)
    {
        EngineerRoute route = EffectiveRoute(cyclops, out NitroxId id);
        if (route != EngineerRoute.Engines)
        {
            return 1f;
        }
        return CyclopsEngineerRules.SpeedFactor(route, IsOverdriveActive(id), flank, VehicleUpgradeSettings.EngineerSpeedBonus);
    }

    /// <summary>The factor on the shield's power use.</summary>
    public static float ShieldCostFactor(SubRoot cyclops)
    {
        return CyclopsEngineerRules.ShieldCostFactor(EffectiveRoute(cyclops, out _), VehicleUpgradeSettings.ShieldSaving);
    }

    /// <summary>The factor on the power that silent running uses.</summary>
    public static float SilentRunningCostFactor(SubRoot cyclops)
    {
        return CyclopsEngineerRules.SilentRunningCostFactor(EffectiveRoute(cyclops, out _));
    }

    /// <summary>The factor on the noise that creatures hear.</summary>
    public static float NoiseFactor(SubRoot cyclops)
    {
        return cyclops ? CyclopsEngineerRules.NoiseFactor(EffectiveRoute(cyclops, out _), cyclops.silentRunning, VehicleUpgradeSettings.SilentRunningBonus) : 1f;
    }

    /// <summary>The factor on the range of the sonars of the navigator.</summary>
    public static float SonarFactor(SubRoot cyclops)
    {
        return CyclopsEngineerRules.SonarFactor(EffectiveRoute(cyclops, out _));
    }
}
