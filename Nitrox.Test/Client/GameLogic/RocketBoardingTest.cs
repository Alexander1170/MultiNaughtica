using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic;

[TestClass]
public class RocketBoardingTest
{
    private static readonly NitroxVector3 cockpit = new(10f, 20f, 30f);

    [TestMethod]
    public void IsAboard_InsideTheSphere_ReturnsTrue()
    {
        RocketBoarding.IsAboard(new NitroxVector3(12f, 20f, 30f), false, false, cockpit).Should().BeTrue();
    }

    [TestMethod]
    public void IsAboard_AtTheEdge_ReturnsTrue()
    {
        RocketBoarding.IsAboard(new NitroxVector3(10f + RocketBoarding.COCKPIT_RADIUS, 20f, 30f), false, false, cockpit).Should().BeTrue();
    }

    [TestMethod]
    public void IsAboard_JustOutsideTheSphere_ReturnsFalse()
    {
        RocketBoarding.IsAboard(new NitroxVector3(10f + RocketBoarding.COCKPIT_RADIUS + 0.2f, 20f, 30f), false, false, cockpit).Should().BeFalse();
    }

    [TestMethod]
    public void IsAboard_JustOutsideTheSphereWithRemoteSlack_ReturnsTrue()
    {
        // A remote player's position is a moment old: one meter more is allowed for them
        NitroxVector3 position = new(10f + RocketBoarding.COCKPIT_RADIUS + 0.5f, 20f, 30f);

        RocketBoarding.IsAboard(position, false, false, cockpit, RocketBoarding.REMOTE_SLACK).Should().BeTrue();
        RocketBoarding.IsAboard(position, false, false, cockpit).Should().BeFalse();
    }

    [TestMethod]
    public void IsAboard_FarAwayEvenWithSlack_ReturnsFalse()
    {
        RocketBoarding.IsAboard(new NitroxVector3(40f, 20f, 30f), false, false, cockpit, RocketBoarding.REMOTE_SLACK).Should().BeFalse();
    }

    [TestMethod]
    public void IsAboardForReceivedLaunch_AcceptsEveryoneTheLauncherCountedAboard()
    {
        // The launcher lets a remote player pass up to the radius plus the slack on the position he saw; the same player, a round trip further out, must pass on his own client
        for (float distance = 0f; distance <= RocketBoarding.COCKPIT_RADIUS + RocketBoarding.REMOTE_SLACK; distance += 0.05f)
        {
            NitroxVector3 position = new(10f + distance, 20f, 30f);
            if (RocketBoarding.IsAboard(position, false, false, cockpit, RocketBoarding.REMOTE_SLACK))
            {
                RocketBoarding.IsAboardForReceivedLaunch(position, false, false, cockpit).Should().BeTrue($"at {distance} m");
            }
        }
    }

    [TestMethod]
    public void IsAboardForReceivedLaunch_AcceptsAPlayerWhoMovedOneMoreMeterThanTheLauncherSaw()
    {
        NitroxVector3 position = new(10f + RocketBoarding.COCKPIT_RADIUS + RocketBoarding.REMOTE_SLACK + 0.9f, 20f, 30f);

        RocketBoarding.IsAboardForReceivedLaunch(position, false, false, cockpit).Should().BeTrue();
    }

    [TestMethod]
    public void IsAboardForReceivedLaunch_StillRefusesUnderwaterInASubAndFarAway()
    {
        RocketBoarding.IsAboardForReceivedLaunch(cockpit, true, false, cockpit).Should().BeFalse();
        RocketBoarding.IsAboardForReceivedLaunch(cockpit, false, true, cockpit).Should().BeFalse();
        RocketBoarding.IsAboardForReceivedLaunch(new NitroxVector3(40f, 20f, 30f), false, false, cockpit).Should().BeFalse();
    }

    [TestMethod]
    public void IsAboard_UnderwaterInsideTheSphere_ReturnsFalse()
    {
        RocketBoarding.IsAboard(cockpit, true, false, cockpit).Should().BeFalse();
    }

    [TestMethod]
    public void IsAboard_InASubInsideTheSphere_ReturnsFalse()
    {
        RocketBoarding.IsAboard(cockpit, false, true, cockpit).Should().BeFalse();
    }
}
