using System;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using NitroxClient.GameLogic.Settings;
using UnityEngine;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The custom player model that this player chose to be seen in (docs/coop/PLAYER_MODELS.md): the id of a model of the <see cref="PlayerModelLibrary" />, kept in the game's PlayerPrefs, empty for the vanilla
/// body. The Options menu shows it as a list ("Default" and one entry per model); the number of the entry is only the position in that list, the id is what is saved and what is sent to the others
/// (<see cref="PlayerModelAnnouncer" />). The player does not see his own model: the others do.
/// </summary>
public static class PlayerModelSelection
{
    private const string KEY = "Nitrox.playerModel";

    /// <summary>Raised with the new id when the player picks another model in the Options menu.</summary>
    public static event Action<string> Changed;

    /// <summary>The saved id; empty (the vanilla body) when nothing was chosen, or when the file of the saved model is gone.</summary>
    public static string SavedId
    {
        get
        {
            string id = PrefsStore.GetString(KEY, PlayerModelRules.VANILLA);
            return PlayerModelRules.IsValidId(id) && PlayerModelLibrary.TryGetPath(id, out _) ? id : PlayerModelRules.VANILLA;
        }
    }

    /// <summary>The position of the saved model in the list of the Options menu (0 is "Default").</summary>
    public static int IndexOfSaved()
    {
        string id = SavedId;
        if (id.Length == 0)
        {
            return 0;
        }
        IReadOnlyList<string> ids = PlayerModelLibrary.Ids;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i + 1;
            }
        }
        return 0;
    }

    /// <summary>The player picked the entry <paramref name="index" /> of the list: 0 is "Default", the others are the models in the order of <see cref="PlayerModelLibrary.Ids" />.</summary>
    public static void Select(int index)
    {
        IReadOnlyList<string> ids = PlayerModelLibrary.Ids;
        string id = index > 0 && index <= ids.Count ? ids[index - 1] : PlayerModelRules.VANILLA;
        PrefsStore.SetString(KEY, id);
        PrefsStore.Save();
        NitroxPrefs.PlayerModelIndex.Value = index;
        Log.Info($"[PlayerModels] the player model is now '{(id.Length == 0 ? "Default" : id)}'");
        if (id.Length > 0)
        {
            // the emote wheel needs the clips of the model: they are read in the background now
            PlayerModelLibrary.EmotesOf(id);
        }
        PlayerModelAnnouncer.Announce();
        Changed?.Invoke(id);
    }

    /// <summary>The same as picking it in the menu, by id (a Debug command and a way to ask for the vanilla body with an empty id).</summary>
    public static void Choose(string id)
    {
        int index = 0;
        IReadOnlyList<string> ids = PlayerModelLibrary.Ids;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                index = i + 1;
            }
        }
        Select(index);
    }
}
