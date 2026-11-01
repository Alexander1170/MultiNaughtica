namespace NitroxClient.GameLogic;

[TestClass]
public class BatteryChargeRulesTest
{
    [TestMethod]
    public void AChangeWithinTheSameWholeNumberIsNotSent()
    {
        BatteryChargeRules.ShouldSend(100.9f, 100.2f).Should().BeFalse();
    }

    [TestMethod]
    public void CrossingAWholeNumberWhileDrainingIsSent()
    {
        BatteryChargeRules.ShouldSend(100.1f, 99.95f).Should().BeTrue();
    }

    [TestMethod]
    public void CrossingAWholeNumberWhileChargingIsSent()
    {
        BatteryChargeRules.ShouldSend(99.95f, 100.05f).Should().BeTrue();
    }

    [TestMethod]
    public void TheStepThatEmptiesTheBatteryIsSentEvenWithinTheLastWholeNumber()
    {
        // IsDepleted is "charge == 0": a replica that keeps 0.4 would look powered
        BatteryChargeRules.ShouldSend(0.4f, 0f).Should().BeTrue();
    }

    [TestMethod]
    public void AnEmptyBatteryThatStaysEmptyIsNotSentAgain()
    {
        BatteryChargeRules.ShouldSend(0f, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void TheLastWholeNumberOfAFullBatteryIsSent()
    {
        // charging up to the capacity (the new value is already limited to it)
        BatteryChargeRules.ShouldSend(199.7f, 200f).Should().BeTrue();
    }
}
