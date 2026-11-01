using NitroxClient.GameLogic.VehicleUpgrades;
using static NitroxClient.GameLogic.VehicleUpgrades.SolarChargeRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     The solar chargers of the Seamoth and the Cyclops: the falloff of a base's solar panel, scaled by the server's setting.
/// </summary>
[TestClass]
public class SolarChargeRulesTest
{
    [TestMethod]
    [DataRow(0f, 1f)]
    [DataRow(-5f, 1f)]
    [DataRow(50f, 0.75f)]
    [DataRow(100f, 0.5f)]
    [DataRow(150f, 0.25f)]
    [DataRow(200f, 0f)]
    [DataRow(250f, 0f)]
    [DataRow(1500f, 0f)]
    public void TheDepthTakesTheChargeAwayLinearlyDownTo200Metres(float depth, float expected)
    {
        DepthScalar(depth).Should().BeApproximately(expected, 0.0001f);
    }

    [TestMethod]
    public void ADepthThatIsNoNumberGivesNoCharge()
    {
        DepthScalar(float.NaN).Should().Be(0f);
        EnergyFor(1f, float.NaN, 1f, 1f).Should().Be(0f);
    }

    [TestMethod]
    public void NightGivesNoChargeAtAnyDepth()
    {
        RechargeScalar(0f, 0f).Should().Be(0f);
        RechargeScalar(50f, 0f).Should().Be(0f);
        RechargeScalar(0f, -0.1f).Should().Be(0f);
        EnergyFor(0.25f, 0f, 0f, 10f).Should().Be(0f);
    }

    [TestMethod]
    public void NoonAtTheSurfaceGivesTheConfiguredEnergyPerSecond()
    {
        EnergyFor(0.25f, 0f, 1f, 1f).Should().BeApproximately(0.25f, 0.0001f);
        EnergyFor(1.25f, 0f, 1f, 0.5f).Should().BeApproximately(0.625f, 0.0001f);
    }

    [TestMethod]
    public void TheLightAndTheDepthMultiply()
    {
        // half the light at 100 m: 0.5 * 0.5 of the surface noon charge
        RechargeScalar(100f, 0.5f).Should().BeApproximately(0.25f, 0.0001f);
        EnergyFor(1f, 100f, 0.5f, 1f).Should().BeApproximately(0.25f, 0.0001f);
    }

    [TestMethod]
    public void ALightScalarAboveOneDoesNotChargeMoreThanNoon()
    {
        RechargeScalar(0f, 3f).Should().Be(1f);
    }

    [TestMethod]
    public void NothingChargesAt250MetresInFullDaylight()
    {
        EnergyFor(5f, 250f, 1f, 100f).Should().Be(0f);
    }

    [TestMethod]
    public void ALongStepIsChargedAsIfItWasOneSecondSoATimeJumpCannotFillTheCell()
    {
        float oneSecond = EnergyFor(1.25f, 0f, 1f, 1f);

        EnergyFor(1.25f, 0f, 1f, 5f).Should().BeApproximately(oneSecond, 0.0001f);
        EnergyFor(1.25f, 0f, 1f, 3600f).Should().BeApproximately(oneSecond, 0.0001f);
        EnergyFor(1.25f, 0f, 1f, float.MaxValue).Should().BeApproximately(oneSecond, 0.0001f);
        MAX_DELTA_SECONDS.Should().Be(1f);
    }

    [TestMethod]
    public void ASettingOfZeroOrNoTimeGivesNothing()
    {
        EnergyFor(0f, 0f, 1f, 1f).Should().Be(0f);
        EnergyFor(0.25f, 0f, 1f, 0f).Should().Be(0f);
        EnergyFor(0.25f, 0f, 1f, -1f).Should().Be(0f);
        EnergyFor(-1f, 0f, 1f, 1f).Should().Be(0f);
    }

    [TestMethod]
    public void TheChargeOfOneFrameAddsUpToTheChargeOfTheSameTimeInOneStep()
    {
        float inFrames = 0f;
        for (int i = 0; i < 60; i++)
        {
            inFrames += EnergyFor(0.25f, 30f, 0.8f, 1f / 60f);
        }

        inFrames.Should().BeApproximately(EnergyFor(0.25f, 30f, 0.8f, 1f), 0.001f);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(-1, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(5, 1)]
    public void OnlyOneInstalledChargerCounts(int installed, int counting)
    {
        CountingModules(installed).Should().Be(counting);
    }

    [TestMethod]
    public void TheChargeNeverFillsMoreThanTheRoomLeft()
    {
        FitIntoRoom(5f, 200f, 100f).Should().Be(5f);
        FitIntoRoom(5f, 200f, 198f).Should().Be(2f);
        FitIntoRoom(5f, 200f, 200f).Should().Be(0f);
        FitIntoRoom(5f, 200f, 250f).Should().Be(0f);
        FitIntoRoom(-1f, 200f, 100f).Should().Be(0f);
    }
}
