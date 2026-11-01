namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
/// Whether a joining player is a new one, decided when his turn in the join queue comes and not when his name was reserved (<see cref="PlayerManager.CreatePlayerData" />).
/// </summary>
internal static class JoinReservationRules
{
    /// <param name="reservedAsBrandNew">The reservation (made when the client connected) did not know the name.</param>
    /// <param name="knownAtJoin">The saved or earlier player of that name is known now: the world finished loading in between.</param>
    public static bool IsBrandNewAtJoin(bool reservedAsBrandNew, bool knownAtJoin)
    {
        return reservedAsBrandNew && !knownAtJoin;
    }
}
