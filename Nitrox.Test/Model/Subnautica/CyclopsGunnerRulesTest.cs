using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica;

/// <summary>The numbers of the gunner station that the server also needs (docs/coop/VEHICLE_UPGRADES.md, station 2).</summary>
[TestClass]
public class CyclopsGunnerRulesTest
{
    [TestMethod]
    public void TheServerKeepsEightyPercentOfTheCooldownAndNeverLessThanAQuarterOfASecond()
    {
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(3f).Should().Be(2400);
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(60f).Should().Be(48000);
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(0.1f).Should().Be(CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS);
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(0f).Should().Be(CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS);
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(-4f).Should().Be(CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS);
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(float.NaN).Should().Be(CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS);
        CyclopsGunnerRules.ServerMinIntervalMilliseconds(float.PositiveInfinity).Should().Be(CyclopsGunnerRules.MIN_SERVER_INTERVAL_MILLISECONDS);
    }

    [TestMethod]
    public void OnlyTheStasisSphereIsKnownAmmoAndAChargeIsKeptInsideZeroAndOne()
    {
        CyclopsGunnerRules.IsKnownAmmo(CyclopsGunnerAmmo.StasisSphere).Should().BeTrue();
        CyclopsGunnerRules.IsKnownAmmo((CyclopsGunnerAmmo)1).Should().BeFalse("a second kind is added to the rules when it is built");

        CyclopsGunnerRules.ClampCharge(0.4f).Should().Be(0.4f);
        CyclopsGunnerRules.ClampCharge(7f).Should().Be(1f);
        CyclopsGunnerRules.ClampCharge(-1f).Should().Be(0f);
        CyclopsGunnerRules.ClampCharge(float.NaN).Should().Be(0f);
        CyclopsGunnerRules.IsUsableCharge(float.NaN).Should().BeFalse();
        CyclopsGunnerRules.IsUsableCharge(float.NegativeInfinity).Should().BeFalse();
        CyclopsGunnerRules.IsUsableCharge(0.2f).Should().BeTrue();
    }

    [TestMethod]
    public void TheServerWaitsLongerForTheAnswerThanARoundTripButLessThanTheClientDoes()
    {
        CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS.Should().BeInRange(1000, 3000);
        CyclopsGunnerRules.PENDING_FORWARD_MILLISECONDS.Should().BeLessThan((long)(NitroxClient.GameLogic.Gunner.GunnerRules.REQUEST_TIMEOUT_SECONDS * 1000f), "the gunner must not have given up on an answer that the server would still grant");
    }
}
