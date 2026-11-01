using Nitrox.Server.Subnautica.Models.Helper;

namespace Nitrox.Test.Server.Helper;

[TestClass]
public class DeterministicGeneratorTest
{
    [TestMethod]
    public void UnityLikeSequence_OnlyDependsOnWorldSeedAndSecondSeed()
    {
        DeterministicGenerator batchA = new("seed", "batch [1, 2, 3]");
        float first = batchA.NextFloat();
        int second = batchA.NextIntRange(0, 1000);

        // Another batch being generated in between (exploration order) must not change what a batch gets
        DeterministicGenerator otherBatch = new("seed", "batch [9, 9, 9]");
        for (int i = 0; i < 50; i++)
        {
            otherBatch.NextFloat();
            otherBatch.NextInsideSphere(4f);
        }

        DeterministicGenerator batchAAgain = new("seed", "batch [1, 2, 3]");
        Assert.AreEqual(first, batchAAgain.NextFloat());
        Assert.AreEqual(second, batchAAgain.NextIntRange(0, 1000));
    }

    [TestMethod]
    public void UnityLikeSequence_DiffersBetweenBatches()
    {
        DeterministicGenerator batchA = new("seed", "batch [1, 2, 3]");
        DeterministicGenerator batchB = new("seed", "batch [4, 5, 6]");

        Assert.AreNotEqual(batchA.NextFloat(), batchB.NextFloat());
    }
}
