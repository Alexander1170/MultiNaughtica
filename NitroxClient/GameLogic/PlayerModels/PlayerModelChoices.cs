using System;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The custom player models that the other players chose (<c>PlayerModelChoice</c>, docs/coop/PLAYER_MODELS.md): the latest choice of every session, kept here until the player's body exists (a player who
/// is already there when this one joins is told after the initial sync, before or after his body is made) and put on his body as soon as the file is checked and the model is loaded. A model is put on only
/// when this game has the file and it has the same length as the chooser's; any other case shows the vanilla body, with one warning for each player and model. A model that is not loaded yet is loaded in
/// steps over several frames (<see cref="PlayerModelLibrary.LoadAsync" />), so that nobody's choice hitches the game.
/// </summary>
public static class PlayerModelChoices
{
    private sealed record Chosen(string Id, long Bytes);

    private static readonly Dictionary<SessionId, Chosen> remembered = new();
    private static readonly Dictionary<SessionId, int> generations = new();
    private static readonly HashSet<string> warned = new();
    // the number of the last Apply: it never goes back, not even when a session ends, so that a load that was started in one session can never match a wish of the next one
    private static int lastGeneration;
    private static bool subscribed;

    /// <summary>The number of players whose choice is remembered.</summary>
    public static int Count => remembered.Count;

    /// <summary>Hooks the end of the session (once): what is remembered here belongs to the session that is running.</summary>
    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>
    /// The session ended: the choices of its players, the loads that were waiting for them and the warnings are gone, and so are the models they wore. A session id comes up again in the next session (on this
    /// server or another), and it must not show a stranger as somebody who is gone.
    /// </summary>
    public static void Reset()
    {
        remembered.Clear();
        generations.Clear();
        warned.Clear();
        CustomPlayerModels.RemoveAll();
    }

    /// <summary>Remembers what this player wears (an empty id is the vanilla body) and puts it on his body if there is one.</summary>
    public static void Choose(SessionId session, string modelId, long fileBytes, PlayerManager playerManager)
    {
        Subscribe();
        string id = PlayerModelRules.SanitizeId(modelId);
        if (id.Length == 0)
        {
            remembered.Remove(session);
        }
        else
        {
            remembered[session] = new Chosen(id, PlayerModelRules.SanitizeBytes(id, fileBytes));
        }
        try
        {
            if (playerManager != null && playerManager.TryFind(session, out RemotePlayer player))
            {
                Apply(player);
            }
        }
        catch (Exception exception)
        {
            // a model problem must not travel on into the packet handling of a join: the vanilla body stays
            Log.Error(exception, "[PlayerModels] what a player chose could not be put on his body");
        }
    }

    /// <summary>A player's body has just been made (or made again): what he chose goes on it. It never throws: a model problem must not break a remote body, or the join that makes it.</summary>
    public static void OnBodyReady(RemotePlayer player)
    {
        try
        {
            Subscribe();
            Apply(player);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] what a new body wears could not be decided: the vanilla body stays");
        }
    }

    /// <summary>A player left: his choice and the warnings about it are forgotten.</summary>
    public static void Forget(SessionId session)
    {
        remembered.Remove(session);
        generations.Remove(session);
        warned.RemoveWhere(key => key.StartsWith($"{(ushort)session}:"));
    }

    /// <summary>What is remembered, for the Debug command that reports it: "Bob:player-example/21741924".</summary>
    public static string Describe(PlayerManager playerManager)
    {
        return remembered.Count == 0 ? "none" : string.Join(" ", remembered.Select(pair => $"{NameOf(pair.Key, playerManager)}:{pair.Value.Id}/{pair.Value.Bytes}"));
    }

    /// <summary>
    /// Puts the model that this player chose on his body, takes the one off that he wears when he chose the vanilla body, and leaves the body alone when it wears the right one already. The file is checked
    /// first (it has to be here and to have the length the chooser has); the model is loaded in steps when it is not loaded yet, and put on when it is.
    /// </summary>
    public static void Apply(RemotePlayer player)
    {
        if (player?.Body == null)
        {
            return;
        }
        SessionId session = player.SessionId;
        int mine = ++lastGeneration;
        generations[session] = mine;

        if (!remembered.TryGetValue(session, out Chosen chosen))
        {
            CustomPlayerModels.Remove(player);
            return;
        }
        CustomPlayerModel worn = CustomPlayerModels.Of(player);
        if (worn && worn.ModelId == chosen.Id)
        {
            return;
        }
        if (!PlayerModelLibrary.TryGetPath(chosen.Id, out _))
        {
            WarnOnce(session, chosen.Id, $"{player.PlayerName} wears the model '{chosen.Id}' and this game has no file of that name: his vanilla body is shown");
            CustomPlayerModels.Remove(player);
            return;
        }
        long own = PlayerModelLibrary.FileLength(chosen.Id);
        if (!PlayerModelRules.SameFile(own, chosen.Bytes))
        {
            WarnOnce(session, chosen.Id, $"{player.PlayerName} wears the model '{chosen.Id}' with a file of {chosen.Bytes} bytes and this game's file of that name has {own}: another model, his vanilla body is shown");
            CustomPlayerModels.Remove(player);
            return;
        }
        PlayerModelLibrary.LoadAsync(chosen.Id, model => Finish(player, chosen.Id, mine, model));
    }

    private static void Finish(RemotePlayer player, string id, int generation, LoadedPlayerModel model)
    {
        if (player?.Body == null || generations.GetValueOrDefault(player.SessionId) != generation)
        {
            // he left, or chose something else while the model was loading: that choice has its own Apply
            return;
        }
        if (model == null)
        {
            WarnOnce(player.SessionId, id, $"the model '{id}' that {player.PlayerName} wears could not be read: his vanilla body is shown");
            CustomPlayerModels.Remove(player);
            return;
        }
        if (CustomPlayerModels.Wear(player, id, out string message))
        {
            Log.Info($"[PlayerModels] {message}");
        }
        else
        {
            WarnOnce(player.SessionId, id, $"the model '{id}' could not be put on {player.PlayerName}: {message}");
        }
    }

    private static void WarnOnce(SessionId session, string id, string text)
    {
        if (warned.Add($"{(ushort)session}:{id}:{text.GetHashCode()}"))
        {
            Log.Warn($"[PlayerModels] {text}");
        }
    }

    private static string NameOf(SessionId session, PlayerManager playerManager)
    {
        return playerManager != null && playerManager.TryFind(session, out RemotePlayer player) ? player.PlayerName : $"session{(ushort)session}";
    }
}
