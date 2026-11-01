using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Gunner;

/// <summary>What the server decided about a seat that this client asked for; the console that wanted it sets <see cref="Accepted" />.</summary>
public sealed class GunnerSeatGrant
{
    public GunnerSeatGrant(NitroxId cyclopsId)
    {
        CyclopsId = cyclopsId;
    }

    public NitroxId CyclopsId { get; }

    /// <summary>Set by the console that still wants the seat. A grant that nobody accepts is given back at once.</summary>
    public bool Accepted { get; set; }
}

/// <summary>
/// Who sits at the gunner desks of the Cyclopses, as this client knows it from the server (<see cref="CyclopsGunnerSeat" />), and this client's own request for a seat. The table is for display (the name on the
/// hand target, "somebody sits there"): a click always asks the server, so that a stale table can never keep a player from a free desk. A seat that the server grants is kept by the console only if it still wants it
/// at that moment; one that nobody accepts (the player moved on while the server was answering) is given back, so that the desk is not held for nothing. Reset when the session ends.
/// </summary>
public static class GunnerSeats
{
    public readonly record struct Seat(SessionId Session, string Name);

    private static readonly Dictionary<NitroxId, Seat> byCyclops = [];
    private static readonly HashSet<NitroxId> busy = [];
    private static bool subscribed;
    private static NitroxId pendingCyclopsId;
    private static float pendingSince;

    /// <summary>The server granted the seat that this client asked for; set <see cref="GunnerSeatGrant.Accepted" /> to keep it.</summary>
    public static event Action<GunnerSeatGrant> Granted;

    /// <summary>The server refused the seat that this client asked for (<see cref="CyclopsGunnerSeat.Reason" /> says why; for an occupied desk the table has the occupant).</summary>
    public static event Action<CyclopsGunnerSeat> Denied;

    /// <summary>The server ended the seat of this client or of another one.</summary>
    public static event Action<CyclopsGunnerSeat> Released;

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
        GunnerFire.Reset();
    }

    public static SessionId LocalSession => NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;

    public static bool TryGetGunner(NitroxId cyclopsId, out Seat seat)
    {
        seat = default;
        return cyclopsId != null && byCyclops.TryGetValue(cyclopsId, out seat);
    }

    /// <summary>The server says that this client sits at the desk of that Cyclops.</summary>
    public static bool IsMine(NitroxId cyclopsId)
    {
        return TryGetGunner(cyclopsId, out Seat seat) && seat.Session == LocalSession;
    }

    /// <summary>Somebody else sits there (as far as this client knows).</summary>
    public static bool IsTakenByOther(NitroxId cyclopsId)
    {
        return TryGetGunner(cyclopsId, out Seat seat) && seat.Session != LocalSession;
    }

    public static bool HasPending(NitroxId cyclopsId)
    {
        return pendingCyclopsId != null && pendingCyclopsId.Equals(cyclopsId) && Time.time - pendingSince <= GunnerRules.REQUEST_TIMEOUT_SECONDS;
    }

    /// <summary>The player used the desk: ask the server for it. A stale table is not consulted.</summary>
    public static void Request(NitroxId cyclopsId)
    {
        Subscribe();
        pendingCyclopsId = cyclopsId;
        pendingSince = Time.time;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsGunnerSeat.Take(cyclopsId));
    }

    /// <summary>The request was not answered in time or the player does not want the seat any more.</summary>
    public static void CancelPending(NitroxId cyclopsId)
    {
        if (pendingCyclopsId != null && pendingCyclopsId.Equals(cyclopsId))
        {
            pendingCyclopsId = null;
        }
    }

    /// <summary>The player leaves the desk: tell the server (the table follows the server's answer).</summary>
    public static void Leave(NitroxId cyclopsId)
    {
        if (cyclopsId != null)
        {
            NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsGunnerSeat.Leave(cyclopsId));
        }
    }

    public static void OnPacket(CyclopsGunnerSeat packet)
    {
        Subscribe();
        if (packet.CyclopsId == null)
        {
            return;
        }
        switch (packet.Message)
        {
            case CyclopsGunnerSeatMessage.Seated:
                Log.Info($"[VehicleUpgrades] gunner seat: {packet.PlayerName} sits at the desk of Cyclops {packet.CyclopsId}{(packet.SessionId == LocalSession ? " (this client)" : string.Empty)}");
                byCyclops[packet.CyclopsId] = new Seat(packet.SessionId, packet.PlayerName ?? string.Empty);
                if (packet.SessionId == LocalSession)
                {
                    bool wanted = HasPending(packet.CyclopsId);
                    pendingCyclopsId = null;
                    GunnerSeatGrant grant = new(packet.CyclopsId);
                    if (wanted)
                    {
                        Granted?.Invoke(grant);
                    }
                    if (!grant.Accepted && !busy.Contains(packet.CyclopsId))
                    {
                        // The player moved on while the server was answering (or the packet is a second grant for a desk that is not open): give the seat back
                        Leave(packet.CyclopsId);
                    }
                }
                break;
            case CyclopsGunnerSeatMessage.Released:
                Log.Info($"[VehicleUpgrades] gunner seat: {packet.PlayerName} left the desk of Cyclops {packet.CyclopsId}: {packet.Reason}{(packet.SessionId == LocalSession ? " (this client)" : string.Empty)}");
                if (byCyclops.TryGetValue(packet.CyclopsId, out Seat seat) && seat.Session == packet.SessionId)
                {
                    byCyclops.Remove(packet.CyclopsId);
                }
                Released?.Invoke(packet);
                break;
            case CyclopsGunnerSeatMessage.Denied:
                Log.Info($"[VehicleUpgrades] gunner seat: the server refused the seat of Cyclops {packet.CyclopsId}: {packet.Reason}{(packet.Reason == CyclopsGunnerSeatReason.Occupied ? $" ({packet.PlayerName} sits there)" : string.Empty)}");
                if (packet.Reason == CyclopsGunnerSeatReason.Occupied && packet.SessionId.IsPlayer)
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

    /// <summary>The console of that desk is asking for the seat, opening or open (so that a repeated grant for it is not given back); set by the console when its state changes.</summary>
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
    public static string DeniedText(CyclopsGunnerSeat packet)
    {
        return packet.Reason switch
        {
            CyclopsGunnerSeatReason.Occupied => string.IsNullOrEmpty(packet.PlayerName) ? "Somebody is at the gunner station" : $"{packet.PlayerName} is at the gunner station",
            CyclopsGunnerSeatReason.SeatedElsewhere => "You sit at the gunner station of another Cyclops",
            CyclopsGunnerSeatReason.NotAboard => "You have to be aboard the Cyclops",
            _ => "The gunner station is not available"
        };
    }

    /// <summary>What the player reads when the server ended their seat.</summary>
    public static string ReleasedText(CyclopsGunnerSeat packet)
    {
        return packet.Reason switch
        {
            CyclopsGunnerSeatReason.CyclopsDestroyed => "The Cyclops is destroyed",
            CyclopsGunnerSeatReason.LeftCyclops => "You left the Cyclops",
            CyclopsGunnerSeatReason.Died => "You died",
            _ => string.Empty
        };
    }
}
