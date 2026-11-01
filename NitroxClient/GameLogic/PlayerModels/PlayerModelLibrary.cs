using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nitrox.Model.Helper;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using UWE;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// The custom player models that this client has on disk (docs/coop/PLAYER_MODELS.md): the files <c>*.glb</c> of the folder that the zip ships (<c>&lt;launcher&gt;\Resources\PlayerModels</c>) and of the player's own
/// folder (<c>%APPDATA%\Nitrox\models</c>), by id (the name of the file without the extension). A file of the player's own folder wins over a shipped one of the same id. An id that came over the network is only ever
/// looked up here, so it cannot name a path.
/// </summary>
public static class PlayerModelLibrary
{
    private static readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LoadedPlayerModel> loaded = new(StringComparer.Ordinal);
    private static bool scanned;
    private static readonly Dictionary<string, List<Action<LoadedPlayerModel>>> waiting = new(StringComparer.Ordinal);
    private static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PlayerModelProfile> profiles = new(StringComparer.Ordinal);

    /// <summary>The ids of the models that were found, in alphabetical order.</summary>
    public static IReadOnlyList<string> Ids
    {
        get
        {
            EnsureScanned();
            return files.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>The folder from which the zip ships its models; null when the launcher's folder is not known (a game that was not started by the launcher).</summary>
    public static string ShippedFolder => NitroxUser.LauncherPath is { } launcher ? Path.Combine(launcher, "Resources", PlayerModelRules.SHIPPED_FOLDER) : null;

    /// <summary>The folder of the models that the player adds (it may not exist).</summary>
    public static string UserFolder => Path.Combine(NitroxDirectory.ConfigPath, PlayerModelRules.USER_FOLDER);

    /// <summary>Looks at the folders again (a model was added while the game runs).</summary>
    public static void Rescan()
    {
        files.Clear();
        failed.Clear();
        profiles.Clear();
        SharedEmotes.Reset();
        lock (emoteLock)
        {
            emoteSurveys.Clear();
        }
        try
        {
            foreach (string folder in new[] { ShippedFolder, UserFolder })
            {
                ScanFolder(folder);
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the model folders could not be found: {exception.Message}");
        }
        scanned = true;
        Log.Info($"[PlayerModels] {files.Count} models found: {string.Join(", ", files.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}");
    }

    /// <summary>
    /// What the profile beside the model's file says (<c>&lt;id&gt;.profile.json</c> in the folder of the file that was found; <see cref="PlayerModelProfile.Default" /> when there is none, or for the vanilla body):
    /// read when the folders are scanned.
    /// </summary>
    public static PlayerModelProfile ProfileOf(string id)
    {
        EnsureScanned();
        return !string.IsNullOrEmpty(id) && profiles.TryGetValue(id, out PlayerModelProfile profile) ? profile : PlayerModelProfile.Default;
    }

    /// <summary>The name that the Options list shows for the model: the one in its profile, else its file name made presentable ("Default" for the vanilla body).</summary>
    public static string DisplayNameOf(string id)
    {
        return string.IsNullOrEmpty(id) ? PlayerModelRules.DisplayName(id) : ProfileOf(id).DisplayNameOr(id);
    }

    public static bool TryGetPath(string id, out string path)
    {
        EnsureScanned();
        path = null;
        return PlayerModelRules.IsValidId(id) && files.TryGetValue(id, out path);
    }

    /// <summary>
    /// The model with this id, read and turned into the assets the game needs (once; the same object afterwards). Null when there is no such file or it cannot be read: the reason is in the log and the vanilla body
    /// stays.
    /// </summary>
    public static LoadedPlayerModel Load(string id)
    {
        if (loaded.TryGetValue(id, out LoadedPlayerModel cached))
        {
            return cached;
        }
        if (!TryGetPath(id, out string path))
        {
            Log.Warn($"[PlayerModels] no model file for the id '{id}'");
            return null;
        }
        try
        {
            LoadedPlayerModel model = PlayerModelBuilder.Load(id, path);
            model.Profile = ProfileOf(id);
            loaded[id] = model;
            WarmSharedEmotes(model);
            return model;
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[PlayerModels] the model '{id}' ({path}) could not be loaded");
            return null;
        }
    }


    /// <summary>
    /// The length of the file of this model on this machine in bytes, 0 when there is none. It goes with the id when the player tells the others which model he wears, so that they can tell whether they have the
    /// same file.
    /// </summary>
    public static long FileLength(string id)
    {
        if (!TryGetPath(id, out string path))
        {
            return 0;
        }
        try
        {
            long length = new FileInfo(path).Length;
#if DEBUG
            // the Debug command "vumodel skew" makes this machine's file a different one for the length check
            length += length > 0 ? DebugLengthSkew : 0;
#endif
            return length;
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the length of {path} could not be read: {exception.Message}");
            return 0;
        }
    }

    /// <summary>The ids of the models that have been read, in alphabetical order.</summary>
    public static IReadOnlyList<string> LoadedIds => loaded.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Whether the model has been read already (a model that is wanted but not loaded yet is loaded in steps over several frames by <see cref="LoadAsync" />).</summary>
    public static bool IsLoaded(string id) => loaded.ContainsKey(id);

    /// <summary>Whether reading the model failed once: it is not tried again until the models are scanned again.</summary>
    public static bool HasFailed(string id) => failed.Contains(id);

    /// <summary>What a model's file says about emotes: the clips that it brings itself and the rest pose of its skeleton (to know whether the shared emotes can be played on it).</summary>
    private sealed class EmoteSurvey
    {
        public EmoteSurvey(IReadOnlyList<EmoteEntry> own, PlayerModelRest rest)
        {
            Own = own;
            Rest = rest;
        }

        public IReadOnlyList<EmoteEntry> Own { get; }

        public PlayerModelRest Rest { get; }
    }

    private static readonly Dictionary<string, EmoteSurvey> emoteSurveys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> emoteSurveysParsing = new(StringComparer.Ordinal);
    private static readonly object emoteLock = new();

    /// <summary>
    /// The emotes of the model with this id, for the wheel: the clips of the model that are emotes, then the shared ones that its skeleton can play (<see cref="SharedEmotes" />). A model that has been read gives
    /// them at once; otherwise the file is parsed once on another thread (nothing of Unity is touched there) and null is returned until that has finished (and the shared clips have been read, which is one more file):
    /// the first call asks, the next ones answer. An empty list when there is no file of that id or it cannot be read.
    /// </summary>
    public static IReadOnlyList<EmoteEntry> EmotesOf(string id)
    {
        EmoteSurvey survey = SurveyOf(id);
        if (survey == null)
        {
            return null;
        }
        return survey.Rest == null ? survey.Own : SharedEmotes.Merged(id, survey.Own, survey.Rest);
    }

    private static EmoteSurvey SurveyOf(string id)
    {
        if (loaded.TryGetValue(id, out LoadedPlayerModel model))
        {
            return new EmoteSurvey(PlayerEmoteRules.EmotesOf(model.Glb.Clips), model.Rest);
        }
        lock (emoteLock)
        {
            if (emoteSurveys.TryGetValue(id, out EmoteSurvey known))
            {
                return known;
            }
        }
        if (!TryGetPath(id, out string path))
        {
            return new EmoteSurvey(Array.Empty<EmoteEntry>(), null);
        }
        bool start;
        lock (emoteLock)
        {
            start = emoteSurveysParsing.Add(id);
        }
        if (start)
        {
            Task.Run(() =>
            {
                EmoteSurvey result;
                try
                {
                    GlbModel glb = GlbModel.Read(GlbDocument.Load(path));
                    result = new EmoteSurvey(PlayerEmoteRules.EmotesOf(glb.Clips), PlayerModelRest.FromGlb(glb));
                }
                catch (Exception exception)
                {
                    Log.Error(exception, $"[PlayerModels] the emotes of '{id}' could not be read");
                    result = new EmoteSurvey(Array.Empty<EmoteEntry>(), null);
                }
                lock (emoteLock)
                {
                    emoteSurveys[id] = result;
                    emoteSurveysParsing.Remove(id);
                }
            });
        }
        return null;
    }

    /// <summary>
    /// Reads the model in steps over several frames so that nothing hitches: the file is read and parsed on another thread, then the mesh and each texture are made in a frame of their own (the longest step is
    /// logged). <paramref name="done" /> gets the model, or null when it cannot be read (the reason is in the log and the vanilla body stays). A model that is loaded already is handed over at once; two wishes
    /// for the same id share one read.
    /// </summary>
    public static void LoadAsync(string id, Action<LoadedPlayerModel> done)
    {
        if (loaded.TryGetValue(id, out LoadedPlayerModel cached))
        {
            done(cached);
            return;
        }
        if (failed.Contains(id) || !TryGetPath(id, out string path))
        {
            done(null);
            return;
        }
        if (waiting.TryGetValue(id, out List<Action<LoadedPlayerModel>> callbacks))
        {
            callbacks.Add(done);
            return;
        }
        waiting[id] = [done];
        CoroutineHost.StartCoroutine(LoadRoutine(id, path));
    }

    private static IEnumerator LoadRoutine(string id, string path)
    {
        ModelLoadResult result = new();
        yield return PlayerModelBuilder.LoadSteps(id, path, result);
        if (result.Model != null)
        {
            result.Model.Profile = ProfileOf(id);
            loaded[id] = result.Model;
            WarmSharedEmotes(result.Model);
        }
        else
        {
            failed.Add(id);
            Log.Error($"[PlayerModels] the model '{id}' ({path}) could not be loaded: {result.Failure}");
        }
        List<Action<LoadedPlayerModel>> callbacks = waiting[id];
        waiting.Remove(id);
        foreach (Action<LoadedPlayerModel> callback in callbacks)
        {
            try
            {
                callback(result.Model);
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"[PlayerModels] what was waiting for the model '{id}' failed");
            }
        }
    }

#if DEBUG
    /// <summary>Added to the length of every file by <see cref="FileLength" />: a Debug aid that makes this machine's file look like another one.</summary>
    public static long DebugLengthSkew { get; set; }

    /// <summary>Forgets that a file of this id exists, until the next scan (a Debug aid: this machine does not have the model).</summary>
    public static void DebugForget(string id)
    {
        EnsureScanned();
        files.Remove(id);
        loaded.Remove(id);
    }
#endif

    /// <summary>Forgets the models that were loaded (their assets are destroyed): the next <see cref="Load" /> reads the file again.</summary>
    public static void Unload()
    {
        foreach (LoadedPlayerModel model in loaded.Values)
        {
            model.Destroy();
        }
        loaded.Clear();
    }

    /// <summary>The list of the Options menu: "Default" and the display name of every model, in the order of <see cref="Ids" />.</summary>
    public static string[] ListItems()
    {
        try
        {
            return new[] { PlayerModelRules.DisplayName(PlayerModelRules.VANILLA) }.Concat(Ids.Select(DisplayNameOf)).ToArray();
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the list of models could not be made: {exception.Message}");
            return [PlayerModelRules.DisplayName(PlayerModelRules.VANILLA)];
        }
    }

    private static void EnsureScanned()
    {
        if (!scanned)
        {
            Rescan();
        }
    }

    /// <summary>A model that brings no emote of its own plays the shared ones (<see cref="SharedEmotes" />): their source is read now, in the background, so that the wheel and the emotes of the model are ready when they are wanted.</summary>
    private static void WarmSharedEmotes(LoadedPlayerModel model)
    {
        if (model.Profile.SharedEmotes && PlayerEmoteRules.EmotesOf(model.Glb.Clips).Count == 0)
        {
            SharedEmotes.TryGet(model.Profile.EmoteSource);
        }
    }

    /// <summary>Reads the profile beside a model file; the default profile when there is none or it cannot be read at all (what is wrong with a field is a line in the log and the field keeps its default).</summary>
    private static PlayerModelProfile ReadProfile(string glbPath, string id)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(glbPath) ?? "", $"{id}{PlayerModelProfile.FILE_SUFFIX}");
            if (!File.Exists(path))
            {
                return PlayerModelProfile.Default;
            }
            if (new FileInfo(path).Length > 64 * 1024)
            {
                Log.Warn($"[PlayerModels] the profile {path} is over 64 KB: it is not read");
                return PlayerModelProfile.Default;
            }
            PlayerModelProfile profile = PlayerModelProfile.Parse(File.ReadAllText(path), out IReadOnlyList<string> problems);
            foreach (string problem in problems)
            {
                Log.Warn($"[PlayerModels] profile of '{id}': {problem}");
            }
            return profile;
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the profile of '{id}' could not be read: {exception.Message}");
            return PlayerModelProfile.Default;
        }
    }

    private static void ScanFolder(string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(folder, $"*{PlayerModelRules.FILE_EXTENSION}"))
            {
                string id = PlayerModelRules.IdFromFileName(file);
                if (id == null)
                {
                    Log.Warn($"[PlayerModels] the file {file} cannot be a model: its name must be 1 to {PlayerModelRules.MAX_ID_LENGTH} letters, digits, dots, dashes or underscores");
                    continue;
                }
                // the player's own folder is scanned last and wins (with the profile that lies beside its file, if there is one)
                files[id] = file;
                profiles[id] = ReadProfile(file, id);
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the folder {folder} could not be read: {exception.Message}");
        }
    }
}
