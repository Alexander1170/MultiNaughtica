using NitroxClient.GameLogic.Settings;

namespace NitroxClient.GameLogic;

/// <summary>
/// Where the mod keeps what the player chose (<see cref="PrefsStore" />): in a live check the players keep it in a file of the run and never in the registry of the user (2026-10-30: a probe that picked a model and
/// put Default back took the real player's choice with it).
/// </summary>
[TestClass]
public class PrefsStoreTest
{
    private static string FindRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Nitrox.slnx")))
            {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.Inconclusive("Nitrox.slnx was not found above the test assembly");
        return "";
    }

    [TestMethod]
    public void TheFileKeepsAnyTextAndLeavesOutALineThatWasCutOff()
    {
        Dictionary<string, string> values = new()
        {
            ["NITROX_PLAYER_PREFS"] = "{\"LastSetPlayerPreference\":{\"PlayerName\":\"Bob\"}}",
            ["Nitrox.playerModel"] = "player-example",
            ["Nitrox.empty"] = "",
            ["Nitrox.lines"] = "one\r\ntwo\nthree=four",
            ["Nitrox.unicode"] = "Nitrox äöü ☃",
        };

        Dictionary<string, string> read = PrefsFileFormat.Parse(PrefsFileFormat.Serialize(values));
        read.Should().BeEquivalentTo(values);

        // the other client was writing the last line when this one read: what was whole is kept, the half line is not an error
        string whole = PrefsFileFormat.Serialize(new Dictionary<string, string> { ["a"] = "1", ["b"] = "two" });
        Dictionary<string, string> withThird = PrefsFileFormat.Parse(whole + "c=dGh5");
        withThird.Should().HaveCount(3);
        withThird["c"].Should().Be("thy");
        PrefsFileFormat.Parse(whole + "d=****").Should().NotContainKey("d");
        PrefsFileFormat.Parse(whole + "no equals sign").Should().HaveCount(2);
        PrefsFileFormat.Parse("").Should().BeEmpty();
        PrefsFileFormat.Parse(null).Should().BeEmpty();
    }

    [TestMethod]
    public void AHarnessRunKeepsEveryPreferenceInItsFileAndNeverReachesTheRegistry()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"nitrox-prefs-test-{Guid.NewGuid():N}");
        string path = Path.Combine(folder, "harness-prefs.txt");
        try
        {
            PrefsStore.UseFileForTests(path);
            PrefsStore.UsesFile.Should().BeTrue();

            // PlayerPrefs cannot be called outside the game (the call throws): that all of this runs is the proof that none of it reached the registry of the user
            PrefsStore.SetInt("Nitrox.test.int", 7);
            PrefsStore.SetFloat("Nitrox.test.float", 0.8333f);
            PrefsStore.SetString("Nitrox.test.string", "player-example");
            PrefsStore.SetString("NITROX_PLAYER_PREFS", "{\"Preferences\":{}}");
            PrefsStore.Save();

            PrefsStore.GetInt("Nitrox.test.int", -1).Should().Be(7);
            PrefsStore.GetFloat("Nitrox.test.float", -1f).Should().BeApproximately(0.8333f, 0.00001f);
            PrefsStore.GetString("Nitrox.test.string").Should().Be("player-example");
            PrefsStore.GetInt("Nitrox.test.missing", 3).Should().Be(3);
            PrefsStore.GetString("Nitrox.test.missing", "none").Should().Be("none");
            File.Exists(path).Should().BeTrue("the preferences of the run are in the file of the run");

            // the other client of the run reads the same file, and what it writes is not lost to the first
            PrefsStore.UseFileForTests(path);
            PrefsStore.GetString("Nitrox.test.string").Should().Be("player-example");
            PrefsStore.SetString("Nitrox.test.other", "bob");
            PrefsStore.UseFileForTests(path);
            PrefsStore.GetString("Nitrox.test.other").Should().Be("bob");
            PrefsStore.GetInt("Nitrox.test.int", -1).Should().Be(7);

            // the options of the mod (a bool that is on unless it is switched off) go through the same file
            NitroxPrefs.ShowOwnModel.Value.Should().BeTrue("the player sees his own model unless he switches it off");
            NitroxPrefs.ShowOwnModel.Value = false;
            NitroxPrefs.ShowOwnModel.Value.Should().BeFalse();
            PrefsStore.UseFileForTests(path);
            NitroxPrefs.ShowOwnModel.Value.Should().BeFalse("it was kept in the file");
        }
        finally
        {
            PrefsStore.UseFileForTests(null);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [TestMethod]
    public void NothingInTheModButThePrefsStoreCallsPlayerPrefs()
    {
        string root = FindRoot();
        List<string> offenders = [];
        foreach (string project in new[] { "NitroxClient", "NitroxPatcher", "NitroxUnity", "Nitrox.Model", "Nitrox.Model.Subnautica" })
        {
            string folder = Path.Combine(root, project);
            if (!Directory.Exists(folder))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file);
                if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || Path.GetFileName(file) == "PrefsStore.cs")
                {
                    continue;
                }
                if (File.ReadAllText(file).Contains("PlayerPrefs."))
                {
                    offenders.Add(relative);
                }
            }
        }
        offenders.Should().BeEmpty("every read and write of the game's PlayerPrefs goes through PrefsStore, which a live check points at a file of the run instead of the registry of the user");
    }
}
