using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic;

[TestClass]
public class PlayerRangeTest
{
    private static readonly NitroxVector3 target = new(0f, 0f, 0f);

    [TestMethod]
    public void TheNearestOfSeveralPlayersCounts()
    {
        PlayerRange.NearestDistance([new NitroxVector3(30f, 0f, 0f), new NitroxVector3(0f, 4f, 0f), new NitroxVector3(100f, 0f, 0f)], target).Should().BeApproximately(4f, 0.001f);
    }

    [TestMethod]
    public void OnePlayerGivesHisDistance()
    {
        PlayerRange.NearestDistance([new NitroxVector3(3f, 4f, 0f)], target).Should().BeApproximately(5f, 0.001f);
    }

    [TestMethod]
    public void NoPlayerIsFarAway()
    {
        PlayerRange.NearestDistance([], target).Should().Be(float.MaxValue);
    }
}
