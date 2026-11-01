namespace NitroxClient.GameLogic;

/// <summary>
/// What a client does with a Sea Emperor baby (<c>SeaEmperorBaby</c>) when its <c>Start</c> has run, and how long the cosmetic hatch babies of the other players may live. Contains no
/// Unity types so that it can be tested without the game (story scenario <c>story_emperor</c>, product findings 1 and 2 of its first live run).
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of baby exist on a client:
/// </para>
/// <list type="bullet">
/// <item>The <b>temporary</b> baby of a hatch that another player simulates: it exists only to play the egg animation (<c>IncubatorEgg_HatchNow_Patch</c>) and is destroyed at the end of it.</item>
/// <item>The baby <b>hatched here</b>: the real, networked baby of the player who holds the lock of the incubator. It is made under the egg's attach point and the egg animation calls
/// <c>SwimToMother</c> at its end, so <c>Start</c> must not. (The game starts the baby late: <c>StartHatchAnimation</c> disables it for the cinematic, and the end of the animation takes it off the
/// egg and calls <c>SwimToMother</c> before <c>Start</c> runs, so by then it often sits under no egg any more and already has its swim target. The <c>HatchedHereBaby</c> marker, put on it in
/// <c>HatchNow</c>, is the guard that does not depend on that order; "under an egg" and "has a swim target" are two more.)</item>
/// <item>A baby <b>spawned by the server</b>: the real baby as every other client, a late joiner and a restarted world get it. The game's own hatch code never ran for it, so nothing calls
/// <c>SwimToMother</c>, and without the call the baby is never registered with the Emperor (<c>SeaEmperor.RegisterBaby</c>), which starts the mother interaction only when as many babies as attach
/// points are registered. The first live run found that the old test for this kind, "the baby has no parent", never holds: the entity spawner parents every baby of the server to the cell root before
/// <c>Start</c> runs, so the Emperor never reacted on the screen of the second player.</item>
/// </list>
/// </remarks>
public static class SeaEmperorBabyRules
{
    /// <summary>
    /// How long the animation baby of a hatch that another player simulates may stay. The animation takes a few seconds when the egg is seen (3 of 5 were over after 8 s in the live run), but an
    /// animator that nobody looks at does not advance (the other 2 of 5 were still there 6 minutes later), and its end event never comes.
    /// </summary>
    public const float TEMPORARY_BABY_LIFETIME_SECONDS = 30f;

    /// <summary>How long a baby of the server waits for the Emperor to be loaded (its cell can come after the cell of the incubator) before it gives up.</summary>
    public const float EMPEROR_WAIT_SECONDS = 60f;

    public enum BabyOrigin
    {
        /// <summary>The animation baby of a hatch simulated by another player.</summary>
        Temporary = 0,

        /// <summary>The networked baby of the hatch this player runs: the egg animation sends it to the mother.</summary>
        HatchedHere = 1,

        /// <summary>A real baby that came from the server.</summary>
        SpawnedByServer = 2
    }

    public enum StartAction
    {
        /// <summary>Do nothing: another piece of code takes care of the baby.</summary>
        LeaveAlone = 0,

        /// <summary>Register the baby with the Emperor and send it to its attach point.</summary>
        SwimToMother = 1,

        /// <summary>The Emperor is not loaded yet: wait for it (up to <see cref="EMPEROR_WAIT_SECONDS" />), then decide again.</summary>
        WaitForEmperor = 2
    }

    /// <param name="isTemporary">The name carries the marker of the animation babies.</param>
    /// <param name="markedAsHatchedHere">The baby carries the marker that <c>IncubatorEgg_HatchNow_Patch</c> puts on the baby of the hatch this player runs.</param>
    /// <param name="underIncubatorEgg">The baby is a child of an <c>IncubatorEgg</c> (it sits under the egg's attach point).</param>
    public static BabyOrigin Classify(bool isTemporary, bool markedAsHatchedHere, bool underIncubatorEgg)
    {
        if (isTemporary)
        {
            return BabyOrigin.Temporary;
        }

        if (markedAsHatchedHere || underIncubatorEgg)
        {
            return BabyOrigin.HatchedHere;
        }

        return BabyOrigin.SpawnedByServer;
    }

    public static StartAction DecideStart(BabyOrigin origin, bool emperorExists, bool hasSwimTarget)
    {
        if (origin != BabyOrigin.SpawnedByServer)
        {
            return StartAction.LeaveAlone;
        }

        // already on its way (the call was made by something else): never register a baby twice
        if (hasSwimTarget)
        {
            return StartAction.LeaveAlone;
        }

        return emperorExists ? StartAction.SwimToMother : StartAction.WaitForEmperor;
    }

    /// <summary>
    /// The place of a baby of the server at the mother. The hatching client numbers its babies by egg (0 to 4); a baby of the server does not know its egg, so it takes the next place after the
    /// babies already registered, and starts again at the first place when the Emperor has taken all of them (she clears her set when the interaction starts).
    /// </summary>
    public static int NextBabyId(int registeredBabies, int attachPoints)
    {
        if (attachPoints <= 0 || registeredBabies <= 0)
        {
            return 0;
        }

        return registeredBabies % attachPoints;
    }

    public static bool TemporaryBabyExpired(float ageSeconds)
    {
        return ageSeconds >= TEMPORARY_BABY_LIFETIME_SECONDS;
    }

    public static bool EmperorWaitOver(float waitedSeconds)
    {
        return waitedSeconds >= EMPEROR_WAIT_SECONDS;
    }
}
