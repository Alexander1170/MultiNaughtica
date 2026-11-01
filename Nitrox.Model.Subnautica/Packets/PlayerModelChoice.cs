using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// The custom player model that a player wears (docs/coop/PLAYER_MODELS.md): the id of a model file (its name without ".glb") and the length of that file, empty and 0 for the vanilla body. A client sends it
/// when its player picks another model in the Options menu and once after joining; the server does not believe the session in it: it puts the real one of the connection in, cleans the id, takes one
/// choice per <c>PlayerModelRules.SERVER_INTERVAL_SECONDS</c> of a player, remembers the latest of every session and passes it to everybody else; a player who joins later gets the remembered ones of the others
/// after his initial sync (<c>PlayerModelLedger</c>). A model file is never sent: every player has the file or sees the vanilla body (the lengths have to be the same, <c>PlayerModelRules.SameFile</c>).
/// </summary>
[Serializable]
public class PlayerModelChoice(SessionId sessionId, string modelId, long fileBytes) : Packet
{
    /// <summary>The player who chose. A client sends its own; the server replaces it with the one of the connection.</summary>
    public SessionId SessionId { get; } = sessionId;

    /// <summary>The id of the model, empty for the vanilla body.</summary>
    public string ModelId { get; } = modelId;

    /// <summary>The length in bytes of the file of that model on the chooser's machine, 0 for the vanilla body.</summary>
    public long FileBytes { get; } = fileBytes;
}
