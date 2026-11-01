using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The custom player models that remote players wear (docs/coop/PLAYER_MODELS.md): puts a model on a remote player, takes it off, and poses every worn model once per frame. The pose is made in the first
/// <c>Camera.onPreCull</c> of a frame: that is after Update, the Animator and the LateUpdate of Final IK, which is where the vanilla skeleton has its final pose. (A LateUpdate of our own could run before the IK
/// and show the arms of the frame before; a <c>DefaultExecutionOrder</c> is baked into scripts when the game is built, so it cannot be relied on for a mod.)
/// </summary>
public static class CustomPlayerModels
{
    private static readonly List<CustomPlayerModel> worn = [];
    private static CustomPlayerModel local;
    private static bool subscribed;
    private static int lastPosedFrame = -1;

    /// <summary>What the models are sized with when they are put on: the hips (the feet stay on the floor) unless the head is asked for.</summary>
    public static PlayerModelScaleSource ScaleSource { get; set; } = PlayerModelScaleSource.Hips;

    public static IReadOnlyList<CustomPlayerModel> Worn => worn;

    /// <summary>The copy of the model that the local player wears (in his own first-person view, <see cref="WearOwn" />, and while his emote plays, <see cref="WearLocal" />: it is one copy), or null.</summary>
    public static CustomPlayerModel Local => local;

    /// <summary>What the pose copy costs: the time that <see cref="OnPreCull" /> spends per frame, all worn models together.</summary>
    public static PoseCopyStats Stats { get; } = new();

    /// <summary>The model that this player wears, or null.</summary>
    public static CustomPlayerModel Of(RemotePlayer player)
    {
        foreach (CustomPlayerModel model in worn)
        {
            if (model && model.Owner == player)
            {
                return model;
            }
        }
        return null;
    }

    /// <summary>
    /// Puts the model <paramref name="id" /> on <paramref name="player" /> (and takes the one he wore off). False with the reason in <paramref name="message" /> when that is not possible: the vanilla body stays.
    /// On success <paramref name="message" /> is the line for the log.
    /// </summary>
    public static bool Wear(RemotePlayer player, string id, out string message)
    {
        if (player == null || player.Body == null)
        {
            message = "the player has no body yet";
            return false;
        }
        Remove(player);
        LoadedPlayerModel model = PlayerModelLibrary.Load(id);
        if (model == null)
        {
            message = $"the model '{id}' could not be loaded";
            return false;
        }
        Material suit = PlayerModelBuilder.SuitMaterial();
        if (!suit)
        {
            message = "no dive suit material to copy";
            return false;
        }
        CustomPlayerModel created = CustomPlayerModel.Create(player, model, suit, ScaleSource, out string failure);
        if (!created)
        {
            message = failure;
            return false;
        }
        worn.Add(created);
        if (!subscribed)
        {
            Camera.onPreCull += OnPreCull;
            subscribed = true;
        }
        message = created.Summary;
        return true;
    }

    /// <summary>Takes the custom model off this player: the vanilla body is drawn again. True when he wore one.</summary>
    public static bool Remove(RemotePlayer player)
    {
        CustomPlayerModel model = Of(player);
        if (!model)
        {
            return false;
        }
        worn.Remove(model);
        Object.Destroy(model.gameObject);
        return true;
    }

    /// <summary>
    /// Puts the model <paramref name="id" /> on the local player's own body for the length of his emote (the emote camera, <c>PlayerEmoteCamera</c>): the same copy that the others see on him, and for as long as it lives
    /// his own first-person body is hidden (it is given back when the copy is destroyed). False with the reason in <paramref name="message" /> when that is not possible; nothing has been changed then.
    /// <see cref="RemoveLocal" /> takes it off again.
    /// </summary>
    public static bool WearLocal(string id, out string message)
    {
        CustomPlayerModel existing = local;
        if (existing && existing.ModelId == id)
        {
            // the copy that the player wears in his own first-person view: no second one, it only shows its head and is drawn whatever the vanilla body does
            existing.SetEmoteView(true);
            message = $"the copy that the player wears in first person is used ({existing.Summary})";
            return true;
        }
        if (!TryCreateLocal(id, out CustomPlayerModel created, out message))
        {
            return false;
        }
        created.SetEmoteView(true);
        return true;
    }

    /// <summary>
    /// Puts the model <paramref name="id" /> on the local player's own body for good (<see cref="FirstPersonModel" />): he sees it where the vanilla body is drawn, with its head hidden. The copy that is already worn
    /// is kept when it is the same model. False with the reason in <paramref name="message" /> when that is not possible; nothing has been changed then.
    /// </summary>
    public static bool WearOwn(string id, out string message)
    {
        CustomPlayerModel existing = local;
        if (existing && existing.ModelId == id)
        {
            existing.SetEmoteView(false);
            message = "the copy is already worn";
            return true;
        }
        if (!TryCreateLocal(id, out CustomPlayerModel created, out message))
        {
            return false;
        }
        created.SetEmoteView(false);
        return true;
    }

    /// <summary>
    /// The emote camera is done with the copy: the copy goes on being worn in the first-person view when the player wants that (the clip is stopped and the head is hidden again), else it is taken off.
    /// </summary>
    public static void ReleaseLocalFromEmote()
    {
        CustomPlayerModel model = local;
        if (!model)
        {
            return;
        }
        model.StopEmote();
        bool keep = FirstPersonModelRules.Wanted(NitroxClient.GameLogic.Settings.NitroxPrefs.ShowOwnModel.Value, PlayerModelSelection.SavedId, NitroxClient.MonoBehaviours.Multiplayer.Active) && model.ModelId == PlayerModelSelection.SavedId;
        if (keep)
        {
            model.SetEmoteView(false);
        }
        else
        {
            RemoveLocal();
        }
    }

    private static bool TryCreateLocal(string id, out CustomPlayerModel created, out string message)
    {
        created = null;
        RemoveLocal();
        GameObject view;
        try
        {
            view = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerModel;
        }
        catch (Exception exception)
        {
            message = $"the local body has no player_view: {exception.Message}";
            return false;
        }
        if (!view)
        {
            message = "the local body has no player_view";
            return false;
        }
        LoadedPlayerModel model = PlayerModelLibrary.Load(id);
        if (model == null)
        {
            message = $"the model '{id}' could not be loaded";
            return false;
        }
        Material suit = PlayerModelBuilder.SuitMaterial();
        if (!suit)
        {
            message = "no dive suit material to copy";
            return false;
        }
        created = CustomPlayerModel.CreateLocal(view, model, suit, ScaleSource, out string failure);
        if (!created)
        {
            message = failure;
            return false;
        }
        worn.Add(created);
        local = created;
        if (!subscribed)
        {
            Camera.onPreCull += OnPreCull;
            subscribed = true;
        }

        // the light of the room that the player is in (a base, a Cyclops) or of the sea: the copy is not a child of anything that the game tells about it
        Player player = Player.main;
        SubRoot sub = player ? player.currentSub : null;
        if (sub)
        {
            SkyEnvironmentChanged.Broadcast(created.gameObject, sub);
        }
        else
        {
            SkyEnvironmentChanged.Broadcast(created.gameObject, (GameObject)null);
        }
        message = created.Summary;
        return true;
    }

    /// <summary>Takes the copy off the local player's body: his first-person body is drawn again.</summary>
    public static void RemoveLocal()
    {
        CustomPlayerModel model = local;
        local = null;
        if (!model)
        {
            return;
        }
        worn.Remove(model);
        Object.Destroy(model.gameObject);
    }

    /// <summary>Takes every custom model off.</summary>
    public static void RemoveAll()
    {
        foreach (CustomPlayerModel model in worn.ToArray())
        {
            if (model)
            {
                Object.Destroy(model.gameObject);
            }
        }
        worn.Clear();
        local = null;
    }

    internal static void Forget(CustomPlayerModel model)
    {
        worn.Remove(model);
        if (local == model)
        {
            local = null;
        }
    }

    /// <summary>Poses every worn model now (the work of the first <c>onPreCull</c> of a frame; the Debug command <c>vumodel perf</c> calls it in a loop to time it).</summary>
    public static void PoseAll()
    {
        for (int i = worn.Count - 1; i >= 0; i--)
        {
            CustomPlayerModel model = worn[i];
            if (!model)
            {
                worn.RemoveAt(i);
                continue;
            }
            if (model.isActiveAndEnabled)
            {
                model.Pose();
            }
        }
    }

    private static void OnPreCull(Camera camera)
    {
        int frame = Time.frameCount;
        if (frame == lastPosedFrame)
        {
            return;
        }
        lastPosedFrame = frame;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            PoseAll();
        }
        catch (Exception exception)
        {
            // a model that cannot be posed must not break every frame: the vanilla bodies are drawn again
            Log.Error(exception, "[PlayerModels] the pose copy failed, every custom model is taken off");
            RemoveAll();
        }
        Stats.Record(System.Diagnostics.Stopwatch.GetTimestamp() - start, worn.Count);
        if (worn.Count == 0)
        {
            Camera.onPreCull -= OnPreCull;
            subscribed = false;
        }
    }
}

/// <summary>The cost of the pose copy per frame, measured with a stopwatch around everything the first <c>onPreCull</c> of a frame does (the budget is 0.2 ms for the pose copy of four remote players).</summary>
public sealed class PoseCopyStats
{
    private long totalTicks;
    private long maxTicks;
    private long modelFrames;

    public long Frames { get; private set; }

    /// <summary>Models posed, summed over the frames (two models over ten frames is twenty).</summary>
    public long ModelFrames => modelFrames;

    public void Record(long ticks, int models)
    {
        Frames++;
        modelFrames += models;
        totalTicks += ticks;
        if (ticks > maxTicks)
        {
            maxTicks = ticks;
        }
    }

    public void Reset()
    {
        Frames = 0;
        modelFrames = 0;
        totalTicks = 0;
        maxTicks = 0;
    }

    /// <summary>Microseconds per frame, on average.</summary>
    public double AverageMicroseconds => Frames == 0 ? 0 : totalTicks * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency / Frames;

    /// <summary>Microseconds of the slowest frame.</summary>
    public double MaxMicroseconds => maxTicks * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
}
