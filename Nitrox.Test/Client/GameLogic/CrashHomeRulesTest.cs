namespace NitroxClient.GameLogic;

/// <summary>
///     The call of <c>CrashHome.Spawn</c> by the player who holds the lock of a sulfur plant: only the holder makes the fish, and a home that has its fish does not make a second one
///     (<see cref="CrashHomeRules" />). The first fish of a run needs its prefab loaded, so the fish of the server reaches a home a few frames after the home exists, and in those frames
///     the holder's <c>Update</c> saw an empty home and started the timer: the one fish that is left in the world "Test" belongs to a home with a timer, which made a second fish.
/// </summary>
[TestClass]
public class CrashHomeRulesTest
{
    [TestMethod]
    public void ThePlayerWithoutTheLockNeverMakesAFish()
    {
        CrashHomeRules.DecideSpawn(holdsLock: false, homeHasLivingFish: false).Should().Be(CrashHomeRules.SpawnCall.NotTheHolder);
        CrashHomeRules.DecideSpawn(holdsLock: false, homeHasLivingFish: true).Should().Be(CrashHomeRules.SpawnCall.NotTheHolder);
    }

    [TestMethod]
    public void TheHolderOfAnEmptyHomeMakesTheFish()
    {
        // a home nobody met (the server writes 0) or a home whose timer ran out
        CrashHomeRules.DecideSpawn(holdsLock: true, homeHasLivingFish: false).Should().Be(CrashHomeRules.SpawnCall.MakeFish);
    }

    [TestMethod]
    public void TheHolderOfAHomeThatHasItsFishMakesNoSecondOne()
    {
        // the timer came with a fish: the number is put right instead
        CrashHomeRules.DecideSpawn(holdsLock: true, homeHasLivingFish: true).Should().Be(CrashHomeRules.SpawnCall.FishAlready);
    }

    [TestMethod]
    public void TheHolderWaitsForTheSavedStateOfTheHome()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: 0f).Should().BeFalse();
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: CrashHomeRules.METADATA_GRACE_SECONDS - 0.1f).Should().BeFalse();
    }

    [TestMethod]
    public void TheHolderActsAsSoonAsTheSavedStateWasApplied()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: true, secondsSinceFirstAsked: 0f).Should().BeTrue();
    }

    [TestMethod]
    public void TheHolderActsAfterTheGraceTimeOfAHomeThatNeverGetsSavedState()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: CrashHomeRules.METADATA_GRACE_SECONDS).Should().BeTrue();
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: 600f).Should().BeTrue();
    }

    [TestMethod]
    public void ABrokenClockKeepsTheHolderWaiting()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void AFishThatArrivesForAHomeWithoutOneIsAdopted()
    {
        CrashHomeRules.DecideIncomingFish(homeHasLivingFish: false, livingFishIsTheIncomingOne: false).Should().Be(CrashHomeRules.IncomingFishCall.Adopt);
    }

    [TestMethod]
    public void AFishThatArrivesAgainIsAdoptedAndNothingIsDestroyed()
    {
        // the home already points to this very object (the spawner is asked twice for it)
        CrashHomeRules.DecideIncomingFish(homeHasLivingFish: true, livingFishIsTheIncomingOne: true).Should().Be(CrashHomeRules.IncomingFishCall.Adopt);
    }

    [TestMethod]
    public void ASecondFishForAHomeThatHasOneReplacesTheOldOne()
    {
        // two Crash entities under one home: the old object would stay in the world for good (nothing but CrashHome.OnDestroy removes the fish and it only knows the one the home points to).
        // It is also the case of a fish that is spawned again while its old object waits to be destroyed: the new one must stay, so the old one is the one that goes
        CrashHomeRules.DecideIncomingFish(homeHasLivingFish: true, livingFishIsTheIncomingOne: false).Should().Be(CrashHomeRules.IncomingFishCall.ReplaceTheOldFish);
    }

    [TestMethod]
    public void AHomeWithAFishIsAtMinusOne()
    {
        // vanilla CrashHome.Spawn writes -1 right after it made the fish, and CrashHome.Update only starts the timer when the number is negative
        CrashHomeRules.FISH_ALIVE.Should().Be(-1f);
    }
}
