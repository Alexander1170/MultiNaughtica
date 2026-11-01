using System.IO.Compression;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

/// <summary>
///     One backup of the world per server start, kept apart from the rolling backups, the last five.
/// </summary>
[TestClass]
public class ServerStartBackupsTest
{
    private static string NewTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"nitrox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Name(int day) => ServerStartBackups.FileNameFor(new DateTime(2026, 10, day, 22, 13, 2));

    [TestMethod]
    public void TheFileNameCarriesTheStartTimeAndNeverLooksLikeARollingBackup()
    {
        string name = ServerStartBackups.FileNameFor(new DateTime(2026, 10, 24, 22, 18, 36));

        name.Should().Be("ServerStart - 2026-10-24 22.18.36.zip");
        name.Should().NotContain("Backup - ", "the pruning of the rolling backups deletes everything with that in its name, and the launcher lists the files that start with it");
        ServerStartBackups.TryParseTime(name, out DateTime time).Should().BeTrue();
        time.Should().Be(new DateTime(2026, 10, 24, 22, 18, 36));
    }

    [TestMethod]
    public void TheOldestBeyondTheLastFiveExpire()
    {
        List<string> names = Enumerable.Range(1, 8).Select(Name).ToList();

        List<string> expired = ServerStartBackups.SelectExpired(names, 5);

        expired.Should().BeEquivalentTo([Name(1), Name(2), Name(3)]);
    }

    [TestMethod]
    public void NothingExpiresWhileThereAreNoMoreThanFive()
    {
        ServerStartBackups.SelectExpired(Enumerable.Range(1, 5).Select(Name), 5).Should().BeEmpty();
        ServerStartBackups.SelectExpired([], 5).Should().BeEmpty();
    }

    [TestMethod]
    public void NamesThatAreNotOursAreNeverSelected()
    {
        List<string> names = [Name(1), Name(2), "Backup - 2026-10-24 22.13.02.zip", "notes.txt", "ServerStart - not a date.zip", Name(3)];

        ServerStartBackups.SelectExpired(names, 2).Should().BeEquivalentTo([Name(1)]);
    }

    [TestMethod]
    public void AtLeastOneIsAlwaysKept()
    {
        ServerStartBackups.SelectExpired([Name(1), Name(2)], 0).Should().BeEquivalentTo([Name(1)]);
    }

    [TestMethod]
    public void TheOrderOfTheStartsDecidesNotTheOrderOfTheNames()
    {
        // 2026-10-09 before 2026-10-10 even though the day is a shorter text
        string earlier = ServerStartBackups.FileNameFor(new DateTime(2026, 10, 9, 8, 0, 0));
        string later = ServerStartBackups.FileNameFor(new DateTime(2026, 10, 10, 8, 0, 0));

        ServerStartBackups.SelectExpired([later, earlier], 1).Should().Equal(earlier);
    }

    [TestMethod]
    public void ABackupHoldsTheFilesOfTheWorldAndNotItsSubfolders()
    {
        string save = NewTempFolder();
        try
        {
            File.WriteAllText(Path.Combine(save, "Version.json"), "version");
            File.WriteAllText(Path.Combine(save, "EntityData.json"), "entities");
            Directory.CreateDirectory(Path.Combine(save, "backups"));
            File.WriteAllText(Path.Combine(save, "backups", "Backup - 2026-10-24 22.13.02.zip"), "rolling");
            string backups = Path.Combine(save, "backups");

            string zipPath = ServerStartBackups.CreateAndPrune(save, backups, new DateTime(2026, 10, 25, 9, 0, 0));

            zipPath.Should().Be(Path.Combine(backups, ServerStartBackups.FOLDER_NAME, "ServerStart - 2026-10-25 09.00.00.zip"));
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            zip.Entries.Select(entry => entry.FullName).Should().BeEquivalentTo(["Version.json", "EntityData.json"]);
            using StreamReader reader = new(zip.GetEntry("EntityData.json")!.Open());
            reader.ReadToEnd().Should().Be("entities");
            File.Exists(Path.Combine(backups, "Backup - 2026-10-24 22.13.02.zip")).Should().BeTrue("the rolling backups are not touched");
        }
        finally
        {
            Directory.Delete(save, true);
        }
    }

    [TestMethod]
    public void OnlyTheLastFiveStartsRemainAndTheRollingBackupsAreLeftAlone()
    {
        string save = NewTempFolder();
        try
        {
            File.WriteAllText(Path.Combine(save, "Version.json"), "version");
            string backups = Path.Combine(save, "backups");
            Directory.CreateDirectory(backups);
            string rolling = Path.Combine(backups, "Backup - 2026-10-24 22.13.02.zip");
            File.WriteAllText(rolling, "rolling");

            for (int day = 1; day <= 8; day++)
            {
                ServerStartBackups.CreateAndPrune(save, backups, new DateTime(2026, 10, day, 12, 0, 0));
            }

            string[] left = Directory.GetFiles(Path.Combine(backups, ServerStartBackups.FOLDER_NAME)).Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            left.Should().Equal(Enumerable.Range(4, 5).Select(day => ServerStartBackups.FileNameFor(new DateTime(2026, 10, day, 12, 0, 0))));
            File.Exists(rolling).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(save, true);
        }
    }
}
