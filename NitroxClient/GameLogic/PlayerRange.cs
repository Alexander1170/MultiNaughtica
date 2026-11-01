using System.Collections.Generic;
using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic;

/// <summary>
/// Distances of "the player" in places where the game only knows the local one (<c>Player.main</c>), so that triggers that drive a shared scene object (the Sea Emperor in the Prison
/// Aquarium) get the same inputs on every client: the nearest of all players counts. Plain data, no game types.
/// </summary>
public static class PlayerRange
{
    /// <param name="playerPositions">The positions of all players (the local player and the bodies of the remote players)</param>
    /// <returns>The distance of the nearest of them to the target, <see cref="float.MaxValue" /> without any</returns>
    public static float NearestDistance(IEnumerable<NitroxVector3> playerPositions, NitroxVector3 target)
    {
        float nearest = float.MaxValue;
        foreach (NitroxVector3 position in playerPositions)
        {
            float distance = NitroxVector3.Distance(position, target);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }
}
