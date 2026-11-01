namespace NitroxClient.GameLogic;

/// <summary>
/// Where the cutting beam of a laser-cut door is drawn (upstream issue 2463: "a beam of light comes from the world origin"). Plain data, no game types.
/// <para>
/// <c>LaserCutObject.OnEnable</c> runs <c>SetCutFxPosition</c>, which stores the position of the cutting node in <c>matchingNodePos</c>; every later call moves the beam and the arms of
/// the cutting player there with <c>Vector3.Lerp(matchingNodePos, node, deltaTime * 2)</c>. The entity spawner of the fork activates the clone (<c>InstantiateWithId</c>) BEFORE it puts
/// it in place (<c>SetupObject</c>), so <c>OnEnable</c> runs with the door at the world origin and <c>matchingNodePos</c> starts there: for the first seconds of a cut the beam, and
/// the cutter's look target, sweep in from the origin of the world. A vanilla door is made in place and never has this.
/// </para>
/// </summary>
public static class LaserCutFxRules
{
    /// <summary>
    /// No node of a door is farther than a few metres from the door's root; the beam is lerped over 30 m only when it was stored somewhere else.
    /// </summary>
    public const float MAX_NODE_DISTANCE = 30f;

    /// <summary>
    /// The stored position is stale (stored while the door was somewhere else): reset it to zero, which <c>SetCutFxPosition</c> takes as "no position yet" and replaces at once.
    /// </summary>
    /// <param name="nodePositionIsZero">The stored position is <c>Vector3.zero</c> (nothing is stored yet).</param>
    /// <param name="squaredDistanceToDoor">The squared distance of the stored position to the root of the door.</param>
    public static bool IsStaleNodePosition(bool nodePositionIsZero, float squaredDistanceToDoor)
    {
        return !nodePositionIsZero && squaredDistanceToDoor > MAX_NODE_DISTANCE * MAX_NODE_DISTANCE;
    }
}
