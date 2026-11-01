using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.GameLogic.Drone;

/// <summary>
/// The traffic of the drone bays between this client and the server (<see cref="CyclopsDroneBay" /> out, <see cref="CyclopsDroneState" /> in). What the server says is kept here until the bay of that Cyclops has
/// carried it out (the bay may not exist yet on a joining client, and the drone's entity may not have spawned): the bay asks for it every poll and reports when it is done. What this client does to its own bay
/// (loads a drone, flies it out, flies it home, picks it up) is reported by the patches of the game's dock; while the bay applies what the server said, <see cref="Applying" /> is true and nothing is reported back.
/// </summary>
public static class DroneBayState
{
    private static readonly Dictionary<NitroxId, CyclopsDroneState> unapplied = [];
    private static readonly Dictionary<NitroxId, CyclopsDroneState> latest = [];
    private static bool subscribed;

    /// <summary>True while a bay carries out what the server said: the dock and undock it does then are the server's, not this player's.</summary>
    public static bool Applying { get; private set; }

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: nothing goes on into the next one.</summary>
    public static void Reset()
    {
        unapplied.Clear();
        latest.Clear();
        Applying = false;
    }

    public static void OnPacket(CyclopsDroneState packet)
    {
        Subscribe();
        if (packet.CyclopsId != null)
        {
            unapplied[packet.CyclopsId] = packet;
            latest[packet.CyclopsId] = packet;
        }
    }

    /// <summary>What the server said last about that Cyclops' bay, carried out or not (false: it has said nothing, which is an empty bay).</summary>
    public static bool TryGetLatest(NitroxId cyclopsId, out CyclopsDroneState state)
    {
        state = null;
        return cyclopsId != null && latest.TryGetValue(cyclopsId, out state);
    }

    /// <summary>What the server said last about that Cyclops' bay and the bay has not carried out yet.</summary>
    public static bool TryGetUnapplied(NitroxId cyclopsId, out CyclopsDroneState state)
    {
        state = null;
        return cyclopsId != null && unapplied.TryGetValue(cyclopsId, out state);
    }

    /// <summary>The bay carried that state out (a newer one that arrived meanwhile stays).</summary>
    public static void MarkApplied(CyclopsDroneState state)
    {
        if (state?.CyclopsId != null && unapplied.TryGetValue(state.CyclopsId, out CyclopsDroneState current) && ReferenceEquals(current, state))
        {
            unapplied.Remove(state.CyclopsId);
        }
    }

    /// <summary>Runs what the bay does for the server's state, so that the patches of the game's dock do not report it back as this player's own.</summary>
    public static void Apply(Action action)
    {
        bool before = Applying;
        Applying = true;
        try
        {
            action();
        }
        finally
        {
            Applying = before;
        }
    }

    public static void Report(CyclopsDroneBayKind kind, NitroxId cyclopsId, NitroxId droneId)
    {
        if (cyclopsId == null || droneId == null)
        {
            return;
        }
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new CyclopsDroneBay(kind, cyclopsId, droneId));
        Log.Info($"[VehicleUpgrades] drone bay: told the server: {kind} {droneId} on Cyclops {cyclopsId}");
    }
}
