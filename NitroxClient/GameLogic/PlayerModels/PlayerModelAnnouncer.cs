using System;
using System.Collections;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// Tells the server which custom player model this player wears (<see cref="PlayerModelChoice" />, docs/coop/PLAYER_MODELS.md): when he picks one in the Options menu, and once after joining (the server
/// remembers the latest choice of every session in memory only). The server takes one choice per <see cref="PlayerModelRules.SERVER_INTERVAL_SECONDS" /> of a player, so a client never sends more often than
/// every <see cref="PlayerModelRules.CLIENT_INTERVAL_SECONDS" />: a choice that comes sooner waits, and what is sent then is the latest one. The length of the model's file goes with the id, so that the others
/// can tell that they have the same file. Nothing is sent outside a session (the Options menu of the main menu only saves the choice), and what waits is dropped when the session ends.
/// </summary>
public static class PlayerModelAnnouncer
{
    private static float lastSentAt = -1000f;
    private static bool waiting;
    // counts the sessions that ended: a wait that was started before the end does not send into the next session
    private static int generation;
    private static bool subscribed;

    /// <summary>What was sent last, for the Debug command that reports it.</summary>
    public static string LastSent { get; private set; } = "";

    /// <summary>Hooks the end of the session (once).</summary>
    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: what was sent and what waits to be sent belong to it, and the next session starts with neither.</summary>
    public static void Reset()
    {
        generation++;
        waiting = false;
        lastSentAt = -1000f;
        LastSent = "";
    }

    /// <summary>
    /// After the initial sync: the others do not know a player who wears the vanilla body, so a player who wears none says nothing. It never throws: a model problem must not break a join (the others then
    /// see the vanilla body).
    /// </summary>
    public static void AnnounceAtJoin()
    {
        try
        {
            Subscribe();
            NitroxClient.GameLogic.Settings.PrefsStore.ReportUse();
            string saved = PlayerModelSelection.SavedId;
            if (saved.Length > 0)
            {
                // the emote wheel needs the clips of the model: they are read in the background now, so that it is ready when the player wants it
                PlayerModelLibrary.EmotesOf(saved);
                Announce();
            }
            // the player sees his own model, too (the Options setting "Show my model in first person")
            FirstPersonModel.Refresh();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] telling the server which model this player wears failed: the join goes on and the others see the vanilla body");
        }
    }

    /// <summary>Sends the saved choice now, or as soon as the interval since the last one allows. Outside a session nothing is sent: the choice is saved and the next join tells the server.</summary>
    public static void Announce()
    {
        try
        {
            Subscribe();
            if (!Multiplayer.Active)
            {
                return;
            }
            if (waiting)
            {
                // the waiting one reads the choice when it is sent
                return;
            }
            float wait = lastSentAt + (float)PlayerModelRules.CLIENT_INTERVAL_SECONDS - Time.unscaledTime;
            if (wait <= 0f)
            {
                SendNow();
                return;
            }
            waiting = true;
            CoroutineHost.StartCoroutine(SendLater(wait, generation));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] telling the server which model this player wears failed");
        }
    }

    private static IEnumerator SendLater(float seconds, int startedIn)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (startedIn != generation)
        {
            // the session ended while this waited: the next one has its own
            yield break;
        }
        waiting = false;
        if (!Multiplayer.Active)
        {
            yield break;
        }
        try
        {
            SendNow();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] telling the server which model this player wears failed");
        }
    }

    private static void SendNow()
    {
        string id = PlayerModelSelection.SavedId;
        long bytes = id.Length == 0 ? 0 : PlayerModelLibrary.FileLength(id);
        LocalPlayer localPlayer = NitroxServiceLocator.LocateService<LocalPlayer>();
        SessionId session = localPlayer.SessionId ?? default;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new PlayerModelChoice(session, id, bytes));
        lastSentAt = Time.unscaledTime;
        LastSent = $"{(id.Length == 0 ? "Default" : id)}/{bytes}";
        Log.Info($"[PlayerModels] told the server that this player wears '{(id.Length == 0 ? "Default" : id)}' ({bytes} bytes)");
    }
}
