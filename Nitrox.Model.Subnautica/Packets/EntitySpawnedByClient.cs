using System;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class EntitySpawnedByClient : Packet
{
    public Entity Entity { get; }
    public bool RequireRespawn { get; }
    public bool RequireSimulation { get; }

    /// <summary>
    /// The sender introduces an entity that other clients may introduce at the same moment under the same id (the creature of a spawner, K15): the server keeps the first
    /// one it gets and ignores the others instead of replacing what it has. The sender has no object for it: the server sends the entity to the sender too.
    /// </summary>
    public bool OnlyIfNew { get; }

    public EntitySpawnedByClient(Entity entity, bool requireRespawn = false, bool requireSimulation = true, bool onlyIfNew = false)
    {
        Entity = entity;
        RequireRespawn = requireRespawn;
        RequireSimulation = requireSimulation;
        OnlyIfNew = onlyIfNew;
    }
}
