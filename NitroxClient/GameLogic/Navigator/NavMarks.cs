using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Extensions;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>One mark on the map of a Cyclops' navigator console, as this client knows it.</summary>
public sealed class NavMark
{
    public NavMark(NitroxId id, NitroxId cyclopsId, Vector3 position, byte kind, string label, SessionId owner, string ownerName)
    {
        Id = id;
        CyclopsId = cyclopsId;
        Position = position;
        Kind = kind;
        Label = label;
        Owner = owner;
        OwnerName = ownerName;
        Key = $"mark:{id}";
    }

    public NitroxId Id { get; }
    public NitroxId CyclopsId { get; }
    public Vector3 Position { get; }
    public byte Kind { get; }
    public string Label { get; }
    public SessionId Owner { get; }
    public string OwnerName { get; }

    /// <summary>The object that carries the ping on the HUD (made when the Cyclops is here and the mark is shown); null until then.</summary>
    public GameObject PingObject { get; set; }

    public PingInstance Ping { get; set; }

    /// <summary>The key of the mark in the dictionaries of the layers that draw it (made once: they ask for it every frame).</summary>
    public string Key { get; }
}

/// <summary>
/// The marks that the navigators of the Cyclopses have placed (docs/coop/VEHICLE_UPGRADES.md, station 1): which marks each Cyclops has as this client knows them, what the player does with them (place, remove,
/// clear: each is applied here at once and told to the server, which keeps the books and tells the others) and the ping that each mark has on the HUD of a player who is aboard. The state is by the Cyclops' id,
/// so a mark that arrives before its Cyclops is in the game is not lost: <see cref="Sync" /> (run by the Cyclops' <c>NavigatorConsole</c>) makes the pings when the Cyclops is there. The marks are not saved with the
/// world.
/// </summary>
public static class NavMarks
{
    private static readonly Dictionary<NitroxId, List<NavMark>> byCyclops = [];
    private static readonly Dictionary<NitroxId, int> nextNumber = [];
    private static bool subscribed;

    /// <summary>Nothing of this session may be left over for the next one.</summary>
    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    public static void Reset()
    {
        foreach (List<NavMark> marks in byCyclops.Values)
        {
            foreach (NavMark mark in marks)
            {
                DestroyPing(mark);
            }
        }
        byCyclops.Clear();
        nextNumber.Clear();
    }

    public static int Count(NitroxId cyclopsId) => byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks) ? marks.Count : 0;

    /// <summary>How many marks of the class of <paramref name="kind" /> (the navigator's or the Scout Drone's) a Cyclops has: each class has its own limit.</summary>
    public static int CountOfClass(NitroxId cyclopsId, byte kind)
    {
        if (!byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks))
        {
            return 0;
        }
        bool drone = CyclopsNavRules.IsDroneKind(kind);
        int count = 0;
        foreach (NavMark mark in marks)
        {
            if (CyclopsNavRules.IsDroneKind(mark.Kind) == drone)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>The oldest mark of the class of <paramref name="kind" /> (null when there is none): the one that gives way when the Scout Drone's operator marks past the limit.</summary>
    public static NavMark OldestOfClass(NitroxId cyclopsId, byte kind)
    {
        if (byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks))
        {
            bool drone = CyclopsNavRules.IsDroneKind(kind);
            foreach (NavMark mark in marks)
            {
                if (CyclopsNavRules.IsDroneKind(mark.Kind) == drone)
                {
                    return mark;
                }
            }
        }
        return null;
    }

    public static int TotalCount
    {
        get
        {
            int total = 0;
            foreach (List<NavMark> marks in byCyclops.Values)
            {
                total += marks.Count;
            }
            return total;
        }
    }

    /// <summary>The marks of a Cyclops (the list is the state itself: do not change it).</summary>
    public static IReadOnlyList<NavMark> MarksOf(NitroxId cyclopsId)
    {
        return byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks) ? marks : System.Array.Empty<NavMark>();
    }

    public static bool TryGet(NitroxId cyclopsId, NitroxId markId, out NavMark found)
    {
        found = null;
        if (byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks))
        {
            foreach (NavMark mark in marks)
            {
                if (mark.Id.Equals(markId))
                {
                    found = mark;
                    return true;
                }
            }
        }
        return false;
    }

    // ---- what the server says

    /// <summary>The server says that a mark was placed or removed, or that the marks were cleared (or answers a refusal with a "removed").</summary>
    public static void OnPacket(CyclopsNavChanged packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }
        switch (packet.Change)
        {
            case CyclopsNavChange.MarkAdded:
                if (packet.MarkId != null)
                {
                    Add(new NavMark(packet.MarkId, packet.CyclopsId, packet.Position.ToUnity(), packet.MarkKind, packet.Label ?? string.Empty, packet.Owner, packet.OwnerName ?? string.Empty));
                }
                break;
            case CyclopsNavChange.MarkRemoved:
                if (packet.MarkId != null)
                {
                    Remove(packet.CyclopsId, packet.MarkId);
                }
                break;
            case CyclopsNavChange.MarksCleared:
                ClearLocal(packet.CyclopsId);
                break;
        }
    }

    // ---- what the player does

    /// <summary>
    /// The player placed a mark at <paramref name="world" /> on the map of <paramref name="cyclopsId" />: it is there at once and the server is told. Null when the Cyclops already has the most marks of that class
    /// (the navigator's marks and the Scout Drone's have a limit each). A <paramref name="label" /> names the mark (the Scout Drone's operator names what they marked); without one it is "Mark 3".
    /// </summary>
    public static NavMark Place(NitroxId cyclopsId, Vector3 world, byte kind, string label = null)
    {
        byte placedKind = CyclopsNavRules.IsKnownKind(kind) ? kind : CyclopsNavRules.MARK_POINT;
        if (CountOfClass(cyclopsId, placedKind) >= CyclopsNavRules.CapOf(placedKind) || !CyclopsNavRules.IsValidPosition(world.x, world.y, world.z))
        {
            return null;
        }
        string text = CyclopsNavRules.SanitizeLabel(label);
        if (text.Length == 0)
        {
            int number = nextNumber.TryGetValue(cyclopsId, out int known) ? known + 1 : 1;
            nextNumber[cyclopsId] = number;
            text = CyclopsNavRules.DefaultLabel(number);
        }
        SessionId session = NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;
        string name = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName;
        NavMark mark = new(new NitroxId(), cyclopsId, world, placedKind, text, session, name ?? string.Empty);
        Add(mark);
        Send(CyclopsNavChange.MarkAdded, mark);
        Log.Info($"[VehicleUpgrades] navigator: placed '{mark.Label}' (kind {mark.Kind}) at {world.ToString("F1")} on Cyclops {cyclopsId}");
        return mark;
    }

    /// <summary>The player took a mark away: it is gone at once and the server is told.</summary>
    public static void RemoveByPlayer(NitroxId cyclopsId, NitroxId markId)
    {
        if (TryGet(cyclopsId, markId, out NavMark mark))
        {
            Remove(cyclopsId, markId);
            Send(CyclopsNavChange.MarkRemoved, mark);
        }
    }

    /// <summary>The player cleared the marks of a Cyclops.</summary>
    public static void ClearByPlayer(NitroxId cyclopsId)
    {
        if (Count(cyclopsId) == 0)
        {
            return;
        }
        ClearLocal(cyclopsId);
        SessionId session = NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new CyclopsNavChanged(CyclopsNavChange.MarksCleared, cyclopsId, new NitroxId(), NitroxVector3.Zero, 0, string.Empty, session, string.Empty));
    }

    /// <summary>The Cyclops is gone from this client (destroyed): its marks are forgotten, the server drops them as well.</summary>
    public static void ForgetCyclops(NitroxId cyclopsId)
    {
        ClearLocal(cyclopsId);
        nextNumber.Remove(cyclopsId);
    }

    private static void Send(CyclopsNavChange change, NavMark mark)
    {
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new CyclopsNavChanged(change, mark.CyclopsId, mark.Id, mark.Position.ToDto(), mark.Kind, mark.Label, mark.Owner, mark.OwnerName));
    }

    // ---- the state

    private static void Add(NavMark mark)
    {
        if (!byCyclops.TryGetValue(mark.CyclopsId, out List<NavMark> marks))
        {
            marks = [];
            byCyclops[mark.CyclopsId] = marks;
        }
        // The same mark again (the server's snapshot after a join, a repeated packet) changes nothing
        foreach (NavMark existing in marks)
        {
            if (existing.Id.Equals(mark.Id))
            {
                return;
            }
        }
        marks.Add(mark);
    }

    private static void Remove(NitroxId cyclopsId, NitroxId markId)
    {
        if (!byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks))
        {
            return;
        }
        for (int i = 0; i < marks.Count; i++)
        {
            if (marks[i].Id.Equals(markId))
            {
                DestroyPing(marks[i]);
                marks.RemoveAt(i);
                break;
            }
        }
        if (marks.Count == 0)
        {
            byCyclops.Remove(cyclopsId);
        }
    }

    private static void ClearLocal(NitroxId cyclopsId)
    {
        if (byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks))
        {
            foreach (NavMark mark in marks)
            {
                DestroyPing(mark);
            }
            byCyclops.Remove(cyclopsId);
        }
    }

    // ---- the pings on the HUD

    /// <summary>
    /// Makes the HUD pings of the marks of a Cyclops (once per mark, when the Cyclops is in the game) and shows them to the player who is aboard that Cyclops and only to them: the pilot and everybody who
    /// walks in the hull sees an icon and the distance; somebody outside sees nothing of it. Run a few times a second by the Cyclops' <c>NavigatorConsole</c>.
    /// </summary>
    public static void Sync(NitroxId cyclopsId, bool show)
    {
        SyncClass(cyclopsId, show, drone: false);
    }

    /// <summary>
    /// The same for the marks of the Scout Drone (a resource, a threat), which are not for the players aboard alone: the operator marks them for the whole crew, so every player sees them on their HUD wherever they
    /// are. Run a few times a second by the Cyclops' <c>DroneBay</c>.
    /// </summary>
    public static void SyncDroneMarks(NitroxId cyclopsId, bool show)
    {
        SyncClass(cyclopsId, show, drone: true);
    }

    private static void SyncClass(NitroxId cyclopsId, bool show, bool drone)
    {
        if (!byCyclops.TryGetValue(cyclopsId, out List<NavMark> marks))
        {
            return;
        }
        foreach (NavMark mark in marks)
        {
            if (CyclopsNavRules.IsDroneKind(mark.Kind) != drone)
            {
                continue;
            }
            if (!mark.PingObject)
            {
                CreatePing(mark, show);
            }
            else if (mark.Ping && mark.Ping.visible != show)
            {
                mark.Ping.SetVisible(show);
            }
        }
    }

    private static void CreatePing(NavMark mark, bool show)
    {
        try
        {
            GameObject pingObject = new($"Nitrox_NavMark_{mark.Id}");
            pingObject.transform.position = mark.Position;
            PingInstance ping = pingObject.AddComponent<PingInstance>();
            ping.pingType = PingType.Signal;
            ping.origin = pingObject.transform;
            ping.minDist = 5f;
            ping.range = 5f;
            ping._label = CyclopsNavRules.PingText(mark.OwnerName, mark.Label);
            ping.displayPingInManager = false;
            ping.visible = show;
            ping.colorIndex = CyclopsNavRules.HudColorIndex(mark.Kind);
            ping._id = $"nitrox_navmark_{mark.Id}";
            ping.Initialize();
            mark.PingObject = pingObject;
            mark.Ping = ping;
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, $"[VehicleUpgrades] navigator: could not make the HUD ping of the mark '{mark.Label}'");
        }
    }

    private static void DestroyPing(NavMark mark)
    {
        if (mark.PingObject)
        {
            // OnDisable takes the ping out of the game's list and off the HUD
            UnityEngine.Object.Destroy(mark.PingObject);
        }
        mark.PingObject = null;
        mark.Ping = null;
    }
}
