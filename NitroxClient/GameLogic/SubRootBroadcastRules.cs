using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic;

/// <summary>
/// What the local player tells the server about the sub root it is in. The server keeps one parent per player (a sub root, a lifepod or nothing) and both
/// <see cref="Nitrox.Model.Subnautica.Packets.SubRootChanged" /> and <see cref="Nitrox.Model.Subnautica.Packets.EscapePodChanged" /> write it.
/// </summary>
public static class SubRootBroadcastRules
{
    public enum SetCurrentSubOutcome
    {
        /// <summary>The caller asked for the sub the player is already in.</summary>
        Unchanged,
        /// <summary>Vanilla switched <c>Player.currentSub</c>.</summary>
        Changed,
        /// <summary>The caller asked for another sub and vanilla refused: <c>Player.CanChangeSub</c> is false for the first three fixed updates after a switch.</summary>
        Rejected
    }

    /// <summary>
    /// Judges a finished <c>Player.SetCurrentSub</c> call. Only a switch that really happened may be reported: a refused request would put the remote players of the
    /// other clients into a sub (and a Cyclops pawn) the local player is not in.
    /// </summary>
    public static SetCurrentSubOutcome ClassifySetCurrentSub(bool currentSubChanged, bool requestedSubIsCurrentSub)
    {
        if (currentSubChanged)
        {
            return SetCurrentSubOutcome.Changed;
        }

        return requestedSubIsCurrentSub ? SetCurrentSubOutcome.Unchanged : SetCurrentSubOutcome.Rejected;
    }

    /// <summary>
    /// Whether <c>Player.MovePlayerToRespawnPoint</c> still has to report the sub root once it is done. Respawning in the lifepod leaves <c>currentSub</c> at null because a
    /// lifepod is no sub root, and the pod itself was reported by <c>EscapePodChanged</c>: a <c>SubRootChanged</c> with nothing after that says "open water" to the server.
    /// </summary>
    public static bool ShouldBroadcastSubRootAfterRespawn(bool inSubRoot, bool inEscapePod) => inSubRoot || !inEscapePod;

    /// <summary>
    /// True if the sub root the local player is in is not the one the server was last told about, which means a switch was never reported. Diagnostics only.
    /// </summary>
    /// <param name="lastBroadcast">The last <c>SubRootChanged</c> sent, null if none was sent yet</param>
    /// <param name="currentSub">The id of <c>Player.currentSub</c>, empty if there is none</param>
    /// <param name="inEscapePod">Whether <c>Player.currentEscapePod</c> is set</param>
    public static bool IsOutOfSync(Optional<NitroxId>? lastBroadcast, Optional<NitroxId> currentSub, bool inEscapePod)
    {
        if (lastBroadcast is not { } last)
        {
            return false;
        }

        // The state of a player in a lifepod travels in the lifepod packets, the last sub root report is allowed to be older than that
        if (!currentSub.HasValue && inEscapePod)
        {
            return false;
        }

        return !last.Equals(currentSub);
    }
}
