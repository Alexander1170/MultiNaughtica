using Nitrox.Model.Configuration;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace Nitrox.Model.Subnautica;

/// <summary>
/// The rules of the quick ping (docs/coop/QUICK_PING.md): what a ping says, how long it lives, who may send one and how often, and the server option that sets the lifetime.
/// </summary>
[TestClass]
public class QuickPingRulesTest
{
    [TestMethod]
    public void ACreatureNameWinsOverAThingNameAndAnEmptyPingSaysLookHere()
    {
        QuickPingRules.TargetLabel("Reaper Leviathan", "Limestone Outcrop").Should().Be("Reaper Leviathan");
        QuickPingRules.TargetLabel(null, "Limestone Outcrop").Should().Be("Limestone Outcrop");
        QuickPingRules.TargetLabel("", "Limestone Outcrop").Should().Be("Limestone Outcrop");
        QuickPingRules.TargetLabel(null, null).Should().Be(QuickPingRules.LOOK_HERE);
        QuickPingRules.TargetLabel("  ", "​\u0007").Should().Be("Look here", "a name of nothing but blanks and control characters counts as none");
    }

    [TestMethod]
    public void ANameIsCleanedAndCutToWhatTheHudCanDraw()
    {
        QuickPingRules.TargetLabel("<size=99><color=red>Reaper</color></size>", null).Should().Be("size=99color=redReaper/c", "no angle brackets get through, so no tag can resize or recolour the line; the rest is cut at 24 characters");
        QuickPingRules.TargetLabel("Reaper\r\nLeviathan\t  Kid", null).Should().Be("Reaper Leviathan Kid");
        QuickPingRules.TargetLabel(new string('x', 200), null).Length.Should().BeLessThanOrEqualTo(CyclopsNavRules.MAX_LABEL_LENGTH);
    }

    [TestMethod]
    public void TheMarkerTextIsThePlayerAndWhatTheyLookedAt()
    {
        QuickPingRules.MarkerText("Bob", "Reaper Leviathan").Should().Be("Bob: Reaper Leviathan");
        QuickPingRules.MarkerText("Bob", null).Should().Be("Bob: Look here");
        QuickPingRules.MarkerText(null, "Peeper").Should().Be("Peeper");
        QuickPingRules.MarkerText("", "").Should().Be("Look here");
        QuickPingRules.MarkerText("<b>Bob</b>", "Peeper").Should().Be("bBob/b: Peeper", "a name is cleaned like the target");
    }

    [TestMethod]
    public void TheLifetimeStaysInsideTheLimitsAndNoNumberIsTheDefault()
    {
        QuickPingRules.Lifetime(20f).Should().Be(20f);
        QuickPingRules.Lifetime(0f).Should().Be(QuickPingRules.MIN_SECONDS);
        QuickPingRules.Lifetime(-3f).Should().Be(QuickPingRules.MIN_SECONDS);
        QuickPingRules.Lifetime(9999f).Should().Be(QuickPingRules.MAX_SECONDS);
        QuickPingRules.Lifetime(float.NaN).Should().Be(QuickPingRules.DEFAULT_SECONDS);
        QuickPingRules.Lifetime(float.PositiveInfinity).Should().Be(QuickPingRules.DEFAULT_SECONDS);
        QuickPingRules.MIN_SECONDS.Should().Be(5f);
        QuickPingRules.MAX_SECONDS.Should().Be(120f);
        QuickPingRules.DEFAULT_SECONDS.Should().Be(20f);
    }

    [TestMethod]
    public void OnlyAPlaceInTheWorldCanBePinged()
    {
        QuickPingRules.IsValidPosition(-80f, -6f, 355f).Should().BeTrue();
        QuickPingRules.IsValidPosition(float.NaN, 0f, 0f).Should().BeFalse();
        QuickPingRules.IsValidPosition(0f, float.PositiveInfinity, 0f).Should().BeFalse();
        QuickPingRules.IsValidPosition(1e9f, 0f, 0f).Should().BeFalse();
        QuickPingRules.IsValidPosition(0f, -9000f, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void TheRayCountsFromHalfAMetreUpToThreeHundred()
    {
        QuickPingRules.AIM_RANGE.Should().Be(300f);
        QuickPingRules.IsAimDistance(0.2f).Should().BeFalse("that is the player's own helmet or tool");
        QuickPingRules.IsAimDistance(0.5f).Should().BeTrue();
        QuickPingRules.IsAimDistance(300f).Should().BeTrue();
        QuickPingRules.IsAimDistance(300.1f).Should().BeFalse();
        QuickPingRules.IsAimDistance(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void AClientPingsOnceInTwoSecondsAndTheServerAllowsALittleLess()
    {
        QuickPingRules.ClientMayPing(float.PositiveInfinity).Should().BeTrue("the first ping of a session");
        QuickPingRules.ClientMayPing(0.5f).Should().BeFalse();
        QuickPingRules.ClientMayPing(1.99f).Should().BeFalse();
        QuickPingRules.ClientMayPing(2f).Should().BeTrue();
        QuickPingRules.ClientMayPing(-5f).Should().BeTrue("the clock restarted");
        QuickPingRules.SERVER_INTERVAL_SECONDS.Should().BeLessThan(QuickPingRules.CLIENT_INTERVAL_SECONDS, "a ping that a client let through is never turned down by the server");
    }

    [TestMethod]
    public void TheLimiterAcceptsOneInTheIntervalPerSender()
    {
        PerSenderRateLimiter<int> limiter = new(2.0);

        limiter.TryAccept(1, 100.0).Should().BeTrue();
        limiter.TryAccept(1, 100.5).Should().BeFalse();
        limiter.TryAccept(1, 101.99).Should().BeFalse();
        limiter.TryAccept(1, 102.0).Should().BeTrue();
        // a refused ping does not push the next one away
        limiter.TryAccept(1, 103.0).Should().BeFalse();
        limiter.TryAccept(1, 104.0).Should().BeTrue();
    }

    [TestMethod]
    public void TheLimitOfOneSenderDoesNotHoldBackAnother()
    {
        PerSenderRateLimiter<int> limiter = new(2.0);

        limiter.TryAccept(1, 100.0).Should().BeTrue();
        limiter.TryAccept(2, 100.1).Should().BeTrue();
        limiter.TryAccept(1, 100.2).Should().BeFalse();
        limiter.TryAccept(2, 100.3).Should().BeFalse();
    }

    [TestMethod]
    public void AClockThatWentBackwardsNeverShutsASenderOut()
    {
        PerSenderRateLimiter<int> limiter = new(2.0);

        limiter.TryAccept(1, 5000.0).Should().BeTrue();
        limiter.TryAccept(1, 10.0).Should().BeTrue("the steady clock restarted");
        limiter.TryAccept(1, 10.5).Should().BeFalse();
    }

    [TestMethod]
    public void TheLimiterForgetsSendersWhoAreLongGone()
    {
        PerSenderRateLimiter<int> limiter = new(2.0);
        for (int sender = 0; sender < 100; sender++)
        {
            limiter.TryAccept(sender, 1.0);
        }

        limiter.TryAccept(1000, 500.0).Should().BeTrue();

        limiter.Count.Should().BeLessThan(5, "the senders of a time long ago are pruned once the table is big");
        limiter.Forget(1000);
        limiter.TryAccept(1000, 500.1).Should().BeTrue("a sender who left is forgotten");
    }

    [TestMethod]
    public void TheOptionIsClampedWhenAPlayerJoinsAndSaysSo()
    {
        SessionSettings told = new();
        List<string> messages = [];

        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { QuickPingSeconds = 500f }, messages);
        told.QuickPingSeconds.Should().Be(QuickPingRules.MAX_SECONDS);
        messages.Should().ContainSingle().Which.Should().Contain("QuickPingSeconds").And.Contain("above 120");

        messages.Clear();
        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { QuickPingSeconds = 1f }, messages);
        told.QuickPingSeconds.Should().Be(QuickPingRules.MIN_SECONDS);
        messages.Should().ContainSingle().Which.Should().Contain("below 5");

        messages.Clear();
        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { QuickPingSeconds = float.NaN }, messages);
        told.QuickPingSeconds.Should().Be(QuickPingRules.DEFAULT_SECONDS);
        messages.Should().ContainSingle().Which.Should().Contain("not a number");

        messages.Clear();
        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { QuickPingSeconds = 45f }, messages);
        told.QuickPingSeconds.Should().Be(45f);
        messages.Should().BeEmpty();
        new SubnauticaServerOptions().QuickPingSeconds.Should().Be(QuickPingRules.DEFAULT_SECONDS);
    }

    [TestMethod]
    public void TheClientTakesTheOptionAndClampsItAgain()
    {
        try
        {
            VehicleUpgradeSettings.Apply(new SessionSettings { QuickPingSeconds = 45f });
            VehicleUpgradeSettings.QuickPingSeconds.Should().Be(45f);

            VehicleUpgradeSettings.Apply(new SessionSettings { QuickPingSeconds = 99999f });
            VehicleUpgradeSettings.QuickPingSeconds.Should().Be(QuickPingRules.MAX_SECONDS);

            VehicleUpgradeSettings.Apply(new SessionSettings { QuickPingSeconds = float.NaN });
            VehicleUpgradeSettings.QuickPingSeconds.Should().Be(QuickPingRules.DEFAULT_SECONDS);
        }
        finally
        {
            VehicleUpgradeSettings.Reset();
        }
        VehicleUpgradeSettings.QuickPingSeconds.Should().Be(QuickPingRules.DEFAULT_SECONDS);
    }

    [TestMethod]
    public void ThePingDoesNotDependOnTheVehicleUpgradeSwitch()
    {
        SessionSettings told = new();

        VehicleUpgradeSettingsRules.Apply(told, new SubnauticaServerOptions { VehicleUpgrades = false, QuickPingSeconds = 33f });

        told.VehicleUpgrades.Should().BeFalse();
        told.QuickPingSeconds.Should().Be(33f);
    }
}
