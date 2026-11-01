using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The first-fire note of the patches on small methods, which the live check reads to prove that a patch is called and was not inlined away.
/// </summary>
[TestClass]
public class PatchProbeTest
{
    [TestMethod]
    public void ANameIsNotedTheFirstTimeAndOnlyOncePerSession()
    {
        try
        {
            PatchProbe.Reset();
            PatchProbe.HasFired("Test.Method").Should().BeFalse();

            PatchProbe.Fired("Test.Method");
            PatchProbe.Fired("Test.Method");

            PatchProbe.HasFired("Test.Method").Should().BeTrue();
            PatchProbe.HasFired("Other.Method").Should().BeFalse();
        }
        finally
        {
            PatchProbe.Reset();
        }
    }

    [TestMethod]
    public void ANewSessionNotesTheNamesAgain()
    {
        try
        {
            PatchProbe.Fired("Test.Method");

            PatchProbe.Reset();

            PatchProbe.HasFired("Test.Method").Should().BeFalse();
        }
        finally
        {
            PatchProbe.Reset();
        }
    }
}
