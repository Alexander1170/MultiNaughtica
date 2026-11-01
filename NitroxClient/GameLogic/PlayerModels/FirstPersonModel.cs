using System;
using Nitrox.Model.Core;
using NitroxClient.GameLogic.Settings;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The player's own model, in his own first-person view (docs/coop/PLAYER_MODELS.md, "Your own model"): while he wears a custom model and the Options setting "Show my model in first person" is on (it is by default),
/// his own body is that model. A copy of the model is put on his body (the same copy that the emote camera uses, <see cref="CustomPlayerModels.WearOwn" />): every frame it takes the pose of the vanilla
/// skeleton, which the game goes on posing as it always did (the arms that grip a tool, the PDA, the seats), the vanilla meshes are not drawn, and the copy is drawn where the vanilla body would be. Its head is
/// hidden. What the hands hold stays at the vanilla hand. This class decides when the copy is made and when it goes: when the setting is switched, when another model is chosen, after the join, at the end of the session.
/// </summary>
public static class FirstPersonModel
{
    // counts the changes of mind: a model that was being loaded for an older wish is not put on
    private static int generation;
    private static bool subscribed;

    /// <summary>What the last attempt to put the copy on said, for the Debug command that reports it.</summary>
    public static string LastMessage { get; private set; } = "";

    /// <summary>Hooks the choice of a model and the end of the session (once).</summary>
    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        PlayerModelSelection.Changed += _ => Refresh();
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: what waits to be put on belongs to it (the copy itself is taken off with every other model).</summary>
    public static void Reset()
    {
        generation++;
        LastMessage = "";
    }

    /// <summary>
    /// Puts the copy on or takes it off as the setting, the choice and the session say (<see cref="FirstPersonModelRules.Wanted" />), now: the Options setting calls it, so does the choice of another model and the
    /// join. A model that is not loaded yet is loaded in steps first (<see cref="PlayerModelLibrary.LoadAsync" />). It never throws.
    /// </summary>
    public static void Refresh()
    {
        try
        {
            Subscribe();
            bool inGame = Multiplayer.Active && Player.main;
            string id = PlayerModelSelection.SavedId;
            bool wanted = FirstPersonModelRules.Wanted(NitroxPrefs.ShowOwnModel.Value, id, inGame);
            if (PlayerEmoteDriver.CameraOut)
            {
                // the emote camera has the copy now: when it is done it keeps the copy or takes it off by the same rules
                return;
            }
            CustomPlayerModel local = CustomPlayerModels.Local;
            if (!wanted)
            {
                generation++;
                if (local)
                {
                    CustomPlayerModels.RemoveLocal();
                    Log.Info("[PlayerModels] the player's own model is not shown in first person any more: the vanilla body is drawn again");
                }
                return;
            }
            if (local && local.ModelId == id)
            {
                local.SetEmoteView(false);
                return;
            }
            int mine = ++generation;
            if (PlayerModelLibrary.IsLoaded(id))
            {
                Wear(id);
                return;
            }
            PlayerModelLibrary.LoadAsync(id, loaded =>
            {
                if (mine != generation)
                {
                    return;
                }
                if (loaded == null)
                {
                    Log.Warn($"[PlayerModels] the model '{id}' could not be loaded: the player sees the vanilla body in first person");
                    return;
                }
                Wear(id);
            });
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] putting the player's own model on in first person failed: the vanilla body is drawn");
        }
    }

    private static void Wear(string id)
    {
        // by now the choice, the setting or the session may have changed
        bool inGame = Multiplayer.Active && Player.main;
        if (!FirstPersonModelRules.Wanted(NitroxPrefs.ShowOwnModel.Value, PlayerModelSelection.SavedId, inGame) || PlayerModelSelection.SavedId != id || PlayerEmoteDriver.CameraOut)
        {
            return;
        }
        if (CustomPlayerModels.WearOwn(id, out string message))
        {
            LastMessage = message;
            Log.Info($"[PlayerModels] the player sees his own model in first person: {message}");
        }
        else
        {
            LastMessage = message;
            Log.Warn($"[PlayerModels] the player's own model could not be put on in first person: {message}");
        }
    }
}
