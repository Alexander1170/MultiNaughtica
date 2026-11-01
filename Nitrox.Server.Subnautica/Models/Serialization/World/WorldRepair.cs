using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;
using Nitrox.Server.Subnautica.Models.GameLogic.Players;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

/// <summary>
///     Repairs a world that was saved by coop.3 after a server restart: the record of generated batches that is shorter than the entity data (<see cref="ParsedBatchRepair" />) and the second
///     copies that the batches generated a second time left (<see cref="DuplicateEntityRepair" />). Runs while the world is loaded, before anything is registered, once per world
///     (<c>WorldData.RepairVersion</c>).
/// </summary>
/// <remarks>
///     Safety, in the order it happens: nothing is written unless the repair would change something; a complete copy of the world folder (<c>&lt;save&gt;\backups\before-heal-&lt;time&gt;</c>, inside the
///     world's backups folder, everything but that folder itself) is made and checked first, and when that fails the world is loaded as it is; the changes are made on the loaded data only, so the world files
///     are replaced by the first save, never edited; every removal is logged with its ids. The mode <see cref="ServerStartOptions.HealDryRun" /> does everything but the changes and the copy and
///     writes the report.
/// </remarks>
internal static class WorldRepair
{
    /// <summary>
    ///     The version this build writes into <c>WorldData.RepairVersion</c> once the repair has been through the world. Raise it when the repair learns something new.
    /// </summary>
    internal const int CURRENT_VERSION = 1;

    internal const string BACKUPS_FOLDER = "backups";
    internal const string COPY_NOTE_FILE = "README_BEFORE_REPAIR.txt";

    /// <returns>The repair version the world is at afterwards (the one it came with when nothing was done: dry run, refused or failed copy).</returns>
    /// <param name="seed">The seed of the world (<c>Seed</c> of server.cfg): the ids of a batch come from the generator of that seed. Without it the batches are recognised by the other evidence only.</param>
    /// <param name="reportDir">Where the report is written; the logs folder of Nitrox when null.</param>
    internal static int Run(PersistedWorldData data, string saveDir, bool dryRun, DateTime now, ILogger logger, string? seed = null, string? reportDir = null)
    {
        WorldData worldData = data.WorldData!;
        int storedVersion = worldData.RepairVersion;
        if (!dryRun && storedVersion >= CURRENT_VERSION)
        {
            logger.ZLogInformation($"World repair: this world has been through repair version {storedVersion} already, nothing to do");
            return storedVersion;
        }

        List<Entity> entities = data.EntityData!.Entities;
        HashSet<NitroxId> protectedIds = CollectProtectedIds(data);
        DuplicateEntityRepair.RepairPlan plan = DuplicateEntityRepair.Plan(entities, protectedIds, DuplicateEntityRepair.IsMobileCreature);
        List<Entity> remaining = plan.RemovedIds.Count == 0 ? entities : entities.Where(entity => !plan.RemovedIds.Contains(entity.Id)).ToList();
        List<NitroxInt3> parsedBefore = worldData.ParsedBatchCells ?? [];
        Func<NitroxInt3, IEnumerable<NitroxId>>? generatedIds = null;
        if (!string.IsNullOrEmpty(seed))
        {
            generatedIds = batchId => ParsedBatchRepair.BatchIdStream(seed, batchId);
        }
        ParsedBatchRepair.Derivation batches = ParsedBatchRepair.Derive(remaining, parsedBefore, generatedIds);
        IdCollisionRepair.RepairPlan idPlan = IdCollisionRepair.Plan(data.GlobalRootData?.Entities ?? [], entities, CollectPlayerReferencedIds(data));

        string saveName = Path.GetFileName(saveDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        bool changes = plan.Sets.Count > 0 || batches.Added.Count > 0 || idPlan.Fixes.Count > 0;
        string summary = $"{plan.CopiesToRemove} duplicate entities in {plan.Sets.Count} sets ({plan.DescendantsToRemove} more below them), {batches.Added.Count} batches missing from the record of generated batches ({parsedBefore.Count} listed), {idPlan.Fixes.Count} items in containers that share an id with another entity, {plan.Review.Count} sets left for review, {plan.TwinsLeftAlone} identical decor copies left alone";

        if (dryRun)
        {
            string path = WriteReport(saveName, true, FormatReport(saveName, true, now, entities.Count, plan, batches, parsedBefore.Count, idPlan, null, null), now, logger, reportDir);
            logger.ZLogInformation($"World repair DRY RUN, nothing was changed: it would repair {summary}. Report: {path}");
            return storedVersion;
        }

        if (!changes)
        {
            logger.ZLogInformation($"World repair: nothing to repair ({summary}). The world is marked as repaired.");
            if (plan.Review.Count > 0 || idPlan.Skips.Count > 0)
            {
                WriteReport(saveName, false, FormatReport(saveName, false, now, entities.Count, plan, batches, parsedBefore.Count, idPlan, null, null), now, logger, reportDir);
            }
            return CURRENT_VERSION;
        }

        string copyPath = BeforeRepairFolder(saveDir, now);
        try
        {
            CopyWorldFolder(saveDir, copyPath);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"World repair REFUSED: the full copy of the world to {copyPath} failed, so the world is loaded exactly as it was saved. It would have repaired {summary}");
            return storedVersion;
        }
        logger.ZLogInformation($"World repair: a full copy of the world (before any change) is in {copyPath}");
        WriteCopyNote(copyPath, saveName);

        foreach (DuplicateEntityRepair.TwinSet set in plan.Sets)
        {
            foreach (NitroxId removedId in set.RemovedIds)
            {
                logger.ZLogInformation($"World repair: removed duplicate {set.Kind} {removedId} (tech type {set.TechType}, class {set.ClassId}, at {set.Position}), kept {set.KeptId}: {set.Reason}");
            }
            if (set.RemovedDescendants > 0)
            {
                logger.ZLogInformation($"World repair: removed {set.RemovedDescendants} entities below the removed copies of {set.KeptId} with them (the report lists the copies)");
            }
        }
        foreach (DuplicateEntityRepair.ReviewSet review in plan.Review)
        {
            string copies = string.Join(", ", review.Ids);
            logger.ZLogWarning($"World repair: left {review.Kind} copies {copies} (tech type {review.TechType}, class {review.ClassId}, at {review.Position}) for review: {review.Reason}");
        }
        if (batches.Added.Count > 0)
        {
            string added = string.Join(", ", batches.Added);
            logger.ZLogInformation($"World repair: added {batches.Added.Count} batches to the record of generated batches: {added}");
        }

        entities.RemoveAll(entity => plan.RemovedIds.Contains(entity.Id));
        worldData.ParsedBatchCells = batches.All;

        List<NitroxId> newIds = IdCollisionRepair.Apply(idPlan);
        for (int i = 0; i < idPlan.Fixes.Count; i++)
        {
            IdCollisionRepair.Fix fix = idPlan.Fixes[i];
            NitroxId newId = newIds[i];
            logger.ZLogInformation($"World repair: gave the {fix.TechType} in {fix.ParentId} the new id {newId} (it was {fix.OldId}, which the {fix.KeptBy} keeps)");
        }
        foreach (IdCollisionRepair.Skipped skipped in idPlan.Skips)
        {
            logger.ZLogWarning($"World repair: left the id {skipped.Id} as it is: {skipped.Reason}");
        }

        string report = WriteReport(saveName, false, FormatReport(saveName, false, now, entities.Count + plan.RemovedIds.Count, plan, batches, parsedBefore.Count, idPlan, newIds, copyPath), now, logger, reportDir);
        logger.ZLogInformation($"World repair done: repaired {summary}. Report: {report}");
        return CURRENT_VERSION;
    }

    /// <summary>
    ///     What a player, a base or a vehicle owns or points at, and so is never taken out: the whole global root tree (bases, vehicles, the escape pod, players' bodies and what they carry) and
    ///     the ids in the data of the players.
    /// </summary>
    internal static HashSet<NitroxId> CollectProtectedIds(PersistedWorldData data)
    {
        HashSet<NitroxId> ids = CollectPlayerReferencedIds(data);
        if (data.GlobalRootData != null)
        {
            Stack<Entity> pending = new();
            foreach (GlobalRootEntity root in data.GlobalRootData.Entities)
            {
                pending.Push(root);
            }
            while (pending.Count > 0)
            {
                Entity entity = pending.Pop();
                if (!ids.Add(entity.Id))
                {
                    continue;
                }
                if (entity.ParentId != null)
                {
                    ids.Add(entity.ParentId);
                }
                foreach (Entity child in entity.ChildEntities)
                {
                    pending.Push(child);
                }
            }
        }
        return ids;
    }

    /// <summary>
    ///     The ids that the data of the players point at: the body of a player, the base or Cyclops the player is in, the equipped items and the items on the quick slots.
    /// </summary>
    internal static HashSet<NitroxId> CollectPlayerReferencedIds(PersistedWorldData data)
    {
        HashSet<NitroxId> ids = [];
        if (data.PlayerData != null)
        {
            foreach (PersistedPlayerData player in data.PlayerData.Players)
            {
                AddIfSet(ids, player.NitroxId);
                AddIfSet(ids, player.SubRootId);
                if (player.EquippedItems != null)
                {
                    foreach (NitroxId equipped in player.EquippedItems.Values)
                    {
                        AddIfSet(ids, equipped);
                    }
                }
                if (player.QuickSlotsBindingIds != null)
                {
                    foreach (Optional<NitroxId> binding in player.QuickSlotsBindingIds)
                    {
                        if (binding.HasValue)
                        {
                            AddIfSet(ids, binding.Value);
                        }
                    }
                }
            }
        }
        return ids;

        static void AddIfSet(HashSet<NitroxId> target, NitroxId? id)
        {
            if (id != null)
            {
                target.Add(id);
            }
        }
    }

    /// <summary>
    ///     A note in the copy says what it is (it sits in the backups folder of the world, which the launcher does not list as a world). Best effort.
    /// </summary>
    private static void WriteCopyNote(string copyPath, string saveName)
    {
        try
        {
            string note = $"This is the complete copy of the world \"{saveName}\" that the server made before it repaired the world.{Environment.NewLine}It is not a second world: do not start it. To go back, stop the server and copy these files over the files of the world folder.{Environment.NewLine}";
            File.WriteAllText(Path.Combine(copyPath, COPY_NOTE_FILE), note, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // the copy is complete without the note
        }
    }

    /// <summary>
    ///     The folder of the copy: <c>&lt;save&gt;\backups\before-heal-&lt;time&gt;</c>, INSIDE the world's own backups folder. A copy next to the world was listed by the launcher as a second world (it lists
    ///     every folder of the saves folder), which the user could have started; the launcher's list of backups to restore reads only the files at the top of <c>backups</c> and the rotation of the
    ///     rolling backups only touches <c>Backup - *.zip</c>, so a subfolder is left alone by both.
    /// </summary>
    internal static string BeforeRepairFolder(string saveDir, DateTime now)
    {
        return Path.Combine(saveDir, BACKUPS_FOLDER, $"before-heal-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    ///     Copies the world folder to a new folder, everything but the rolling <c>backups</c> folder at its top, and checks that every file arrived with the size it has. Never merges into an
    ///     existing folder.
    /// </summary>
    /// <exception cref="IOException">When the target exists, a copy fails or the check finds a file missing or of another size.</exception>
    internal static void CopyWorldFolder(string sourceDir, string targetDir)
    {
        if (Directory.Exists(targetDir) || File.Exists(targetDir))
        {
            throw new IOException($"'{targetDir}' exists already: a copy is never merged into another folder");
        }

        CopyDirectory(sourceDir, targetDir, true);

        List<string> problems = [];
        foreach (string file in EnumerateFiles(sourceDir, true))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            FileInfo copy = new(Path.Combine(targetDir, relative));
            if (!copy.Exists || copy.Length != new FileInfo(file).Length)
            {
                problems.Add(relative);
            }
        }
        if (problems.Count > 0)
        {
            throw new IOException($"The copy of the world is incomplete: {string.Join(", ", problems.Take(10))}");
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool isRoot)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            if (isRoot && string.Equals(Path.GetFileName(directory), BACKUPS_FOLDER, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            CopyDirectory(directory, Path.Combine(targetDir, Path.GetFileName(directory)), false);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string directory, bool isRoot)
    {
        foreach (string file in Directory.GetFiles(directory))
        {
            yield return file;
        }
        foreach (string child in Directory.GetDirectories(directory))
        {
            if (isRoot && string.Equals(Path.GetFileName(child), BACKUPS_FOLDER, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (string file in EnumerateFiles(child, false))
            {
                yield return file;
            }
        }
    }

    internal static string FormatReport(string saveName, bool dryRun, DateTime now, int entityCount, DuplicateEntityRepair.RepairPlan plan, ParsedBatchRepair.Derivation batches, int parsedListed, IdCollisionRepair.RepairPlan idPlan, List<NitroxId>? newIds, string? copyPath)
    {
        StringBuilder text = new();
        text.AppendLine(dryRun
                            ? $"World repair report (DRY RUN: nothing was changed) for the world \"{saveName}\", {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"
                            : $"World repair report (applied) for the world \"{saveName}\", {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        if (copyPath != null)
        {
            text.AppendLine($"Full copy of the world before the repair: {copyPath}");
        }
        text.AppendLine($"Entities in the entity data: {entityCount}");
        text.AppendLine();
        text.AppendLine($"1. Duplicate top-level entities: {plan.CopiesToRemove} copies in {plan.Sets.Count} sets are removed ({plan.DescendantsToRemove} entities below them go with them)");
        foreach (DuplicateEntityRepair.TwinSet set in plan.Sets)
        {
            text.AppendLine($"  {set.Kind} tech type {set.TechType} class {set.ClassId} at {set.Position}");
            text.AppendLine($"    KEEP   {set.KeptId}");
            foreach (NitroxId removed in set.RemovedIds)
            {
                text.AppendLine($"    REMOVE {removed}");
            }
            text.AppendLine($"    because {set.Reason}");
        }
        text.AppendLine();
        text.AppendLine($"2. Sets left for review, nothing is removed from them: {plan.Review.Count}");
        foreach (DuplicateEntityRepair.ReviewSet review in plan.Review)
        {
            text.AppendLine($"  {review.Kind} tech type {review.TechType} class {review.ClassId} at {review.Position}");
            text.AppendLine($"    copies {string.Join(", ", review.Ids)}");
            text.AppendLine($"    because {review.Reason}");
        }
        text.AppendLine();
        text.AppendLine($"3. Identical decor copies (SerializedWorldEntity: the baked game data holds a few objects twice, so a pair says nothing) left alone: {plan.TwinsLeftAlone}");
        text.AppendLine();
        text.AppendLine($"4. Record of generated batches: {parsedListed} listed in the save, {batches.Added.Count} added because the entities prove they were generated: {string.Join(", ", batches.Added)}");
        text.AppendLine();
        text.AppendLine();
        text.AppendLine($"5. Id fixes: {idPlan.Fixes.Count} items in containers share an id with a different entity and get a new id (nothing is deleted)");
        for (int i = 0; i < idPlan.Fixes.Count; i++)
        {
            IdCollisionRepair.Fix fix = idPlan.Fixes[i];
            string newId = newIds != null && i < newIds.Count ? newIds[i].ToString() : "(a new id when it is applied)";
            text.AppendLine($"  {fix.TechType} in {fix.ParentId}: {fix.OldId} becomes {newId}; the id stays with the {fix.KeptBy}");
        }
        text.AppendLine($"   Left as they are: {idPlan.Skips.Count}");
        foreach (IdCollisionRepair.Skipped skipped in idPlan.Skips)
        {
            text.AppendLine($"  {skipped.Id}: {skipped.Reason}");
        }
        text.AppendLine("   (The entity list of a save holds an id once, the loader refuses a file with an id twice; the clashes of a second generation hide inside the child lists of the global root data.)");
        return text.ToString();
    }

    private static string WriteReport(string saveName, bool dryRun, string report, DateTime now, ILogger logger, string? reportDir)
    {
        string directory = reportDir ?? NitroxDirectory.LogsPath;
        string path = Path.Combine(directory, $"world-repair-{saveName}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{(dryRun ? "-dryrun" : "")}.txt");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, report, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"The report of the world repair could not be written to {path}");
            return "(not written)";
        }
        return path;
    }
}
