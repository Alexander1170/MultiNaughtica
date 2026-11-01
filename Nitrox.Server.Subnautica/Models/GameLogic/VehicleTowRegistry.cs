using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The tows that are on (feature group 2, the tow hooks): which Seamoth or Cyclops tows which vehicle, and which player tows. Kept in memory only; a tow is never saved. The registry decides
///     nothing about physics; it keeps the books, so that a client that joins hears about the tows that are on, and that a tow whose towing player left, whose vehicle was destroyed or whose
///     vehicle was taken over by another player is ended for everybody.
/// </summary>
/// <remarks>
///     A vehicle tows one vehicle and is towed by one tower, and a vehicle that is towed does not tow (no chains and no rings). An entry names the tower, the towed vehicle and the towing player's session by value (never the <see cref="Player" />, who may have
///     reconnected under another session). Every method returns what it ended, and the callers (the processors, <see cref="OnEventAsync" />) send the "detached" packets.
/// </remarks>
internal sealed class VehicleTowRegistry(IPacketSender packetSender) : ISessionCleaner
{
    internal readonly record struct Entry(NitroxId TowerId, NitroxId TowedId, SessionId Session);

    private readonly IPacketSender packetSender = packetSender;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, Entry> byTowed = [];

    public int Count
    {
        get
        {
            lock (gate)
            {
                return byTowed.Count;
            }
        }
    }

    /// <summary>The tows that are on, for a client that joined.</summary>
    public IReadOnlyList<Entry> Snapshot()
    {
        lock (gate)
        {
            return byTowed.Values.ToList();
        }
    }

    /// <summary>
    ///     Records a tow. False (nothing recorded) when the towed vehicle is already towed by a different tower, the tower already tows a different vehicle, the tower is itself towed or the towed
    ///     vehicle tows (chains and rings); the same tow again is accepted (it only refreshes the session).
    /// </summary>
    public bool TryAttach(NitroxId towerId, NitroxId towedId, SessionId session)
    {
        lock (gate)
        {
            if (byTowed.TryGetValue(towedId, out Entry existing) && !existing.TowerId.Equals(towerId))
            {
                return false;
            }
            if (byTowed.Values.Any(entry => entry.TowerId.Equals(towerId) && !entry.TowedId.Equals(towedId)))
            {
                return false;
            }
            if (byTowed.ContainsKey(towerId) || byTowed.Values.Any(entry => entry.TowerId.Equals(towedId)))
            {
                return false;
            }
            byTowed[towedId] = new Entry(towerId, towedId, session);
            return true;
        }
    }

    /// <summary>Ends a tow the towing player let go of. Only the tow of that tower, towed vehicle and session is ended: a late "let go" of an earlier tow must not end a newer one.</summary>
    public bool TryDetach(NitroxId towerId, NitroxId towedId, SessionId session)
    {
        lock (gate)
        {
            if (byTowed.TryGetValue(towedId, out Entry existing) && existing.TowerId.Equals(towerId) && existing.Session == session)
            {
                byTowed.Remove(towedId);
                return true;
            }
            return false;
        }
    }

    /// <summary>The towing player's session ended: its tows are over.</summary>
    public IReadOnlyList<Entry> SessionEnded(SessionId session) => Remove(entry => entry.Session == session);

    /// <summary>A vehicle was destroyed: the tow it was part of (as tower or as towed vehicle) is over.</summary>
    public IReadOnlyList<Entry> EntityDestroyed(NitroxId id) => Remove(entry => entry.TowerId.Equals(id) || entry.TowedId.Equals(id));

    /// <summary>
    ///     A player got the simulation lock of a vehicle. If it is the towed vehicle or the tower, and the player is not the one who tows, the tow is over: somebody got into the towed vehicle, or
    ///     took the tower over.
    /// </summary>
    public IReadOnlyList<Entry> LockTaken(NitroxId id, SessionId newHolder) => Remove(entry => (entry.TowerId.Equals(id) || entry.TowedId.Equals(id)) && entry.Session != newHolder);

    private IReadOnlyList<Entry> Remove(Func<Entry, bool> predicate)
    {
        lock (gate)
        {
            List<Entry> ended = byTowed.Values.Where(predicate).ToList();
            foreach (Entry entry in ended)
            {
                byTowed.Remove(entry.TowedId);
            }
            return ended;
        }
    }

    /// <summary>Tells everybody that these tows are over (the server's own "detached", also to the player who towed, unless that player is the one who left: <paramref name="except" />).</summary>
    public async Task BroadcastEndedAsync(IReadOnlyList<Entry> ended, SessionId? except = null)
    {
        foreach (Entry entry in ended)
        {
            VehicleTowChanged packet = new(entry.TowerId, entry.TowedId, false, entry.Session);
            if (except.HasValue)
            {
                await packetSender.SendPacketToOthersAsync(packet, except.Value);
            }
            else
            {
                await packetSender.SendPacketToAllAsync(packet);
            }
        }
    }

    /// <summary>Tells a client that joined about the tows that are on.</summary>
    public async Task SendSnapshotAsync(SessionId session)
    {
        foreach (Entry entry in Snapshot())
        {
            await packetSender.SendPacketAsync(new VehicleTowChanged(entry.TowerId, entry.TowedId, true, entry.Session), session);
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        await BroadcastEndedAsync(SessionEnded(args.Session.Id), args.Session.Id);
    }
}
