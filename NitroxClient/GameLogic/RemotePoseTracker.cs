using Nitrox.Model.GameLogic.PlayerAnimation;

namespace NitroxClient.GameLogic;

/// <summary>
/// Whether the copy of a remote player is drawn in the swimming pose (bug 6, PLAYERS.md PL9 (6)). Plain values, no game types, so that it can be tested; <see cref="RemotePlayer" /> reads
/// the result and writes it into the animator.
/// </summary>
/// <remarks>
/// The swimming pose is hung from the root of the body like the walking pose is, but the two are not the same shape: a player who walks has his root a full controller height above the
/// floor (<c>PlayerController.standheight</c> 1.5 m, vanilla constructor) and the walking pose reaches down to it, a swimmer has a controller of <c>swimheight</c> 0.5 m, and vanilla
/// lifts the root of a player by the difference (1.0 m, <c>GroundMotor.SetControllerHeight</c>) at the moment he gets up from the water. So a body that is placed for a walking player (root
/// a standing height above the floor, which is what the pawn of a Cyclops gives it) and drawn in the swimming pose has its legs about the difference too high above the floor.
/// <para>
/// The state of the pose is the animator parameter <c>is_underwater</c> of the copy. It was only ever written by the animation events of the player (<c>AnimationChangeEvent</c>), it
/// starts as <c>true</c> (<c>AnimationController.Awake</c>), and an event that arrived before the body existed was dropped. What this class adds: (1) the last reported state is kept
/// whether the body exists or not, so that the body starts from it and not from the state at the time the player joined; (2) a player whose copy is a pawn in a Cyclops is walking,
/// whatever the last event said: he sends <c>PlayerInCyclopsMovement</c> from the ground motor of the Cyclops, the vanilla game never lets a player swim in a Cyclops
/// (<c>SubRoot.IsUnderwater</c> is false), and when he leaves the hull the last reported state is back.
/// </para>
/// </remarks>
public sealed class RemotePoseTracker
{
    /// <summary>
    /// The state a body has before any event (<c>AnimationController.Awake</c> sets <c>is_underwater</c> to true).
    /// </summary>
    public const bool DEFAULT_UNDERWATER = true;

    private bool reportedUnderwater = DEFAULT_UNDERWATER;
    private bool hasReport;
    private bool walkingInHull;
    private bool holdingBar;
    private float walkingSince = float.NaN;

    /// <summary>
    /// The state the player last reported (an animation event of the type underwater), or the state of the join if none came yet.
    /// </summary>
    public bool ReportedUnderwater => reportedUnderwater;

    /// <summary>
    /// An animation event of the type underwater has been seen (the join state is only a start value).
    /// </summary>
    public bool HasReport => hasReport;

    /// <summary>
    /// The latest movement of the player came from the ground motor of a Cyclops (a pawn walks).
    /// </summary>
    public bool WalkingInHull => walkingInHull;

    /// <summary>
    /// When (the time the caller gave <see cref="SetWalkingInHull" />) the player began to walk in the hull, <see cref="float.NaN" /> while he does not.
    /// </summary>
    public float WalkingSince => walkingSince;

    /// <summary>
    /// The animator parameter <c>is_underwater</c> the copy has to have.
    /// </summary>
    public bool Swimming => holdingBar || (reportedUnderwater && !walkingInHull);

    /// <summary>
    /// The player holds on to the rider bar of a Seamoth. The game's locked mode makes the rider report "not underwater" (the water does not count for him), but the copy hangs in the water and has
    /// to be drawn swimming.
    /// </summary>
    public bool HoldingBar => holdingBar;

    public void SetHoldingBar(bool holding)
    {
        holdingBar = holding;
    }

    /// <summary>
    /// How long (seconds) the player has walked in the hull at <paramref name="now" />, <see cref="float.NaN" /> while he does not.
    /// </summary>
    public float WalkingFor(float now)
    {
        return walkingInHull ? now - walkingSince : float.NaN;
    }

    /// <summary>
    /// The copy walks and the last report still says underwater, for longer than <see cref="PoseRefreshRules.SETTLED_AFTER" />. The first pawn packet of a player who climbs into the hull
    /// out of the water arrives while the report still says underwater (the state change is a separate message of the same client, and which of the two comes first is not defined), so
    /// "walks and reported underwater" alone is the normal boarding. Only a state that has outlived the refresh of the sender (<see cref="PoseRefreshRules.REFRESH_INTERVAL" />) is not:
    /// the sender really reports underwater while he walks in a Cyclops, or his announcements do not arrive.
    /// </summary>
    public bool IsStuckUnderwater(float now)
    {
        return walkingInHull && reportedUnderwater && PoseRefreshRules.IsSettled(WalkingFor(now));
    }

    /// <summary>
    /// How long the player had walked when this report is the one that ends the swimming state he was walking in: the seconds since his first pawn packet, <see cref="float.NaN" /> for
    /// any other report or when he does not walk. Asks only, changes nothing: ask before <see cref="Report" /> takes the report in, then judge it with
    /// <see cref="PoseRefreshRules.IsLateCorrection" /> (about zero: the boarding race, a second or more: a state that had to be corrected).
    /// </summary>
    public float SwimmingEndedWhileWalking(AnimChangeType type, AnimChangeState state, float now)
    {
        return walkingInHull && reportedUnderwater && type == AnimChangeType.UNDERWATER && state == AnimChangeState.OFF ? WalkingFor(now) : float.NaN;
    }

    /// <summary>
    /// An animation event of the player. Events of other types (the infection reveal) say nothing about the pose. Returns whether it was one about the pose.
    /// </summary>
    public bool Report(AnimChangeType type, AnimChangeState state)
    {
        if (type != AnimChangeType.UNDERWATER)
        {
            return false;
        }

        reportedUnderwater = state != AnimChangeState.OFF;
        hasReport = true;
        return true;
    }

    /// <summary>
    /// The animation the server sent with the player when he joined: a start value, taken only while no event has come, and only if it is about the pose.
    /// </summary>
    public void SeedFromJoin(AnimChangeType type, AnimChangeState state)
    {
        if (hasReport || type != AnimChangeType.UNDERWATER)
        {
            return;
        }

        reportedUnderwater = state != AnimChangeState.OFF;
    }

    /// <summary>
    /// A movement packet of the player arrived: from the ground motor of a Cyclops (walking) or a world movement of any other kind (the reported state decides again). <paramref name="now" />
    /// is the time of the packet; a packet that does not change the state keeps the time at which he began to walk.
    /// </summary>
    public void SetWalkingInHull(bool walking, float now)
    {
        if (walking == walkingInHull)
        {
            return;
        }

        walkingInHull = walking;
        walkingSince = walking ? now : float.NaN;
    }
}
