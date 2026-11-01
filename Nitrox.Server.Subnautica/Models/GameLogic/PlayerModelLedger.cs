using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The custom player model that each connected player wears (<see cref="PlayerModelChoice" />, docs/coop/PLAYER_MODELS.md), as the server was told: the latest choice of every session, in memory only (nothing is
///     saved with the world; a player picks again when he joins, and his client announces it after its initial sync). The vanilla body is not kept: a session without an entry wears none. A player who joins is
///     sent the choices of the others after his initial sync (<see cref="SendSnapshotAsync" />, called by <c>PlayerSyncFinishedProcessor</c>); a session that ends loses its entry.
/// </summary>
internal sealed class PlayerModelLedger(IPacketSender packetSender) : ISessionCleaner
{
    internal readonly record struct Choice(SessionId Session, string ModelId, long FileBytes);

    private readonly Lock gate = new();
    private readonly IPacketSender packetSender = packetSender;
    private readonly Dictionary<SessionId, Choice> choices = [];

    /// <summary>The number of sessions that wear a custom model.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return choices.Count;
            }
        }
    }

    /// <summary>Remembers what this session wears; an empty id (the vanilla body) forgets it.</summary>
    public void Set(SessionId session, string modelId, long fileBytes)
    {
        lock (gate)
        {
            if (modelId.Length == 0)
            {
                choices.Remove(session);
            }
            else
            {
                choices[session] = new Choice(session, modelId, fileBytes);
            }
        }
    }

    public bool TryGet(SessionId session, out Choice choice)
    {
        lock (gate)
        {
            return choices.TryGetValue(session, out choice);
        }
    }

    /// <summary>The choices of everybody but <paramref name="except" />, by session.</summary>
    public List<Choice> SnapshotExcept(SessionId except)
    {
        lock (gate)
        {
            return choices.Values.Where(choice => choice.Session != except).OrderBy(choice => (ushort)choice.Session).ToList();
        }
    }

    /// <summary>Tells a player who has just finished his initial sync what the others wear.</summary>
    public async Task SendSnapshotAsync(SessionId joiner)
    {
        foreach (Choice choice in SnapshotExcept(joiner))
        {
            await packetSender.SendPacketAsync(new PlayerModelChoice(choice.Session, choice.ModelId, choice.FileBytes), joiner);
        }
    }

    public Task OnEventAsync(ISessionCleaner.Args args)
    {
        lock (gate)
        {
            choices.Remove(args.Session.Id);
        }
        return Task.CompletedTask;
    }
}
