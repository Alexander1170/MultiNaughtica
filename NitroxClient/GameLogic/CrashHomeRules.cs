namespace NitroxClient.GameLogic;

/// <summary>
/// What the player who holds the lock of a sulfur plant (<c>CrashHome</c>) does when the game asks for a Crashfish. Contains no Unity types so that it can be tested without the game.
/// </summary>
/// <remarks>
/// A home keeps one number, <c>spawnTime</c>: -1 while a fish lives in it, the game time at which the next fish is made after the fish died (the death starts a timer of
/// <c>CrashHome.respawnDelay</c>, 20 minutes), and 0 for a home no player has met yet (<c>CrashHomeBootstrapper</c> of the server). <c>CrashHome.Update</c> asks for a fish as soon as the
/// number is not negative and the game time has reached it, whatever the home holds at that moment. The fish is an entity of the server, a child of its home, and comes back with it when a
/// cell is loaded, so a home can hold a fish and a timer at the same time: the lock holder made the timer before the fish of the server reached it (a home is created before its children,
/// and the first Crashfish of a run needs its prefab loaded), or a save kept both.
/// </remarks>
public static class CrashHomeRules
{
    /// <summary>Value of <c>CrashHome.spawnTime</c> while a fish lives in the home.</summary>
    public const float FISH_ALIVE = -1f;

    public enum SpawnCall
    {
        /// <summary>The player does not hold the lock of the home: the player who does makes the fish and the server tells this one.</summary>
        NotTheHolder = 0,

        /// <summary>The home has its fish: a second one is not made, the number is put right (<see cref="FISH_ALIVE" />) and told to the others.</summary>
        FishAlready = 1,

        /// <summary>The home is empty and it is time: make the fish.</summary>
        MakeFish = 2
    }

    /// <param name="holdsLock">This player holds a simulation lock of the home.</param>
    /// <param name="homeHasLivingFish">The home's <c>crash</c> is an object that exists (Unity's null check: a fish that was destroyed does not count).</param>
    public static SpawnCall DecideSpawn(bool holdsLock, bool homeHasLivingFish)
    {
        if (!holdsLock)
        {
            return SpawnCall.NotTheHolder;
        }

        return homeHasLivingFish ? SpawnCall.FishAlready : SpawnCall.MakeFish;
    }

    /// <summary>
    /// How long (seconds) the holder of a home waits for the saved state of the home before it acts on the number the prefab came with.
    /// </summary>
    public const float METADATA_GRACE_SECONDS = 20f;

    /// <summary>
    /// Whether the holder of a home may act on its number (start the default timer of an empty home and tell the others, or make a fish): a home is created from its prefab with the prefab's
    /// number and gets the saved state (<c>CrashHomeMetadata</c>) a little later. A holder that acts in between overwrites the saved timer of an emptied home with a new one (live run
    /// 2026-10-26, restart stage: 2211.2 instead of the saved 2044.8) or makes a fish for a home whose saved timer has not run out. The wait ends when the saved state arrived, or after
    /// <see cref="METADATA_GRACE_SECONDS" /> (a home the server has no state for).
    /// </summary>
    /// <param name="metadataApplied">The saved state of the home was applied to it.</param>
    /// <param name="secondsSinceFirstAsked">Real seconds since the holder was first asked to act on this home.</param>
    public static bool MayActOnItsNumber(bool metadataApplied, float secondsSinceFirstAsked)
    {
        // Negated so that a broken clock (NaN) keeps waiting
        return metadataApplied || secondsSinceFirstAsked >= METADATA_GRACE_SECONDS;
    }

    public enum IncomingFishCall
    {
        /// <summary>The home has no living fish, or the one it has is the incoming one: the incoming fish becomes the fish of the home.</summary>
        Adopt = 0,

        /// <summary>The home has a living fish that is another object: it is destroyed and the incoming fish takes its place.</summary>
        ReplaceTheOldFish = 1
    }

    /// <summary>
    /// What <c>CrashEntitySpawner</c> does with the home's <c>crash</c> when the Crashfish of the server for that home reaches the client. A fish that is not a child of its home is not destroyed with
    /// it by anything but <c>CrashHome.OnDestroy</c>, which only knows the one object <c>crash</c> points to: a fish that stops being pointed at stays in the world as a ghost until the scene ends.
    /// The incoming fish is the one that stays (never the old one): a fish that is spawned again while its old object is still waiting to be destroyed has the same id, and the old one must go.
    /// </summary>
    /// <param name="homeHasLivingFish">The home's <c>crash</c> is an object that exists (Unity's null check).</param>
    /// <param name="livingFishIsTheIncomingOne">That object is the very object that arrives.</param>
    public static IncomingFishCall DecideIncomingFish(bool homeHasLivingFish, bool livingFishIsTheIncomingOne)
    {
        return homeHasLivingFish && !livingFishIsTheIncomingOne ? IncomingFishCall.ReplaceTheOldFish : IncomingFishCall.Adopt;
    }
}
