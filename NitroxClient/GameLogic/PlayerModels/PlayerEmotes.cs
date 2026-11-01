using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// What a client does with the emotes of the other players (<c>PlayerEmote</c>, docs/coop/PLAYER_MODELS.md): a start plays the clip of that name on the custom model that the player wears (the pose copy of his
/// body pauses until the clip is over or a stop comes), a stop gives the body back. A player who wears the vanilla body has no clips, so nothing happens for him. Nothing here throws: an emote that cannot be
/// played is one warning and the body goes on as it was.
/// </summary>
public static class PlayerEmotes
{
    private static readonly HashSet<string> warned = new();
    private static bool subscribed;

    /// <summary>Hooks the end of the session (once): the warnings are about the players of that session.</summary>
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
        warned.Clear();
    }

    /// <summary>The <c>PlayerEmote</c> packet of another player.</summary>
    public static void OnRemote(SessionId session, string emoteId, bool playing, PlayerManager playerManager)
    {
        try
        {
            Subscribe();
            if (playerManager == null || !playerManager.TryFind(session, out RemotePlayer player))
            {
                return;
            }
            CustomPlayerModel model = CustomPlayerModels.Of(player);
            if (!model)
            {
                // the vanilla body has no clips
                return;
            }
            if (!playing)
            {
                model.StopEmote();
                return;
            }
            string id = PlayerEmoteRules.SanitizeId(emoteId);
            if (id.Length == 0)
            {
                return;
            }
            if (model.PlayEmote(id, out string message))
            {
                Log.Info($"[PlayerModels] {message}");
            }
            else
            {
                WarnOnce(session, id, $"{player.PlayerName} started the emote '{id}' and {message}: his body goes on as it was");
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] an emote of another player could not be played");
        }
    }

    private static void WarnOnce(SessionId session, string id, string text)
    {
        if (warned.Add($"{(ushort)session}:{id}"))
        {
            Log.Warn($"[PlayerModels] {text}");
        }
    }
}
