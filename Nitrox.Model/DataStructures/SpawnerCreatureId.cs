using Nitrox.Model.Extensions;

namespace Nitrox.Model.DataStructures;

/// <summary>
/// The id of the creature that a spawner object makes (K15). Spawner prefabs (a Skyray spawner and the like) create their creature at runtime on every client that loads them,
/// so the creature has no id that the clients share. Every client computes the same id from the spawner's own id and the place of the spawner component in the prefab: the
/// server keeps the first creature that is introduced under it, the others do not keep a private copy.
/// </summary>
public static class SpawnerCreatureId
{
    /// <param name="spawnerId">The id of the entity that holds the spawner (the spawner prefab).</param>
    /// <param name="slotKey">Where the spawner component is in that prefab: the names and sibling positions of the objects from the entity down to the spawner and the position of the component
    /// on its object. Made of what the prefab itself contains, not of what happens to exist when a client looks (the objects that spawners make appear at different times on different clients).</param>
    public static NitroxId For(NitroxId spawnerId, string slotKey)
    {
        return new NitroxId($"spawner-creature:{spawnerId}:{slotKey}".ToMd5Hash());
    }
}
