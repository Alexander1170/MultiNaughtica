using System.Collections.Generic;
using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic;

/// <summary>
/// Who counts as being at the Sunbeam landing site (ST1h). When the Sunbeam is shot down, vanilla plays one of two PDA goals depending on whether the player is at the landing
/// site. Completed goals are shared by all players, so there can be only one outcome: it counts as seen from the landing site if ANY player is there. That keeps the scene a
/// player went to see (the crew talking to the survivor at the site) even if the partner stayed behind, and otherwise gives the same "no one was there" log as vanilla.
/// </summary>
public static class SunbeamRange
{
    /// <summary>
    /// The vanilla test (<c>StoryGoalCustomEventHandler.IsPlayerAtLandingSite</c>), applied to every player instead of the local one: the plain distance to the landing site,
    /// the border counts as inside. Underwater, in a sub or in a vehicle makes no difference.
    /// </summary>
    /// <param name="playerPositions">The position of every connected player the caller knows about, its own included. A player who left is simply not in the list.</param>
    /// <returns>True if at least one player is at the landing site, false if nobody is (or there is nobody)</returns>
    public static bool IsAnyoneAtLandingSite(IEnumerable<NitroxVector3> playerPositions, NitroxVector3 landingSite, float landingSiteRadius)
    {
        foreach (NitroxVector3 position in playerPositions)
        {
            if (NitroxVector3.Distance(position, landingSite) <= landingSiteRadius)
            {
                return true;
            }
        }
        return false;
    }
}
