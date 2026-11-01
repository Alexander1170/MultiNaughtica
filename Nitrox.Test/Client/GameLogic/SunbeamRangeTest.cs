using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic;

[TestClass]
public class SunbeamRangeTest
{
    private static readonly NitroxVector3 landingSite = new(100f, 0f, 200f);
    private const float RADIUS = 150f;

    private static NitroxVector3 Inside() => new(120f, -5f, 210f);

    private static NitroxVector3 Outside() => new(-800f, 0f, 900f);

    [TestMethod]
    public void IsAnyoneAtLandingSite_NoPlayers_ReturnsFalse()
    {
        // Vanilla: no local player means not at the landing site
        SunbeamRange.IsAnyoneAtLandingSite([], landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_OnePlayerAtTheSite_ReturnsTrue()
    {
        SunbeamRange.IsAnyoneAtLandingSite([Inside()], landingSite, RADIUS).Should().BeTrue();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_OnePlayerAway_ReturnsFalse()
    {
        SunbeamRange.IsAnyoneAtLandingSite([Outside()], landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_NobodyAtTheSite_ReturnsFalse()
    {
        SunbeamRange.IsAnyoneAtLandingSite([Outside(), new NitroxVector3(900f, -40f, -300f)], landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_OnlyTheOtherPlayerAtTheSite_ReturnsTrue()
    {
        // The case ST1h is about: the client that runs the check stands elsewhere, the partner went to the landing site. Order must not matter
        SunbeamRange.IsAnyoneAtLandingSite([Outside(), Inside()], landingSite, RADIUS).Should().BeTrue();
        SunbeamRange.IsAnyoneAtLandingSite([Inside(), Outside()], landingSite, RADIUS).Should().BeTrue();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_EveryoneAtTheSite_ReturnsTrue()
    {
        SunbeamRange.IsAnyoneAtLandingSite([Inside(), Inside(), landingSite], landingSite, RADIUS).Should().BeTrue();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_PlayerAtTheSiteDisconnected_ReturnsFalse()
    {
        // A player who left has no RemotePlayer anymore, so the caller no longer lists the position
        List<NitroxVector3> players = [Outside(), Inside()];
        SunbeamRange.IsAnyoneAtLandingSite(players, landingSite, RADIUS).Should().BeTrue();

        players.RemoveAt(1);

        SunbeamRange.IsAnyoneAtLandingSite(players, landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_OnTheBorder_ReturnsTrue()
    {
        // Vanilla: not (distance > radius)
        SunbeamRange.IsAnyoneAtLandingSite([new NitroxVector3(landingSite.X + RADIUS, landingSite.Y, landingSite.Z)], landingSite, RADIUS).Should().BeTrue();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_JustOutsideTheBorder_ReturnsFalse()
    {
        SunbeamRange.IsAnyoneAtLandingSite([new NitroxVector3(landingSite.X + RADIUS + 0.5f, landingSite.Y, landingSite.Z)], landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_HeightCountsLikeInVanilla_ReturnsFalse()
    {
        // The vanilla test is the 3D distance: right above the site but further than the radius is not at the site
        SunbeamRange.IsAnyoneAtLandingSite([new NitroxVector3(landingSite.X, landingSite.Y + RADIUS + 1f, landingSite.Z)], landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_NotANumber_ReturnsFalse()
    {
        // Vanilla (cgt.un + ceq 0) also answers "not at the site" for a position that is not a number
        SunbeamRange.IsAnyoneAtLandingSite([new NitroxVector3(float.NaN, 0f, 0f)], landingSite, RADIUS).Should().BeFalse();
    }

    [TestMethod]
    public void IsAnyoneAtLandingSite_ZeroRadius_OnlyTheExactSpotCounts()
    {
        SunbeamRange.IsAnyoneAtLandingSite([landingSite], landingSite, 0f).Should().BeTrue();
        SunbeamRange.IsAnyoneAtLandingSite([Inside()], landingSite, 0f).Should().BeFalse();
    }
}
