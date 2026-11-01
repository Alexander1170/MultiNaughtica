using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nitrox.Model.Constants;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

/// <summary>
///     One backup of the world per server start, taken before anything is loaded, and kept apart from the rolling backups of <c>SaveService</c> (a new one every 2 minutes, the last 10 kept): a
///     state from before a restart is still there an hour later, when the rolling ones only reach back 20 minutes.
/// </summary>
/// <remarks>
///     The zips are in <c>backups/server-starts/</c> of the world and named <c>ServerStart - yyyy-MM-dd HH.mm.ss.zip</c>, so that neither the pruning of the rolling backups (it only touches names with
///     "Backup - ") nor the restore list of the launcher (which only lists the top folder of <c>backups</c>) sees them. To restore one: stop the server and unpack the zip into the world folder.
/// </remarks>
internal static class ServerStartBackups
{
    internal const string FOLDER_NAME = "server-starts";
    internal const string FILE_PREFIX = "ServerStart - ";
    internal const int KEPT = 5;

    internal static string FileNameFor(DateTime time)
    {
        return $"{FILE_PREFIX}{time.ToString(PersistanceConstants.BACKUP_DATE_TIME_FORMAT, CultureInfo.InvariantCulture)}.zip";
    }

    /// <summary>
    ///     Which of the existing start backups to delete so that <paramref name="keep" /> remain: the oldest ones. A name that is not one of ours is never selected.
    /// </summary>
    /// <param name="fileNames">File names (not paths) in the folder of the start backups.</param>
    internal static List<string> SelectExpired(IEnumerable<string> fileNames, int keep)
    {
        List<(string Name, DateTime Time)> ours = [];
        foreach (string name in fileNames)
        {
            if (TryParseTime(name, out DateTime time))
            {
                ours.Add((name, time));
            }
        }

        return ours.OrderByDescending(entry => entry.Time)
                   .ThenByDescending(entry => entry.Name, StringComparer.Ordinal)
                   .Skip(Math.Max(1, keep))
                   .Select(entry => entry.Name)
                   .ToList();
    }

    internal static bool TryParseTime(string fileName, out DateTime time)
    {
        time = default;
        if (!fileName.StartsWith(FILE_PREFIX, StringComparison.Ordinal) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string text = fileName.Substring(FILE_PREFIX.Length, fileName.Length - FILE_PREFIX.Length - ".zip".Length);
        return DateTime.TryParseExact(text, PersistanceConstants.BACKUP_DATE_TIME_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>
    ///     Zips the files of the world folder (not its subfolders: no backups of backups) into the start backup folder and deletes the oldest start backups beyond <paramref name="keep" />.
    /// </summary>
    /// <returns>The path of the new zip.</returns>
    internal static string CreateAndPrune(string saveDir, string backupsDir, DateTime now, int keep = KEPT)
    {
        string folder = Path.Combine(backupsDir, FOLDER_NAME);
        Directory.CreateDirectory(folder);

        string zipPath = Path.Combine(folder, FileNameFor(now));
        string tempPath = $"{zipPath}.tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (FileStream output = new(tempPath, FileMode.CreateNew, FileAccess.Write))
        using (ZipArchive zip = new(output, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.GetFiles(saveDir))
            {
                // Fastest: this runs while the server starts and the files are mostly JSON
                ZipArchiveEntry entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Fastest);
                using Stream source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using Stream target = entry.Open();
                source.CopyTo(target);
            }
        }
        File.Move(tempPath, zipPath, true);

        List<string> existing = Directory.GetFiles(folder, $"{FILE_PREFIX}*.zip").Select(path => Path.GetFileName(path)).ToList();
        foreach (string expired in SelectExpired(existing, keep))
        {
            File.Delete(Path.Combine(folder, expired));
        }
        return zipPath;
    }
}
