namespace NitroxClient.GameLogic.Spawning;

/// <summary>
///     How the ghost probe of the live check (bug 9, the fish in the base that only one player sees, <c>HarnessConsoleCommands.Ghosts.cs</c>) judges one creature of this client's world.
///     A creature is a ghost when nothing of the sync can reach it any more: its id belongs to another object (<see cref="Verdict.Shadowed" />: the updates of the id, the destruction of the
///     id and the lock of the id all go to the other object), or no player's position reaches it any more (<see cref="Verdict.GoneQuiet" />: followed once, nothing for a long time).
/// </summary>
internal static class CreatureCopyRules
{
    /// <summary>
    ///     A creature that swims calls for a new course every few seconds and its simulating player reports it every 0.25 s, so a copy that was followed and has been silent for this long is
    ///     not followed any more (a creature that sits still reports nothing, which is why a copy that was never updated is not judged by time).
    /// </summary>
    public const float GONE_QUIET_AFTER_SECONDS = 20f;

    internal enum Verdict
    {
        /// <summary>This client simulates the creature (it holds the lock).</summary>
        SimulatedHere,

        /// <summary>The simulating player's position updates reach the copy.</summary>
        Followed,

        /// <summary>No update ever arrived: normal for a creature that does not move, and for one that was just spawned.</summary>
        NeverUpdated,

        /// <summary>Updates arrived once and stopped for <see cref="GONE_QUIET_AFTER_SECONDS" />: nothing follows this copy any more.</summary>
        GoneQuiet,

        /// <summary>The id of this object is registered to another object: nothing can ever update, find, lock or destroy this one.</summary>
        Shadowed,

        /// <summary>The object has no id: the server does not know it.</summary>
        NoId
    }

    /// <param name="hasId">The object carries a Nitrox id.</param>
    /// <param name="registeredToThisObject">The id of the object is registered to this very object.</param>
    /// <param name="simulatedHere">This client holds the lock of the id.</param>
    /// <param name="everUpdated">A position update of the simulating player ever reached the copy.</param>
    /// <param name="secondsSinceUpdate">Seconds since the last update, meaningless when <paramref name="everUpdated" /> is false.</param>
    internal static Verdict Judge(bool hasId, bool registeredToThisObject, bool simulatedHere, bool everUpdated, float secondsSinceUpdate)
    {
        if (!hasId)
        {
            return Verdict.NoId;
        }

        if (!registeredToThisObject)
        {
            return Verdict.Shadowed;
        }

        if (simulatedHere)
        {
            return Verdict.SimulatedHere;
        }

        if (!everUpdated)
        {
            return Verdict.NeverUpdated;
        }

        return secondsSinceUpdate > GONE_QUIET_AFTER_SECONDS ? Verdict.GoneQuiet : Verdict.Followed;
    }

    /// <summary>
    ///     The verdicts that the live check counts as ghosts. <see cref="Verdict.NeverUpdated" /> is not one: the probe on the other client decides, by the ids that only one of the two lists has.
    /// </summary>
    internal static bool IsGhost(Verdict verdict)
    {
        return verdict is Verdict.Shadowed or Verdict.GoneQuiet or Verdict.NoId;
    }
}
