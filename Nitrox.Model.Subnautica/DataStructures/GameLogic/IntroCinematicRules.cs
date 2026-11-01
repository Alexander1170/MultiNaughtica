using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures.Unity;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
///     What the server and the clients decide about the two-player intro (the crash of the lifepod, <c>uGUI_SceneIntro</c>) and about where a player who has not finished it stands.
///     Holds no Unity type and no service, so the decisions can be tested without the game. The modes are the ones of <see cref="IntroCinematicMode" />: a player starts as
///     <see cref="IntroCinematicMode.LOADING" />, reports <see cref="IntroCinematicMode.WAITING" /> when he is ready, gets <see cref="IntroCinematicMode.START" /> from the server together with a
///     partner, plays (<see cref="IntroCinematicMode.PLAYING" />, or <see cref="IntroCinematicMode.SINGLEPLAYER" /> when he chose to play alone) and ends as <see cref="IntroCinematicMode.COMPLETED" />.
/// </summary>
public static class IntroCinematicRules
{
    /// <summary>
    ///     Where the game puts a player who arrives in a lifepod, relative to the pivot of the pod (the client moves <c>EscapePod.playerSpawn</c> to the pivot of the pod plus this).
    ///     The pods are never rotated, so the offset is the same in world space.
    /// </summary>
    public static readonly NitroxVector3 EscapePodSpawnOffset = new(0.9f, 2.1f, 0f);

    /// <summary>
    ///     How far from the pivot of the lifepod (horizontally) a player can be and still count as in the pod. The pod is a few metres long; the game itself lets go of the pod
    ///     (<c>Player.ValidateEscapePod</c>) when the player is farther than its <c>escapePodRadius</c> from it.
    /// </summary>
    public const float ESCAPE_POD_MAX_HORIZONTAL_DISTANCE = 15f;

    /// <summary>
    ///     How far below the pivot of the pod a player in the pod can be. The pod floats at the surface, the sea floor is far deeper than this: a player in "pod mode" (he walks, he does not swim)
    ///     below this stands on the sea floor with the pod's gravity and sky, which is what a player saw after rejoining during the intro (2026-10-23).
    /// </summary>
    public const float ESCAPE_POD_MAX_BELOW = 6f;

    /// <summary>
    ///     How far above the pivot of the pod a player in the pod can be (the roof, the hatch).
    /// </summary>
    public const float ESCAPE_POD_MAX_ABOVE = 10f;

    /// <summary>
    ///     Positions closer than this to the origin of the world are the default of a player who never moved, not a place (see <c>PlayerPositionInitialSyncProcessor</c>).
    /// </summary>
    private const float UNKNOWN_POSITION_TOLERANCE = 0.0002f;

    /// <returns>True for the modes in which a player is in the middle of the cinematic or about to be (the server paired him, or he chose to play alone)</returns>
    public static bool IsRunning(IntroCinematicMode mode) => mode is IntroCinematicMode.START or IntroCinematicMode.PLAYING or IntroCinematicMode.SINGLEPLAYER;

    /// <returns>True if the player is past the intro: the cinematic can never be paired with him any more</returns>
    public static bool IsPast(IntroCinematicMode mode) => mode == IntroCinematicMode.COMPLETED || IsRunning(mode);

    /// <summary>
    ///     True for a player who reported that he is in the intro (waiting for his partner, or in the cinematic) and did not report that it is over. When he leaves in this state, the pose the
    ///     server keeps for him is the pose of the cinematic. <see cref="IntroCinematicMode.LOADING" /> is no part of this: it is also what a player stays at for good when the game never
    ///     plays the intro for him (a creative world), and he must not be sent back to his lifepod every time he joins.
    /// </summary>
    public static bool IsMidIntro(IntroCinematicMode mode) => mode == IntroCinematicMode.WAITING || IsRunning(mode);

    /// <summary>
    ///     The intro mode a player gets in his initial sync. A player the server knows already has seen the intro (or was sent past it): <see cref="IntroCinematicMode.COMPLETED" />.
    ///     A new player is <see cref="IntroCinematicMode.LOADING" /> and waits for a partner, unless there is nobody left to play it with: another player is in the middle of the intro or past it, and
    ///     none waits for a partner. He would sit at "waiting for a partner" forever (or play the whole cinematic next to a partner who is gone), so the intro is over for him
    ///     (<see cref="IntroCinematicMode.COMPLETED" />): his client skips it cleanly and puts him in the lifepod (2026-10-23: the second player of a new world stayed on a black screen).
    /// </summary>
    /// <param name="wasBrandNewPlayer">True if the server saw this player for the first time</param>
    /// <param name="modesOfOtherPlayers">The intro mode of every other player who is connected</param>
    public static IntroCinematicMode ChooseJoinMode(bool wasBrandNewPlayer, IEnumerable<IntroCinematicMode> modesOfOtherPlayers)
    {
        if (!wasBrandNewPlayer)
        {
            return IntroCinematicMode.COMPLETED;
        }

        return HasNobodyToPairWith(modesOfOtherPlayers) ? IntroCinematicMode.COMPLETED : IntroCinematicMode.LOADING;
    }

    /// <summary>
    ///     True if no other player can be paired with a player who is ready for the intro: at least one of them is mid-intro or past it, and none of them waits for a partner (one who waits is paired
    ///     with him as soon as he reports <see cref="IntroCinematicMode.WAITING" /> too). Players who have not started yet (<see cref="IntroCinematicMode.LOADING" />) count as nothing: they may still come.
    /// </summary>
    /// <param name="modesOfOtherPlayers">The intro mode of every other player</param>
    public static bool HasNobodyToPairWith(IEnumerable<IntroCinematicMode> modesOfOtherPlayers)
    {
        bool someoneIsPast = false;
        foreach (IntroCinematicMode mode in modesOfOtherPlayers)
        {
            if (mode == IntroCinematicMode.WAITING)
            {
                return false;
            }

            someoneIsPast |= IsPast(mode);
        }

        return someoneIsPast;
    }

    /// <summary>
    ///     Whether a player who left has to be put back in his lifepod when he joins again, instead of where the server last saw him.
    /// </summary>
    /// <param name="introUnfinished">The player left before his intro was over: what the server saw last is the pose of the cinematic (the seat, the animated pod), which is no place to stand at</param>
    /// <param name="isInEscapePod">The server has the player in the lifepod that was assigned to him</param>
    /// <param name="position">The position the server has for the player (world space)</param>
    /// <param name="escapePodPosition">The position of the pivot of that lifepod</param>
    public static bool ShouldPlaceAtEscapePod(bool introUnfinished, bool isInEscapePod, NitroxVector3 position, NitroxVector3 escapePodPosition)
    {
        if (introUnfinished)
        {
            return true;
        }

        // Saved before the player moved, or by a server that kept no pose: the first movement packet is far away, the spawn is the safe place
        return isInEscapePod && (IsUnknownPosition(position) || !IsPoseInEscapePod(position, escapePodPosition));
    }

    /// <summary>
    ///     Whether the client puts a player back at the spawn of his lifepod a moment after his initial sync (<c>PlayerPositionInitialSyncProcessor</c>). The server and the first placement of the client
    ///     get him into the lifepod; the log of the first live session hints that the second player of a new world was not there a moment later (2026-10-23: the story goal for swimming reached the
    ///     server 0.85 s after her sync, and the simulation of creatures moved to her 2.8 s after it although she had joined the lifepod of the host; the logs cannot tell her pose from a position
    ///     the server did not know yet, so this is a safety net that does nothing for a player who is where he belongs). Only a player who joined in his lifepod is judged, and only by whether he is
    ///     in it: one who joined somewhere else (a base, a Cyclops, the open water) is not touched.
    /// </summary>
    /// <param name="joinedInEscapePod">The initial sync put the player in the lifepod that was assigned to him</param>
    /// <param name="cinematicRunning">The cinematic of the intro runs (or the player is held by any cinematic): his pose is the one of the animation, it is not for the placement to judge</param>
    /// <param name="position">Where the player is now (world space)</param>
    /// <param name="escapePodPosition">The pivot of that lifepod</param>
    public static bool ShouldReplaceAfterSync(bool joinedInEscapePod, bool cinematicRunning, NitroxVector3 position, NitroxVector3 escapePodPosition)
    {
        return joinedInEscapePod && !cinematicRunning && !IsPoseInEscapePod(position, escapePodPosition);
    }

    /// <returns>True if the position is the default of a player who never moved (the origin), which no player ever stands at</returns>
    public static bool IsUnknownPosition(NitroxVector3 position)
    {
        return Math.Abs(position.X) < UNKNOWN_POSITION_TOLERANCE && Math.Abs(position.Y) < UNKNOWN_POSITION_TOLERANCE && Math.Abs(position.Z) < UNKNOWN_POSITION_TOLERANCE;
    }

    /// <summary>
    ///     Whether a player who is in a lifepod can be at this position: near its pivot, not at the sea floor below it. A player in the pod who is not there is walking (the pod's mode) in the open water,
    ///     he falls to the sea floor and looks like he is "inside a base" there.
    /// </summary>
    public static bool IsPoseInEscapePod(NitroxVector3 position, NitroxVector3 escapePodPosition)
    {
        float dx = position.X - escapePodPosition.X;
        float dz = position.Z - escapePodPosition.Z;
        float dy = position.Y - escapePodPosition.Y;
        return dx * dx + dz * dz <= ESCAPE_POD_MAX_HORIZONTAL_DISTANCE * ESCAPE_POD_MAX_HORIZONTAL_DISTANCE && dy >= -ESCAPE_POD_MAX_BELOW && dy <= ESCAPE_POD_MAX_ABOVE;
    }

    /// <returns>Where a player stands who arrives in the lifepod at <paramref name="escapePodPosition" /></returns>
    public static NitroxVector3 GetEscapePodSpawn(NitroxVector3 escapePodPosition) => escapePodPosition + EscapePodSpawnOffset;
}
