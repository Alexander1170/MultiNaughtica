using Nitrox.Model.DataStructures;
using static NitroxClient.GameLogic.SubRootBroadcastRules;

namespace NitroxClient.GameLogic;

[TestClass]
public class SubRootBroadcastRulesTest
{
    private static readonly Optional<NitroxId> cyclops = new NitroxId();
    private static readonly Optional<NitroxId> base1 = new NitroxId();
    private static readonly Optional<NitroxId> nothing = Optional.Empty;

    [TestMethod]
    public void ClassifySetCurrentSub_SubSwitched_IsChanged()
    {
        ClassifySetCurrentSub(true, true).Should().Be(SetCurrentSubOutcome.Changed);
    }

    [TestMethod]
    public void ClassifySetCurrentSub_AlreadyInThatSub_IsUnchanged()
    {
        // The moonpool hammers SetCurrentSub with the sub the player is in
        ClassifySetCurrentSub(false, true).Should().Be(SetCurrentSubOutcome.Unchanged);
    }

    [TestMethod]
    public void ClassifySetCurrentSub_AskedForAnotherSubAndNothingHappened_IsRejected()
    {
        // CanChangeSub was false: the player walked out of the Cyclops a moment ago and Player.currentSub is still null
        ClassifySetCurrentSub(false, false).Should().Be(SetCurrentSubOutcome.Rejected);
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, true)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    public void ShouldBroadcastSubRootAfterRespawn_ByWhereThePlayerEndedUp(bool inSubRoot, bool inEscapePod, bool expected)
    {
        ShouldBroadcastSubRootAfterRespawn(inSubRoot, inEscapePod).Should().Be(expected);
    }

    [TestMethod]
    public void ShouldBroadcastSubRootAfterRespawn_LifepodRespawn_IsTheOnlyCaseThatStaysSilent()
    {
        // Respawn in a base: reported. Respawn in the lifepod (currentSub null, currentEscapePod set): the pod packets say it. Neither (no pod left, no base): open water is the truth.
        ShouldBroadcastSubRootAfterRespawn(inSubRoot: true, inEscapePod: false).Should().BeTrue();
        ShouldBroadcastSubRootAfterRespawn(inSubRoot: false, inEscapePod: true).Should().BeFalse();
        ShouldBroadcastSubRootAfterRespawn(inSubRoot: false, inEscapePod: false).Should().BeTrue();
    }

    [TestMethod]
    public void IsOutOfSync_NothingBroadcastYet_IsFalse()
    {
        // The initial sync puts the player into its sub without a packet: the server already knows
        IsOutOfSync(null, cyclops, false).Should().BeFalse();
    }

    [TestMethod]
    public void IsOutOfSync_SameSubRoot_IsFalse()
    {
        IsOutOfSync(cyclops, cyclops, false).Should().BeFalse();
        IsOutOfSync(nothing, nothing, false).Should().BeFalse();
    }

    [TestMethod]
    public void IsOutOfSync_PlayerLeftWithoutReport_IsTrue()
    {
        IsOutOfSync(cyclops, nothing, false).Should().BeTrue();
    }

    [TestMethod]
    public void IsOutOfSync_PlayerEnteredWithoutReport_IsTrue()
    {
        IsOutOfSync(nothing, cyclops, false).Should().BeTrue();
    }

    [TestMethod]
    public void IsOutOfSync_OtherSubRoot_IsTrue()
    {
        IsOutOfSync(cyclops, base1, false).Should().BeTrue();
    }

    [TestMethod]
    public void IsOutOfSync_InLifepodAfterLeavingASubRoot_IsFalse()
    {
        // The lifepod packets carry that state, the last SubRootChanged is allowed to be older
        IsOutOfSync(cyclops, nothing, true).Should().BeFalse();
    }

    [TestMethod]
    public void IsOutOfSync_InLifepodButAlsoInASubRootNotReported_IsTrue()
    {
        IsOutOfSync(nothing, cyclops, true).Should().BeTrue();
    }
}
