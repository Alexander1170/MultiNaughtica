using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Drone;

/// <summary>What the server decided about a seat that this client asked for; the console that wanted it sets <see cref="Accepted" />.</summary>
public sealed class DroneSeatGrant
{
    public DroneSeatGrant(NitroxId cyclopsId)
    {
        CyclopsId = cyclopsId;
    }

    public NitroxId CyclopsId { get; }

    /// <summary>Set by the console that still wants the seat. A grant that nobody accepts is given back at once.</summary>
    public bool Accepted { get; set; }
}

/// <summary>
/// Who sits at the drone consoles of the Cyclopses, as this client knows it from the server (<see cref="CyclopsDroneSeat" />), and this client's own request for a seat. The table is for display (the name on the
/// hand target, "somebody sits there"): a click always asks the server, so that a stale table can never keep a player from a free console. A seat that the server grants is kept by the console only if it still wants it
/// at that moment; one that nobody accepts (the player moved on while the server was answering) is given back, so that the console is not held for nothing. Reset when the session ends.
/// </summary>
public static class DroneSeats
{
    public readonly record struct Seat(SessionId Session, string Name);

    private static readonly Dictionary<NitroxId, Seat> byCyclops = [];
    private static readonly HashSet<NitroxId> busy = [];
    private static bool subscribed;
    private static NitroxId pendingCyclopsId;
    private static float pendingSince;

    /// <summary>The server granted the seat that this client asked for; set <see cref="DroneSeatGrant.Accepted" /> to keep it.</summary>
    public static event Action<DroneSeatGrant> Granted;

    /// <summary>The server refused the seat that this client asked for (<see cref="CyclopsDroneSeat.Reason" /> says why; for an occupied console the table has the occupant).</summary>
    public static event Action<CyclopsDroneSeat> Denied;

    /// <summary>The server ended the seat of this client or of another one.</summary>
    public static event Action<CyclopsDroneSeat> Released;

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: no seat goes on into the next one.</summary>
    public static void Reset()
    {
        byCyclops.Clear();
        busy.Clear();
        pendingCyclopsId = null;
    }

    public static SessionId LocalSession => NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;

    public static bool TryGetOperator(NitroxId cyclopsId, out Seat seat)
    {
        seat = default;
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out seat);
    }

    /// <summary>The server says that this client sits at the console of that Cyclops.</summary>
    public static bool IsMine(NitroxId cyclopsId)
    {
        return TryGetOperator(cyclopsId, out Seat seat) && seat.Session == LocalSession;
    }

    /// <summary>Somebody else sits there (as far as this client knows).</summary>
    public static bool IsTakenByOther(NitroxId cyclopsId)
    {
        return TryGetOperator(cyclopsId, out Seat seat) && seat.Session != LocalSession;
    }

    public static bool HasPending(NitroxId cyclopsId)
    {
        return pendingCyclopsId != null && pendingCyclopsId.Equals(cyclopsId) && Time.time - pendingSince <= ScoutDroneRules.REQUEST_TIMEOUT_SECONDS;
    }

    /// <summary>The player used the console: ask the server for it. A stale table is not consulted.</summary>
    public static void Request(NitroxId cyclopsId)
    {
        Subscribe();
        pendingCyclopsId = cyclopsId;
        pendingSince = Time.time;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsDroneSeat.Take(cyclopsId));
    }

    /// <summary>The request was not answered in time or the player does not want the seat any more.</summary>
    public static void CancelPending(NitroxId cyclopsId)
    {
        if (pendingCyclopsId != null && pendingCyclopsId.Equals(cyclopsId))
        {
            pendingCyclopsId = null;
        }
    }

    /// <summary>The player leaves the console: tell the server (the table follows the server's answer).</summary>
    public static void Leave(NitroxId cyclopsId)
    {
        if (cyclopsId != null)
        {
            NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsDroneSeat.Leave(cyclopsId));
        }
    }

    public static void OnPacket(CyclopsDroneSeat packet)
    {
        Subscribe();
        if (packet.CyclopsId == null)
        {
            return;
        }
        switch (packet.Message)
        {
            case CyclopsDroneSeatMessage.Seated:
                Log.Info($"[VehicleUpgrades] operator seat: {packet.PlayerName} sits at the console of Cyclops {packet.CyclopsId}{(packet.SessionId == LocalSession ? " (this client)" : string.Empty)}");
                byCyclops[packet.CyclopsId] = new Seat(packet.SessionId, packet.PlayerName ?? string.Empty);
                if (packet.SessionId == LocalSession)
                {
                    bool wanted = HasPending(packet.CyclopsId);
                    pendingCyclopsId = null;
                    DroneSeatGrant grant = new(packet.CyclopsId);
                    if (wanted)
                    {
                        Granted?.Invoke(grant);
                    }
                    if (!grant.Accepted && !busy.Contains(packet.CyclopsId))
                    {
                        // The player moved on while the server was answering (or the packet is a second grant for a console that is not open): give the seat back
                        Leave(packet.CyclopsId);
                    }
                }
                break;
            case CyclopsDroneSeatMessage.Released:
                Log.Info($"[VehicleUpgrades] operator seat: {packet.PlayerName} left the console of Cyclops {packet.CyclopsId}: {packet.Reason}{(packet.SessionId == LocalSession ? " (this client)" : string.Empty)}");
                if (byCyclops.TryGetValue(packet.CyclopsId, out Seat seat) && seat.Session == packet.SessionId)
                {
                    byCyclops.Remove(packet.CyclopsId);
                }
                Released?.Invoke(packet);
                break;
            case CyclopsDroneSeatMessage.Denied:
                Log.Info($"[VehicleUpgrades] operator seat: the server refused the seat of Cyclops {packet.CyclopsId}: {packet.Reason}{(packet.Reason == CyclopsDroneSeatReason.Occupied ? $" ({packet.PlayerName} sits there)" : string.Empty)}");
                if (packet.Reason == CyclopsDroneSeatReason.Occupied && packet.SessionId.IsPlayer)
                {
                    byCyclops[packet.CyclopsId] = new Seat(packet.SessionId, packet.PlayerName ?? string.Empty);
                }
                if (pendingCyclopsId != null && pendingCyclopsId.Equals(packet.CyclopsId))
                {
                    pendingCyclopsId = null;
                    Denied?.Invoke(packet);
                }
                break;
        }
    }

    /// <summary>The console of that Cyclops is asking for the seat, opening or open (so that a repeated grant for it is not given back); set by the console when its state changes.</summary>
    public static void SetBusy(NitroxId cyclopsId, bool isBusy)
    {
        if (cyclopsId == null)
        {
            return;
        }
        if (isBusy)
        {
            busy.Add(cyclopsId);
        }
        else
        {
            busy.Remove(cyclopsId);
        }
    }

    /// <summary>What the player reads when a seat is refused.</summary>
    public static string DeniedText(CyclopsDroneSeat packet)
    {
        return packet.Reason switch
        {
            CyclopsDroneSeatReason.Occupied => string.IsNullOrEmpty(packet.PlayerName) ? "Somebody is at the drone console" : $"{packet.PlayerName} is at the drone console",
            CyclopsDroneSeatReason.SeatedElsewhere => "You sit at the drone console of another Cyclops",
            CyclopsDroneSeatReason.NotAboard => "You have to be aboard the Cyclops",
            _ => "The drone console is not available"
        };
    }

    /// <summary>What the player reads when the server ended their seat.</summary>
    public static string ReleasedText(CyclopsDroneSeat packet)
    {
        return packet.Reason switch
        {
            CyclopsDroneSeatReason.CyclopsDestroyed => "The Cyclops is destroyed",
            CyclopsDroneSeatReason.LeftCyclops => "You left the Cyclops",
            CyclopsDroneSeatReason.Died => "You died",
            CyclopsDroneSeatReason.DroneDestroyed => "The Scout Drone is destroyed",
            _ => string.Empty
        };
    }
}
