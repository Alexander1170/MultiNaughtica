using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>The emote clips of one model (the source of the shared emotes) and its skeleton, read once: what every model without emotes of its own plays on its own bones.</summary>
internal sealed class SharedEmoteSet
{
    public SharedEmoteSet(string sourceId, PlayerModelRest rest, PlayerModelBoneNames names, IReadOnlyList<EmoteEntry> entries, IReadOnlyDictionary<string, GlbClipData> clips)
    {
        SourceId = sourceId;
        Rest = rest;
        Names = names;
        Entries = entries;
        Clips = clips;
    }

    /// <summary>The id of the model whose clips these are.</summary>
    public string SourceId { get; }

    /// <summary>The rest pose of its skeleton: the clips turn its bones, which the other model's bones follow (<see cref="PlayerModelEmoteRetarget" />).</summary>
    public PlayerModelRest Rest { get; }

    public PlayerModelBoneNames Names { get; }

    /// <summary>What the wheel shows for the clips, in the order of the file.</summary>
    public IReadOnlyList<EmoteEntry> Entries { get; }

    public IReadOnlyDictionary<string, GlbClipData> Clips { get; }
}

/// <summary>
/// The shared emotes (docs/coop/PLAYER_MODELS.md, "Shared emotes"): a model that brings no emotes of its own, a model made with Mixamo's skeleton and nothing but Running and Walking, plays the emotes of another installed model
/// (the source, named in the profile of the model by <c>emotes.source</c>, <see cref="PlayerModelProfile.EmoteSource" />) on its own bones, by bone name. A model's own clips win: a shared clip only fills the wheel for the ids
/// that the model does not bring itself. The source is read once, on another thread (a model file can be tens of megabytes), and until it is read the wheel of such a model is not ready (<see cref="Merged" /> answers null, like
/// <see cref="PlayerModelLibrary.EmotesOf" /> does for a model that is still being read).
/// </summary>
internal static class SharedEmotes
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, SharedEmoteSet> ready = new(StringComparer.Ordinal);
    private static readonly HashSet<string> loading = new(StringComparer.Ordinal);
    private static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IReadOnlyList<EmoteEntry>> merged = new(StringComparer.Ordinal);

    /// <summary>Forgets what was read (the models were scanned again).</summary>
    public static void Reset()
    {
        lock (gate)
        {
            ready.Clear();
            failed.Clear();
            merged.Clear();
        }
    }

    /// <summary>Whether reading the source failed (it is not tried again until <see cref="Reset" />).</summary>
    public static bool HasFailed(string sourceId)
    {
        lock (gate)
        {
            return failed.Contains(sourceId);
        }
    }

    /// <summary>
    /// The clips of the source model, when they have been read; otherwise null, and the first call starts reading them on another thread. Called on the main thread (it asks the library where the file is).
    /// </summary>
    public static SharedEmoteSet TryGet(string sourceId)
    {
        lock (gate)
        {
            if (ready.TryGetValue(sourceId, out SharedEmoteSet set))
            {
                return set;
            }
            if (failed.Contains(sourceId) || !loading.Add(sourceId))
            {
                return null;
            }
        }
        if (!PlayerModelLibrary.TryGetPath(sourceId, out string path))
        {
            lock (gate)
            {
                loading.Remove(sourceId);
                failed.Add(sourceId);
            }
            Log.Warn($"[PlayerModels] the model '{sourceId}', which the shared emotes come from, is not here: a model with no emotes of its own has none");
            return null;
        }
        PlayerModelBoneNames names = PlayerModelLibrary.ProfileOf(sourceId).BoneNames;
        Task.Run(() =>
        {
            try
            {
                SharedEmoteSet read = Read(sourceId, path, names);
                lock (gate)
                {
                    ready[sourceId] = read;
                    loading.Remove(sourceId);
                }
                Log.Info($"[PlayerModels] the shared emotes of '{sourceId}' are ready: {string.Join(", ", read.Entries.Select(entry => entry.Id))}");
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    loading.Remove(sourceId);
                    failed.Add(sourceId);
                }
                Log.Error(exception, $"[PlayerModels] the shared emotes of '{sourceId}' could not be read");
            }
        });
        return null;
    }

    /// <summary>Reads the emote clips and the skeleton of a model file (nothing of Unity is touched: it runs on another thread). A clip that cannot be read is left out, with a warning.</summary>
    internal static SharedEmoteSet Read(string sourceId, string path, PlayerModelBoneNames names)
    {
        GlbModel glb = GlbModel.Read(GlbDocument.Load(path));
        List<EmoteEntry> entries = [];
        Dictionary<string, GlbClipData> clips = new(StringComparer.Ordinal);
        foreach (EmoteEntry entry in PlayerEmoteRules.EmotesOf(glb.Clips))
        {
            try
            {
                clips[entry.Id] = glb.ReadClipData(glb.FindClip(entry.Id));
                entries.Add(entry);
            }
            catch (Exception exception)
            {
                Log.Warn($"[PlayerModels] the clip '{entry.Id}' of '{sourceId}' cannot be read and is not shared: {exception.Message}");
            }
        }
        return new SharedEmoteSet(sourceId, PlayerModelRest.FromGlb(glb), names, entries, clips);
    }

    /// <summary>
    /// The entries of the wheel of the model <paramref name="id" />: its own emotes, then the shared ones whose ids it does not bring itself, as many as the wheel has room for. Just <paramref name="own" /> when the
    /// profile of the model says it plays no shared emotes, when the skeletons do not match or when the source cannot be read. Null while the source is being read.
    /// </summary>
    public static IReadOnlyList<EmoteEntry> Merged(string id, IReadOnlyList<EmoteEntry> own, PlayerModelRest rest)
    {
        PlayerModelProfile profile = PlayerModelLibrary.ProfileOf(id);
        if (!profile.SharedEmotes || rest == null)
        {
            return own;
        }
        lock (gate)
        {
            if (merged.TryGetValue(id, out IReadOnlyList<EmoteEntry> known))
            {
                return known;
            }
        }
        SharedEmoteSet set = TryGet(profile.EmoteSource);
        if (set == null)
        {
            return HasFailed(profile.EmoteSource) ? own : null;
        }
        IReadOnlyList<EmoteEntry> result = own;
        if (set.SourceId != id && PlayerModelEmoteRetarget.SharedBoneCount(set.Rest, set.Names, rest, profile.BoneNames) >= PlayerModelEmoteRetarget.MIN_MAPPED_BONES)
        {
            List<EmoteEntry> list = [.. own];
            foreach (EmoteEntry entry in set.Entries)
            {
                if (list.Count >= PlayerEmoteRules.MAX_WHEEL_ENTRIES)
                {
                    break;
                }
                if (!list.Any(existing => existing.Id == entry.Id))
                {
                    list.Add(entry);
                }
            }
            result = list;
        }
        lock (gate)
        {
            merged[id] = result;
        }
        return result;
    }

    /// <summary>
    /// The shared clip with this name and the retarget that plays it on <paramref name="model" />'s bones; null (and no retarget) when the model plays no shared emotes, the source is not read yet or has no such clip,
    /// or the skeletons do not match.
    /// </summary>
    public static GlbClipData ClipFor(LoadedPlayerModel model, string clipName, out PlayerModelEmoteRetarget retarget)
    {
        retarget = null;
        PlayerModelProfile profile = model.Profile;
        if (!profile.SharedEmotes)
        {
            return null;
        }
        SharedEmoteSet set = TryGet(profile.EmoteSource);
        if (set == null || set.SourceId == model.Id || !set.Clips.TryGetValue(clipName, out GlbClipData clip))
        {
            return null;
        }
        retarget = model.RetargetFrom(set);
        return retarget != null ? clip : null;
    }
}
