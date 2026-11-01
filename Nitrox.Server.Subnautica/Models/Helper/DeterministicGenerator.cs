using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;

namespace Nitrox.Server.Subnautica.Models.Helper;

internal sealed class DeterministicGenerator(string worldSeed, string secondSeedValue)
{
    private readonly Random random = new($"{worldSeed}{secondSeedValue}".ToMd5HashedInt32());

    /// <summary>
    ///     Unity-like random sequence (used where the game's own algorithms are mirrored) derived from the same seed. Per generator, so the
    ///     results do not depend on which other batches were generated before.
    /// </summary>
    private readonly XorRandom unityLikeRandom = new($"{worldSeed}{secondSeedValue}".ToMd5HashedInt32());

    public float NextFloat() => unityLikeRandom.NextFloat();

    public int NextIntRange(int min, int max) => unityLikeRandom.NextIntRange(min, max);

    public NitroxVector3 NextInsideSphere(float radius = 1f) => unityLikeRandom.NextInsideSphere(radius);

    public double NextDouble()
    {
        return random.NextDouble();
    }

    public double NextDouble(double min, double max)
    {
        return random.NextDouble() * (max - min) + min;
    }

    public int NextInt(int min, int max)
    {
        return random.Next(min, max);
    }

    public NitroxId NextId()
    {
        byte[] bytes = new byte[16];
        random.NextBytes(bytes);
        return new NitroxId(bytes);
    }
}
