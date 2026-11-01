using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// Put on a spawner object (<see cref="PrefabSpawnBase"/>) when it starts to make something. The spawned object comes later for spawners that load their prefab asynchronously, and
/// the spawner may not have its id yet when it makes it: this waits for both and hands a creature to <see cref="SpawnerCreatures"/>. After the creature was introduced it waits for the
/// creature of the server to show up here and gives it to the spawner as its own (so that an intermittent spawner makes no new one while that lives), then it goes away.
/// </summary>
public class SpawnerCreatureWatcher : MonoBehaviour
{
    private PrefabSpawnBase spawner;
    private int framesLeft = SpawnerCreatures.MAX_FRAMES_TO_WAIT;
    private NitroxId expectedCreatureId;

    /// <summary>
    /// A spawner object can hold several spawners, each has its own watcher.
    /// </summary>
    public PrefabSpawnBase Spawner => spawner;

    public void Init(PrefabSpawnBase spawnerToWatch)
    {
        spawner = spawnerToWatch;
    }

    private void Update()
    {
        if (!spawner || --framesLeft <= 0)
        {
            Destroy(this);
            return;
        }

        GameObject spawned = spawner.spawnedObj;

        if (expectedCreatureId != null)
        {
            if (NitroxEntity.TryGetObjectFrom(expectedCreatureId, out GameObject serverCreature))
            {
                spawner.spawnedObj = serverCreature;
                Destroy(this);
                return;
            }

            if (!spawned)
            {
                // the server's creature is not here (yet, or not in range of this client)
                return;
            }

            // an intermittent spawner made another creature meanwhile: it is handled like a new one
            expectedCreatureId = null;
            framesLeft = SpawnerCreatures.MAX_FRAMES_TO_WAIT;
        }

        if (!spawned)
        {
            // not made yet
            return;
        }

        if (!spawned.GetComponent<Creature>() || spawned.TryGetComponent<NitroxEntity>(out _))
        {
            // something else (a plant, a rock) or already an entity of the world
            Destroy(this);
            return;
        }

        NitroxEntity spawnerEntity = SpawnerCreatures.GetSpawnerEntity(spawner);
        if (!spawnerEntity)
        {
            // not an entity of the world (yet): wait for its id until the frames run out
            return;
        }

        expectedCreatureId = SpawnerCreatures.Introduce(spawner, spawned, spawnerEntity.Id, SpawnerCreatures.GetSlotKey(spawner, spawnerEntity));
        if (expectedCreatureId == null)
        {
            Destroy(this);
            return;
        }

        framesLeft = SpawnerCreatures.MAX_FRAMES_TO_WAIT_FOR_THE_SERVER;
    }
}
