namespace NitroxClient.GameLogic;

[TestClass]
public class RemoteHitRulesTest
{
    [TestMethod]
    public void ShouldReplayHit_ARealDropOnACopy_IsTrue()
    {
        RemoteHitRules.ShouldReplayHit(100f, 80f, false, true, false).Should().BeTrue();
    }

    [TestMethod]
    public void ShouldReplayHit_TheSpawnStateOfAnObject_IsFalse()
    {
        // initial sync, a vehicle streaming in damaged: the first health is not a hit
        RemoteHitRules.ShouldReplayHit(100f, 80f, true, true, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldReplayHit_WhileTheInitialSyncIsRunning_IsFalse()
    {
        RemoteHitRules.ShouldReplayHit(100f, 80f, false, false, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldReplayHit_OnTheClientThatSimulatesTheObject_IsFalse()
    {
        // it has the real TakeDamage, and a lock can move while a health packet is in flight
        RemoteHitRules.ShouldReplayHit(100f, 80f, false, true, true).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-5f)]
    [DataRow(float.NaN)]
    public void ShouldReplayHit_ADropToZeroOrNothing_IsFalse(float remoteHealth)
    {
        // a death is replicated by the destruction, never as a last hit: nothing on the copy may reach Kill
        RemoteHitRules.ShouldReplayHit(100f, remoteHealth, false, true, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldReplayHit_TheHitThatEmptiesTheHealth_IsFalse()
    {
        // the last bite on a nearly dead vehicle: the copy is told 0 and lets the replicated destruction handle it
        RemoteHitRules.ShouldReplayHit(3f, 0f, false, true, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldReplayHit_ADropOfExactlyTheMinimum_IsTrue()
    {
        RemoteHitRules.ShouldReplayHit(100f, 99.5f, false, true, false).Should().BeTrue();
    }

    [TestMethod]
    public void ShouldReplayHit_ADropBelowTheMinimum_IsFalse()
    {
        RemoteHitRules.ShouldReplayHit(100f, 99.8f, false, true, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldReplayHit_AHealOrNoChange_IsFalse()
    {
        RemoteHitRules.ShouldReplayHit(80f, 100f, false, true, false).Should().BeFalse();
        RemoteHitRules.ShouldReplayHit(80f, 80f, false, true, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldPlayDamageSound_FollowsTheVanillaCondition()
    {
        RemoteHitRules.ShouldPlayDamageSound(10f, 0f, false).Should().BeTrue();
        // vanilla needs damage > 0 even when the minimum is 0
        RemoteHitRules.ShouldPlayDamageSound(0f, 0f, false).Should().BeFalse();
        // below the minimum
        RemoteHitRules.ShouldPlayDamageSound(3f, 5f, false).Should().BeFalse();
        // damage >= minimum
        RemoteHitRules.ShouldPlayDamageSound(5f, 5f, false).Should().BeTrue();
    }

    [TestMethod]
    public void ShouldPlayDamageSound_ASyncedEvent_IsNotPlayedAgain()
    {
        // the player who simulates the object shares it already
        RemoteHitRules.ShouldPlayDamageSound(10f, 0f, true).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldSpawnDamageEffect_OncePerSecondLikeVanilla()
    {
        RemoteHitRules.ShouldSpawnDamageEffect(10f, 5f, 3.9f).Should().BeTrue();
        // strict >, one second apart is still too early
        RemoteHitRules.ShouldSpawnDamageEffect(10f, 4f, 3f).Should().BeFalse();
        RemoteHitRules.ShouldSpawnDamageEffect(10f, 3.5f, 3f).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldSpawnDamageEffect_NoDamage_IsFalse()
    {
        RemoteHitRules.ShouldSpawnDamageEffect(0f, 10f, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldSpawnLoopingEffect_BelowTheThresholdWithoutAnEffectYet_IsTrue()
    {
        RemoteHitRules.ShouldSpawnLoopingEffect(0.2f, 0.25f, true, false).Should().BeTrue();
    }

    [TestMethod]
    public void ShouldSpawnLoopingEffect_AtTheThreshold_IsFalse()
    {
        // vanilla: GetHealthFraction() < loopEffectBelowPercent
        RemoteHitRules.ShouldSpawnLoopingEffect(0.25f, 0.25f, true, false).Should().BeFalse();
    }

    [TestMethod]
    public void ShouldSpawnLoopingEffect_WithoutAPrefabOrWithAnEffectAlready_IsFalse()
    {
        RemoteHitRules.ShouldSpawnLoopingEffect(0.2f, 0.25f, false, false).Should().BeFalse();
        RemoteHitRules.ShouldSpawnLoopingEffect(0.2f, 0.25f, true, true).Should().BeFalse();
    }
}
