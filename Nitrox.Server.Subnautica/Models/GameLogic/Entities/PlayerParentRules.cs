using System.Diagnostics.CodeAnalysis;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
/// A player entity is parented to a sub root, to a lifepod or to nothing, and two packets write that parent: <see cref="Nitrox.Model.Subnautica.Packets.SubRootChanged" /> and
/// <see cref="Nitrox.Model.Subnautica.Packets.EscapePodChanged" />.
/// Each one only speaks for its own kind of parent. "No sub root" does not take a player out of a lifepod (a lifepod is no sub root: the client reports it with the lifepod packets
/// and ends a respawn with "no sub root"), and "no lifepod" does not take a player out of a sub root (the client drops its lifepod once it is far enough from it, wherever it is).
/// </summary>
internal static class PlayerParentRules
{
    /// <returns>True if the packet says "no sub root" while the player sits in a lifepod: the parent stays the lifepod</returns>
    internal static bool KeepsEscapePodParent(Optional<NitroxId> newSubRootId, Entity? currentParent) => !newSubRootId.HasValue && currentParent is EscapePodEntity;

    /// <returns>True if the packet says "no lifepod" while the player sits in a sub root: the parent stays the sub root</returns>
    internal static bool KeepsSubRootParent(Optional<NitroxId> newEscapePodId, Entity? currentParent) => !newEscapePodId.HasValue && currentParent is not null and not EscapePodEntity;

    /// <returns>
    /// True if the entity is a Cyclops. Only a world entity with a tech type can be one: a base (<see cref="Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases.BuildEntity" />) has none and must answer false
    /// instead of throwing: a player who left inside a base made every join throw a NullReferenceException (2026-10-24, the world "Test" after a server restart)
    /// </returns>
    internal static bool IsCyclops(Entity? entity) => entity?.TechType?.Name == "Cyclops";

    internal static bool TryGetParent(EntityRegistry entityRegistry, NitroxId playerEntityId, [NotNullWhen(true)] out Entity? parent)
    {
        parent = null;
        return entityRegistry.TryGetEntityById(playerEntityId, out Entity? player) && player.ParentId != null && entityRegistry.TryGetEntityById(player.ParentId, out parent);
    }
}
