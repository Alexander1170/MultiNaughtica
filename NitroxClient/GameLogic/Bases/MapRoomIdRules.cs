using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     The NitroxId of a scanner room that was built from a ghost. Plain data, no game types.
/// </summary>
/// <remarks>
///     A room that is built into an existing base takes the id of its ghost like every other module. A room can also be the first piece of a new base (vanilla allows it on the
///     seabed): the new Base takes the id of the ghost, so the room takes the next one, as the first-piece moonpool does (<see cref="BuildingHandler.BuildBase" />). The server stores
///     the room as a <c>MapRoomEntity</c> with this id and <c>InteriorPieceEntitySpawner.RestoreMapRoom</c> gives it back after a join or a resync. The builder and every receiving
///     client take the id from here (both go through <see cref="BuildUtils.TryTransferIdFromGhostToModule" />), so they cannot name the room differently.
/// </remarks>
public static class MapRoomIdRules
{
    /// <param name="ghostId">The id of the ghost (<c>ConstructableBase</c>) that became the scanner room.</param>
    /// <param name="ghostIsInBase">The ghost was under a Base when it was finished (the room is added to an existing base). False for the first piece of a new base.</param>
    public static NitroxId GetMapRoomId(NitroxId ghostId, bool ghostIsInBase)
    {
        return ghostIsInBase ? ghostId : ghostId.Increment();
    }
}
