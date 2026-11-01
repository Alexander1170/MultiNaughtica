using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic;

[TestClass]
public class RecentModuleInstallsTest
{
    private sealed class FakeClock
    {
        public float Now { get; set; }

        public float Read() => Now;
    }

    [TestMethod]
    public void IsRecent_InsideTheWindow_IsTrue_AfterTheWindow_IsFalse()
    {
        FakeClock clock = new();
        RecentModuleInstalls installs = new(clock.Read);
        NitroxId module = new();

        installs.Add(module);
        installs.IsRecent(module).Should().BeTrue();

        clock.Now = RecentModuleInstalls.WINDOW_SECONDS - 0.1f;
        installs.IsRecent(module).Should().BeTrue();

        clock.Now = RecentModuleInstalls.WINDOW_SECONDS;
        installs.IsRecent(module).Should().BeFalse("the window is half open: ten seconds after the install it is over");

        clock.Now = 3600f;
        installs.IsRecent(module).Should().BeFalse();
    }

    [TestMethod]
    public void IsRecent_UnknownOrMissingId_IsFalse()
    {
        RecentModuleInstalls installs = new(() => 0f);
        installs.Add(new NitroxId());

        installs.IsRecent(new NitroxId()).Should().BeFalse("a module this client did not install (prefab, join snapshot) is never taken out");
        installs.IsRecent(null).Should().BeFalse();
    }

    [TestMethod]
    public void Add_InstallingAgain_RefreshesTheTime()
    {
        FakeClock clock = new();
        RecentModuleInstalls installs = new(clock.Read);
        NitroxId module = new();
        installs.Add(module);

        clock.Now = 8f;
        installs.Add(module);
        clock.Now = 15f;

        installs.IsRecent(module).Should().BeTrue("seven seconds after the second install");
        installs.Count.Should().Be(1);
    }

    [TestMethod]
    public void Forget_ClearsTheModule_AndIgnoresUnknownAndMissingIds()
    {
        RecentModuleInstalls installs = new(() => 0f);
        NitroxId module = new();
        NitroxId other = new();
        installs.Add(module);
        installs.Add(other);

        installs.Forget(module);
        installs.Forget(new NitroxId());
        installs.Forget(null);

        installs.IsRecent(module).Should().BeFalse("it left its slot");
        installs.IsRecent(other).Should().BeTrue();
        installs.Count.Should().Be(1);
    }

    [TestMethod]
    public void Add_MissingId_IsIgnored()
    {
        RecentModuleInstalls installs = new(() => 0f);

        installs.Add(null);

        installs.Count.Should().Be(0);
    }

    [TestMethod]
    public void Add_ManyModulesWithAMovingClock_StaysBounded()
    {
        FakeClock clock = new();
        RecentModuleInstalls installs = new(clock.Read);

        for (int i = 0; i < 5000; i++)
        {
            installs.Add(new NitroxId());
            clock.Now += 1f;
        }

        installs.Count.Should().BeLessThan(100);
    }

    [TestMethod]
    public void Add_TrimDropsExpiredModulesAndKeepsRecentOnes()
    {
        FakeClock clock = new();
        RecentModuleInstalls installs = new(clock.Read);
        List<NitroxId> old = [];
        for (int i = 0; i < 100; i++)
        {
            NitroxId id = new();
            old.Add(id);
            installs.Add(id);
        }
        installs.Count.Should().Be(100, "everything is inside the window, nothing can be dropped");

        clock.Now = 9f;
        NitroxId recent = new();
        installs.Add(recent);
        old.Should().OnlyContain(id => installs.IsRecent(id));

        clock.Now = 11f;
        NitroxId latest = new();
        installs.Add(latest);

        installs.Count.Should().Be(2, "the hundred that expired were trimmed");
        installs.IsRecent(recent).Should().BeTrue();
        installs.IsRecent(latest).Should().BeTrue();
        old.Should().OnlyContain(id => !installs.IsRecent(id));
    }
}
