using System.Reflection;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Spawner objects (a Skyray spawner and the like) make their creature on every client that loads them, with no id that the clients share. This starts a
/// <see cref="SpawnerCreatureWatcher"/> on the spawner that gives the creature the id of its slot once it exists (K15, see <see cref="NitroxClient.GameLogic.SpawnerCreatures"/>).
/// Only the start of the spawning is patched: the creature itself may come later (addressable prefabs), which is why the watcher waits for it.
/// </summary>
public sealed partial class PrefabSpawnBase_SpawnObj_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrefabSpawnBase t) => t.SpawnObj(default));

    public static void Postfix(PrefabSpawnBase __instance)
    {
        if (!__instance)
        {
            return;
        }

        foreach (SpawnerCreatureWatcher watcher in __instance.GetComponents<SpawnerCreatureWatcher>())
        {
            if (watcher.Spawner == __instance)
            {
                return;
            }
        }

        __instance.gameObject.AddComponent<SpawnerCreatureWatcher>().Init(__instance);
    }
}
