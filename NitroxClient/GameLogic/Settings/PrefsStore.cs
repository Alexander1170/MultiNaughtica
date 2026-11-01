using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Nitrox.Model.Core;
using UnityEngine;

namespace NitroxClient.GameLogic.Settings;

/// <summary>
/// The lines of the file that stands in for the registry when a live check runs (<see cref="PrefsStore" />): <c>key=value</c>, with the value as the base64 of its UTF-8 text, so that it may hold anything (the JSON of
/// the player preferences has quotes, and a text may have line breaks). A line that is not complete (the other client was writing it) is left out.
/// </summary>
public static class PrefsFileFormat
{
    public static string Serialize(IReadOnlyDictionary<string, string> values)
    {
        StringBuilder text = new();
        foreach (KeyValuePair<string, string> pair in values)
        {
            text.Append(pair.Key).Append('=').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value ?? string.Empty))).Append('\n');
        }
        return text.ToString();
    }

    public static Dictionary<string, string> Parse(string text)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return values;
        }
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            int at = trimmed.IndexOf('=');
            if (at <= 0)
            {
                continue;
            }
            try
            {
                values[trimmed.Substring(0, at)] = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed.Substring(at + 1)));
            }
            catch (FormatException)
            {
                // a line that was cut off: nothing is lost that was ever saved whole
            }
        }
        return values;
    }
}

/// <summary>
/// Where the mod keeps what the player chose: the game's PlayerPrefs, which on Windows are the registry of the user (the custom model, the Nitrox options, the helm map and the hologram radius, the name and colour of
/// the join screen). Every read and write of the mod goes through here. A <b>Debug</b> build that was started by the live check harness (<c>NITROX_HARNESS_PREFS=&lt;file&gt;</c>, which <c>run_two_client.ps1</c> sets for
/// every run) keeps them in that file instead, which belongs to the run: the players of a live run (Alice and Bob) never read or change what the real player has chosen (a probe that picked a model and put "Default"
/// back at its end took the choice of the real player with it, 2026-10-30). The file is shared by the clients of the run, like the registry was, and it is gone with the run folder. A Release build never looks at the
/// variable. <c>PrefsStoreTest</c> checks that nothing else in the mod calls <c>PlayerPrefs</c>.
/// </summary>
public static class PrefsStore
{
    public const string ENV_NAME = "NITROX_HARNESS_PREFS";

    private static readonly object gate = new();
    private static string harnessFile = null;
    private static bool resolved;
    private static Dictionary<string, string> cache = new(StringComparer.Ordinal);
    private static DateTime cacheStamp = DateTime.MinValue;

    /// <summary>The file that stands in for the registry; null when the real PlayerPrefs are used (always, in a Release build).</summary>
    public static string HarnessFile
    {
        get
        {
            if (!resolved)
            {
                resolved = true;
#if DEBUG
                string path = Environment.GetEnvironmentVariable(ENV_NAME);
                harnessFile = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
#endif
            }
            return harnessFile;
        }
    }

    /// <summary>Whether the preferences are kept in the harness file and not in the registry.</summary>
    public static bool UsesFile => HarnessFile != null;

    /// <summary>
    /// Says in the log, when the player has joined (the first read of a preference comes before the logger is up), where the preferences are kept, if it is a file: the runner of the live checks asks the log of
    /// both clients for this line (<c>Test-HarnessPrefs</c>) before a probe picks a model.
    /// </summary>
    public static void ReportUse()
    {
        if (UsesFile)
        {
            Log.Info($"[Prefs] harness preferences file in use: {HarnessFile} (the registry of the player is not read or written)");
        }
    }

    /// <summary>The tests (and nothing else) point the store at a file, or back at the registry with null.</summary>
    internal static void UseFileForTests(string path)
    {
        lock (gate)
        {
            harnessFile = path;
            resolved = true;
            cache = new Dictionary<string, string>(StringComparer.Ordinal);
            cacheStamp = DateTime.MinValue;
        }
    }

    public static int GetInt(string key, int defaultValue)
    {
        if (!UsesFile)
        {
            return PlayerPrefs.GetInt(key, defaultValue);
        }
        return TryRead(key, out string text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : defaultValue;
    }

    public static void SetInt(string key, int value)
    {
        if (!UsesFile)
        {
            PlayerPrefs.SetInt(key, value);
            return;
        }
        Write(key, value.ToString(CultureInfo.InvariantCulture));
    }

    public static float GetFloat(string key, float defaultValue)
    {
        if (!UsesFile)
        {
            return PlayerPrefs.GetFloat(key, defaultValue);
        }
        return TryRead(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : defaultValue;
    }

    public static void SetFloat(string key, float value)
    {
        if (!UsesFile)
        {
            PlayerPrefs.SetFloat(key, value);
            return;
        }
        Write(key, value.ToString("R", CultureInfo.InvariantCulture));
    }

    public static string GetString(string key, string defaultValue = "")
    {
        if (!UsesFile)
        {
            return PlayerPrefs.GetString(key, defaultValue);
        }
        return TryRead(key, out string text) ? text : defaultValue;
    }

    public static void SetString(string key, string value)
    {
        if (!UsesFile)
        {
            PlayerPrefs.SetString(key, value);
            return;
        }
        Write(key, value ?? string.Empty);
    }

    /// <summary>Writes what was set to disk: the registry's own save, or nothing for the file (every set of the file is written at once).</summary>
    public static void Save()
    {
        if (!UsesFile)
        {
            PlayerPrefs.Save();
        }
    }

    private static bool TryRead(string key, out string value)
    {
        lock (gate)
        {
            Refresh();
            return cache.TryGetValue(key, out value);
        }
    }

    /// <summary>Reads the file again when it changed since it was read (the other client of the run may have written it).</summary>
    private static void Refresh()
    {
        string path = harnessFile;
        try
        {
            if (!File.Exists(path))
            {
                cache = new Dictionary<string, string>(StringComparer.Ordinal);
                cacheStamp = DateTime.MinValue;
                return;
            }
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == cacheStamp)
            {
                return;
            }
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(stream, Encoding.UTF8);
            cache = PrefsFileFormat.Parse(reader.ReadToEnd());
            cacheStamp = stamp;
        }
        catch (IOException)
        {
            // the other client is writing it just now: what is known stays, the next read tries again
        }
    }

    private static void Write(string key, string value)
    {
        lock (gate)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    // what the other client wrote since is kept: the file is read again first
                    cacheStamp = DateTime.MinValue;
                    Refresh();
                    cache[key] = value;
                    string directory = Path.GetDirectoryName(harnessFile);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(PrefsFileFormat.Serialize(cache));
                    using FileStream stream = new(harnessFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                    stream.Write(bytes, 0, bytes.Length);
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }
    }
}
