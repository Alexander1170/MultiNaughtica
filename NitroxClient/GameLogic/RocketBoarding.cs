using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic;

/// <summary>
/// Who counts as being inside the launch rocket. Used when the rocket launches (<see cref="Rockets.RocketLaunch(UnityEngine.GameObject)" />) and to decide whether it may
/// launch at all (<see cref="Rockets.GetPlayersNotAboard" />): both need the same answer.
/// </summary>
public static class RocketBoarding
{
    /// <summary>
    /// Pre-calculated radius (meters) of the sphere around the middle of stage 3 of the rocket that counts as inside.
    /// </summary>
    public const float COCKPIT_RADIUS = 5.55f;

    /// <summary>
    /// Extra distance (meters) given to remote players, whose position is a moment old.
    /// </summary>
    public const float REMOTE_SLACK = 1f;

    /// <summary>
    /// Extra distance (meters) that a client gives itself when a launch arrives: the launcher let it pass with <see cref="REMOTE_SLACK" /> on the position he saw (a moment old),
    /// and the real position can be another round trip's movement beyond that. Without it a player standing at the edge was counted aboard by the launcher and refused by himself.
    /// </summary>
    public const float RECEIVED_LAUNCH_SLACK = 2f * REMOTE_SLACK;

    /// <returns>True if the player is in the rocket: not underwater, not in a sub, base or vehicle and in reach of the cockpit sphere</returns>
    public static bool IsAboard(NitroxVector3 position, bool isUnderwater, bool isInSub, NitroxVector3 cockpitCenter, float slack = 0f)
    {
        return !isUnderwater && !isInSub && NitroxVector3.Distance(position, cockpitCenter) <= COCKPIT_RADIUS + slack;
    }

    /// <returns>True if a launch that another player started applies to the local player: everyone that the launcher counted aboard has to be able to pass this</returns>
    public static bool IsAboardForReceivedLaunch(NitroxVector3 position, bool isUnderwater, bool isInSub, NitroxVector3 cockpitCenter)
    {
        return IsAboard(position, isUnderwater, isInSub, cockpitCenter, RECEIVED_LAUNCH_SLACK);
    }
}
