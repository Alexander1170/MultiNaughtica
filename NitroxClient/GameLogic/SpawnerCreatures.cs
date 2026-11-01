using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// The creatures that spawner objects (<see cref="PrefabSpawnBase"/>, a Skyray spawner and the like) create on every client that loads them (K15). Vanilla instantiates the creature
/// in the spawner's Start, so each client had a private creature without an id: nobody saw anybody else's, and a kill or a bite stayed on one client.
/// <para>
/// Here the creature that the game makes is only a seed. It gets destroyed and its entity is introduced to the server under an id that every client computes the same way
/// (<see cref="SpawnerCreatureId"/>); the server keeps the first introduction (<see cref="Nitrox.Model.Subnautica.Packets.EntitySpawnedByClient.OnlyIfNew"/>), gives the lock to its sender and sends
/// the entity to every client that can see it, the sender included. The creature that every client ends up with is therefore made by the same path as every other creature of the world
/// (cell root, cell management, id, lock), and cannot be a second one: there is no step where this client holds a registered creature that the server may not know.
/// </para>
/// </summary>
public static class SpawnerCreatures
{
    /// <summary>
    /// How many frames a spawner may take to make its creature and to have an id itself (the spawner entity gets its id right after the prefab was instantiated, the creature of an
    /// addressable spawner comes a few frames later). A spawner that is not an entity of the world (scene objects) never gets one: it keeps its private creature.
    /// </summary>
    public const int MAX_FRAMES_TO_WAIT = 600;

    /// <summary>
    /// How many frames the watcher waits for the creature of the server to show up here after it introduced one (the spawner is told about it, so that it does not make another).
    /// </summary>
    public const int MAX_FRAMES_TO_WAIT_FOR_THE_SERVER = 600;

    public enum Outcome
    {
        /// <summary>The creature of this spawner is not here: this one is only introduced to the server (and destroyed, the server's creature replaces it).</summary>
        INTRODUCE,

        /// <summary>The creature of this spawner is already here (it came from the server first): this one is dropped.</summary>
        ADOPT_KNOWN
    }

    public static Outcome Decide(bool someCreatureHasTheId)
    {
        return someCreatureHasTheId ? Outcome.ADOPT_KNOWN : Outcome.INTRODUCE;
    }

    /// <summary>
    /// The entity that the spawner belongs to: the outermost one. Objects inside a prefab can get ids of their own after the spawner started, the entity of the prefab itself already has one.
    /// </summary>
    public static NitroxEntity GetSpawnerEntity(PrefabSpawnBase spawner)
    {
        NitroxEntity[] entities = spawner.GetComponentsInParent<NitroxEntity>(true);
        return entities.Length > 0 ? entities[^1] : null;
    }

    /// <summary>
    /// Where the spawner is in its prefab, made of the names and sibling positions of the objects between the entity and the spawner and the position of the component on its object.
    /// Nothing in it depends on which other objects exist at this moment: the objects that spawners make are appended to the end of their parent and have their own part of the path.
    /// </summary>
    public static string GetSlotKey(PrefabSpawnBase spawner, NitroxEntity spawnerEntity)
    {
        List<string> parts = [];
        for (Transform current = spawner.transform; current && current != spawnerEntity.transform; current = current.parent)
        {
            parts.Add($"{current.name}#{current.GetSiblingIndex()}");
        }
        parts.Reverse();
        string path = string.Join("/", parts);

        PrefabSpawnBase[] onSameObject = spawner.GetComponents<PrefabSpawnBase>();
        int componentIndex = System.Array.IndexOf(onSameObject, spawner);
        return $"{path}|{componentIndex}";
    }

    /// <returns>The id that the server's creature will have here when the creature was introduced, otherwise null.</returns>
    public static NitroxId Introduce(PrefabSpawnBase spawner, GameObject creature, NitroxId spawnerId, string slotKey)
    {
        NitroxId creatureId = SpawnerCreatureId.For(spawnerId, slotKey);
        bool known = NitroxEntity.TryGetObjectFrom(creatureId, out GameObject knownCreature);
        if (Decide(known) == Outcome.ADOPT_KNOWN)
        {
            // The spawner counts the known creature as its own: it makes no new one while that lives
            spawner.spawnedObj = knownCreature;
            Object.Destroy(creature);
            Log.Debug($"[SpawnerCreatures] {creature.name} of spawner {spawnerId} ({slotKey}) dropped, the creature {creatureId} is already here");
            return null;
        }

        UniqueIdentifier uniqueIdentifier = creature.GetComponent<UniqueIdentifier>();
        TechType techType = CraftData.GetTechType(creature);
        if (!uniqueIdentifier || techType == TechType.None)
        {
            Log.Debug($"[SpawnerCreatures] {creature.name} of spawner {spawnerId} has no class id or tech type, left as it is");
            return null;
        }

        int level = creature.TryGetComponent(out LargeWorldEntity largeWorldEntity) ? (int)largeWorldEntity.cellLevel : (int)LargeWorldEntity.CellLevel.Near;
        WorldEntity entity = new(creature.transform.ToWorldDto(), level, uniqueIdentifier.classId, false, creatureId, techType.ToDto(), null, null, new List<Entity>());

        // Only a seed: the server's creature (made like every other creature of the world) replaces it on every client that can see it, this one included
        Object.Destroy(creature);
        NitroxServiceLocator.LocateService<Entities>().BroadcastEntitySpawnedByClient(entity, onlyIfNew: true);
        Log.Debug($"[SpawnerCreatures] {creature.name} ({techType}) of spawner {spawnerId} ({slotKey}) introduced as {creatureId}");
        return creatureId;
    }
}
